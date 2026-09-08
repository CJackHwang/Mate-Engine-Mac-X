using System.Xml;
using NUnit.Framework;
using Unity.Pipeline.Editor.BuildProcessors;
using Unity.Pipeline.HotReload;

namespace Unity.Pipeline.Tests.Editor
{
    /// <summary>
    /// Tests for the generated link.xml that preserves the interpreter's host-binding surface under
    /// IL2CPP managed stripping. The build-time contribution runs only inside a real player build, but
    /// the XML generator is pure and editor-callable, so its output is verified directly here.
    /// </summary>
    class HotReloadLinkXmlGeneratorTests
    {
        [Test]
        public void GenerateLinkXml_IsWellFormedLinkerDocument()
        {
            var xml = HotReloadLinkXmlGenerator.GenerateLinkXml();

            var doc = new XmlDocument();
            Assert.DoesNotThrow(() => doc.LoadXml(xml), "Generated link.xml must be well-formed XML.");
            Assert.AreEqual("linker", doc.DocumentElement.Name, "Root element must be <linker>.");
            Assert.IsNotEmpty(doc.SelectNodes("/linker/assembly"), "Expected at least one <assembly> group.");
        }

        [Test]
        public void GenerateLinkXml_PreservesUnityEngineColor_TheGreenYellowCase()
        {
            // Color.greenYellow exists in the Unity API but is stripped from an IL2CPP player when the
            // built game never references it. AllowType<Color>() registers the whole type, so preserving
            // Color with preserve="all" is exactly what keeps greenYellow (and every other member)
            // callable from hot-reloaded code.
            var xml = HotReloadLinkXmlGenerator.GenerateLinkXml();

            var doc = new XmlDocument();
            doc.LoadXml(xml);

            var colorNode = doc.SelectSingleNode(
                "/linker/assembly/type[@fullname='UnityEngine.Color']");
            Assert.IsNotNull(colorNode, "UnityEngine.Color must be preserved so its members survive stripping.");
            Assert.AreEqual("all", colorNode.Attributes["preserve"]?.Value,
                "Color must be preserved with preserve=\"all\" to keep members like greenYellow.");
        }

        [Test]
        public void GenerateLinkXml_GroupsColorUnderCoreModule()
        {
            var xml = HotReloadLinkXmlGenerator.GenerateLinkXml();

            var doc = new XmlDocument();
            doc.LoadXml(xml);

            var node = doc.SelectSingleNode(
                "/linker/assembly[@fullname='UnityEngine.CoreModule']/type[@fullname='UnityEngine.Color']");
            Assert.IsNotNull(node, "Color should be grouped under its owning assembly, UnityEngine.CoreModule.");
        }

        [Test]
        public void GenerateLinkXml_WithPlayerAssemblies_PreservesUserAndEngineModulesWholesale()
        {
            // Demand-time auto-bind lets reloaded code call into any user or UnityEngine type, so the
            // dev build must preserve those assemblies wholesale; BCL and Unity.* package assemblies
            // stay per-type (the auto-bind policy declines the BCL, and Unity.* packages are AOT-heavy).
            var xml = HotReloadLinkXmlGenerator.GenerateLinkXml(
                new[] { "Assembly-CSharp", "MyGame.Gameplay", "UnityEngine.CoreModule", "UnityEngine.AIModule",
                        "System.Core", "mscorlib", "Unity.Burst", "Unity.Pipeline" },
                out _);

            var doc = new XmlDocument();
            doc.LoadXml(xml);

            foreach (var expected in new[]
                { "Assembly-CSharp", "MyGame.Gameplay", "UnityEngine.CoreModule", "UnityEngine.AIModule" })
            {
                var node = doc.SelectSingleNode($"/linker/assembly[@fullname='{expected}']");
                Assert.IsNotNull(node, $"{expected} must be preserved wholesale.");
                Assert.AreEqual("all", node.Attributes["preserve"]?.Value,
                    $"{expected} must carry assembly-level preserve=\"all\".");
                Assert.AreEqual(0, node.ChildNodes.Count,
                    $"{expected} is wholesale — per-type entries are subsumed.");
            }

            foreach (var excluded in new[] { "System.Core", "mscorlib", "Unity.Burst", "Unity.Pipeline" })
            {
                var node = doc.SelectSingleNode($"/linker/assembly[@fullname='{excluded}']");
                Assert.IsTrue(node == null || node.Attributes["preserve"] == null,
                    $"{excluded} must not be preserved wholesale.");
            }
        }

        [Test]
        public void GenerateLinkXml_WithoutPlayerAssemblies_OnlyInterpreterEngineIsWholesale()
        {
            // Null player-assembly set (unreadable linker input, or the parameterless test entry
            // point) degrades to per-type host-surface preservation plus the always-on interpreter
            // engine assemblies — nothing else may be preserved wholesale.
            var xml = HotReloadLinkXmlGenerator.GenerateLinkXml();

            var doc = new XmlDocument();
            doc.LoadXml(xml);

            foreach (XmlNode asm in doc.SelectNodes("/linker/assembly"))
            {
                var name = asm.Attributes["fullname"]?.Value;
                if (name == "Unity.Pipeline.IlInterpreter" ||
                    name == "System.Reflection.Metadata" ||
                    name == "System.Collections.Immutable")
                    continue;
                Assert.IsNull(asm.Attributes["preserve"],
                    $"without a player assembly set '{name}' may not be preserved wholesale");
            }
        }

        [Test]
        public void GenerateLinkXml_AlwaysPreservesInterpreterEngineAssembliesWholesale()
        {
            // The interpreter parses pushed override IL via System.Reflection.Metadata and resolves
            // host members by name; under IL2CPP stripping those paths look unused. This preservation
            // used to live in a static Runtime/link.xml, but that file applied to release builds too
            // (real size cost for machinery that is compiled out of release players). The generator
            // only contributes to development builds, so it now owns this set — with or without a
            // player-assembly list.
            foreach (var xml in new[]
            {
                HotReloadLinkXmlGenerator.GenerateLinkXml(),
                HotReloadLinkXmlGenerator.GenerateLinkXml(new[] { "Assembly-CSharp" }, out _),
            })
            {
                var doc = new XmlDocument();
                doc.LoadXml(xml);

                foreach (var expected in new[]
                    { "Unity.Pipeline.IlInterpreter", "System.Reflection.Metadata", "System.Collections.Immutable" })
                {
                    var node = doc.SelectSingleNode($"/linker/assembly[@fullname='{expected}']");
                    Assert.IsNotNull(node, $"{expected} must be preserved so the interpreter survives stripping.");
                    Assert.AreEqual("all", node.Attributes["preserve"]?.Value,
                        $"{expected} must carry assembly-level preserve=\"all\".");
                }
            }
        }

        [Test]
        public void GenerateLinkXml_UnityPrefixedGameAssemblies_AreTreatedAsUserCode()
        {
            // A bare "Unity"/"System" prefix would misclassify game assemblies that merely start
            // with the word — "UnityChanDemo", "SystemsRuntime" — as engine/BCL code, silently
            // dropping them from wholesale preservation and from the [HotReload] target scan
            // (both share IsNonUserAssembly). Only real engine/BCL names are non-user.
            var xml = HotReloadLinkXmlGenerator.GenerateLinkXml(
                new[] { "UnityChanDemo", "SystemsRuntime", "Unity", "Unity.Burst", "System.Core" },
                out _);

            var doc = new XmlDocument();
            doc.LoadXml(xml);

            foreach (var user in new[] { "UnityChanDemo", "SystemsRuntime" })
            {
                var node = doc.SelectSingleNode($"/linker/assembly[@fullname='{user}']");
                Assert.IsNotNull(node, $"{user} is user code and must be preserved wholesale.");
                Assert.AreEqual("all", node.Attributes["preserve"]?.Value,
                    $"{user} must carry assembly-level preserve=\"all\".");
            }

            foreach (var engine in new[] { "Unity", "Unity.Burst", "System.Core" })
            {
                var node = doc.SelectSingleNode($"/linker/assembly[@fullname='{engine}']");
                Assert.IsTrue(node == null || node.Attributes["preserve"] == null,
                    $"{engine} is engine/BCL code and must not be preserved wholesale.");
            }
        }

        // Rescue probe: this test assembly is Unity.Pipeline.Tests.Editor — it matches the
        // engine-assembly name pattern, yet carries this [HotReload] member, standing in for a
        // game assembly named e.g. "Unity.MyGame".
        class EngineNamedAssemblyProbe { [HotReload] void Tick() { } }

        [Test]
        public void GenerateLinkXml_FindsHotReloadTargets_InEngineNamedAssemblies()
        {
            // The [HotReload] attribute must be authoritative for the target scan; the engine-name
            // check is only a fast path. [HotReload] lives in Unity.Pipeline, so an engine-named
            // assembly that references Unity.Pipeline can carry targets and must still be scanned —
            // otherwise its private state is stripped and reload fails on device only.
            var xml = HotReloadLinkXmlGenerator.GenerateLinkXml();

            var expected = typeof(EngineNamedAssemblyProbe).FullName.Replace('+', '/');
            StringAssert.Contains($"<type fullname=\"{expected}\" preserve=\"all\"/>", xml,
                "[HotReload] targets in an engine-named assembly must still be preserved.");
        }

        [Test]
        public void GenerateLinkXml_WithPlayerAssemblies_DropsReloadTargetsNotStagedForTheBuild()
        {
            // The AppDomain scan sees editor-only and test assemblies that are not part of the
            // player build; their [HotReload] entries are noise in a game build's link.xml. When
            // the staged set is known, only reload targets actually in the build are emitted —
            // which also covers user test asmdefs. A test-player build stages its test
            // assemblies, so fixtures keep their entries exactly where they matter.
            var probe = typeof(EngineNamedAssemblyProbe).FullName.Replace('+', '/');

            var xml = HotReloadLinkXmlGenerator.GenerateLinkXml(new[] { "Assembly-CSharp" }, out _);
            StringAssert.DoesNotContain(probe, xml,
                "an unstaged assembly must not contribute reload-target entries");
        }

        [Test]
        public void GenerateLinkXml_PromotesStagedEngineNamedAssembliesCarryingHotReloadTargets()
        {
            // A staged, engine-named assembly carrying [HotReload] members has identified itself
            // as user code participating in hot reload — a package-like name (e.g. "Unity.MyGame")
            // must not demote it. Preserving only its [HotReload] types would leave the rest of the
            // assembly strippable, so demand-time auto-bind could hit stripped members on device
            // only; instead it is preserved wholesale like any other user assembly. This test
            // assembly (Unity.Pipeline.Tests.Editor, carrying the probe above) plays that role.
            var xml = HotReloadLinkXmlGenerator.GenerateLinkXml(
                new[] { "Assembly-CSharp", "Unity.Pipeline.Tests.Editor" }, out _);

            var doc = new XmlDocument();
            doc.LoadXml(xml);

            var node = doc.SelectSingleNode("/linker/assembly[@fullname='Unity.Pipeline.Tests.Editor']");
            Assert.IsNotNull(node, "the promoted assembly must appear in the link.xml");
            Assert.AreEqual("all", node.Attributes["preserve"]?.Value,
                "the promoted assembly must carry assembly-level preserve=\"all\"");
            Assert.AreEqual(0, node.ChildNodes.Count,
                "wholesale preservation subsumes the per-type [HotReload] entries");
        }

        [Test]
        public void CollectPlayerAssemblyNames_WithEmptyInputDir_RecoversUnityEngineModulesFromTheEditor()
        {
            // The linker input directory is empty at IUnityLinkerProcessor time on some build backends
            // (the symptom that stripped UITK ctors on device: a live-added `new Slider()` couldn't
            // resolve because UnityEngine.UIElementsModule wasn't preserved wholesale). The collector
            // must fall back to the editor's own assembly view so wholesale preservation still fires.
            var names = HotReloadLinkXmlGenerator.CollectPlayerAssemblyNames("/no/such/linker/input/dir");

            Assert.IsNotNull(names, "an empty input dir must still yield the editor's player-assembly view");
            CollectionAssert.Contains(names, "UnityEngine.UIElementsModule",
                "the UnityEngine module carrying Slider must be recovered so its ctors survive stripping");
            CollectionAssert.Contains(names, "UnityEngine.CoreModule",
                "core UnityEngine modules must be recovered from the loaded editor assemblies");
        }

        [Test]
        public void CollectPlayerAssemblyNames_FeedsWholesalePreservationOfUIElements()
        {
            // End-to-end for the Slider fix: the recovered assembly set, fed through the generator,
            // must preserve UnityEngine.UIElementsModule wholesale (assembly-level preserve="all"),
            // which is what keeps Slider's constructors callable from a hot-reloaded body on device.
            var names = HotReloadLinkXmlGenerator.CollectPlayerAssemblyNames(null);
            var xml = HotReloadLinkXmlGenerator.GenerateLinkXml(names, out _);

            var doc = new XmlDocument();
            doc.LoadXml(xml);

            var node = doc.SelectSingleNode("/linker/assembly[@fullname='UnityEngine.UIElementsModule']");
            Assert.IsNotNull(node, "UnityEngine.UIElementsModule must be preserved so Slider's ctors survive.");
            Assert.AreEqual("all", node.Attributes["preserve"]?.Value,
                "UnityEngine.UIElementsModule must carry assembly-level preserve=\"all\".");
        }

        [Test]
        public void GenerateLinkXml_EveryTypeIsPreservedAll()
        {
            var xml = HotReloadLinkXmlGenerator.GenerateLinkXml();

            var doc = new XmlDocument();
            doc.LoadXml(xml);

            foreach (XmlNode type in doc.SelectNodes("/linker/assembly/type"))
            {
                Assert.AreEqual("all", type.Attributes["preserve"]?.Value,
                    $"Type {type.Attributes["fullname"]?.Value} must use preserve=\"all\".");
            }
        }
    }
}
