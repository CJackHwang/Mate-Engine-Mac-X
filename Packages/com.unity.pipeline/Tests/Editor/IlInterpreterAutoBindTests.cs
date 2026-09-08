using System;
using System.Linq;
using NUnit.Framework;
using Unity.Pipeline.Compilation;
using UnityEngine;

namespace Unity.Pipeline.Tests.Editor
{
    /// <summary>Host type deliberately outside every curated surface — resolvable only through a
    /// test-supplied <c>AutoBindResolver</c> (the standard policy declines Unity.Pipeline.* assemblies,
    /// including this test assembly).</summary>
    public static class AutoBindProbeType
    {
        public static int Give() => 42;
    }

    /// <summary>
    /// Demand-time auto-bind: with an <c>AutoBindResolver</c> installed on the binding
    /// (<c>IlInterpreterHostBindings.CreateStandard</c> does this), the interpreter's Load pass resolves a
    /// script's TypeRefs and registers uncurated host types on first use, so the curated allowlist is a
    /// fast-path core rather than a capability boundary. Unresolvable members are reported through
    /// <c>ScriptInterpreter.UnboundHostMembers</c> at load time instead of only throwing when reached.
    /// </summary>
    class IlInterpreterAutoBindTests
    {
        static IlInterpreter.Interpreter.ScriptInterpreter LoadViaInterpreter(
            string body, IlInterpreter.Interpreter.HostBinding binding)
        {
            var source = $@"using UnityEngine;
public static class Probe {{ public static object Run() {{ {body} }} }}";

            var compile = RoslynCompilationService.Compile(new CompilationRequest
            {
                SourceCode = source,
                AssemblyName = "IlInterpreterAutoBindProbe",
                SkipLoad = true, // interpreter walks the bytes; no Assembly.Load
            });
            Assert.IsTrue(compile.Success, "probe source should compile");

            var interp = new IlInterpreter.Interpreter.ScriptInterpreter(binding);
            interp.Load(new BytesScript(compile.AssemblyBytes));
            return interp;
        }

        [Test]
        public void TryAutoBindType_RegistersResolvedType_AndDeclinesUnknownNames()
        {
            var binding = new IlInterpreter.Interpreter.HostBinding();
            Assert.IsFalse(binding.TryAutoBindType("Whatever.Name"),
                "without a resolver the binding must stay a strict allowlist");

            binding.AutoBindResolver = n =>
                n == "Unity.Pipeline.Tests.Editor.AutoBindProbeType" ? typeof(AutoBindProbeType) : null;

            Assert.IsFalse(binding.RegisteredTypes.Contains(typeof(AutoBindProbeType)));
            Assert.IsTrue(binding.TryAutoBindType("Unity.Pipeline.Tests.Editor.AutoBindProbeType"));
            Assert.IsTrue(binding.RegisteredTypes.Contains(typeof(AutoBindProbeType)),
                "auto-binding must register the type like a curated AllowType");
            Assert.IsFalse(binding.TryAutoBindType("Some.Other.Name"),
                "names the resolver declines must not bind");
        }

        [Test]
        public void Load_AutoBinds_UncuratedUnityEngineType()
        {
            // SystemInfo is not in the curated AddStandardUnity list; before demand-time auto-bind
            // this call resolved to a throwing stub. CreateStandard's resolver accepts UnityEngine
            // types, so the Load pass registers it and the call binds like any curated member.
            using var interp = LoadViaInterpreter(
                "return SystemInfo.processorCount;", IlInterpreterHostBindings.CreateStandard());

            Assert.IsEmpty(interp.UnboundHostMembers,
                "every host member of the probe should have bound");
            Assert.Greater((int)interp.Invoke("Run"), 0);
        }

        [Test]
        public void Load_AutoBinds_ResolverSuppliedGameplayType_EndToEnd()
        {
            // Overlay the standard policy with this test assembly's probe type (the standard policy
            // declines Unity.Pipeline.* assemblies) — proving the TypeRef pass feeds resolver results
            // through to a successful host call.
            var binding = IlInterpreterHostBindings.CreateStandard();
            var standard = binding.AutoBindResolver;
            binding.AutoBindResolver = n =>
                n == "Unity.Pipeline.Tests.Editor.AutoBindProbeType" ? typeof(AutoBindProbeType) : standard(n);

            using var interp = LoadViaInterpreter(
                "return Unity.Pipeline.Tests.Editor.AutoBindProbeType.Give();", binding);

            Assert.IsEmpty(interp.UnboundHostMembers);
            Assert.AreEqual(42, (int)interp.Invoke("Run"));
        }

        [Test]
        public void StrictBinding_ReportsUnboundMember_AtLoad_AndThrowsOnlyWhenReached()
        {
            // Null resolver = the pre-auto-bind strict behavior: the script loads (stub install),
            // the gap is visible in UnboundHostMembers, and the call throws only when reached.
            var binding = IlInterpreterHostBindings.CreateStandard();
            binding.AutoBindResolver = null;

            using var interp = LoadViaInterpreter("return SystemInfo.processorCount;", binding);

            Assert.That(interp.UnboundHostMembers,
                Has.Some.Contains("SystemInfo.get_processorCount"),
                "the unresolved member must be reported at load time");
            Assert.Catch<IlInterpreter.ScriptException>(() => interp.Invoke("Run"));
        }

        [Test]
        public void Load_OpenGenericMemberRef_IsNotReportedUnbound_WhenInstantiationResolves()
        {
            // A generic method reaches the interpreter twice: as the open MemberRef row (the
            // metadata parent) and as the MethodSpec instantiation that IL actually calls. Only
            // the MethodSpec lookup matters; the open row used to be reported too, flagging every
            // generic call as unbound ("GameObject.GetComponent/1") even when the instantiation
            // resolved. Real instantiation gaps are still reported, as "Type.Name<T>/N".
            using var interp = LoadViaInterpreter(
                "GameObject go = null; return go != null ? (object)go.GetComponent<Transform>() : null;",
                IlInterpreterHostBindings.CreateStandard());

            Assert.IsEmpty(interp.UnboundHostMembers,
                "the open generic MemberRef row must not be reported when its instantiation resolves");
        }

        [Test]
        public void AutoBindPolicy_DeclinesBcl_SoAllowBclShimsStayAuthoritative()
        {
            // Environment is BCL and outside the hand-rolled AllowBcl surface; the standard policy
            // must decline it (auto-binding raw BCL types would register double-returning members
            // that bypass the float-number-space shims), so it reports unbound and throws if reached.
            using var interp = LoadViaInterpreter(
                "return System.Environment.ProcessorCount;", IlInterpreterHostBindings.CreateStandard());

            Assert.That(interp.UnboundHostMembers,
                Has.Some.Contains("Environment.get_ProcessorCount"),
                "BCL members outside AllowBcl must stay unbound under the standard policy");
            Assert.Catch<IlInterpreter.ScriptException>(() => interp.Invoke("Run"));
        }

        sealed class BytesScript : IlInterpreter.IScript
        {
            public BytesScript(byte[] bytes) { Il = bytes; }
            public string Name => "AutoBindProbe";
            public ReadOnlyMemory<byte> Il { get; }
        }
    }
}
