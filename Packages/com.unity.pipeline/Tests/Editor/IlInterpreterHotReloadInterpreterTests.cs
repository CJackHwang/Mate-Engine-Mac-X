using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using Unity.Pipeline.Compilation;
using Unity.Pipeline.HotReload;
using Unity.Pipeline.Runtime.Commands;
using UnityEngine;
using UnityEngine.TestTools;

namespace Unity.Pipeline.Tests.Editor
{
    /// <summary>
    /// Top-level probe with a woven [HotReload] method, reloaded through the <b>interpreter</b> backend.
    /// Top-level (not nested) so the generated static override can name it from a separate assembly.
    /// </summary>
    public class IlInterpreterReloadProbe
    {
        public int baseline;
        public int hotValue;

        [HotReload]
        public void Compute()
        {
            baseline = 1; // original body
        }
    }

    /// <summary>
    /// Probe with a PRIVATE field, reloaded through the interpreter. Verifies that a [HotReload] body can
    /// read/write private state now that the override is compiled with access checks disabled and the host
    /// binding registers non-public instance members. A public getter exposes the private field for asserts.
    /// </summary>
    public class IlInterpreterPrivateReloadProbe
    {
        private int m_Secret;
        public int Secret => m_Secret;

        [HotReload]
        public void Compute()
        {
            m_Secret = 1; // original body
        }
    }

    /// <summary>
    /// Probe whose reloaded body CALLS a private helper method (not just reads a private field). Mirrors
    /// TankAI, whose reloaded SeekUpdate calls the private GetPathLength. The interpreter must resolve the
    /// private method token to its registered binding (it looked methods up with BindingFlags.Public only,
    /// so private calls hit "Host method not registered" and the whole override fell back).
    /// </summary>
    public class IlInterpreterPrivateMethodProbe
    {
        public int result;
        private int Helper(int x) => x * 3;

        [HotReload]
        public void Compute()
        {
            result = 0; // original body
        }
    }

    /// <summary>
    /// Probe with a PRIVATE nested enum field switched on in a [HotReload] body — the TankAI pattern
    /// (Update switches on a private <c>enum State</c>). The interpreter classified enum host fields as O
    /// (boxed), so the switch matched no case and the method silently no-op'd; enum fields must read as
    /// their underlying int.
    /// </summary>
    public class IlInterpreterEnumSwitchProbe
    {
        private enum Mode { Idle, Run, Stop }
        private Mode m_Mode = Mode.Run;
        public int picked = -1;

        [HotReload]
        public void Compute() { picked = -1; } // original body
    }

    /// <summary>
    /// Probe whose reloaded Compute calls another reloaded method (Inner) on the
    /// same instance — a host call that woven-dispatches back into the SAME interpreter VM (reentrant).
    /// The VM ran every Invoke at frame base 0, so the nested call clobbered Compute's live frame; verify
    /// Compute's locals survive the nested call. Mirrors TankAI.Update calling TankAI.SeekUpdate.
    /// </summary>
    public class IlInterpreterReentrancyProbe
    {
        public int result;
        public int innerRan;
        [HotReload]
        public void Inner() { innerRan = -1; }  // original body
        [HotReload]
        public void Compute() { result = -1; }  // original body
    }

    /// <summary>
    /// Probe whose reloaded body constructs a UnityEngine reference type (<c>new NavMeshPath()</c>) — a
    /// <c>newobj</c> over a type NOT in the standard binding. Verifies the interpreter registers referenced
    /// engine types so the ctor token resolves (otherwise lowering fails "opcode 0x0073 (newobj) not supported").
    /// </summary>
    public class IlInterpreterNewobjReloadProbe
    {
        public int made = -1;

        [HotReload]
        public void Compute()
        {
            made = -1; // original body
        }
    }

    /// <summary>
    /// Probe whose reloaded body raises IMGUI's control-flow exception (GUIUtility.ExitGUI). The
    /// dispatch must PROPAGATE the original exception object — not log-and-fall-back — so a
    /// hot-reloaded method behaves like its compiled equivalent (IMGUI catches ExitGUIException
    /// by type). The VM wraps it in ScriptRuntimeException with the original as InnerException;
    /// the registry unwraps and rethrows it.
    /// </summary>
    public class IlInterpreterExitGuiProbe
    {
        public int marker = -1;

        [HotReload]
        public void Compute()
        {
            marker = 0; // original body
        }
    }

    /// <summary>
    /// Probe whose reloaded body throws at runtime. Verifies interpreter errors carry a source
    /// location ("at line N (File.cs)") resolved from the embedded PDB + the transform's #line
    /// directives — not a raw IL offset ("at IL+0x000D"), which users can't act on.
    /// </summary>
    public class IlInterpreterLineInfoProbe
    {
        public int made = -1;

        [HotReload]
        public void Compute()
        {
            made = 0; // original body
        }
    }

    /// <summary>
    /// Probe with Nullable&lt;T&gt; fields — the TankMovement pattern (its reloaded methods read
    /// <c>InputUser.controlScheme.Value.name</c>, an <c>InputControlScheme?</c>). Nullable members
    /// are interpreter intrinsics over the boxed-T-or-null representation; before that,
    /// <c>Nullable`1.get_Value</c> failed as "not registered in the binding".
    /// </summary>
    public class IlInterpreterNullableProbe
    {
        public float? maybeFloat = 2.5f;
        public float? emptyFloat = null;
        public int? maybeInt = 7;
        public int? emptyInt = null;
        public float sum;
        public int result = -1;

        [HotReload]
        public void Compute()
        {
            result = 0; // original body
        }
    }

    /// <summary>
    /// Probe whose reloaded body reads UnityEngine.Random statics — `value` (a float) and
    /// `rotationUniform` (a STATIC property returning a flat struct, Quaternion). Repro for
    /// "Random.rotationUniform does not hot reload".
    /// </summary>
    public class IlInterpreterRandomProbe
    {
        public int made = -1;

        [HotReload]
        public void Compute()
        {
            made = 0; // original body
        }
    }

    /// <summary>
    /// Probe whose compiled body mutates state; the empty-body reload test replaces it with a no-op.
    /// </summary>
    public class IlInterpreterEmptyBodyProbe
    {
        public int computedValue;

        [HotReload]
        public void Compute()
        {
            computedValue = 42; // original body
        }
    }

    /// <summary>
    /// Probe for reloads that DECLARE a new method and call it: CompiledHelper is the compiled
    /// twin (same body text as the LiveHelper the edited source adds) — its output is the oracle
    /// the interpreted, live-added helper must match.
    /// </summary>
    public class IlInterpreterNewMethodProbe
    {
        public int result;

        public int CompiledHelper(int x)
        {
            int acc = 0;
            for (int i = 1; i <= x; i++)
                acc += i * x - 1;
            return acc + x / 2;
        }

        [HotReload]
        public void Compute()
        {
            result = -1; // original body
        }
    }

    /// <summary>
    /// Probe for a reload that adds a new Click method and wires it to a UITK-style callback —
    /// a method group over a live-added method (the DebugUIController Button case). The event
    /// and Fire() stand in for Button.clicked, which needs a live panel to raise.
    /// </summary>
    public class IlInterpreterClickProbe
    {
        public int clicks;
        public event System.Action Clicked;
        public void Fire() => Clicked?.Invoke();

        [HotReload]
        public void Compute()
        {
            clicks = -1; // original body
        }
    }

    /// <summary>
    /// Probe for a reload that adds a NEW [HotReload]-tagged method: the new method can't register
    /// (no woven prologue exists for it) but must not poison the rest of the file's overrides.
    /// </summary>
    public class IlInterpreterNewTaggedProbe
    {
        public int result;

        [HotReload]
        public void Compute()
        {
            result = -1; // original body
        }
    }

    /// <summary>
    /// End-to-end interpreter hot reload: the same reload_file pipeline as the Assembly.Load path but
    /// via <c>reload_file_editor_interpreter</c>, so the edited [HotReload] body is dispatched through the
    /// IlInterpreter VM (<c>InterpreterHotReloadExecutor</c>) rather than reflection. Mirrors
    /// <see cref="HotReloadInPlaceTests"/>'s end-to-end test with the interpreter backend selected.
    /// </summary>
    class IlInterpreterHotReloadInterpreterTests
    {
        [SetUp]
        public void Setup() => HotReloadRegistry.ClearAllForTesting();

        [TearDown]
        public void TearDown() => HotReloadRegistry.ClearAllForTesting();

        [Test]
        public void ReloadFileInterpreter_EditedBody_AppliesViaInterpreterDispatch()
        {
            // Auto-discovery does this at play start; register the target explicitly for the test.
            HotReloadRegistry.RegisterReloadableMethod(
                typeof(IlInterpreterReloadProbe).GetMethod(nameof(IlInterpreterReloadProbe.Compute)),
                new HotReloadWithOverridesAttribute());

            var editedSource = @"
using Unity.Pipeline.HotReload;
namespace Unity.Pipeline.Tests.Editor
{
    public class IlInterpreterReloadProbe
    {
        public int baseline;
        public int hotValue;

        [HotReload]
        public void Compute()
        {
            hotValue = 99;
        }
    }
}";
            var path = Path.Combine(Application.temporaryCachePath, "IlInterpreterReloadProbe_edit.cs");
            File.WriteAllText(path, editedSource);

            try
            {
                var response = HotReloadCommands.ReloadFileEditorInterpreter(path);
                Assert.IsTrue(response.Success,
                    $"reload_file_editor_interpreter should succeed. Error: {response.ErrorDetails}");

                var probe = new IlInterpreterReloadProbe();
                probe.Compute();

                Assert.AreEqual(99, probe.hotValue,
                    "Edited [HotReload] body should run via the interpreter dispatch");
                Assert.AreEqual(0, probe.baseline,
                    "Original compiled body should be skipped when the interpreter override runs");
            }
            finally
            {
                if (File.Exists(path)) File.Delete(path);
            }
        }

        [Test]
        public void ReloadFileInterpreter_RandomStatics_DispatchViaInterpreter()
        {
            HotReloadRegistry.RegisterReloadableMethod(
                typeof(IlInterpreterRandomProbe).GetMethod(nameof(IlInterpreterRandomProbe.Compute)),
                new HotReloadWithOverridesAttribute());

            // rotationUniform is a static property returning a flat struct (Quaternion); value is
            // a static float property. A unit quaternion's squared magnitude is 1, so made == 2
            // proves both statics dispatched with real values (a fallback leaves made == 0).
            var editedSource = @"
using Unity.Pipeline.HotReload;
namespace Unity.Pipeline.Tests.Editor
{
    public class IlInterpreterRandomProbe
    {
        public int made = -1;

        [HotReload]
        public void Compute()
        {
            made = 0;
            var v = UnityEngine.Random.value;
            if (v >= 0f && v <= 1f) made = made + 1;
            var q = UnityEngine.Random.rotationUniform;
            float mag = q.x * q.x + q.y * q.y + q.z * q.z + q.w * q.w;
            if (mag > 0.99f && mag < 1.01f) made = made + 1;
        }
    }
}";
            var path = Path.Combine(Application.temporaryCachePath, "IlInterpreterRandomProbe_edit.cs");
            File.WriteAllText(path, editedSource);

            try
            {
                var response = HotReloadCommands.ReloadFileEditorInterpreter(path);
                Assert.IsTrue(response.Success, $"reload should succeed. Error: {response.ErrorDetails}");

                var probe = new IlInterpreterRandomProbe();
                probe.Compute();

                Assert.AreEqual(2, probe.made,
                    "Random.value and Random.rotationUniform should both dispatch via the interpreter");
            }
            finally
            {
                if (File.Exists(path)) File.Delete(path);
            }
        }

        [Test]
        public void ReloadFileInterpreter_EmptyBody_AppliesNoOpOverride()
        {
            HotReloadRegistry.RegisterReloadableMethod(
                typeof(IlInterpreterEmptyBodyProbe).GetMethod(nameof(IlInterpreterEmptyBodyProbe.Compute)),
                new HotReloadWithOverridesAttribute());

            // Emptying a [HotReload] body is a legitimate edit (e.g. deleting a Debug.Log): the
            // reload must bind a no-op override, not fail with "No [HotReload] methods found"
            // (which would leave the previous override live).
            var editedSource = @"
using Unity.Pipeline.HotReload;
namespace Unity.Pipeline.Tests.Editor
{
    public class IlInterpreterEmptyBodyProbe
    {
        public int computedValue;

        [HotReload]
        public void Compute()
        {
        }
    }
}";
            var path = Path.Combine(Application.temporaryCachePath, "IlInterpreterEmptyBodyProbe_edit.cs");
            File.WriteAllText(path, editedSource);

            try
            {
                var response = HotReloadCommands.ReloadFileEditorInterpreter(path);
                Assert.IsTrue(response.Success,
                    $"reload of an emptied [HotReload] body should succeed. Error: {response.ErrorDetails}");

                var probe = new IlInterpreterEmptyBodyProbe();
                probe.Compute();

                Assert.AreEqual(0, probe.computedValue,
                    "Empty override should replace the original body (which sets 42)");
            }
            finally
            {
                if (File.Exists(path)) File.Delete(path);
            }
        }

        [Test]
        public void ReloadFileInterpreter_PrivateFieldAccess_AppliesViaInterpreterDispatch()
        {
            HotReloadRegistry.RegisterReloadableMethod(
                typeof(IlInterpreterPrivateReloadProbe).GetMethod(nameof(IlInterpreterPrivateReloadProbe.Compute)),
                new HotReloadWithOverridesAttribute());

            var editedSource = @"
using Unity.Pipeline.HotReload;
namespace Unity.Pipeline.Tests.Editor
{
    public class IlInterpreterPrivateReloadProbe
    {
        private int m_Secret;
        public int Secret => m_Secret;

        [HotReload]
        public void Compute()
        {
            m_Secret = 77;
        }
    }
}";
            var path = Path.Combine(Application.temporaryCachePath, "IlInterpreterPrivateReloadProbe_edit.cs");
            File.WriteAllText(path, editedSource);

            try
            {
                var response = HotReloadCommands.ReloadFileEditorInterpreter(path);
                Assert.IsTrue(response.Success,
                    $"reload_file_editor_interpreter should succeed for private field access. Error: {response.ErrorDetails}");

                var probe = new IlInterpreterPrivateReloadProbe();
                probe.Compute();

                Assert.AreEqual(77, probe.Secret,
                    "Edited [HotReload] body should write the private field via the interpreter dispatch");
            }
            finally
            {
                if (File.Exists(path)) File.Delete(path);
            }
        }

        [Test]
        public void ReloadFileInterpreter_PrivateMethodCall_AppliesViaInterpreterDispatch()
        {
            HotReloadRegistry.RegisterReloadableMethod(
                typeof(IlInterpreterPrivateMethodProbe).GetMethod(nameof(IlInterpreterPrivateMethodProbe.Compute)),
                new HotReloadWithOverridesAttribute());

            // If the interpreter can't resolve the private Helper it throws "Host method not registered"
            // and falls back (result stays 0); result==39 proves the private call dispatched.
            var editedSource = @"
using Unity.Pipeline.HotReload;
namespace Unity.Pipeline.Tests.Editor
{
    public class IlInterpreterPrivateMethodProbe
    {
        public int result;
        private int Helper(int x) => x * 3;

        [HotReload]
        public void Compute()
        {
            result = Helper(13);
        }
    }
}";
            var path = Path.Combine(Application.temporaryCachePath, "IlInterpreterPrivateMethodProbe_edit.cs");
            File.WriteAllText(path, editedSource);

            try
            {
                var response = HotReloadCommands.ReloadFileEditorInterpreter(path);
                Assert.IsTrue(response.Success,
                    $"reload_file_editor_interpreter should succeed for a private method call. Error: {response.ErrorDetails}");

                var probe = new IlInterpreterPrivateMethodProbe();
                probe.Compute();

                Assert.AreEqual(39, probe.result,
                    "Edited body should call the private Helper via the interpreter dispatch (13*3)");
            }
            finally
            {
                if (File.Exists(path)) File.Delete(path);
            }
        }

        [Test]
        public void ReloadFileInterpreter_EnumFieldSwitch_DispatchesViaInterpreter()
        {
            HotReloadRegistry.RegisterReloadableMethod(
                typeof(IlInterpreterEnumSwitchProbe).GetMethod(nameof(IlInterpreterEnumSwitchProbe.Compute)),
                new HotReloadWithOverridesAttribute());

            // m_Mode==Run, so case Run must set picked=20. If the enum field reads as a boxed
            // enum (the bug), no case matches and picked stays -1.
            var editedSource = @"
using Unity.Pipeline.HotReload;
namespace Unity.Pipeline.Tests.Editor
{
    public class IlInterpreterEnumSwitchProbe
    {
        private enum Mode { Idle, Run, Stop }
        private Mode m_Mode = Mode.Run;
        public int picked = -1;

        [HotReload]
        public void Compute()
        {
            switch (m_Mode)
            {
                case Mode.Idle: picked = 10; break;
                case Mode.Run: picked = 20; break;
                case Mode.Stop: picked = 30; break;
            }
        }
    }
}";
            var path = Path.Combine(Application.temporaryCachePath, "IlInterpreterEnumSwitchProbe_edit.cs");
            File.WriteAllText(path, editedSource);
            try
            {
                var response = HotReloadCommands.ReloadFileEditorInterpreter(path);
                Assert.IsTrue(response.Success, $"reload should succeed. Error: {response.ErrorDetails}");
                var probe = new IlInterpreterEnumSwitchProbe();
                probe.Compute();
                Assert.AreEqual(20, probe.picked,
                    "switch on the private enum field should hit case Run via the interpreter");
            }
            finally { if (File.Exists(path)) File.Delete(path); }
        }

        [Test]
        public void ReloadFileInterpreter_ReentrantCall_PreservesCallerFrame()
        {
            HotReloadRegistry.RegisterReloadableMethod(
                typeof(IlInterpreterReentrancyProbe).GetMethod(nameof(IlInterpreterReentrancyProbe.Compute)),
                new HotReloadWithOverridesAttribute());
            HotReloadRegistry.RegisterReloadableMethod(
                typeof(IlInterpreterReentrancyProbe).GetMethod(nameof(IlInterpreterReentrancyProbe.Inner)),
                new HotReloadWithOverridesAttribute());

            // Compute keeps locals a/b live across a call to Inner (itself an interpreter override, invoked
            // reentrantly via the host/woven dispatch). Inner uses its own locals; if the reentrant call ran
            // at frame base 0 it would clobber a/b and result != 11.
            var editedSource = @"
using Unity.Pipeline.HotReload;
namespace Unity.Pipeline.Tests.Editor
{
    public class IlInterpreterReentrancyProbe
    {
        public int result;
        public int innerRan;
        [Unity.Pipeline.HotReload.HotReload]
        public void Inner() { int x = 99; int y = x + 1; innerRan = y; }
        [Unity.Pipeline.HotReload.HotReload]
        public void Compute() { int a = 5; int b = 6; Inner(); result = a + b; }
    }
}";
            var path = Path.Combine(Application.temporaryCachePath, "IlInterpreterReentrancyProbe_edit.cs");
            File.WriteAllText(path, editedSource);
            try
            {
                var response = HotReloadCommands.ReloadFileEditorInterpreter(path);
                Assert.IsTrue(response.Success, $"reload should succeed. Error: {response.ErrorDetails}");
                var probe = new IlInterpreterReentrancyProbe();
                probe.Compute();
                Assert.AreEqual(100, probe.innerRan, "reentrant Inner override should have run");
                Assert.AreEqual(11, probe.result,
                    "caller frame (locals a,b) must survive the reentrant Inner call");
            }
            finally { if (File.Exists(path)) File.Delete(path); }
        }

        [Test]
        public void ReloadFileInterpreter_NewobjHostType_AppliesViaInterpreterDispatch()
        {
            HotReloadRegistry.RegisterReloadableMethod(
                typeof(IlInterpreterNewobjReloadProbe).GetMethod(nameof(IlInterpreterNewobjReloadProbe.Compute)),
                new HotReloadWithOverridesAttribute());

            // NavMeshPath is not in the standard binding, so the newobj token only resolves if the
            // interpreter registers referenced engine types (AllowReferencedTypes). If newobj or the
            // instance call fails, the override falls back and `made` stays -1; made==1 proves the
            // host-type construction dispatched.
            var editedSource = @"
using Unity.Pipeline.HotReload;
namespace Unity.Pipeline.Tests.Editor
{
    public class IlInterpreterNewobjReloadProbe
    {
        public int made = -1;

        [HotReload]
        public void Compute()
        {
            var p = new UnityEngine.AI.NavMeshPath();
            p.ClearCorners();
            made = 1;
        }
    }
}";
            var path = Path.Combine(Application.temporaryCachePath, "IlInterpreterNewobjReloadProbe_edit.cs");
            File.WriteAllText(path, editedSource);

            try
            {
                var response = HotReloadCommands.ReloadFileEditorInterpreter(path);
                Assert.IsTrue(response.Success,
                    $"reload_file_editor_interpreter should succeed for `new NavMeshPath()`. Error: {response.ErrorDetails}");

                var probe = new IlInterpreterNewobjReloadProbe();
                probe.Compute();

                Assert.AreEqual(1, probe.made,
                    "Edited body should construct NavMeshPath (newobj over a host type) and run via the interpreter");
            }
            finally
            {
                if (File.Exists(path)) File.Delete(path);
            }
        }

        [Test]
        public void ReloadFileInterpreter_NullableMembers_DispatchViaIntrinsics()
        {
            HotReloadRegistry.RegisterReloadableMethod(
                typeof(IlInterpreterNullableProbe).GetMethod(nameof(IlInterpreterNullableProbe.Compute)),
                new HotReloadWithOverridesAttribute());

            // Exercises every Nullable<T> shape the intrinsics model: HasValue, Value (local and
            // field receivers), `??` (GetValueOrDefault), a lifted null comparison, GetValueOrDefault
            // with and without a fallback, and a T→T? conversion (Nullable ctor via ldloca+call).
            var editedSource = @"
using Unity.Pipeline.HotReload;
namespace Unity.Pipeline.Tests.Editor
{
    public class IlInterpreterNullableProbe
    {
        public float? maybeFloat = 2.5f;
        public float? emptyFloat = null;
        public int? maybeInt = 7;
        public int? emptyInt = null;
        public float sum;
        public int result = -1;

        [HotReload]
        public void Compute()
        {
            float total = 0f;
            var f = maybeFloat;
            if (f.HasValue)
                total += f.Value;               // +2.5
            total += emptyFloat ?? 3.0f;        // +3.0 (null branch)
            float? made = 4.0f;                 // Nullable ctor
            if (made != null)                   // lifted comparison → HasValue
                total += made.Value;            // +4.0
            sum = total;                        // 9.5
            result = maybeInt.GetValueOrDefault() + emptyInt.GetValueOrDefault(2); // 7 + 2
        }
    }
}";
            var path = Path.Combine(Application.temporaryCachePath, "IlInterpreterNullableProbe_edit.cs");
            File.WriteAllText(path, editedSource);

            try
            {
                var response = HotReloadCommands.ReloadFileEditorInterpreter(path);
                Assert.IsTrue(response.Success, $"reload should succeed. Error: {response.ErrorDetails}");

                var probe = new IlInterpreterNullableProbe();
                probe.Compute();

                Assert.AreEqual(9.5f, probe.sum, 0.001f,
                    "HasValue/Value/??/ctor over float? should all dispatch via the interpreter");
                Assert.AreEqual(9, probe.result,
                    "GetValueOrDefault (0- and 1-arg) over int? should dispatch via the interpreter");
            }
            finally
            {
                if (File.Exists(path)) File.Delete(path);
            }
        }

        [Test]
        public void ReloadFileInterpreter_RuntimeError_ReportsSourceLineAndFile()
        {
            HotReloadRegistry.RegisterReloadableMethod(
                typeof(IlInterpreterLineInfoProbe).GetMethod(nameof(IlInterpreterLineInfoProbe.Compute)),
                new HotReloadWithOverridesAttribute());

            // The edited body dereferences a null host object on LINE 13 of this source (the
            // `made = p.corners.Length;` statement — line 1 is the empty line after the opening
            // quote). The interpreter's error must carry that line and the file name, resolved
            // from the embedded PDB whose #line directives map the body to the pushed file.
            var editedSource = @"
using Unity.Pipeline.HotReload;
namespace Unity.Pipeline.Tests.Editor
{
    public class IlInterpreterLineInfoProbe
    {
        public int made = -1;

        [HotReload]
        public void Compute()
        {
            UnityEngine.AI.NavMeshPath p = null;
            made = p.corners.Length;
        }
    }
}";
            var path = Path.Combine(Application.temporaryCachePath, "IlInterpreterLineInfoProbe_edit.cs");
            File.WriteAllText(path, editedSource);

            try
            {
                var response = HotReloadCommands.ReloadFileEditorInterpreter(path);
                Assert.IsTrue(response.Success, $"reload should succeed. Error: {response.ErrorDetails}");

                // The dispatch logs the interpreter error and falls back to the original body.
                LogAssert.Expect(LogType.Error, new Regex(
                    @"HotReload: Error invoking override for 'IlInterpreterLineInfoProbe\.Compute':.*" +
                    @"NullReferenceException.* at line 13 \(IlInterpreterLineInfoProbe_edit\.cs\)"));
                LogAssert.Expect(LogType.Error, new Regex("HotReload: Stack trace:"));

                var probe = new IlInterpreterLineInfoProbe();
                probe.Compute();

                Assert.AreEqual(0, probe.made,
                    "After the override throws, the original body should run as the fallback");
            }
            finally
            {
                if (File.Exists(path)) File.Delete(path);
            }
        }

        [Test]
        public void ReloadFileInterpreter_RuntimeError_WithLineInfoDisabled_ReportsIlOffset()
        {
            HotReloadRegistry.RegisterReloadableMethod(
                typeof(IlInterpreterLineInfoProbe).GetMethod(nameof(IlInterpreterLineInfoProbe.Compute)),
                new HotReloadWithOverridesAttribute());

            // Same failing body as the line-info test, but with the "Error Line Info" setting off:
            // no embedded PDB is shipped, so the error falls back to the raw IL offset.
            var editedSource = @"
using Unity.Pipeline.HotReload;
namespace Unity.Pipeline.Tests.Editor
{
    public class IlInterpreterLineInfoProbe
    {
        public int made = -1;

        [HotReload]
        public void Compute()
        {
            UnityEngine.AI.NavMeshPath p = null;
            made = p.corners.Length;
        }
    }
}";
            var path = Path.Combine(Application.temporaryCachePath, "IlInterpreterLineInfoProbe_noinfo_edit.cs");
            File.WriteAllText(path, editedSource);

            var previous = HotReloadCompiler.EmitSourceLineInfo;
            HotReloadCompiler.EmitSourceLineInfo = false;
            try
            {
                var response = HotReloadCommands.ReloadFileEditorInterpreter(path);
                Assert.IsTrue(response.Success, $"reload should succeed. Error: {response.ErrorDetails}");

                LogAssert.Expect(LogType.Error, new Regex(
                    @"HotReload: Error invoking override for 'IlInterpreterLineInfoProbe\.Compute':.*" +
                    @"NullReferenceException.* at IL\+0x"));
                LogAssert.Expect(LogType.Error, new Regex("HotReload: Stack trace:"));

                var probe = new IlInterpreterLineInfoProbe();
                probe.Compute();

                Assert.AreEqual(0, probe.made,
                    "After the override throws, the original body should run as the fallback");
            }
            finally
            {
                HotReloadCompiler.EmitSourceLineInfo = previous;
                if (File.Exists(path)) File.Delete(path);
            }
        }

        [Test]
        public void ReloadFileInterpreter_OverrideThrows_PropagatesOriginalException()
        {
            HotReloadRegistry.RegisterReloadableMethod(
                typeof(IlInterpreterExitGuiProbe).GetMethod(nameof(IlInterpreterExitGuiProbe.Compute)),
                new HotReloadWithOverridesAttribute());

            var editedSource = @"
using Unity.Pipeline.HotReload;
namespace Unity.Pipeline.Tests.Editor
{
    public class IlInterpreterExitGuiProbe
    {
        public int marker = -1;

        [HotReload]
        public void Compute()
        {
            marker = 42;
            UnityEngine.GUIUtility.ExitGUI();
        }
    }
}";
            var path = UnityEditor.FileUtil.GetUniqueTempPathInProject() + ".cs";
            File.WriteAllText(path, editedSource);

            try
            {
                var response = HotReloadCommands.ReloadFileEditorInterpreter(path);
                Assert.IsTrue(response.Success, $"reload should succeed. Error: {response.ErrorDetails}");

                var probe = new IlInterpreterExitGuiProbe();
                Assert.Throws<ExitGUIException>(() => probe.Compute(),
                    "The override body's exception must propagate with its type intact (IMGUI " +
                    "catches ExitGUIException by type), not be swallowed into a fallback");

                Assert.AreEqual(42, probe.marker,
                    "The override ran up to the throw, and the original body (marker = 0) must " +
                    "NOT run as a fallback");
            }
            finally
            {
                if (File.Exists(path)) File.Delete(path);
            }
        }

        [Test]
        public void ReloadFileInterpreter_NewHelperMethod_MatchesCompiledTwin()
        {
            HotReloadRegistry.RegisterReloadableMethod(
                typeof(IlInterpreterNewMethodProbe).GetMethod(nameof(IlInterpreterNewMethodProbe.Compute)),
                new HotReloadWithOverridesAttribute());

            // LiveHelper does not exist on the compiled type: the transform must co-emit it into
            // the override class so the changed Compute reaches it via call_script. Same body text
            // as CompiledHelper, so the compiled twin's output is the oracle.
            var editedSource = @"
using Unity.Pipeline.HotReload;
namespace Unity.Pipeline.Tests.Editor
{
    public class IlInterpreterNewMethodProbe
    {
        public int result;

        public int CompiledHelper(int x)
        {
            int acc = 0;
            for (int i = 1; i <= x; i++)
                acc += i * x - 1;
            return acc + x / 2;
        }

        [HotReload]
        public void Compute()
        {
            result = LiveHelper(9);
        }

        int LiveHelper(int x)
        {
            int acc = 0;
            for (int i = 1; i <= x; i++)
                acc += i * x - 1;
            return acc + x / 2;
        }
    }
}";
            var path = Path.Combine(Application.temporaryCachePath, "IlInterpreterNewMethodProbe_edit.cs");
            File.WriteAllText(path, editedSource);

            try
            {
                var response = HotReloadCommands.ReloadFileEditorInterpreter(path);
                Assert.IsTrue(response.Success,
                    $"reload with a live-added helper should succeed. Error: {response.ErrorDetails}");

                var probe = new IlInterpreterNewMethodProbe();
                probe.Compute();

                Assert.AreEqual(probe.CompiledHelper(9), probe.result,
                    "The live-added LiveHelper must produce the same output as its compiled twin");
            }
            finally
            {
                if (File.Exists(path)) File.Delete(path);
            }
        }

        [Test]
        public void ReloadFileInterpreter_NewClickMethodWiredToCallback_Fires()
        {
            HotReloadRegistry.RegisterReloadableMethod(
                typeof(IlInterpreterClickProbe).GetMethod(nameof(IlInterpreterClickProbe.Compute)),
                new HotReloadWithOverridesAttribute());

            // Click does not exist compiled; the reloaded body subscribes it as a method group.
            // The transform wraps it in a lambda over the co-emitted static, and the interpreter
            // creates a real Action that re-enters the VM when the host fires it.
            var editedSource = @"
using Unity.Pipeline.HotReload;
namespace Unity.Pipeline.Tests.Editor
{
    public class IlInterpreterClickProbe
    {
        public int clicks;
        public event System.Action Clicked;
        public void Fire() => Clicked?.Invoke();

        [HotReload]
        public void Compute()
        {
            clicks = 0;
            Clicked += Click;
        }

        void Click()
        {
            clicks = clicks + 1;
        }
    }
}";
            var path = Path.Combine(Application.temporaryCachePath, "IlInterpreterClickProbe_edit.cs");
            File.WriteAllText(path, editedSource);

            try
            {
                var response = HotReloadCommands.ReloadFileEditorInterpreter(path);
                Assert.IsTrue(response.Success,
                    $"reload wiring a live-added Click method should succeed. Error: {response.ErrorDetails}");

                var probe = new IlInterpreterClickProbe();
                probe.Compute();
                probe.Fire();
                probe.Fire();

                Assert.AreEqual(2, probe.clicks,
                    "The live-added Click must run interpreted each time the host raises the event");
            }
            finally
            {
                if (File.Exists(path)) File.Delete(path);
            }
        }

        [Test]
        public void ReloadFileInterpreter_NewTaggedMethod_DoesNotPoisonFile()
        {
            HotReloadRegistry.RegisterReloadableMethod(
                typeof(IlInterpreterNewTaggedProbe).GetMethod(nameof(IlInterpreterNewTaggedProbe.Compute)),
                new HotReloadWithOverridesAttribute());

            // `Added` is a NEW [HotReload] method, declared FIRST so it is names[0] at registration.
            // It has no woven prologue and can't register — but Compute's override must still apply.
            var editedSource = @"
using Unity.Pipeline.HotReload;
namespace Unity.Pipeline.Tests.Editor
{
    public class IlInterpreterNewTaggedProbe
    {
        public int result;

        [HotReload]
        public void Added()
        {
            result = 1000;
        }

        [HotReload]
        public void Compute()
        {
            result = 7;
        }
    }
}";
            var path = Path.Combine(Application.temporaryCachePath, "IlInterpreterNewTaggedProbe_edit.cs");
            File.WriteAllText(path, editedSource);

            try
            {
                var response = HotReloadCommands.ReloadFileEditorInterpreter(path);
                Assert.IsTrue(response.Success,
                    $"reload should succeed with the new method skipped, not poison the file. Error: {response.ErrorDetails}");

                var probe = new IlInterpreterNewTaggedProbe();
                probe.Compute();

                Assert.AreEqual(7, probe.result,
                    "Compute's override must register even though the new 'Added' method can't");
            }
            finally
            {
                if (File.Exists(path)) File.Delete(path);
            }
        }

        [Test]
        public void ReloadFileInterpreter_HostValueTypeArray_AppliesViaInterpreterDispatch()
        {
            HotReloadRegistry.RegisterReloadableMethod(
                typeof(IlInterpreterNewobjReloadProbe).GetMethod(nameof(IlInterpreterNewobjReloadProbe.Compute)),
                new HotReloadWithOverridesAttribute());

            // NavMeshPath.corners is a Vector3[] — a host-returned VALUE-TYPE array (not the interpreter's
            // object?[]). Reading .Length exercises ldlen over a real typed array, which used to throw
            // InvalidCastException (the (object?[]) cast fails for value-type arrays). An empty path → 0.
            var editedSource = @"
using Unity.Pipeline.HotReload;
namespace Unity.Pipeline.Tests.Editor
{
    public class IlInterpreterNewobjReloadProbe
    {
        public int made = -1;

        [HotReload]
        public void Compute()
        {
            var p = new UnityEngine.AI.NavMeshPath();
            made = p.corners.Length;
        }
    }
}";
            var path = Path.Combine(Application.temporaryCachePath, "IlInterpreterVtArrayProbe_edit.cs");
            File.WriteAllText(path, editedSource);

            try
            {
                var response = HotReloadCommands.ReloadFileEditorInterpreter(path);
                Assert.IsTrue(response.Success,
                    $"reload_file_editor_interpreter should succeed for a host Vector3[] read. Error: {response.ErrorDetails}");

                var probe = new IlInterpreterNewobjReloadProbe();
                probe.Compute();

                Assert.AreEqual(0, probe.made,
                    "ldlen over a host Vector3[] should run via the interpreter (empty NavMeshPath → 0 corners)");
            }
            finally
            {
                if (File.Exists(path)) File.Delete(path);
            }
        }
    }
}
