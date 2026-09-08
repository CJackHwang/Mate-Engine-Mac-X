using System;
using System.Text;
using NUnit.Framework;
using Unity.Pipeline.Compilation;

namespace Unity.Pipeline.Tests.Editor
{
    /// <summary>
    /// 64-bit (I8/R8) values through the interpreter UNDER THE EDITOR'S MONO RUNTIME — the
    /// dotnet suite proves semantics on CoreCLR, but the wide-slot frame code (unaligned
    /// 8-byte reads, Unsafe.WriteUnaligned) is exactly the kind of thing runtime differences
    /// expose. Compiles with the same Roslyn service hot reload uses and binds the standard
    /// host surface.
    /// </summary>
    class IlInterpreterWideValueEditorTests
    {
        sealed class RawScript : IlInterpreter.IScript
        {
            readonly byte[] m_Bytes;
            public RawScript(byte[] bytes) { m_Bytes = bytes; }
            public string Name => "WideValueProbe";
            public ReadOnlyMemory<byte> Il => m_Bytes;
        }

        const string Source = @"
public class Script {
    static long s_total;
    public static string Run() {
        long acc = 0;
        for (int i = 0; i < 10; i++) acc += 3000000000L;
        s_total += acc;
        ulong h = 1469598103934665603UL; h ^= 42; h *= 1099511628211UL;
        double d = 0.1 + 0.2;
        double big = 16777217.0;
        long[] arr = { 1L, 5000000000L, -3L };
        long sum = 0; foreach (var v in arr) sum += v;
        return acc + ""|"" + s_total + ""|"" + (h >> 32) + ""|"" + d.ToString(""R"") + ""|""
            + (d == 0.3 ? ""eq"" : ""neq"") + ""|"" + (int)big + ""|"" + sum + ""|""
            + System.Math.Sqrt(2.0).ToString(""R"") + ""|"" + (acc > int.MaxValue);
    }
}";

        [Test]
        public void WideValues_RunAtFullPrecision_UnderMono()
        {
            var compile = RoslynCompilationService.Compile(new CompilationRequest
            {
                SourceCode = Source,
                AssemblyName = "WideValueProbe",
                SkipLoad = true,
            });
            Assert.IsTrue(compile.Success,
                "probe should compile: " + string.Join("; ", compile.Diagnostics));

            using var interp = new IlInterpreter.Interpreter.ScriptInterpreter(
                IlInterpreterHostBindings.CreateStandard(), _ => { });
            interp.Load(new RawScript(compile.AssemblyBytes));

            // Two invocations: the second proves the boxed long static persists correctly.
            Assert.AreEqual(
                "30000000000|30000000000|1153257940|0.30000000000000004|neq|16777217|4999999998|1.4142135623730951|True",
                (string)interp.Invoke("Run"));
            Assert.AreEqual(
                "30000000000|60000000000|1153257940|0.30000000000000004|neq|16777217|4999999998|1.4142135623730951|True",
                (string)interp.Invoke("Run"));
        }
    }
}
