using System.Collections.Generic;
using NUnit.Framework;
using Unity.Pipeline.Compilation;

namespace Unity.Pipeline.Tests.Editor
{
    /// <summary>
    /// Push-time host-surface validation
    /// (<see cref="InterpreterHotReloadExecutor.ValidateBindingSurface"/>): the editor-side load
    /// pass that reports member refs which would resolve to throwing stubs on the player, so
    /// reload_file_player_interpreter surfaces binding gaps synchronously in its response instead of after the async
    /// device ack (which carries only a count, with the member list buried in the device log).
    /// </summary>
    class PushBindingValidationTests
    {
        static byte[] CompileProbe(string body)
        {
            var compile = RoslynCompilationService.Compile(new CompilationRequest
            {
                SourceCode = $@"using UnityEngine;
public static class Probe {{ public static void Run() {{ {body} }} }}",
                AssemblyName = "PushValidationProbe",
                SkipLoad = true, // validation walks the bytes; no Assembly.Load
            });
            Assert.IsTrue(compile.Success, "probe source should compile");
            return compile.AssemblyBytes;
        }

        static readonly List<string> ProbeMethods = new List<string> { "Run" };

        [Test]
        public void UnregisteredBclMember_IsReportedBeforePush()
        {
            // Environment.ProcessorCount has no AllowBcl shim (TickCount gained one), and the
            // auto-bind policy declines System.* — exactly the class of gap that used to surface
            // only as a mid-play ScriptRuntimeException on the device.
            var bytes = CompileProbe("Debug.Log(System.Environment.ProcessorCount);");
            var unbound = InterpreterHotReloadExecutor.ValidateBindingSurface(bytes, "Probe", ProbeMethods, out _);
            Assert.That(unbound, Has.Some.Contains("Environment.get_ProcessorCount"),
                "the unregistered BCL member should be named in the validation result");
        }

        [Test]
        public void StandardSurface_IncludingTypeEquality_ReportsNothing()
        {
            // The gameplay pattern that motivated this check (GetType() comparison via
            // Type.op_Equality) plus ordinary standard-surface calls: a clean result here is what
            // reload_file_player_interpreter now guarantees before sending.
            var bytes = CompileProbe(
                "GameObject a = new GameObject(\"push-validate\"); " +
                "bool same = a.GetType() == typeof(GameObject); Debug.Log(same);");
            var unbound = InterpreterHotReloadExecutor.ValidateBindingSurface(bytes, "Probe", ProbeMethods, out var note);
            Assert.IsEmpty(unbound, $"unexpected unbound members (note: {note ?? "none"})");
        }
    }
}
