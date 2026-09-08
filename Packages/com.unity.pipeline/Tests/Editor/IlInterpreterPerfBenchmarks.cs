using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using NUnit.Framework;
using Unity.Pipeline.Compilation;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace Unity.Pipeline.Tests.Editor
{
    /// <summary>
    /// Reusable interpreter performance benchmarks: each workload compiles ONE source through
    /// <see cref="RoslynCompilationService"/> and runs it twice — through the interpreter (the same
    /// binding hot reload uses) and as the Mono-compiled assembly — so every row is an
    /// interpreted-vs-compiled ratio plus allocated bytes per iteration (the boxing signal).
    ///
    /// Run on demand: <c>run_tests --mode editor --filter IlInterpreterPerfBenchmarks</c>.
    /// The default budget keeps the whole fixture a few seconds so it can ride along in CI; set the
    /// ILINTERP_BENCH_SECONDS env var (e.g. 2) before launching the editor for low-noise numbers.
    /// Results are logged as one table and appended to Library/IlInterpreterBench/ for diffing a
    /// future optimization against today's baseline.
    /// </summary>
    class IlInterpreterPerfBenchmarks
    {
        // ---- workload matrix -------------------------------------------------------------------
        // Every source declares `public static class Probe` with `public static object Run(...)`
        // containing an inner loop of N iterations; measurements are per inner iteration, so the
        // outer invoke overhead (reflection for compiled, interp.Invoke for interpreted) washes out.

        sealed class Workload
        {
            public string Name;
            public int N;                     // inner iterations per Run() call
            public string Source;
            public Func<object[]> MakeArgs;   // fresh args per measurement phase (null = none)
            public Action Cleanup;
            public string What;               // which interpreter path the row isolates
            public bool KnownDivergent;       // documented interpreter bug: report, don't fail
        }

        static Workload[] BuildWorkloads()
        {
            GameObject go = null;

            return new[]
            {
                new Workload
                {
                    Name = "int_arith", N = 4000, What = "pure dispatch loop (no host, no boxing)",
                    Source = Probe(@"
                        int s = 0;
                        for (int i = 0; i < 4000; i++) { s = s * 31 + i; s ^= s >> 3; }
                        return s;"),
                },
                new Workload
                {
                    Name = "float_math", N = 4000, What = "float ALU ops",
                    Source = Probe(@"
                        float s = 1.0001f;
                        for (int i = 0; i < 4000; i++) { s = s * 1.00001f + 0.001f; if (s > 100f) s -= 99f; }
                        return s;"),
                },
                new Workload
                {
                    Name = "script_calls", N = 2000, What = "interpreted->interpreted static calls",
                    Source = @"public static class Probe {
                        static int Add(int a, int b) { return a + b; }
                        public static object Run() {
                            int s = 0;
                            for (int i = 0; i < 2000; i++) s = Add(s, i);
                            return s;
                        } }",
                },
                new Workload
                {
                    Name = "instance_fields", N = 2000, What = "script-object field read/write",
                    Source = @"public static class Probe {
                        class Counter { public int Value; public float Scale = 1.5f; }
                        public static object Run() {
                            var c = new Counter();
                            for (int i = 0; i < 2000; i++) { c.Value = c.Value + 1; c.Scale = c.Scale * 1.0001f; }
                            return c.Value;
                        } }",
                },
                new Workload
                {
                    Name = "box_unbox", N = 2000, What = "explicit object boxing in script code",
                    Source = Probe(@"
                        int s = 0;
                        for (int i = 0; i < 2000; i++) { object o = i; s += (int)o; }
                        return s;"),
                },
                new Workload
                {
                    Name = "vector3_math", N = 1000, What = "curated flat-struct path (AllowTypeStruct<Vector3>)",
                    Source = Probe(@"
                        var v = new UnityEngine.Vector3(1f, 2f, 3f);
                        var w = new UnityEngine.Vector3(0.001f, 0.002f, 0.003f);
                        for (int i = 0; i < 1000; i++) v = v + w;
                        return v.x + v.y + v.z;"),
                },
                new Workload
                {
                    Name = "vector3_magnitude", N = 1000, What = "computed struct member (ref-receiver closure)",
                    Source = Probe(@"
                        var v = new UnityEngine.Vector3(1f, 2f, 3f);
                        float s = 0f;
                        for (int i = 0; i < 1000; i++) { s += v.magnitude; v.x += 0.0001f; }
                        return s;"),
                },
                new Workload
                {
                    // This row found the flat-arg-into-host-call zeroing bug (2026-08-13, fixed
                    // in VmEngine.RdArg): a Vt-slot argument read as null and Invoke turned it
                    // into default(T). AutoBindFlatMixTests pins the fix dotnet-side; the
                    // divergence gate here enforces it against the real Bounds/Vector3 pair.
                    Name = "bounds_autobind", N = 500,
                    What = "auto-bound host struct (reflection AllowType path)",
                    Source = Probe(@"
                        var b = new UnityEngine.Bounds(new UnityEngine.Vector3(1f, 2f, 3f), UnityEngine.Vector3.one);
                        float s = 0f;
                        for (int i = 0; i < 500; i++) {
                            var c = new UnityEngine.Bounds(b.center, b.size);
                            s += c.size.x + c.center.y;
                        }
                        return s;"),
                },
                new Workload
                {
                    Name = "rect_props", N = 500, What = "curated non-generic struct props (Rect, boxed path)",
                    Source = Probe(@"
                        var r = new UnityEngine.Rect(0f, 0f, 10f, 20f);
                        float s = 0f;
                        for (int i = 0; i < 500; i++) { r.width = r.width + 0.5f; s += r.width; }
                        return s;"),
                },
                new Workload
                {
                    Name = "mathf_statics", N = 1000, What = "curated host static calls (Mathf)",
                    Source = Probe(@"
                        float s = 0.5f;
                        for (int i = 0; i < 1000; i++) s = UnityEngine.Mathf.Clamp(s * 1.01f, 0f, UnityEngine.Mathf.Max(2f, s));
                        return s;"),
                },
                new Workload
                {
                    Name = "transform_position", N = 500, What = "native property get/set through host binding",
                    Source = @"public static class Probe {
                        public static object Run(UnityEngine.Transform t) {
                            t.position = new UnityEngine.Vector3(0f, 0f, 0f);
                            for (int i = 0; i < 500; i++) {
                                var p = t.position;
                                p.x += 0.0001f;
                                t.position = p;
                            }
                            return t.position.x;
                        } }",
                    MakeArgs = () =>
                    {
                        go = new GameObject("IlInterpBenchProbe");
                        return new object[] { go.transform };
                    },
                    Cleanup = () => { if (go != null) UnityEngine.Object.DestroyImmediate(go); go = null; },
                },
                new Workload
                {
                    Name = "string_concat", N = 500, What = "string append (allocation inherent in workload)",
                    Source = Probe(@"
                        string s = """";
                        int len = 0;
                        for (int i = 0; i < 500; i++) {
                            s = s + (char)('a' + (i & 15));
                            if (s.Length > 64) { len += s.Length; s = """"; }
                        }
                        return len;"),
                },
                new Workload
                {
                    Name = "delegate_call", N = 2000, What = "lambda capture + invocation",
                    Source = Probe(@"
                        int k = 3;
                        System.Func<int, int> f = x => x + k;
                        int s = 0;
                        for (int i = 0; i < 2000; i++) s = f(s);
                        return s;"),
                },
            };
        }

        static string Probe(string body) =>
            "public static class Probe { public static object Run() {" + body + "\n} }";

        // ---- measurement -----------------------------------------------------------------------

        const double DefaultSecondsPerPhase = 0.20;
        const double WarmupSeconds = 0.08;

        // Unity's Mono stubs GC.GetAllocatedBytesForCurrentThread to 0, so allocations are
        // measured in a separate short pass: quiesce the GC, run a call count small enough that
        // the nursery cannot fill, and read the heap growth. If gen0 collected anyway, halve the
        // count and retry; -1 means no collection-free window was found (report n/a).
        static double AllocPerIt(Func<object> call, int innerN)
        {
            for (int calls = 64; calls >= 1; calls /= 4)
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
                int gen0 = GC.CollectionCount(0);
                long before = GC.GetTotalMemory(false);
                for (int i = 0; i < calls; i++)
                    call();
                long delta = GC.GetTotalMemory(false) - before;
                if (GC.CollectionCount(0) == gen0 && delta >= 0)
                    return (double)delta / ((long)calls * innerN);
            }
            return -1;
        }

        struct Phase
        {
            public double NsPerIt;
            public double BytesPerIt; // -1 when unavailable
            public object Result;
        }

        static Phase Measure(Func<object> call, int innerN, double seconds)
        {
            var sw = Stopwatch.StartNew();
            object result = null;
            while (sw.Elapsed.TotalSeconds < WarmupSeconds)
                result = call();

            sw.Restart();
            long calls = 0;
            while (sw.Elapsed.TotalSeconds < seconds) { result = call(); calls++; }
            sw.Stop();

            long its = calls * innerN;
            return new Phase
            {
                NsPerIt = sw.Elapsed.TotalMilliseconds * 1e6 / its,
                BytesPerIt = AllocPerIt(call, innerN),
                Result = result,
            };
        }

        [Test]
        public void MeasureAll()
        {
            double seconds = DefaultSecondsPerPhase;
            var env = Environment.GetEnvironmentVariable("ILINTERP_BENCH_SECONDS");
            if (!string.IsNullOrEmpty(env) && double.TryParse(env, out var s) && s > 0)
                seconds = s;

            var rows = new List<string>();
            var divergences = new List<string>();
            var table = new StringBuilder();
            table.AppendLine($"IlInterpreter editor bench — {seconds:0.##}s/phase, Mono, {Application.unityVersion}");
            table.AppendLine($"{"workload",-20} {"interp ns/it",13} {"compiled ns/it",15} {"ratio",8} {"interp B/it",12} {"compiled B/it",14}  path");

            foreach (var w in BuildWorkloads())
            {
                var compile = RoslynCompilationService.Compile(new CompilationRequest
                {
                    SourceCode = w.Source,
                    AssemblyName = $"IlInterpBench_{w.Name}",
                    SkipLoad = false, // Mono-load the same bytes for the compiled baseline
                });
                Assert.IsTrue(compile.Success, $"{w.Name}: bench source must compile:\n{w.Source}");
                var compiledRun = compile.Assembly.GetType("Probe").GetMethod("Run");

                using var interp = new IlInterpreter.Interpreter.ScriptInterpreter(
                    IlInterpreterHostBindings.CreateStandard());
                interp.StepLimit = int.MaxValue;
                interp.Load(new RawScript(compile.AssemblyBytes));

                try
                {
                    var interpArgs = w.MakeArgs?.Invoke() ?? Array.Empty<object>();
                    var ip = Measure(() => interp.Invoke("Run", interpArgs), w.N, seconds);
                    w.Cleanup?.Invoke();

                    var compiledArgs = w.MakeArgs?.Invoke() ?? Array.Empty<object>();
                    var cp = Measure(() => compiledRun.Invoke(null, compiledArgs), w.N, seconds);
                    w.Cleanup?.Invoke();

                    // Same source, same semantics: a mismatch means the row measures a
                    // divergence, not a slowdown. Record it, keep the table complete, and fail
                    // the test at the end so a bug never hides the remaining rows.
                    if (!SameResult(cp.Result, ip.Result) && !w.KnownDivergent)
                        divergences.Add($"{w.Name}: interpreted={ip.Result} compiled={cp.Result}");
                    if (SameResult(cp.Result, ip.Result) && w.KnownDivergent)
                        Debug.Log($"IlInterpreter bench: '{w.Name}' no longer diverges — its known bug looks fixed; drop KnownDivergent to re-enforce.");

                    string ib = ip.BytesPerIt < 0 ? "n/a" : ip.BytesPerIt.ToString("0.0");
                    string cb = cp.BytesPerIt < 0 ? "n/a" : cp.BytesPerIt.ToString("0.0");
                    string flag = SameResult(cp.Result, ip.Result) ? "" : "  [DIVERGED]";
                    table.AppendLine(
                        $"{w.Name,-20} {ip.NsPerIt,13:0.0} {cp.NsPerIt,15:0.00} {ip.NsPerIt / cp.NsPerIt,8:0.0}x {ib,12} {cb,14}  {w.What}{flag}");
                    rows.Add($"{w.Name},{ip.NsPerIt:0.0},{cp.NsPerIt:0.00},{ip.BytesPerIt:0.0},{cp.BytesPerIt:0.0}");
                }
                finally
                {
                    w.Cleanup?.Invoke();
                }
            }

            var report = table.ToString();
            Debug.Log(report);

            // Append a machine-readable copy so future optimization branches can diff against it.
            var dir = Path.Combine("Library", "IlInterpreterBench");
            Directory.CreateDirectory(dir);
            var file = Path.Combine(dir, $"bench-{DateTime.Now:yyyyMMdd-HHmmss}.csv");
            File.WriteAllText(file,
                "workload,interp_ns_it,compiled_ns_it,interp_b_it,compiled_b_it\n" + string.Join("\n", rows) + "\n");
            Debug.Log($"IlInterpreter bench CSV: {Path.GetFullPath(file)}");

            if (divergences.Count > 0)
                Assert.Fail("interpreted result diverged from compiled on:\n" + string.Join("\n", divergences));
        }

        static bool SameResult(object compiled, object interpreted)
        {
            if (compiled is float cf && interpreted is float inf)
                return Math.Abs(cf - inf) <= Math.Abs(cf) * 1e-3f + 1e-3f;
            return Equals(compiled, interpreted);
        }

        sealed class RawScript : IlInterpreter.IScript
        {
            readonly byte[] m_Bytes;
            public RawScript(byte[] bytes) { m_Bytes = bytes; }
            public string Name => "IlInterpBench";
            public ReadOnlyMemory<byte> Il => m_Bytes;
        }
    }
}
