using NUnit.Framework;
using Unity.Pipeline.Config;
using Unity.Pipeline.Editor;
using UnityEngine;

namespace Unity.Pipeline.Tests.Editor
{
    class RuntimePipelineSettingsProviderTests
    {
        [SetUp]
        public void SetUp()
        {
            RuntimePipelineResourceTestGuard.AssertNoConfigAssetExists();
        }

        [TearDown]
        public void TearDown()
        {
            RuntimePipelineResourceTestGuard.DeleteConfigIfExists();
        }

        [Test]
        public void LoadOrCreateConfig_NoSettingsYet_ReturnsUnsavedDefaultsWithoutAuthoringFile()
        {
            var config = RuntimePipelineSettingsProvider.LoadOrCreateConfig();

            try
            {
                Assert.IsNotNull(config);
                Assert.IsFalse(config.enableInBuilds);
                Assert.AreEqual(0, config.port);
                Assert.AreEqual(30000, config.requestTimeoutMs);
                Assert.IsTrue(config.enableAuditLogging);
                Assert.IsTrue(config.autoStart);
                Assert.AreEqual(10, config.maxWorkItemsPerFrame);

                Assert.IsNull(RuntimePipelineConfig.Load(),
                    "LoadOrCreateConfig must not author the settings file merely from being read — a " +
                    "read-only view must never have the side effect of writing to disk");
            }
            finally
            {
                Object.DestroyImmediate(config);
            }
        }

        [Test]
        public void LoadOrCreateConfig_CalledAgainAfterEdit_ReturnsPersistedValue()
        {
            var first = RuntimePipelineSettingsProvider.LoadOrCreateConfig();
            first.port = 7913;
            first.Save();

            var second = RuntimePipelineSettingsProvider.LoadOrCreateConfig();

            Assert.AreEqual(7913, second.port, "A second call must load the persisted settings, not recreate defaults");
        }
    }
}
