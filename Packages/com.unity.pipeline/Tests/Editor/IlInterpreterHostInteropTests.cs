using System;
using NUnit.Framework;
using Unity.Pipeline.Compilation;
using UnityEngine;

namespace Unity.Pipeline.Tests.Editor
{
    /// <summary>
    /// Native-object interop through the interpreter host binding (<c>IlInterpreterHostBindings</c>): real
    /// UnityEngine structs (<see cref="Vector3"/> via the flat-frame <c>AllowTypeStruct</c> path),
    /// native-backed objects (<see cref="GameObject"/>/<see cref="Transform"/>), and fake-null
    /// semantics on a destroyed <see cref="UnityEngine.Object"/> — none of which the dotnet layer can
    /// exercise.
    /// </summary>
    class IlInterpreterHostInteropTests
    {
        // Compile a statement body and run it through the interpreter directly, against the shared
        // host binding (IlInterpreterHostBindings.CreateStandard) — the same surface hot reload uses. No
        // eval command, no Assembly.Load, so the interop coverage holds under IL2CPP.
        static object RunViaInterpreter(string body)
        {
            var source = $@"using UnityEngine;
public static class Probe {{ public static object Run() {{ {body} }} }}";

            var compile = RoslynCompilationService.Compile(new CompilationRequest
            {
                SourceCode = source,
                AssemblyName = "IlInterpreterInteropProbe",
                SkipLoad = true, // interpreter walks the bytes; no Assembly.Load
            });
            Assert.IsTrue(compile.Success, "probe source should compile");

            using var interp = new IlInterpreter.Interpreter.ScriptInterpreter(IlInterpreterHostBindings.CreateStandard());
            interp.Load(new RawScript(compile.AssemblyBytes));
            return interp.Invoke("Run");
        }

        [Test]
        public void Vector3_Struct_RoundTrips_ThroughFlatFrame()
        {
            // AllowTypeStruct<Vector3>: ctor + x/y/z field reads through the unmanaged Value frame.
            var result = RunViaInterpreter("var v = new Vector3(1f, 2f, 3f); return v.x + v.y + v.z;");
            Assert.AreEqual(6f, (float)result, 1e-4f);
        }

        [Test]
        public void Transform_Position_SetAndGet()
        {
            var result = RunViaInterpreter(
                "var go = new GameObject(\"t\"); go.transform.position = new Vector3(1f, 2f, 3f); return go.transform.position.x;");
            Assert.AreEqual(1f, (float)result, 1e-4f);
        }

        [Test]
        public void Mathf_StaticCall()
        {
            var result = RunViaInterpreter("return Mathf.Max(3f, 7f);");
            Assert.AreEqual(7f, (float)result, 1e-4f);
        }

        // Regression for the gameplay pattern `a.GetType() == b.GetType()`: `==` on System.Type
        // operands compiles to Type.op_Equality, a BCL MemberRef the auto-bind policy skips — it
        // must be registered explicitly in AllowBcl (was: "Host method 'Type.op_Equality' is not
        // registered in the binding" from hot-reloaded CharacterInputController.UseConsumable).
        [Test]
        public void TypeEquality_GetTypeComparison_BindsThroughStandardSurface()
        {
            var result = RunViaInterpreter(
                "GameObject a = new GameObject(\"type-eq-a\"); GameObject b = new GameObject(\"type-eq-b\"); " +
                "return a.GetType() == b.GetType();");
            Assert.AreEqual(1, Convert.ToInt32(result), "same runtime types should compare equal");
        }

        [Test]
        public void TypeInequality_DifferentTypes_BindsThroughStandardSurface()
        {
            var result = RunViaInterpreter("return typeof(GameObject) != typeof(Transform);");
            Assert.AreEqual(1, Convert.ToInt32(result), "different types should compare not-equal");
        }

        // `.Name` on a Type receiver binds through MemberInfo.get_Name (the declaring property),
        // not Type.get_Name — found by the push-time surface validation on real gameplay code.
        [Test]
        public void TypeName_ViaMemberInfo_BindsThroughStandardSurface()
        {
            var result = RunViaInterpreter("GameObject g = new GameObject(\"type-name\"); return g.GetType().Name;");
            Assert.AreEqual("GameObject", (string)result);
        }

        // A destroyed UnityEngine.Object compares == null via Unity's overloaded operator, not
        // reference equality. Driven directly (not via eval) so we can register op_Equality and pass
        // the destroyed instance in as an argument — proving the interpreter honours fake-null.
        static object InvokeIsNull(UnityEngine.Object arg)
        {
            const string source = @"
public class S { public static bool Run(UnityEngine.Object o) { return o == null; } }";

            var compile = RoslynCompilationService.Compile(new CompilationRequest
            {
                SourceCode = source,
                AssemblyName = "IlInterpreterFakeNullProbe",
                SkipLoad = true, // interpreter walks the bytes; no Assembly.Load
            });
            Assert.IsTrue(compile.Success, "probe source should compile");

            var binding = new IlInterpreter.Interpreter.HostBinding()
                .AllowBcl()
                .AllowStatic("Object", "op_Equality",
                    (_, a) => (object)((UnityEngine.Object)a[0] == (UnityEngine.Object)a[1]), 2);

            using var interp = new IlInterpreter.Interpreter.ScriptInterpreter(binding);
            interp.Load(new RawScript(compile.AssemblyBytes));
            return interp.Invoke("Run", arg);
        }

        [Test]
        public void DestroyedObject_ComparesEqualToNull()
        {
            var go = new GameObject("to-destroy");
            UnityEngine.Object.DestroyImmediate(go);
            // The host op_Equality returns bool, so the interpreter hands it back boxed as bool
            // (unlike interpreter-internal bools, which come back as boxed int).
            Assert.IsTrue((bool)InvokeIsNull(go), "destroyed object should be fake-null");
        }

        [Test]
        public void LiveObject_DoesNotCompareEqualToNull()
        {
            var go = new GameObject("alive");
            try
            {
                Assert.IsFalse((bool)InvokeIsNull(go), "live object should not be null");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
            }
        }

        // Roslyn emits `call Array.Empty<T>()` when a params T[] parameter receives no arguments.
        // The interpreter must bind that MethodSpec or the call throws at load time.
        [Test]
        public void ParamsArray_OmittedArgument_BindsArrayEmpty()
        {
            // Sum() with no args → Roslyn emits Array.Empty<int>() for the params int[].
            var result = RunViaInterpreter(
                "int Sum(params int[] ns) { int s = 0; foreach (var n in ns) s += n; return s; } return Sum();");
            Assert.AreEqual(0, Convert.ToInt32(result));
        }

        [Test]
        public void ParamsArray_OmittedStringArgument_BindsArrayEmpty()
        {
            var result = RunViaInterpreter(
                "int Count(params string[] ss) { return ss.Length; } return Count();");
            Assert.AreEqual(0, Convert.ToInt32(result));
        }

        static object RunViaArrayArgHost(string body)
        {
            var source = $@"using Unity.Pipeline.Tests.Editor;
public static class Probe {{ public static object Run() {{ {body} }} }}";

            var compile = RoslynCompilationService.Compile(new CompilationRequest
            {
                SourceCode = source,
                AssemblyName = "IlInterpreterArrayArgProbe",
                SkipLoad = true, // interpreter walks the bytes; no Assembly.Load
            });
            Assert.IsTrue(compile.Success, "probe source should compile");

            var binding = new IlInterpreter.Interpreter.HostBinding()
                .AllowBcl()
                .AllowType<ArrayArgHost>();

            using var interp = new IlInterpreter.Interpreter.ScriptInterpreter(binding);
            interp.Load(new RawScript(compile.AssemblyBytes));
            return interp.Invoke("Run");
        }

        [Test]
        public void StringArrayLiteral_PassedToHostMethodTakingStringArray_DoesNotThrow()
        {
            var result = RunViaArrayArgHost(
                "return ArrayArgHost.Count(new string[] { \"a\", \"b\", \"c\" });");
            Assert.AreEqual(3, Convert.ToInt32(result));
        }

        /// <summary>Minimal IScript over raw Roslyn-emitted PE bytes.</summary>
        sealed class RawScript : IlInterpreter.IScript
        {
            public RawScript(byte[] bytes) { Il = bytes; }
            public string Name => "IlInterpreterProbe";
            public ReadOnlyMemory<byte> Il { get; }
        }
    }

    /// <summary>
    /// Minimal host method taking a reference-type array parameter, registered through
    /// <c>HostBinding.AllowType</c> the same reflection-based way any Unity API method would be.
    /// </summary>
    public sealed class ArrayArgHost
    {
        public static int Count(string[] items) => items.Length;
    }
}
