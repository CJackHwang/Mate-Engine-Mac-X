using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Unity.Pipeline.Config;
using Unity.Pipeline.Editor.BuildProcessors;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEditor.TestTools;
using UnityEngine;
using UnityEngine.TestTools;

namespace Unity.Pipeline.Tests.Editor
{
    /// <summary>
    /// Tests for Pipeline runtime build support using RuntimePipelineConfig. Validates build
    /// processor behavior and the transient RuntimePipelineConfig/RuntimePipelineBuildInfo asset
    /// generation/cleanup.
    /// </summary>
    class RuntimePipelineBuildTests
    {
        private const string TransientConfigAssetPath = "Assets/Settings/Pipeline/Resources/RuntimePipelineConfig.asset";
        private const string BuildInfoAssetPath = "Assets/Settings/Pipeline/Resources/RuntimePipelineBuildInfo.asset";
        private PipelineRuntimeBuildProcessor m_BuildProcessor;
        private bool m_PreviousDevelopment;

        [SetUp]
        public void SetUp()
        {
            RuntimePipelineResourceTestGuard.AssertNoConfigAssetExists();

            m_BuildProcessor = new PipelineRuntimeBuildProcessor();
            DeleteIfExists(TransientConfigAssetPath);
            DeleteIfExists(BuildInfoAssetPath);

            // Force a Development Build context so OnPreprocessBuild never enters the interactive
            // security dialog (EditorUtility.DisplayDialogComplex): popping it for real here would
            // block the live Editor waiting for a click nobody can give. That dialog and the
            // release-build warning are verified manually instead.
            m_PreviousDevelopment = EditorUserBuildSettings.development;
            EditorUserBuildSettings.development = true;
        }

        [TearDown]
        public void TearDown()
        {
            EditorUserBuildSettings.development = m_PreviousDevelopment;
            DeleteIfExists(TransientConfigAssetPath);
            DeleteIfExists(BuildInfoAssetPath);
            RuntimePipelineResourceTestGuard.DeleteConfigIfExists();
        }

        private static void DeleteIfExists(string path)
        {
            if (AssetDatabase.LoadAssetAtPath<Object>(path) != null)
                AssetDatabase.DeleteAsset(path);
        }

        private static void CreateConfig(bool enableInBuilds)
        {
            var config = ScriptableObject.CreateInstance<RuntimePipelineConfig>();
            config.enableInBuilds = enableInBuilds;
            config.port = 7920;
            config.Save();
            Object.DestroyImmediate(config);
        }

        /// <summary>
        /// Write both transient build-only assets directly, simulating what an interrupted build
        /// (crash, force-quit) leaves behind before OnPostprocessBuild ever ran. Uses port 7999 and
        /// an obviously-fake reload root so a test can tell "this is the stale leftover" apart from
        /// whatever the current authored config/CollectProjectRoots would actually produce.
        /// </summary>
        private static void SeedStaleTransientAssets(bool enableInBuilds)
        {
            EnsureFolder(Path.GetDirectoryName(TransientConfigAssetPath).Replace('\\', '/'));

            var staleConfig = ScriptableObject.CreateInstance<RuntimePipelineConfig>();
            staleConfig.enableInBuilds = enableInBuilds;
            staleConfig.port = 7999;
            AssetDatabase.CreateAsset(staleConfig, TransientConfigAssetPath);

            var staleBuildInfo = ScriptableObject.CreateInstance<RuntimePipelineBuildInfo>();
            staleBuildInfo.allowedReloadRoots = new List<string> { "Z:/stale/leftover/root" };
            AssetDatabase.CreateAsset(staleBuildInfo, BuildInfoAssetPath);

            AssetDatabase.SaveAssets();
        }

        private static void EnsureFolder(string folder)
        {
            var parts = folder.Split('/');
            var current = parts[0];
            for (var i = 1; i < parts.Length; i++)
            {
                var next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next))
                    AssetDatabase.CreateFolder(current, parts[i]);
                current = next;
            }
        }

#if UNITY_6000_3_OR_NEWER
        private BuildCallbackContext CreateMockBuildReport()
#else
        private BuildReport CreateMockBuildReport()
#endif
        {
            // Neither preprocess nor postprocess actually reads the report parameter.
            return null;
        }

        // authorDisabledConfig: whether an authored RuntimePipelineConfig with enableInBuilds=false
        // exists (vs. no config file at all). seedStaleAssets: whether a leftover ENABLED transient
        // asset pair (as an interrupted earlier build would leave) is present beforehand. The
        // seedStaleAssets:true rows are the regression case: before the fix, an early return in
        // OnPreprocessBuild left the stale enabled asset in place, so a build that should have
        // Pipeline disabled would still ship it.
        [TestCase(false, false)]
        [TestCase(false, true)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        public void Preprocess_PipelineNotShipping_LeavesNoTransientAssets(bool authorDisabledConfig, bool seedStaleAssets)
        {
            if (authorDisabledConfig)
                CreateConfig(enableInBuilds: false);
            if (seedStaleAssets)
                SeedStaleTransientAssets(enableInBuilds: true);

            Assert.DoesNotThrow(() => m_BuildProcessor.OnPreprocessBuild(CreateMockBuildReport()));

            Assert.IsNull(AssetDatabase.LoadAssetAtPath<Object>(BuildInfoAssetPath),
                "No build-info asset should remain when Pipeline is not shipping in this build.");
            Assert.IsNull(AssetDatabase.LoadAssetAtPath<Object>(TransientConfigAssetPath),
                "No transient config asset should remain when Pipeline is not shipping in this build " +
                "— including a stale, possibly-enabled one left behind by a previous interrupted build.");
        }

        [Test]
        public void Preprocess_StaleTransientAssets_PipelineEnabled_ReplacesWithCurrentValues()
        {
            // Stale leftover pretends Pipeline was disabled and points at a fake reload root.
            SeedStaleTransientAssets(enableInBuilds: false);
            // Current authored config: enabled, port 7920 (CreateConfig's fixed value).
            CreateConfig(enableInBuilds: true);

            m_BuildProcessor.OnPreprocessBuild(CreateMockBuildReport());

            var baked = AssetDatabase.LoadAssetAtPath<RuntimePipelineConfig>(TransientConfigAssetPath);
            Assert.IsNotNull(baked, "OnPreprocessBuild should bake the current authored config");
            Assert.IsTrue(baked.enableInBuilds, "Baked asset must reflect the current authored config, not the stale leftover");
            Assert.AreEqual(7920, baked.port, "Baked asset must reflect the current authored config's port, not the stale leftover's 7999");

            var buildInfo = AssetDatabase.LoadAssetAtPath<RuntimePipelineBuildInfo>(BuildInfoAssetPath);
            Assert.IsNotNull(buildInfo);
            var expectedRoots = PipelineRuntimeBuildProcessor.CollectProjectRoots();
            CollectionAssert.AreEquivalent(expectedRoots, buildInfo.allowedReloadRoots,
                "Build-info asset must reflect the current project roots, not the stale leftover's fake root");
        }

        [Test]
        public void Preprocess_StaleAssetWithMismatchedType_IsStillPurged()
        {
            // Simulate the worst-case leftover: an asset at RuntimePipelineConfig's path that can no
            // longer be loaded AS RuntimePipelineConfig (e.g. a script reference broken by a package
            // upgrade). A typed AssetDatabase.LoadAssetAtPath<RuntimePipelineConfig> check would miss
            // this entirely and leave it on disk, inside Resources, for the next build to package.
            EnsureFolder(Path.GetDirectoryName(TransientConfigAssetPath).Replace('\\', '/'));
            var wrongTypeAsset = ScriptableObject.CreateInstance<RuntimePipelineBuildInfo>();
            AssetDatabase.CreateAsset(wrongTypeAsset, TransientConfigAssetPath);
            AssetDatabase.SaveAssets();
            Assert.IsNull(AssetDatabase.LoadAssetAtPath<RuntimePipelineConfig>(TransientConfigAssetPath),
                "Precondition: a typed load must not find the mismatched-type asset — that's the gap this test guards against");

            m_BuildProcessor.OnPreprocessBuild(CreateMockBuildReport());

            Assert.IsNull(AssetDatabase.LoadAssetAtPath<Object>(TransientConfigAssetPath),
                "A stale asset must be purged even when it cannot be loaded as its expected type");
        }

        [Test]
        public void BuildProcessor_ConfigEnabled_WritesBuildInfoWithProjectRoots()
        {
            CreateConfig(enableInBuilds: true);

            m_BuildProcessor.OnPreprocessBuild(CreateMockBuildReport());

            var buildInfo = AssetDatabase.LoadAssetAtPath<RuntimePipelineBuildInfo>(BuildInfoAssetPath);
            Assert.IsNotNull(buildInfo, "OnPreprocessBuild should create the build-info asset when enabled");
            var expectedRoots = PipelineRuntimeBuildProcessor.CollectProjectRoots();
            CollectionAssert.AreEquivalent(expectedRoots, buildInfo.allowedReloadRoots);
        }

        [Test]
        public void BuildProcessor_ConfigEnabled_WritesTransientConfigAssetWithSameValues()
        {
            CreateConfig(enableInBuilds: true);

            m_BuildProcessor.OnPreprocessBuild(CreateMockBuildReport());

            var baked = AssetDatabase.LoadAssetAtPath<RuntimePipelineConfig>(TransientConfigAssetPath);
            Assert.IsNotNull(baked, "OnPreprocessBuild should bake the authored config into a transient Resources asset");
            Assert.IsTrue(baked.enableInBuilds);
            Assert.AreEqual(7920, baked.port);
        }

        [Test]
        public void BuildProcessor_PostprocessBuild_DeletesBuildInfoAndConfigAssets()
        {
            CreateConfig(enableInBuilds: true);
            m_BuildProcessor.OnPreprocessBuild(CreateMockBuildReport());
            Assert.IsNotNull(AssetDatabase.LoadAssetAtPath<RuntimePipelineBuildInfo>(BuildInfoAssetPath),
                "Precondition: build-info asset must exist before postprocess runs");
            Assert.IsNotNull(AssetDatabase.LoadAssetAtPath<RuntimePipelineConfig>(TransientConfigAssetPath),
                "Precondition: transient config asset must exist before postprocess runs");

            // OnPostprocessBuild's signature is not versioned by UNITY_6000_3_OR_NEWER (unlike
            // OnPreprocessBuild), so it always takes a BuildReport — pass null directly rather than
            // via CreateMockBuildReport(), whose return type tracks the versioned preprocess signature.
            m_BuildProcessor.OnPostprocessBuild(null);

            Assert.IsNull(AssetDatabase.LoadAssetAtPath<RuntimePipelineBuildInfo>(BuildInfoAssetPath),
                "OnPostprocessBuild must remove the transient build-info asset");
            Assert.IsNull(AssetDatabase.LoadAssetAtPath<RuntimePipelineConfig>(TransientConfigAssetPath),
                "OnPostprocessBuild must remove the transient config asset");
        }

        // The enableInBuilds-without-Development-Build security warning/dialog
        // (PipelineRuntimeBuildProcessor's !EditorUserBuildSettings.development branch) is
        // deliberately not covered here: exercising it for real would pop a modal dialog and block
        // the live Editor. Verified manually instead.
    }

    /// <summary>
    /// Platform-specific build tests following Unity test patterns.
    /// Tests actual build generation with authored RuntimePipelineConfig settings.
    /// </summary>
    [RequirePlatformSupport(BuildTarget.StandaloneWindows64)]
    class RuntimePipelineWindows64BuildTests : RuntimePipelineBuildTestBase
    {
        protected override BuildTarget Target => BuildTarget.StandaloneWindows64;
    }

    /// <summary>
    /// Abstract base class for build generation tests.
    /// Follows Unity test patterns for build validation.
    /// </summary>
    abstract class RuntimePipelineBuildTestBase
    {
        protected abstract BuildTarget Target { get; }
        protected string BuildDirectory => $"TestBuild/{GetType().Name}";
        private const string TransientConfigAssetPath = "Assets/Settings/Pipeline/Resources/RuntimePipelineConfig.asset";
        private const string TransientBuildInfoAssetPath = "Assets/Settings/Pipeline/Resources/RuntimePipelineBuildInfo.asset";
        private bool m_PreviousDevelopment;

        [SetUp]
        public void SetUp()
        {
            RuntimePipelineResourceTestGuard.AssertNoConfigAssetExists();

            // BuildOptions.Development on this test's BuildPipeline.BuildPlayer call does not
            // reliably flip the global EditorUserBuildSettings.development before OnPreprocessBuild
            // runs, so force it directly — otherwise the security dialog
            // (EditorUtility.DisplayDialogComplex) pops for real and blocks the live Editor.
            m_PreviousDevelopment = EditorUserBuildSettings.development;
            EditorUserBuildSettings.development = true;
        }

        [TearDown]
        public void TearDown()
        {
            EditorUserBuildSettings.development = m_PreviousDevelopment;
            RuntimePipelineResourceTestGuard.DeleteConfigIfExists();
        }

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            if (Directory.Exists(BuildDirectory))
                Directory.Delete(BuildDirectory, true);
        }

        [OneTimeTearDown]
        public void OneTimeTearDown()
        {
            if (Directory.Exists(BuildDirectory))
                Directory.Delete(BuildDirectory, true);
        }

        [Test]
        public void BuildPlayerWithRuntimeConfig_ValidConfiguration_BuildSucceeds()
        {
            if (!IsPlatformSupportInstalled(Target))
            {
                Assert.Ignore($"Platform support for {Target} is not installed");
            }

            // Note: When calling BuildPlayer the list of scenes is ALWAYS overridden with what is in the current build profile
            var testScenePath = CreateTestSceneWithConfig(validConfiguration: true);

            try
            {
                var buildOptions = new BuildPlayerOptions
                {
                    scenes = new[] { testScenePath },
                    locationPathName = Path.Combine(BuildDirectory, "TestBuild.exe"),
                    target = Target,
                    options = BuildOptions.Development // Use development for faster builds
                };

                var report = BuildPipeline.BuildPlayer(buildOptions);

                Assert.AreEqual(BuildResult.Succeeded, report.summary.result,
                    $"Build should succeed: {string.Join("; ", report.steps.SelectMany(s => s.messages).Select(m => m.content))}");

                var outputFiles = report.GetFiles();
                Assert.IsTrue(outputFiles.Any(), "Build should produce output files");
                Assert.IsTrue(File.Exists(buildOptions.locationPathName), "Executable should exist after successful build");
            }
            finally
            {
                AssetDatabase.DeleteAsset(testScenePath);
                if (AssetDatabase.LoadAssetAtPath<Object>(TransientConfigAssetPath) != null)
                    AssetDatabase.DeleteAsset(TransientConfigAssetPath);
                if (AssetDatabase.LoadAssetAtPath<Object>(TransientBuildInfoAssetPath) != null)
                    AssetDatabase.DeleteAsset(TransientBuildInfoAssetPath);
            }
        }

        /// <summary>Create an empty test scene and author matching Pipeline settings (Unity still requires >=1 scene to build).</summary>
        protected string CreateTestSceneWithConfig(bool validConfiguration)
        {
            var scenePath = $"Assets/TestScene_{Target}_{System.Guid.NewGuid():N}.unity";
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene);
            EditorSceneManager.SaveScene(scene, scenePath);

            var config = ScriptableObject.CreateInstance<RuntimePipelineConfig>();
            config.enableInBuilds = validConfiguration;
            config.Save();
            Object.DestroyImmediate(config);

            return scenePath;
        }

        /// <summary>Check if platform support is installed for the target.</summary>
        protected bool IsPlatformSupportInstalled(BuildTarget target)
        {
            try
            {
                var group = BuildPipeline.GetBuildTargetGroup(target);
                return true; // If we can get the group, platform support is available
            }
            catch
            {
                return false;
            }
        }
    }
}
