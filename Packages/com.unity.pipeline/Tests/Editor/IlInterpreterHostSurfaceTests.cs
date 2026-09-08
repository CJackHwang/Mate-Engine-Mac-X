using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using Unity.Pipeline.Compilation;
using Unity.Pipeline.Editor.BuildProcessors;
using Unity.Pipeline.HotReload;
using IlInterpreter.Interpreter;

namespace Unity.Pipeline.Tests.Editor
{
    /// <summary>
    /// Locks the host-surface drift guarantee: the runtime interpreter binding and the build-time
    /// <c>link.xml</c> preservation set are both projections of the one builder
    /// <see cref="IlInterpreterHostBindings.CreateStandard"/>. If a future change reconstructs the surface
    /// at any one site instead of going through the builder, these tests fail.
    /// </summary>
    class IlInterpreterHostSurfaceTests
    {
        // Normalize a registered type to the link.xml fullname the generator emits:
        // constructed generics collapse to their open definition; nested '+' becomes '/'.
        static string LinkName(Type t)
        {
            var def = t.IsGenericType && !t.IsGenericTypeDefinition ? t.GetGenericTypeDefinition() : t;
            return def.FullName?.Replace('+', '/');
        }

        static HashSet<string> RegisteredLinkNames(HostBinding binding) =>
            binding.RegisteredTypes
                .Select(LinkName)
                .Where(n => !string.IsNullOrEmpty(n))
                .ToHashSet(StringComparer.Ordinal);

        [Test]
        public void CreateStandard_IsDeterministic()
        {
            var a = RegisteredLinkNames(IlInterpreterHostBindings.CreateStandard());
            var b = RegisteredLinkNames(IlInterpreterHostBindings.CreateStandard());
            Assert.That(a, Is.EquivalentTo(b), "CreateStandard() must build an identical surface every call.");
        }

        [Test]
        public void CreateStandard_IncludesDebugAndKeyHazardTypes()
        {
            var names = RegisteredLinkNames(IlInterpreterHostBindings.CreateStandard());
            // Debug consolidated into the canonical surface (was eval-only string-wired before).
            Assert.That(names, Contains.Item("UnityEngine.Debug"), "Debug must be in RegisteredTypes so link.xml preserves it.");
            // Reflection-walked types that IL2CPP would strip without link.xml.
            Assert.That(names, Contains.Item("UnityEngine.GameObject"));
            Assert.That(names, Contains.Item("UnityEngine.Transform"));
            Assert.That(names, Contains.Item("UnityEngine.Vector3"));
        }

        [Test]
        public void AutoBindPolicy_SkipsBcl_ButNotSystemPrefixedGameAssemblies()
        {
            // The BCL must stay authoritative (the AllowBcl shims collapse doubles into the
            // script's float number space), but a game assembly that merely starts with the word —
            // "SystemsRuntime" — must stay auto-bindable, or reloaded code calling its types fails
            // with "not registered" even though the dev link.xml preserved it.
            foreach (var skipped in new[]
                { "System", "System.Core", "mscorlib", "netstandard", "Unity.Pipeline", "Unity.Pipeline.IlInterpreter" })
                Assert.IsTrue(IlInterpreterHostBindings.IsAutoBindSkippedAssembly(skipped),
                    $"'{skipped}' must be skipped by the auto-bind policy.");

            foreach (var bindable in new[]
                { "SystemsRuntime", "UnityChanDemo", "Assembly-CSharp", "UnityEngine.CoreModule", "Unity.Burst" })
                Assert.IsFalse(IlInterpreterHostBindings.IsAutoBindSkippedAssembly(bindable),
                    $"'{bindable}' must remain resolvable by demand-time auto-bind.");
        }

        // The hot-reload sink builds CreateStandard() then overlays AllowType(targetType). The parity
        // claim is "identical modulo the target type": the overlay only adds, never removes.
        class DummyReloadTarget { public int Value; public void Tick() { } }

        [Test]
        public void HotReloadOverlay_OnlyAdds_TargetType()
        {
            var baseNames = RegisteredLinkNames(IlInterpreterHostBindings.CreateStandard());
            var overlay = RegisteredLinkNames(
                IlInterpreterHostBindings.CreateStandard().AllowType(typeof(DummyReloadTarget)));

            Assert.That(overlay, Is.SupersetOf(baseNames), "Overlay must not drop any standard type.");
            Assert.That(overlay, Contains.Item(LinkName(typeof(DummyReloadTarget))), "Overlay must register the target type.");
        }

        // The drift guarantee: link.xml preserves the full CreateStandard() surface (no host type dropped),
        // plus reloadable [HotReload] target components (so their private fields survive IL2CPP stripping).
        // Every emitted entry must be one or the other — nothing else leaks in.
        [Test]
        public void LinkXml_Preserves_HostSurface_And_Only_ReloadTargets_Beyond()
        {
            var hostSurface = RegisteredLinkNames(IlInterpreterHostBindings.CreateStandard());

            var xml = HotReloadLinkXmlGenerator.GenerateLinkXml();
            var emitted = Regex.Matches(xml, "<type fullname=\"([^\"]+)\"")
                .Select(m => m.Groups[1].Value)
                .ToHashSet(StringComparer.Ordinal);

            Assert.That(emitted, Is.SupersetOf(hostSurface),
                "link.xml must preserve every host type CreateStandard() registers.");

            foreach (var name in emitted.Except(hostSurface))
                Assert.IsTrue(IsReloadableTargetByLinkName(name),
                    $"link.xml preserved a type that is neither a host-surface type nor a [HotReload] target: {name}");
        }

        // Resolve a link.xml fullname ('/' = nested separator) to a loaded Type and check whether it carries
        // a class-level [HotReload] or any [HotReload]/[HotReloadWithOverrides] method — the same predicate
        // HotReloadLinkXmlGenerator uses to decide what reload targets to preserve.
        static bool IsReloadableTargetByLinkName(string linkName)
        {
            var full = linkName.Replace('/', '+');
            foreach (var asm in PipelineUtils.GetLoadedAssemblies())
            {
                Type t;
                try { t = asm.GetType(full, throwOnError: false); }
                catch { continue; }
                if (t == null) continue;

                if (t.IsDefined(typeof(HotReloadAttribute), inherit: false))
                    return true;
                const System.Reflection.BindingFlags f = System.Reflection.BindingFlags.Public
                    | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance
                    | System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.DeclaredOnly;
                foreach (var m in t.GetMethods(f))
                    if (m.IsDefined(typeof(HotReloadAttribute), inherit: false) ||
                        m.IsDefined(typeof(HotReloadWithOverridesAttribute), inherit: false))
                        return true;
                return false;
            }
            return false;
        }
    }
}
