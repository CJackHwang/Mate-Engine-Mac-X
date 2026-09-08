using System.Collections;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using Unity.Pipeline.HotReload;
using Unity.Pipeline.Runtime.Commands;
using UnityEngine;

namespace Unity.Pipeline.Tests.Editor
{
    /// <summary>
    /// Probe with a method-level [HotReload] coroutine. The IL weaver routes IEnumerator
    /// returns through TryInvokeHotReloadResult (like any value-returning method), so a
    /// registered override supplies the iterator that the caller — in Unity, StartCoroutine —
    /// actually drives.
    /// </summary>
    public class CoroutineReloadProbe
    {
        public int originalRuns;
        public int hotRuns;
        public int touched;

        [HotReload]
        public IEnumerator Run()
        {
            originalRuns++; // original body
            yield return null;
        }

        [HotReload]
        public void Touch()
        {
            touched++; // original body
        }
    }

    static class CoroutineOverrides
    {
        [HotReloadOverrideMethod("CoroutineReloadProbe.Run")]
        public static IEnumerator Run(CoroutineReloadProbe instance)
        {
            instance.hotRuns++;
            yield return null;
        }
    }

    /// <summary>
    /// Probe for the INTERPRETER backend: the reloaded coroutine body runs as a IlInterpreter
    /// state machine behind the ScriptEnumerator bridge (what an IL2CPP player does).
    /// </summary>
    public class IlInterpreterCoroutineProbe
    {
        public int originalSteps;
        public int hotSteps;

        [HotReload]
        public IEnumerator Run()
        {
            originalSteps++; // original body
            yield return null;
        }
    }

    /// <summary>
    /// Coroutine hot reload: `[HotReload] IEnumerator` methods weave a result dispatch at
    /// iterator CREATION, so each new StartCoroutine-style call picks up the active override
    /// while in-flight iterators keep running the code they were created from.
    /// </summary>
    class HotReloadCoroutineTests
    {
        [SetUp]
        public void Setup() => HotReloadRegistry.ClearAllForTesting();

        [TearDown]
        public void TearDown() => HotReloadRegistry.ClearAllForTesting();

        [Test]
        public void Transform_CoroutineBody_EmitsIteratorOverride_KeepingYields()
        {
            const string coroutineSource = @"
using System.Collections;
using UnityEngine;
using Unity.Pipeline.HotReload;

public class CoDemo : MonoBehaviour
{
    public int step;

    [HotReload]
    IEnumerator Run()
    {
        step = 1;
        yield return null;
        step = 2;
    }
}";
            // The transform lifts the body into a static method with the same IEnumerator
            // return type; the yields survive, so the override compiles as a normal C# iterator.
            var bodies = new Dictionary<string, string> { ["Run"] = "" };
            var output = SourceCodeTransformer.TransformMethodBodies(
                bodies, "CoDemo", new Dictionary<string, MethodSignatureInfo>(), coroutineSource);

            StringAssert.Contains("[HotReloadOverrideMethod(\"CoDemo.Run\")]", output);
            StringAssert.Contains("public static IEnumerator Run(CoDemo instance", output);
            StringAssert.Contains("yield return null", output);
            StringAssert.Contains("instance.step", output);
        }

        [Test]
        public void WovenDispatch_RoutesNewIteratorsToOverride()
        {
            HotReloadRegistry.RegisterReloadableMethod(
                typeof(CoroutineReloadProbe).GetMethod(nameof(CoroutineReloadProbe.Run)),
                new HotReloadWithOverridesAttribute());

            var registered = HotReloadRegistry.RegisterMethodOverride(
                typeof(CoroutineOverrides).GetMethod(nameof(CoroutineOverrides.Run)),
                new HotReloadOverrideMethodAttribute("CoroutineReloadProbe.Run"),
                typeof(CoroutineOverrides),
                out var reason);
            Assert.IsTrue(registered, reason);

            // Run() was woven (IEnumerator returns go through TryInvokeHotReloadResult), so the
            // returned iterator IS the override's state machine.
            var probe = new CoroutineReloadProbe();
            var routine = probe.Run();
            Assert.IsTrue(routine.MoveNext()); // iterators are lazy — body runs on first MoveNext

            Assert.AreEqual(0, probe.originalRuns, "Original body must not run while an override is active");
            Assert.AreEqual(1, probe.hotRuns, "Override iterator should have run");
        }

        [Test]
        public void WovenDispatch_InFlightIteratorKeepsOldBody_NextCallGetsNew()
        {
            HotReloadRegistry.RegisterReloadableMethod(
                typeof(CoroutineReloadProbe).GetMethod(nameof(CoroutineReloadProbe.Run)),
                new HotReloadWithOverridesAttribute());

            var probe = new CoroutineReloadProbe();
            var inFlight = probe.Run(); // created BEFORE the override lands

            HotReloadRegistry.RegisterMethodOverride(
                typeof(CoroutineOverrides).GetMethod(nameof(CoroutineOverrides.Run)),
                new HotReloadOverrideMethodAttribute("CoroutineReloadProbe.Run"),
                typeof(CoroutineOverrides),
                out _);

            inFlight.MoveNext();
            Assert.AreEqual(1, probe.originalRuns, "In-flight iterator keeps the body it was created from");
            Assert.AreEqual(0, probe.hotRuns);

            var next = probe.Run(); // created AFTER — dispatches to the override
            next.MoveNext();
            Assert.AreEqual(1, probe.originalRuns);
            Assert.AreEqual(1, probe.hotRuns, "Next iterator creation should route to the override");
        }

        [Test]
        public void ReloadFileInPlace_CoroutineOnlyFile_Succeeds()
        {
            HotReloadRegistry.RegisterReloadableMethod(
                typeof(CoroutineReloadProbe).GetMethod(nameof(CoroutineReloadProbe.Run)),
                new HotReloadWithOverridesAttribute());

            var editedSource = @"
using System.Collections;
using Unity.Pipeline.HotReload;
namespace Unity.Pipeline.Tests.Editor
{
    public class CoroutineReloadProbe
    {
        public int originalRuns;
        public int hotRuns;

        [HotReload]
        public IEnumerator Run()
        {
            hotRuns = 99;
            yield return null;
        }
    }
}";
            var path = Path.Combine(Application.temporaryCachePath, "CoroutineReloadProbe_edit.cs");
            File.WriteAllText(path, editedSource);

            try
            {
                var response = HotReloadCommands.ReloadFile(path);
                Assert.IsTrue(response.Success,
                    $"reload_file should succeed for a coroutine-only file. Error: {response.ErrorDetails}");

                var probe = new CoroutineReloadProbe();
                var routine = probe.Run();
                routine.MoveNext(); // iterators are lazy — body only runs on first MoveNext
                Assert.AreEqual(0, probe.originalRuns, "Original body must not run after reload");
                Assert.AreEqual(99, probe.hotRuns, "Edited coroutine body should run via the woven dispatch");
            }
            finally
            {
                if (File.Exists(path)) File.Delete(path);
            }
        }

        [Test]
        public void ReloadFileInPlace_CoroutineNextToVoidMethod_ReloadsBoth()
        {
            HotReloadRegistry.RegisterReloadableMethod(
                typeof(CoroutineReloadProbe).GetMethod(nameof(CoroutineReloadProbe.Run)),
                new HotReloadWithOverridesAttribute());
            HotReloadRegistry.RegisterReloadableMethod(
                typeof(CoroutineReloadProbe).GetMethod(nameof(CoroutineReloadProbe.Touch)),
                new HotReloadWithOverridesAttribute());

            var editedSource = @"
using System.Collections;
using Unity.Pipeline.HotReload;
namespace Unity.Pipeline.Tests.Editor
{
    public class CoroutineReloadProbe
    {
        public int originalRuns;
        public int hotRuns;
        public int touched;

        [HotReload]
        public IEnumerator Run()
        {
            hotRuns = 99;
            yield return null;
        }

        [HotReload]
        public void Touch()
        {
            touched = 42;
        }
    }
}";
            var path = Path.Combine(Application.temporaryCachePath, "CoroutineReloadProbe_mixed_edit.cs");
            File.WriteAllText(path, editedSource);

            try
            {
                var result = InPlaceReloadProcessor.ProcessSourceFileOnMainThread(path);

                Assert.IsTrue(result.Success, $"Mixed file should reload. Error: {result.ErrorMessage}");
                CollectionAssert.Contains(result.ExtractedMethods, "Touch");
                CollectionAssert.Contains(result.ExtractedMethods, "Run");

                var probe = new CoroutineReloadProbe();
                probe.Touch();
                Assert.AreEqual(42, probe.touched, "Edited void body should run via the woven dispatch");

                var routine = probe.Run();
                routine.MoveNext();
                Assert.AreEqual(0, probe.originalRuns);
                Assert.AreEqual(99, probe.hotRuns, "Edited coroutine body should run via the woven dispatch");
            }
            finally
            {
                if (File.Exists(path)) File.Delete(path);
            }
        }

        [Test]
        public void ReloadFileInterpreter_Coroutine_RunsAsInterpretedStateMachine()
        {
            // The IL2CPP-shaped path: the edited coroutine compiles to a state machine the
            // IlInterpreter VM interprets; the woven dispatch returns a ScriptEnumerator bridge
            // that the caller (in Unity, StartCoroutine) drives like any IEnumerator.
            HotReloadRegistry.RegisterReloadableMethod(
                typeof(IlInterpreterCoroutineProbe).GetMethod(nameof(IlInterpreterCoroutineProbe.Run)),
                new HotReloadWithOverridesAttribute());

            var editedSource = @"
using System.Collections;
using Unity.Pipeline.HotReload;
namespace Unity.Pipeline.Tests.Editor
{
    public class IlInterpreterCoroutineProbe
    {
        public int originalSteps;
        public int hotSteps;

        [HotReload]
        public IEnumerator Run()
        {
            hotSteps = 1;
            yield return null;
            hotSteps = 2;
            yield return new UnityEngine.WaitForSeconds(0.25f);
            hotSteps = 3;
        }
    }
}";
            var path = Path.Combine(Application.temporaryCachePath, "IlInterpreterCoroutineProbe_edit.cs");
            File.WriteAllText(path, editedSource);

            try
            {
                var response = HotReloadCommands.ReloadFileEditorInterpreter(path);
                Assert.IsTrue(response.Success,
                    $"reload_file_editor_interpreter should succeed for a coroutine. Error: {response.ErrorDetails}");

                var probe = new IlInterpreterCoroutineProbe();
                var routine = probe.Run(); // woven dispatch → interpreter → bridge

                Assert.IsTrue(routine.MoveNext());
                Assert.IsNull(routine.Current, "yield return null must surface as a null step");
                Assert.AreEqual(1, probe.hotSteps);

                Assert.IsTrue(routine.MoveNext());
                Assert.AreEqual(2, probe.hotSteps);
                Assert.IsInstanceOf<WaitForSeconds>(routine.Current,
                    "a host object yielded from the interpreted body must cross the bridge intact");

                Assert.IsFalse(routine.MoveNext());
                Assert.AreEqual(3, probe.hotSteps);
                Assert.AreEqual(0, probe.originalSteps, "original compiled body must not have run");
            }
            finally
            {
                if (File.Exists(path)) File.Delete(path);
            }
        }
    }
}
