using System;
using System.Collections;
using NUnit.Framework;
using Unity.Pipeline.Compilation;
using Unity.Pipeline.Config;
using UnityEngine;
using UnityEngine.TestTools;

namespace Unity.Pipeline.Tests.Runtime
{
    /// <summary>
    /// PlayMode paths for the IlInterpreter interpreter: interpreter execution while the player ticks, and
    /// the <see cref="RuntimePipelineDriver"/> MonoBehaviour lifecycle alongside it. These same tests,
    /// run in an IL2CPP standalone player, are the AOT payload (see <see cref="IlInterpreterAotSmokeTests"/>).
    /// </summary>
    class IlInterpreterPlayModeTests
    {
        // Compile a statement body and run it through the interpreter directly, against the shared host
        // binding (IlInterpreterHostBindings.CreateStandard) — the IL2CPP-safe path hot reload uses in the
        // player (no Assembly.Load).
        static object RunViaInterpreter(string body)
        {
            var source = $@"using UnityEngine;
public static class Probe {{ public static object Run() {{ {body} }} }}";

            var compile = RoslynCompilationService.Compile(new CompilationRequest
            {
                SourceCode = source,
                AssemblyName = "IlInterpreterPlayModeProbe",
                SkipLoad = true,
            });
            Assert.IsTrue(compile.Success, "probe source should compile");

            using var interp = new IlInterpreter.Interpreter.ScriptInterpreter(IlInterpreterHostBindings.CreateStandard());
            interp.Load(new RawScript(compile.AssemblyBytes));
            return interp.Invoke("Run");
        }

        [UnityTest]
        public IEnumerator Interpreter_RunsAcrossFrames()
        {
            yield return null; // ensure we're a frame into play mode

            var result = RunViaInterpreter("return 6 * 7;");

            Assert.AreEqual(42, result);
        }

        [UnityTest]
        public IEnumerator Interpreter_WorksWhileManagerTicks()
        {
            var config = ScriptableObject.CreateInstance<RuntimePipelineConfig>();
            config.enableInBuilds = true;
            config.autoStart = false; // no server needed; we only need the MonoBehaviour lifecycle ticking

            var go = new GameObject("IlInterpreterRuntimeManager");
            var driver = go.AddComponent<RuntimePipelineDriver>();
            driver.Initialize(config, null);
            go.SetActive(true);

            // Let Awake/Start/Update run.
            yield return null;
            yield return null;

            var result = RunViaInterpreter("var v = new Vector3(2f, 3f, 6f); return v.x + v.y + v.z;");

            Assert.AreEqual(11f, (float)result, 1e-4f);

            UnityEngine.Object.Destroy(go);
        }

        /// <summary>Minimal IScript over raw Roslyn-emitted PE bytes.</summary>
        sealed class RawScript : IlInterpreter.IScript
        {
            public RawScript(byte[] bytes) { Il = bytes; }
            public string Name => "IlInterpreterPlayModeProbe";
            public ReadOnlyMemory<byte> Il { get; }
        }
    }
}
