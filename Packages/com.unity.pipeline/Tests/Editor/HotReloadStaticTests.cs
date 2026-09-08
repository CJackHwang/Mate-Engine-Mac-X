using System.Collections;
using System.IO;
using NUnit.Framework;
using Unity.Pipeline.HotReload;
using Unity.Pipeline.Runtime.Commands;
using UnityEngine;

namespace Unity.Pipeline.Tests.Editor
{
    /// <summary>
    /// Probe with method-level [HotReload] STATIC methods. The weaver injects the same dispatch
    /// prologue as for instance methods, passing null as the instance argument; the override
    /// keeps the leading instance parameter and receives null.
    /// </summary>
    public class StaticReloadProbe
    {
        public static int baseline;
        public static int hotValue;
        public int instanceValue;

        [HotReload]
        public static void Compute()
        {
            baseline++; // original body
        }

        [HotReload]
        public static void Mark(StaticReloadProbe target)
        {
            target.instanceValue = 1; // original body
        }

        [HotReload]
        public static IEnumerator Countdown(StaticReloadProbe target)
        {
            target.instanceValue = -1; // original body
            yield return null;
        }
    }

    static class StaticOverrides
    {
        [HotReloadOverrideMethod("StaticReloadProbe.Compute")]
        public static void Compute(StaticReloadProbe instance) // instance arrives null
        {
            StaticReloadProbe.hotValue++;
        }
    }

    /// <summary>
    /// Static method hot reload: woven dispatch with a null instance, end-to-end reload_file on
    /// the compiled (Assembly.Load) and interpreter (IlInterpreter) backends, and the static +
    /// coroutine combination.
    /// </summary>
    class HotReloadStaticTests
    {
        [SetUp]
        public void Setup()
        {
            HotReloadRegistry.ClearAllForTesting();
            StaticReloadProbe.baseline = 0;
            StaticReloadProbe.hotValue = 0;
        }

        [TearDown]
        public void TearDown() => HotReloadRegistry.ClearAllForTesting();

        static void RegisterProbeMethod(string name) =>
            HotReloadRegistry.RegisterReloadableMethod(
                typeof(StaticReloadProbe).GetMethod(name),
                new HotReloadWithOverridesAttribute());

        [Test]
        public void WovenDispatch_StaticMethod_RoutesToOverride_WithNullInstance()
        {
            RegisterProbeMethod(nameof(StaticReloadProbe.Compute));

            var registered = HotReloadRegistry.RegisterMethodOverride(
                typeof(StaticOverrides).GetMethod(nameof(StaticOverrides.Compute)),
                new HotReloadOverrideMethodAttribute("StaticReloadProbe.Compute"),
                typeof(StaticOverrides),
                out var reason);
            Assert.IsTrue(registered, reason);

            StaticReloadProbe.Compute();

            Assert.AreEqual(0, StaticReloadProbe.baseline, "Original body must not run while an override is active");
            Assert.AreEqual(1, StaticReloadProbe.hotValue, "Override should have run (with a null instance)");
        }

        [Test]
        public void ReloadFileCompiled_StaticMethod_AppliesEdit()
        {
            RegisterProbeMethod(nameof(StaticReloadProbe.Compute));

            var editedSource = @"
using Unity.Pipeline.HotReload;
namespace Unity.Pipeline.Tests.Editor
{
    public class StaticReloadProbe
    {
        public static int baseline;
        public static int hotValue;

        [HotReload]
        public static void Compute()
        {
            hotValue = 99;
        }
    }
}";
            var path = Path.Combine(Application.temporaryCachePath, "StaticReloadProbe_edit.cs");
            File.WriteAllText(path, editedSource);

            try
            {
                var response = HotReloadCommands.ReloadFile(path);
                Assert.IsTrue(response.Success,
                    $"reload_file should succeed for a static method. Error: {response.ErrorDetails}");

                StaticReloadProbe.Compute();
                Assert.AreEqual(99, StaticReloadProbe.hotValue, "Edited static body should run via the woven dispatch");
                Assert.AreEqual(0, StaticReloadProbe.baseline, "Original body must not run");
            }
            finally
            {
                if (File.Exists(path)) File.Delete(path);
            }
        }

        [Test]
        public void ReloadFileInterpreter_StaticMethod_WritesThroughParameter()
        {
            RegisterProbeMethod(nameof(StaticReloadProbe.Mark));

            var editedSource = @"
using Unity.Pipeline.HotReload;
namespace Unity.Pipeline.Tests.Editor
{
    public class StaticReloadProbe
    {
        public int instanceValue;

        [HotReload]
        public static void Mark(StaticReloadProbe target)
        {
            target.instanceValue = 42;
        }
    }
}";
            var path = Path.Combine(Application.temporaryCachePath, "StaticReloadProbe_mark_edit.cs");
            File.WriteAllText(path, editedSource);

            try
            {
                var response = HotReloadCommands.ReloadFileEditorInterpreter(path);
                Assert.IsTrue(response.Success,
                    $"reload_file_editor_interpreter should succeed for a static method. Error: {response.ErrorDetails}");

                var probe = new StaticReloadProbe();
                StaticReloadProbe.Mark(probe);
                Assert.AreEqual(42, probe.instanceValue,
                    "Edited static body should run via the interpreter dispatch (null instance, real args)");
            }
            finally
            {
                if (File.Exists(path)) File.Delete(path);
            }
        }

        [Test]
        public void ReloadFileInterpreter_StaticMethod_WritesStaticField()
        {
            RegisterProbeMethod(nameof(StaticReloadProbe.Compute));

            var editedSource = @"
using Unity.Pipeline.HotReload;
namespace Unity.Pipeline.Tests.Editor
{
    public class StaticReloadProbe
    {
        public static int baseline;
        public static int hotValue;

        [HotReload]
        public static void Compute()
        {
            hotValue = 77;
        }
    }
}";
            var path = Path.Combine(Application.temporaryCachePath, "StaticReloadProbe_sfield_edit.cs");
            File.WriteAllText(path, editedSource);

            try
            {
                var response = HotReloadCommands.ReloadFileEditorInterpreter(path);
                Assert.IsTrue(response.Success,
                    $"reload_file_editor_interpreter should succeed. Error: {response.ErrorDetails}");

                StaticReloadProbe.Compute();
                Assert.AreEqual(77, StaticReloadProbe.hotValue,
                    "Edited static body should write the host static field via the interpreter");
                Assert.AreEqual(0, StaticReloadProbe.baseline, "Original body must not run");
            }
            finally
            {
                if (File.Exists(path)) File.Delete(path);
            }
        }

        [Test]
        public void ReloadFileInterpreter_StaticCoroutine_RunsAsInterpretedStateMachine()
        {
            RegisterProbeMethod(nameof(StaticReloadProbe.Countdown));

            var editedSource = @"
using System.Collections;
using Unity.Pipeline.HotReload;
namespace Unity.Pipeline.Tests.Editor
{
    public class StaticReloadProbe
    {
        public int instanceValue;

        [HotReload]
        public static IEnumerator Countdown(StaticReloadProbe target)
        {
            target.instanceValue = 1;
            yield return null;
            target.instanceValue = 2;
        }
    }
}";
            var path = Path.Combine(Application.temporaryCachePath, "StaticReloadProbe_coro_edit.cs");
            File.WriteAllText(path, editedSource);

            try
            {
                var response = HotReloadCommands.ReloadFileEditorInterpreter(path);
                Assert.IsTrue(response.Success,
                    $"reload_file_editor_interpreter should succeed for a static coroutine. Error: {response.ErrorDetails}");

                var probe = new StaticReloadProbe();
                var routine = StaticReloadProbe.Countdown(probe); // woven dispatch → interpreter → bridge

                Assert.IsTrue(routine.MoveNext());
                Assert.AreEqual(1, probe.instanceValue);
                Assert.IsFalse(routine.MoveNext());
                Assert.AreEqual(2, probe.instanceValue,
                    "Static coroutine edit should run as an interpreted state machine");
            }
            finally
            {
                if (File.Exists(path)) File.Delete(path);
            }
        }
    }
}
