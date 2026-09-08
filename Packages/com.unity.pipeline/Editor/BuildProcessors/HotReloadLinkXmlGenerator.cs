using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.Compilation;
using UnityEditor.UnityLinker;
using UnityEngine;
using Unity.Pipeline.Compilation;
using Unity.Pipeline.HotReload;
using IlInterpreter.Interpreter;
using Assembly = System.Reflection.Assembly;

namespace Unity.Pipeline.Editor.BuildProcessors
{
    /// <summary>
    /// Contributes an auto-generated <c>link.xml</c> that preserves the host-binding surface the
    /// interpreter exposes to hot-reloaded / eval'd code, so IL2CPP managed stripping cannot remove
    /// members the built game never referenced (symptom: "Host method '…' is not registered in the
    /// binding", on device only).
    ///
    /// Unity's own generated <c>link.xml</c> can't cover this: it preserves what the build *references*,
    /// and the whole point of hot reload is to call members the build never referenced. So preserve
    /// (a) every type in <see cref="HostBinding.RegisteredTypes"/> with <c>preserve="all"</c>, and
    /// (b) — because demand-time auto-bind (<see cref="HostBinding.AutoBindResolver"/>) lets reloaded
    /// code reach ANY user or UnityEngine type — the build's user assemblies and UnityEngine modules
    /// wholesale.
    ///
    /// Development builds only: the interpreter reload/eval sinks compile out of release players.
    /// Also preserves the interpreter engine itself (IlInterpreter + its reflection/metadata
    /// dependencies) — kept here, rather than in a static <c>Runtime/link.xml</c>, precisely so
    /// release players don't pay the size cost for machinery they compile out.
    /// </summary>
    class HotReloadLinkXmlGenerator : IUnityLinkerProcessor
    {
        public int callbackOrder => 0;

        public string GenerateAdditionalLinkXmlFile(BuildReport report, UnityLinkerBuildPipelineData data)
        {
            // Only development builds carry the interpreter; release builds need no extra preservation.
            if (report == null || !report.summary.options.HasFlag(BuildOptions.Development))
                return string.Empty;

            // The managed assemblies staged for this player build — the universe for the wholesale
            // preservation pass. See CollectPlayerAssemblyNames: it unions the linker input directory
            // with the editor's own player-assembly view, because on some build backends the input
            // directory is empty at IUnityLinkerProcessor time and relying on it alone silently
            // collapsed preservation to the explicit per-type surface — stripping everything
            // demand-time auto-bind reaches (e.g. a live-added `new Slider()`, whose UITK ctors the
            // built game never referenced) on device only.
            var playerAssemblies = CollectPlayerAssemblyNames(data.inputDirectory);

            var path = Path.GetFullPath(FileUtil.GetUniqueTempPathInProject() + "-hostbindings.link.xml");
            File.WriteAllText(path, GenerateLinkXml(playerAssemblies, out var stats));
            // The stats line is the diagnostic: if a game assembly with [HotReload] types is missing
            // from the reloadable-target list (e.g. its name matches the engine-assembly skip
            // prefixes), its private state can be stripped and binding fails on device only.
            Debug.Log($"Pipeline: generated hot-reload host-binding link.xml at {path} — {stats}");
            return path;
        }

        /// <summary>
        /// The simple names of the managed assemblies that end up in this player build — the universe
        /// the wholesale-preservation pass draws from. Unions two sources so a gap in either can't
        /// silently collapse preservation:
        /// <list type="bullet">
        /// <item>the linker input directory (the exact staged set, incl. test assemblies for a test
        /// player) — recursively, since some backends nest the managed DLLs in a subfolder;</item>
        /// <item>the editor's own view — <see cref="CompilationPipeline"/> player assemblies (user
        /// code) plus every loaded <c>UnityEngine*</c> module (the precompiled engine modules
        /// demand-time auto-bind can construct from) — which stays populated even when the input
        /// directory is empty at <see cref="IUnityLinkerProcessor"/> time.</item>
        /// </list>
        /// A union only adds; it never drops an assembly the input scan found, so a test player keeps
        /// its test assemblies. Returns null only if every source came up empty (preserve per-type only).
        /// </summary>
        internal static string[] CollectPlayerAssemblyNames(string inputDirectory)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);

            try
            {
                if (!string.IsNullOrEmpty(inputDirectory) && Directory.Exists(inputDirectory))
                    foreach (var dll in Directory.GetFiles(inputDirectory, "*.dll", SearchOption.AllDirectories))
                        names.Add(Path.GetFileNameWithoutExtension(dll));
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"Pipeline: could not enumerate linker input assemblies " +
                    $"('{inputDirectory}'): {ex.Message} — falling back to the editor's " +
                    "player-assembly view for wholesale preservation.");
            }

            try
            {
                foreach (var a in CompilationPipeline.GetAssemblies(AssembliesType.Player))
                    if (!string.IsNullOrEmpty(a.name))
                        names.Add(a.name);
            }
            catch { /* editor API unavailable in this context — the UnityEngine sweep still runs */ }

            foreach (var a in PipelineUtils.GetLoadedAssemblies())
            {
                var n = a.GetName().Name;
                if (!string.IsNullOrEmpty(n) && n.StartsWith("UnityEngine", StringComparison.Ordinal))
                    names.Add(n);
            }

            return names.Count > 0 ? new List<string>(names).ToArray() : null;
        }

        /// <summary>
        /// Build the interpreter's default host binding and emit a <c>link.xml</c> preserving every
        /// registered type, grouped by owning assembly. Pure and deterministic — directly unit-testable.
        /// Derives from <see cref="IlInterpreterHostBindings.CreateStandard"/> — the builder the hot-reload
        /// executor runs — so the preservation set cannot drift from the runtime surface. The per-reload
        /// <c>AllowType(targetType)</c> overlay is excluded: the target is the user's own component,
        /// which the built player references and IL2CPP won't strip.
        /// </summary>
        public static string GenerateLinkXml() => GenerateLinkXml(null, out _);

        public static string GenerateLinkXml(out string stats) => GenerateLinkXml(null, out stats);

        /// <summary><paramref name="stats"/>: one-line summary of what was preserved, logged at build
        /// time so a missing game assembly is visible in the build log.
        /// <paramref name="playerAssemblyNames"/>: simple names of the managed assemblies staged for
        /// the player build; when provided, user assemblies and UnityEngine modules from that set are
        /// preserved wholesale (including engine-named assemblies carrying [HotReload] members — the
        /// attribute marks them as user code), and reload-target entries are limited to assemblies in
        /// the set (editor-only and test assemblies drop out of game builds); when null, only per-type
        /// preservation is emitted, unfiltered.</summary>
        public static string GenerateLinkXml(IEnumerable<string> playerAssemblyNames, out string stats)
        {
            var binding = IlInterpreterHostBindings.CreateStandard();

            // SortedSet/SortedDictionary give a stable, diffable ordering and dedupe (closed generics
            // collapsing onto their definition can produce repeats).
            var byAssembly = new SortedDictionary<string, SortedSet<string>>(StringComparer.Ordinal);
            foreach (var t in binding.RegisteredTypes)
            {
                // Preserve the open definition for constructed generics (a closed-generic fullname is
                // assembly-qualified and won't match in link.xml).
                var def = t.IsGenericType && !t.IsGenericTypeDefinition ? t.GetGenericTypeDefinition() : t;

                var asm = def.Assembly.GetName().Name;
                var full = def.FullName;
                if (string.IsNullOrEmpty(asm) || string.IsNullOrEmpty(full))
                    continue;

                full = full.Replace('+', '/'); // link.xml uses '/' as the nested-type separator

                if (!byAssembly.TryGetValue(asm, out var types))
                    byAssembly[asm] = types = new SortedSet<string>(StringComparer.Ordinal);
                types.Add(full);
            }

            // Also preserve the reloadable target components. The target type IS referenced by the
            // built player, but a PRIVATE field only the interpreter touches can still be stripped,
            // breaking the reflection walk; preserve="all" keeps every member so
            // AllowNonPublicInstanceMembers(targetType) can resolve private state on device.
            int hostTypeCount = 0;
            foreach (var kv in byAssembly)
                hostTypeCount += kv.Value.Count;

            // The AppDomain scan below also sees editor-only and test assemblies that are not part
            // of the player build; when the staged set is known, reload targets outside it are
            // dropped as noise. A test-player build stages its test assemblies, so fixtures keep
            // their entries exactly where they matter.
            HashSet<string> staged = null;
            if (playerAssemblyNames != null)
            {
                staged = new HashSet<string>(StringComparer.Ordinal);
                foreach (var name in playerAssemblyNames)
                    if (!string.IsNullOrEmpty(name))
                        staged.Add(name);
            }

            int targetTypeCount = 0;
            var targetAssemblies = new SortedSet<string>(StringComparer.Ordinal);
            var engineNamedTargetAssemblies = new SortedSet<string>(StringComparer.Ordinal);
            foreach (var t in CollectReloadableTargetTypes())
            {
                var asm = t.Assembly.GetName().Name;
                var full = t.FullName;
                if (string.IsNullOrEmpty(asm) || string.IsNullOrEmpty(full))
                    continue;
                if (staged != null && !staged.Contains(asm))
                    continue;
                full = full.Replace('+', '/');
                if (!byAssembly.TryGetValue(asm, out var types))
                    byAssembly[asm] = types = new SortedSet<string>(StringComparer.Ordinal);
                types.Add(full);
                targetTypeCount++;
                targetAssemblies.Add(asm);
                if (IsNonUserAssembly(asm))
                    engineNamedTargetAssemblies.Add(asm);
            }

            // Dev-build widening: demand-time auto-bind (HostBinding.AutoBindResolver) lets a reloaded
            // or eval'd body call into ANY user or UnityEngine type, so RegisteredTypes is not the full
            // callable set — preserve the build's user assemblies and UnityEngine modules wholesale.
            // Unity.* packages (Burst, Collections, …) stay per-type: AOT-heavy and rarely touched from
            // reload bodies. (The project-extension seam that could promote a specific type was
            // deferred to a future release, so on-device reload bodies can only reach package types
            // the stripper kept.)
            var wholesale = new SortedSet<string>(StringComparer.Ordinal);
            // The interpreter engine itself: it parses pushed override IL via
            // System.Reflection.Metadata and resolves host members by name, so those paths look
            // unused to the stripper. Emitted here (dev builds only) rather than in a static
            // Runtime/link.xml, which would bloat release players that compile the interpreter out.
            foreach (var name in InterpreterEngineAssemblies)
                wholesale.Add(name);
            if (staged != null)
            {
                foreach (var name in staged)
                {
                    if (name.StartsWith("UnityEngine", StringComparison.Ordinal)) wholesale.Add(name);
                    else if (!IsNonUserAssembly(name)) wholesale.Add(name);
                }

                // A staged, engine-named assembly carrying [HotReload] members has identified itself
                // as user code participating in hot reload — a package-like name (e.g. "Unity.MyGame")
                // must not demote it. Preserving only its [HotReload] types would leave the rest of
                // it strippable (demand-time auto-bind would hit stripped members on device only),
                // so promote it into the wholesale set like any other user assembly.
                foreach (var asm in engineNamedTargetAssemblies)
                    wholesale.Add(asm);
            }

            stats = $"{hostTypeCount} host-binding type(s) and {targetTypeCount} reloadable target type(s) " +
                $"across {byAssembly.Count} assembly(ies); target assemblies: " +
                (targetAssemblies.Count > 0 ? string.Join(", ", targetAssemblies) : "none") +
                $"; {wholesale.Count} assembly(ies) preserved wholesale for demand-time auto-bind";

            var sb = new StringBuilder();
            sb.AppendLine("<linker>");
            sb.AppendLine("  <!--");
            sb.AppendLine("    AUTO-GENERATED by HotReloadLinkXmlGenerator (development builds only). Do not edit.");
            sb.AppendLine("    Preserves the IlInterpreter interpreter engine and its host-binding surface");
            sb.AppendLine("    (HostBinding.RegisteredTypes), plus — wholesale — the build's user assemblies and");
            sb.AppendLine("    UnityEngine modules, so hot-reloaded/eval'd code can call members the built game");
            sb.AppendLine("    never referenced (including types the interpreter binds on demand), which IL2CPP");
            sb.AppendLine("    managed stripping would otherwise remove.");
            sb.AppendLine("  -->");
            // Wholesale assemblies first (their per-type entries are subsumed), then per-type blocks.
            foreach (var name in wholesale)
                sb.AppendLine($"  <assembly fullname=\"{name}\" preserve=\"all\"/>");
            foreach (var kv in byAssembly)
            {
                if (wholesale.Contains(kv.Key)) continue;
                sb.AppendLine($"  <assembly fullname=\"{kv.Key}\">");
                foreach (var type in kv.Value)
                    sb.AppendLine($"    <type fullname=\"{type}\" preserve=\"all\"/>");
                sb.AppendLine("  </assembly>");
            }
            sb.AppendLine("</linker>");
            return sb.ToString();
        }

        // The IlInterpreter engine and its reflection/metadata dependencies — always preserved
        // wholesale so pushed hot reload keeps working on device.
        static readonly string[] InterpreterEngineAssemblies =
        {
            "Unity.Pipeline.IlInterpreter",
            "System.Reflection.Metadata",
            "System.Collections.Immutable",
        };

        // Engine/BCL/tooling assembly names — NOT user code. Decides which staged assemblies are
        // preserved wholesale, and which reload-target assemblies get the engine-named warning.
        // Matching is deliberately precise (exact "Unity"/"System" or a dotted prefix, never the
        // bare word) so a game assembly that merely starts with the word — "UnityChanDemo",
        // "SystemsRuntime" — is still preserved wholesale.
        static bool IsNonUserAssembly(string name) =>
            name == "Unity" ||
            name.StartsWith("Unity.", StringComparison.Ordinal) ||
            name.StartsWith("UnityEngine", StringComparison.Ordinal) ||
            name.StartsWith("UnityEditor", StringComparison.Ordinal) ||
            name == "System" ||
            name.StartsWith("System.", StringComparison.Ordinal) ||
            name.StartsWith("Microsoft.", StringComparison.Ordinal) ||
            name.StartsWith("Mono.", StringComparison.Ordinal) ||
            name == "mscorlib" ||
            name == "netstandard" ||
            name.StartsWith("nunit.", StringComparison.Ordinal);

        /// <summary>
        /// User-code types that carry a class-level <c>[HotReload]</c> or any <c>[HotReload]</c> /
        /// <c>[HotReloadWithOverrides]</c> method — i.e. every type whose members can be reloaded through
        /// the interpreter and therefore needs its (possibly private) fields kept under stripping.
        /// The attributes live in <c>Unity.Pipeline</c>, so only assemblies referencing it can carry
        /// them — a cheap, metadata-only check that gates the expensive type scan. Assembly *names*
        /// play no part here, so game assemblies named like packages ("Unity.MyGame") are covered.
        /// </summary>
        static IEnumerable<Type> CollectReloadableTargetTypes()
        {
            var results = new List<Type>();
            foreach (var asm in PipelineUtils.GetLoadedAssemblies())
            {
                if (!ReferencesPipelineRuntime(asm))
                    continue;

                Type[] types;
                try { types = asm.GetTypes(); }
                catch (ReflectionTypeLoadException ex) { types = ex.Types; }
                catch { continue; }

                foreach (var t in types)
                {
                    if (t == null) continue;
                    try
                    {
                        if (t.IsDefined(typeof(HotReloadAttribute), inherit: false) || HasReloadableMethod(t))
                            results.Add(t);
                    }
                    catch { /* type that can't be inspected — skip */ }
                }
            }
            return results;
        }

        static bool ReferencesPipelineRuntime(Assembly asm)
        {
            var pipeline = typeof(HotReloadAttribute).Assembly.GetName().Name;
            try
            {
                foreach (var reference in asm.GetReferencedAssemblies())
                    if (string.Equals(reference.Name, pipeline, StringComparison.Ordinal))
                        return true;
            }
            catch { /* dynamic/unloadable assembly — treat as not referencing */ }
            return false;
        }

        static bool HasReloadableMethod(Type t)
        {
            const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic
                | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
            foreach (var m in t.GetMethods(flags))
            {
                if (m.IsDefined(typeof(HotReloadAttribute), inherit: false) ||
                    m.IsDefined(typeof(HotReloadWithOverridesAttribute), inherit: false))
                    return true;
            }
            return false;
        }
    }
}
