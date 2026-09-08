using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using NUnit.Framework;
using Newtonsoft.Json.Linq;
using Unity.Pipeline;
using Unity.Pipeline.Editor.Commands.Scripts;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Unity.Pipeline.Tests.Editor.Scripts
{
    /// <summary>
    /// Tests for the run_script command (AUTHAPI-26): single-file in-memory compile + named entry
    /// point execution, exercised directly and via PipelineClient. Covers the ticket's acceptance
    /// criteria: a valid entry runs and returns its result, compile errors return Roslyn diagnostics
    /// without executing, missing/ambiguous entry names error clearly, missing required params error,
    /// arg coercion (int/float/enum/string[]/ObjectRef), dry_run, defines, and hotpatch delegation.
    ///
    /// The scripts are written to a temp folder OUTSIDE Assets/ on purpose: run_script must compile
    /// them in memory with no asset import and no domain reload.
    /// </summary>
    class RunScriptCommandTests
    {
        private readonly List<string> m_TempFiles = new List<string>();
        private readonly List<Object> m_Spawned = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            foreach (var path in m_TempFiles)
            {
                try { if (File.Exists(path)) File.Delete(path); } catch { /* best effort */ }
            }
            m_TempFiles.Clear();

            foreach (var o in m_Spawned)
                if (o != null) Object.DestroyImmediate(o);
            m_Spawned.Clear();
        }

        private string WriteTempScript(string source)
        {
            var path = Path.Combine(Path.GetTempPath(), "run_script_test_" + System.Guid.NewGuid().ToString("N") + ".cs");
            File.WriteAllText(path, source);
            m_TempFiles.Add(path);
            return path;
        }

        private GameObject TrackGo(GameObject go)
        {
            m_Spawned.Add(go);
            return go;
        }

        #region Valid entry point

        [Test]
        public async Task RunScript_SinglePublicStatic_AutoDetectsAndReturnsResult()
        {
            var file = WriteTempScript(@"
public static class Builder
{
    public static int Build() { return 42; }
}");
            var r = await RunScriptCommand.RunScript(file);

            Assert.IsTrue(r.Success, r.Error + " / " + r.ErrorDetails);
            Assert.AreEqual(42, r.Result);
            // A warm Roslyn compile of a trivial file can complete in under 1ms — don't flake on it.
            Assert.GreaterOrEqual(r.CompileMs, 0, "compileMs should be recorded");
            Assert.IsNotNull(r.AssemblyName);
        }

        [Test]
        public async Task RunScript_ExplicitQualifiedEntry_Runs()
        {
            var file = WriteTempScript(@"
namespace My.Space
{
    public static class Tools
    {
        public static string Greet() { return ""hi""; }
        public static int Other() { return 1; }
    }
}");
            var r = await RunScriptCommand.RunScript(file, entry: "My.Space.Tools.Greet");

            Assert.IsTrue(r.Success, r.Error + " / " + r.ErrorDetails);
            Assert.AreEqual("hi", r.Result);
        }

        [Test]
        public async Task RunScript_UnityApiInEntry_Works()
        {
            var file = WriteTempScript(@"
using UnityEngine;
public static class Builder
{
    public static string Build() { return Application.unityVersion; }
}");
            var r = await RunScriptCommand.RunScript(file);

            Assert.IsTrue(r.Success, r.Error + " / " + r.ErrorDetails);
            Assert.IsInstanceOf<string>(r.Result);
        }

        #endregion

        #region Compile errors and dry_run

        [Test]
        public async Task RunScript_CompileError_ReturnsDiagnostics_NothingExecutes()
        {
            var file = WriteTempScript(@"
public static class Builder
{
    public static int Build() { return 2 +; }  // syntax error
}");
            var r = await RunScriptCommand.RunScript(file);

            Assert.IsFalse(r.Success);
            Assert.AreEqual("Compilation Failed", r.Error);
            Assert.Greater(r.Diagnostics.Count, 0, "should surface Roslyn diagnostics");
            Assert.IsNotEmpty(r.Diagnostics[0].Id, "diagnostic should carry a Roslyn id (e.g. CS1525)");
            Assert.AreEqual(0, r.ExecuteMs, "nothing should have executed");
        }

        [Test]
        public async Task RunScript_DryRun_CompilesWithoutExecuting()
        {
            var file = WriteTempScript(@"
public static class Builder
{
    public static int Build() { throw new System.Exception(""should not run""); }
}");
            var r = await RunScriptCommand.RunScript(file, dryRun: true);

            Assert.IsTrue(r.Success, r.Error + " / " + r.ErrorDetails);
            Assert.IsNull(r.Result, "dry_run must not execute the entry");
            Assert.AreEqual(0, r.ExecuteMs);
            // dry_run is emit-only: the compiled assembly is never loaded into the domain (Mono
            // can't unload it), so there is no assembly name to report either.
            Assert.IsNull(r.AssemblyName, "dry_run must not load (or name) an assembly");
        }

        [Test]
        public async Task RunScript_DryRun_CompileError_ReturnsSameDiagnostics()
        {
            var file = WriteTempScript(@"
public static class Builder
{
    public static int Build() { return 2 +; }
}");
            var r = await RunScriptCommand.RunScript(file, dryRun: true);

            Assert.IsFalse(r.Success);
            Assert.AreEqual("Compilation Failed", r.Error);
            Assert.Greater(r.Diagnostics.Count, 0);
        }

        #endregion

        #region Entry point resolution errors

        [Test]
        public async Task RunScript_UnknownEntry_ErrorsClearly()
        {
            var file = WriteTempScript(@"
public static class Builder
{
    public static int Build() { return 1; }
}");
            var r = await RunScriptCommand.RunScript(file, entry: "Nope.DoesNotExist");

            Assert.IsFalse(r.Success);
            Assert.AreEqual("Entry Point Not Found", r.Error);
        }

        [Test]
        public async Task RunScript_AmbiguousEntry_ErrorsClearly()
        {
            var file = WriteTempScript(@"
public static class Builder
{
    public static int A() { return 1; }
    public static int B() { return 2; }
}");
            var r = await RunScriptCommand.RunScript(file);

            Assert.IsFalse(r.Success);
            Assert.AreEqual("Entry Point Not Found", r.Error);
            StringAssert.Contains("Ambiguous", r.ErrorDetails);
        }

        [Test]
        public async Task RunScript_NoEntry_FallsBackToMain()
        {
            var file = WriteTempScript(@"
public static class Builder
{
    public static int Helper() { return 1; }
    public static int Main() { return 99; }
}");
            var r = await RunScriptCommand.RunScript(file);

            Assert.IsTrue(r.Success, r.Error + " / " + r.ErrorDetails);
            Assert.AreEqual(99, r.Result);
        }

        [Test]
        public async Task RunScript_InstanceMethodEntry_Rejected()
        {
            var file = WriteTempScript(@"
public class Builder
{
    public int Build() { return 1; }   // instance, not static
    public static int Trigger() { return 0; }
}");
            var r = await RunScriptCommand.RunScript(file, entry: "Builder.Build");

            Assert.IsFalse(r.Success);
            Assert.AreEqual("Entry Point Not Found", r.Error);
            StringAssert.Contains("instance method", r.ErrorDetails);
        }

        [Test]
        public async Task RunScript_GenericEntry_RejectedClearly()
        {
            // A generic method can't be invoked without type arguments — instead of a raw
            // reflection exception, the resolver must say so.
            var file = WriteTempScript(@"
public static class Builder
{
    public static T Build<T>() { return default(T); }
    public static int Other() { return 1; }
}");
            var r = await RunScriptCommand.RunScript(file, entry: "Builder.Build");

            Assert.IsFalse(r.Success);
            Assert.AreEqual("Entry Point Not Found", r.Error);
            StringAssert.Contains("generic entry points are not supported", r.ErrorDetails);
        }

        [Test]
        public async Task RunScript_NestedTypeEntry_Resolves()
        {
            // Callers naturally write nested types with dots ("Outer.Inner.Method"); reflection
            // names them "Outer+Inner". The resolver retries the '+' form.
            var file = WriteTempScript(@"
public static class Outer
{
    public static class Inner
    {
        public static int Build() { return 7; }
    }
}");
            var r = await RunScriptCommand.RunScript(file, entry: "Outer.Inner.Build");

            Assert.IsTrue(r.Success, r.Error + " / " + r.ErrorDetails);
            Assert.AreEqual(7, r.Result);
        }

        #endregion

        #region Argument coercion

        [Test]
        public async Task RunScript_Args_PrimitivesEnumAndStringArray_Coerced()
        {
            var file = WriteTempScript(@"
public static class Builder
{
    public enum Kind { Red, Green, Blue }
    public static string Build(int i, float f, Kind k, string[] names)
    {
        return i + "":"" + f + "":"" + k + "":"" + names.Length;
    }
}");
            var args = new JArray(3, 2.5f, "Green", new JArray("x", "y"));
            var r = await RunScriptCommand.RunScript(file, entry: "Builder.Build", args: args);

            Assert.IsTrue(r.Success, r.Error + " / " + r.ErrorDetails);
            Assert.AreEqual("3:2.5:Green:2", r.Result);
        }

        [Test]
        public async Task RunScript_Args_ObjectRefParameter_ResolvesUnityObject()
        {
            var go = TrackGo(new GameObject("RunScript_RefTarget"));

            var file = WriteTempScript(@"
using UnityEngine;
public static class Builder
{
    public static string Build(GameObject go) { return go.name; }
}");
            var handle = JObject.FromObject(new { instanceId = PipelineUtils.GetObjectId(go) });
            var r = await RunScriptCommand.RunScript(file, entry: "Builder.Build", args: new JArray(handle));

            Assert.IsTrue(r.Success, r.Error + " / " + r.ErrorDetails);
            Assert.AreEqual("RunScript_RefTarget", r.Result);
        }

        [Test]
        public async Task RunScript_Args_MissingRequiredArgument_Errors()
        {
            var file = WriteTempScript(@"
public static class Builder
{
    public static int Build(int required) { return required; }
}");
            var r = await RunScriptCommand.RunScript(file, entry: "Builder.Build", args: new JArray());

            Assert.IsFalse(r.Success);
            Assert.AreEqual("Bad Request", r.Error);
            StringAssert.Contains("Missing required argument", r.ErrorDetails);
        }

        [Test]
        public async Task RunScript_Args_DefaultValueUsedWhenOmitted()
        {
            var file = WriteTempScript(@"
public static class Builder
{
    public static int Build(int a, int b = 7) { return a + b; }
}");
            var r = await RunScriptCommand.RunScript(file, entry: "Builder.Build", args: new JArray(3));

            Assert.IsTrue(r.Success, r.Error + " / " + r.ErrorDetails);
            Assert.AreEqual(10, r.Result);
        }

        [Test]
        public async Task RunScript_Args_TooMany_Errors()
        {
            var file = WriteTempScript(@"
public static class Builder
{
    public static int Build(int a) { return a; }
}");
            var r = await RunScriptCommand.RunScript(file, entry: "Builder.Build", args: new JArray(1, 2, 3));

            Assert.IsFalse(r.Success);
            Assert.AreEqual("Bad Request", r.Error);
        }

        [Test]
        public async Task RunScript_Args_NullForValueType_ErrorsInsteadOfSilentDefault()
        {
            // JSON null used to silently become default(T) for value-type parameters — a caller
            // mistake that must surface as a coercion error instead.
            var file = WriteTempScript(@"
public static class Builder
{
    public static int Build(int a) { return a; }
}");
            var r = await RunScriptCommand.RunScript(file, entry: "Builder.Build", args: new JArray((object)null));

            Assert.IsFalse(r.Success);
            Assert.AreEqual("Bad Request", r.Error);
            StringAssert.Contains("null", r.ErrorDetails);
            StringAssert.Contains("value type", r.ErrorDetails);
        }

        [Test]
        public async Task RunScript_Args_NullForNullableValueType_StillWorks()
        {
            var file = WriteTempScript(@"
public static class Builder
{
    public static string Build(int? a) { return a.HasValue ? a.Value.ToString() : ""none""; }
}");
            var r = await RunScriptCommand.RunScript(file, entry: "Builder.Build", args: new JArray((object)null));

            Assert.IsTrue(r.Success, r.Error + " / " + r.ErrorDetails);
            Assert.AreEqual("none", r.Result);
        }

        #endregion

        #region Runtime exceptions

        [Test]
        public async Task RunScript_RuntimeException_StructuredError()
        {
            var file = WriteTempScript(@"
public static class Builder
{
    public static int Build() { throw new System.InvalidOperationException(""boom""); }
}");
            var r = await RunScriptCommand.RunScript(file);

            Assert.IsFalse(r.Success);
            Assert.AreEqual("Runtime Error", r.Error);
            StringAssert.Contains("boom", r.ErrorDetails);
        }

        [Test]
        public async Task RunScript_RuntimeException_ErrorDetails_MapToSourceFileAndLine()
        {
            // The PDB acceptance criterion: ephemeral runs always emit a source-mapped portable
            // PDB, so a runtime exception's stack trace must map back to the original .cs file
            // and line — not to an anonymous in-memory buffer.
            var file = WriteTempScript(
                "public static class Builder\n" +
                "{\n" +
                "    public static int Build()\n" +
                "    {\n" +
                "        throw new System.InvalidOperationException(\"mapped\");\n" + // line 5
                "    }\n" +
                "}\n");
            var r = await RunScriptCommand.RunScript(file);

            Assert.IsFalse(r.Success);
            Assert.AreEqual("Runtime Error", r.Error);
            var fileName = Path.GetFileName(file);
            StringAssert.Contains(fileName, r.ErrorDetails, "stack trace should reference the source file");
            // .NET renders "file.cs:line 5", Mono "file.cs:5" — accept either, but demand the
            // throw site's actual line number.
            Assert.IsTrue(
                Regex.IsMatch(r.ErrorDetails, Regex.Escape(fileName) + @":(line )?5\b"),
                $"stack trace should carry the source line of the throw. ErrorDetails:\n{r.ErrorDetails}");
        }

        #endregion

        #region Async entry points

        [Test]
        public async Task RunScript_AsyncTaskEntry_Awaited_ExceptionSurfaced()
        {
            // A Task-returning entry point must be awaited: without it the command reports
            // success the moment the first await yields, and the exception is never observed.
            var file = WriteTempScript(@"
using System.Threading.Tasks;
public static class Builder
{
    public static async Task Build()
    {
        await Task.Delay(10).ConfigureAwait(false);
        throw new System.InvalidOperationException(""async boom"");
    }
}");
            var r = await RunScriptCommand.RunScript(file);

            Assert.IsFalse(r.Success, "the faulted task's exception must fail the run");
            Assert.AreEqual("Runtime Error", r.Error);
            StringAssert.Contains("async boom", r.ErrorDetails);
        }

        [Test]
        public async Task RunScript_AsyncTaskEntry_CompletesAndReturnsNull()
        {
            var file = WriteTempScript(@"
using System.Threading.Tasks;
public static class Builder
{
    public static int Ran;
    public static async Task Build()
    {
        await Task.Delay(10).ConfigureAwait(false);
        Ran = 1;
    }
}");
            var r = await RunScriptCommand.RunScript(file);

            Assert.IsTrue(r.Success, r.Error + " / " + r.ErrorDetails);
            // async Task materializes as Task<VoidTaskResult>; the placeholder must not leak
            // into the result.
            Assert.IsNull(r.Result, "a plain async Task entry has no value to return");
        }

        [Test]
        public async Task RunScript_AsyncTaskIntEntry_ReturnsValue()
        {
            var file = WriteTempScript(@"
using System.Threading.Tasks;
public static class Builder
{
    public static async Task<int> Build()
    {
        await Task.Delay(10).ConfigureAwait(false);
        return 42;
    }
}");
            var r = await RunScriptCommand.RunScript(file);

            Assert.IsTrue(r.Success, r.Error + " / " + r.ErrorDetails);
            Assert.AreEqual(42, r.Result, "Task<int>.Result should be unwrapped into the response");
        }

        [Test]
        public async Task RunScript_AsyncEntry_ResumingOnUnityContext_CompletesWithoutDeadlock()
        {
            // THE deadlock regression (found by manual testing): awaits without
            // ConfigureAwait(false) resume on Unity's main-thread SynchronizationContext — the
            // default way anyone writes async in Unity. The old blocking GetResult() wait
            // deadlocked the whole Editor permanently on this input; the await-based wait must
            // let the editor loop pump those continuations and complete normally.
            var file = WriteTempScript(@"
using System.Threading.Tasks;
public static class Builder
{
    public static async Task<int> Build(int a, int b)
    {
        await Task.Yield();
        return a * b;
    }
}");
            var r = await RunScriptCommand.RunScript(file, args: new JArray(6, 7));

            Assert.IsTrue(r.Success, r.Error + " / " + r.ErrorDetails);
            Assert.AreEqual(42, r.Result);
        }

        [Test]
        public async Task RunScript_AsyncEntry_ExceedingBudget_TimesOutStructured()
        {
            // An entry task that outlives timeout_ms must produce a structured Timeout error
            // (the task keeps running detached), never hang the command or the Editor.
            var file = WriteTempScript(@"
using System.Threading.Tasks;
public static class Builder
{
    public static async Task<int> Build()
    {
        await Task.Delay(30000);
        return 1;
    }
}");
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var r = await RunScriptCommand.RunScript(file, timeoutMs: 1500);
            sw.Stop();

            Assert.IsFalse(r.Success, "the entry task cannot have completed within the budget");
            Assert.AreEqual("Timeout", r.Error);
            StringAssert.Contains("keeps running detached", r.ErrorDetails);
            Assert.Less(sw.ElapsedMilliseconds, 15000,
                "the command must give up at ~timeout_ms, not wait out the 30s entry task");
        }

        #endregion

        #region Bad input

        [Test]
        public async Task RunScript_NullFile_BadRequest()
        {
            var r = await RunScriptCommand.RunScript(null);
            Assert.IsFalse(r.Success);
            Assert.AreEqual("Bad Request", r.Error);
        }

        [Test]
        public async Task RunScript_NonCsFile_BadRequest()
        {
            var r = await RunScriptCommand.RunScript("Something.txt");
            Assert.IsFalse(r.Success);
            Assert.AreEqual("Bad Request", r.Error);
        }

        [Test]
        public async Task RunScript_MissingFile_FileNotFound()
        {
            var path = Path.Combine(Path.GetTempPath(), "run_script_missing_" + System.Guid.NewGuid().ToString("N") + ".cs");
            var r = await RunScriptCommand.RunScript(path);
            Assert.IsFalse(r.Success);
            Assert.AreEqual("File Not Found", r.Error);
        }

        [Test]
        public async Task RunScript_UnknownMode_BadRequest()
        {
            var file = WriteTempScript(@"
public static class Builder { public static int Build() { return 1; } }");
            var r = await RunScriptCommand.RunScript(file, mode: "banana");
            Assert.IsFalse(r.Success);
            Assert.AreEqual("Bad Request", r.Error);
        }

        #endregion

        #region defines

        [Test]
        public async Task RunScript_Defines_ExtendActiveEditorDefines()
        {
            // Ephemeral compiles are seeded with the project's active editor defines
            // (EditorUserBuildSettings.activeScriptCompilationDefines), so UNITY_EDITOR is set by
            // default — sources behave like project code. The user's `defines` are APPENDED to
            // that set, not a replacement for it.
            var source = @"
public static class Builder
{
    public static string Build()
    {
        var parts = new System.Collections.Generic.List<string>();
#if UNITY_EDITOR
        parts.Add(""editor"");
#endif
#if MY_FLAG
        parts.Add(""flag"");
#endif
        return string.Join("","", parts);
    }
}";
            var file = WriteTempScript(source);

            var withoutFlag = await RunScriptCommand.RunScript(file);
            Assert.IsTrue(withoutFlag.Success, withoutFlag.Error + " / " + withoutFlag.ErrorDetails);
            Assert.AreEqual("editor", withoutFlag.Result, "UNITY_EDITOR should be defined by default");

            var withFlag = await RunScriptCommand.RunScript(file, defines: new[] { "MY_FLAG" });
            Assert.IsTrue(withFlag.Success, withFlag.Error + " / " + withFlag.ErrorDetails);
            Assert.AreEqual("editor,flag", withFlag.Result, "a custom define must extend, not replace, the active defines");
        }

        #endregion

        #region Path resolution

        [Test]
        public async Task RunScript_RelativePath_ResolvesAgainstProjectRoot()
        {
            // Relative paths must resolve against the PROJECT ROOT (parent of Assets/), not the
            // process working directory. Temporarily move the CWD elsewhere to prove it.
            var projectRoot = Directory.GetParent(Application.dataPath).FullName;
            var fileName = "run_script_rel_" + System.Guid.NewGuid().ToString("N") + ".cs";
            var relative = Path.Combine("Temp", fileName);
            var absolute = Path.Combine(projectRoot, relative);
            Directory.CreateDirectory(Path.Combine(projectRoot, "Temp"));
            File.WriteAllText(absolute, @"
public static class Builder
{
    public static int Build() { return 11; }
}");
            m_TempFiles.Add(absolute);

            var originalCwd = Directory.GetCurrentDirectory();
            try
            {
                Directory.SetCurrentDirectory(Path.GetTempPath());
                var r = await RunScriptCommand.RunScript(relative);

                Assert.IsTrue(r.Success, r.Error + " / " + r.ErrorDetails);
                Assert.AreEqual(11, r.Result);
            }
            finally
            {
                Directory.SetCurrentDirectory(originalCwd);
            }
        }

        #endregion

        #region hotpatch delegation

        [Test]
        public async Task RunScript_Hotpatch_MissingFile_MapsReloadFileFailure()
        {
            // hotpatch delegates to reload_file; a clearly-missing file must surface as a mapped
            // failure rather than throwing, proving the delegation + error mapping.
            var path = Path.Combine(Path.GetTempPath(), "run_script_hotpatch_missing_" + System.Guid.NewGuid().ToString("N") + ".cs");
            var r = await RunScriptCommand.RunScript(path, mode: "hotpatch");

            Assert.IsFalse(r.Success);
            Assert.IsNotNull(r.Error);
        }

        [Test]
        public async Task RunScript_Hotpatch_DryRun_Rejected_NothingApplied()
        {
            // dry_run has no compile-only meaning in hotpatch mode — reload_file would apply live
            // method replacements. It must be rejected up front, not silently applied.
            // Proof nothing ran: the file deliberately does not exist. If the guard didn't fire,
            // reload_file would answer "File Not Found" — a "Bad Request" naming dry_run proves the
            // request was rejected before any hotpatch machinery (or compile) was touched.
            var path = Path.Combine(Path.GetTempPath(), "run_script_hotpatch_dryrun_" + System.Guid.NewGuid().ToString("N") + ".cs");
            var r = await RunScriptCommand.RunScript(path, mode: "hotpatch", dryRun: true);

            Assert.IsFalse(r.Success);
            Assert.AreEqual("Bad Request", r.Error);
            StringAssert.Contains("dry_run", r.ErrorDetails);
            StringAssert.Contains("hotpatch", r.ErrorDetails);
            Assert.AreEqual(0, r.CompileMs, "nothing should have compiled");
            Assert.AreEqual(0, r.ExecuteMs, "nothing should have been applied");
        }

        [Test]
        public async Task RunScript_Hotpatch_EntrySupplied_Rejected()
        {
            // hotpatch never invokes an entry point, so a supplied entry must be rejected by name
            // instead of silently ignored.
            var path = Path.Combine(Path.GetTempPath(), "run_script_hotpatch_entry_" + System.Guid.NewGuid().ToString("N") + ".cs");
            var r = await RunScriptCommand.RunScript(path, entry: "Builder.Build", mode: "hotpatch");

            Assert.IsFalse(r.Success);
            Assert.AreEqual("Bad Request", r.Error);
            StringAssert.Contains("'entry'", r.ErrorDetails);
        }

        [Test]
        public async Task RunScript_Hotpatch_ArgsSupplied_Rejected()
        {
            var path = Path.Combine(Path.GetTempPath(), "run_script_hotpatch_args_" + System.Guid.NewGuid().ToString("N") + ".cs");
            var r = await RunScriptCommand.RunScript(path, args: new JArray(1), mode: "hotpatch");

            Assert.IsFalse(r.Success);
            Assert.AreEqual("Bad Request", r.Error);
            StringAssert.Contains("'args'", r.ErrorDetails);
        }

        #endregion

        #region ViaClient

        [Test]
        public void RunScript_ViaClient_ReturnsResult()
        {
            var file = WriteTempScript(@"
public static class Builder
{
    public static int Build() { return 6 * 7; }
}");
            using (var server = new PipelineTestServer())
            {
                var response = server.Execute("run_script", new { file, timeout_ms = 15000 });
                Assert.IsTrue(response.IsSuccess, response.Error);

                var r = response.GetTypedResponse<RunScriptResponse>();
                Assert.IsNotNull(r, "Should deserialize a RunScriptResponse");
                Assert.IsTrue(r.Success, r.Error + " / " + r.ErrorDetails);
                Assert.AreEqual(42, r.Result);
            }
        }

        [Test]
        public void RunScript_ViaClient_ArgsCoercedFromJsonArray()
        {
            var file = WriteTempScript(@"
public static class Builder
{
    public static string Build(int i, string[] names) { return i + "":"" + names.Length; }
}");
            using (var server = new PipelineTestServer())
            {
                var response = server.Execute("run_script", new
                {
                    file,
                    entry = "Builder.Build",
                    args = new object[] { 5, new[] { "a", "b", "c" } }
                });

                Assert.IsTrue(response.IsSuccess, response.Error);
                var r = response.GetTypedResponse<RunScriptResponse>();
                Assert.IsNotNull(r);
                Assert.IsTrue(r.Success, r.Error + " / " + r.ErrorDetails);
                Assert.AreEqual("5:3", r.Result);
            }
        }

        [Test]
        public void RunScript_ViaClient_TimeoutMs_GovernsDispatcherWaitBudget()
        {
            // Proves timeout_ms actually reaches the dispatcher's wait budget end-to-end
            // (UUM-148641 semantics extended to run_script): an entry that sleeps 2000ms with
            // timeout_ms=300 must time out at ~300ms instead of waiting out the sleep (or the
            // dispatcher's 60s default).
            var file = WriteTempScript(@"
public static class Builder
{
    public static int Build() { System.Threading.Thread.Sleep(2000); return 1; }
}");
            using (var server = new PipelineTestServer())
            {
                LogAssert.Expect(LogType.Error, new Regex("Failed to handle /api/exec request.*timed out after 300ms"));

                // Like the matching eval test: PipelineTestServer.Execute's pump loop runs the
                // dequeued work item synchronously, so it can't observe completion until the
                // compile + 2000ms sleep finish — give the outer wait headroom even though the
                // dispatcher timeout itself fires at ~300ms.
                var response = server.Execute("run_script", new { file, timeout_ms = 300 }, timeoutMs: 30000);

                Assert.IsFalse(response.IsSuccess,
                    $"Expected the 300ms budget to time out instead of waiting for the 2000ms sleep: {response.RawResponse}");
            }
        }

        [Test]
        public void RunScript_ViaClient_CompileError_ReturnsDiagnostics()
        {
            var file = WriteTempScript(@"
public static class Builder
{
    public static int Build() { return 2 +; }
}");
            using (var server = new PipelineTestServer())
            {
                var response = server.Execute("run_script", new { file });

                var r = response.GetTypedResponse<RunScriptResponse>();
                Assert.IsNotNull(r);
                Assert.IsFalse(r.Success);
                Assert.AreEqual("Compilation Failed", r.Error);
                Assert.Greater(r.Diagnostics.Count, 0);
            }
        }

        #endregion
    }
}
