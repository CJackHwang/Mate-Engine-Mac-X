using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using Unity.Pipeline.Config;
using UnityEngine;
using UnityEngine.TestTools;

namespace Unity.Pipeline.Tests.Editor
{
    class RuntimePipelineConfigTests
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
        public void DefaultValues_AreSecure()
        {
            var config = ScriptableObject.CreateInstance<RuntimePipelineConfig>();

            Assert.IsFalse(config.enableInBuilds, "enableInBuilds should default to false for security");
            Assert.AreEqual(0, config.port, "Port should default to 0 for auto-assignment");
            Assert.IsTrue(config.autoStart, "Should default to auto-start");
            Assert.AreEqual(10, config.maxWorkItemsPerFrame, "Should have a reasonable default for work items per frame");

            Object.DestroyImmediate(config);
        }

        [Test]
        public void Load_SettingsFileExists_FindsIt()
        {
            var config = ScriptableObject.CreateInstance<RuntimePipelineConfig>();
            config.enableInBuilds = true;
            config.Save();
            Object.DestroyImmediate(config);

            var loaded = RuntimePipelineConfig.Load();

            Assert.IsNotNull(loaded, "Load() should find the settings file written by Save()");
            Assert.IsTrue(loaded.enableInBuilds);
        }

        [Test]
        public void Load_NoSettingsFile_ReturnsNull()
        {
            Assert.IsNull(RuntimePipelineConfig.Load());
        }

        [Test]
        public void SaveThenLoad_RoundTripsAllFields()
        {
            var config = ScriptableObject.CreateInstance<RuntimePipelineConfig>();
            config.enableInBuilds = true;
            config.port = 7912;
            config.requestTimeoutMs = 45000;
            config.enableAuditLogging = false;
            config.autoStart = false;
            config.maxWorkItemsPerFrame = 25;
            config.Save();
            Object.DestroyImmediate(config);

            var loaded = RuntimePipelineConfig.Load();

            Assert.IsNotNull(loaded);
            Assert.IsTrue(loaded.enableInBuilds);
            Assert.AreEqual(7912, loaded.port);
            Assert.AreEqual(45000, loaded.requestTimeoutMs);
            Assert.IsFalse(loaded.enableAuditLogging);
            Assert.IsFalse(loaded.autoStart);
            Assert.AreEqual(25, loaded.maxWorkItemsPerFrame);
        }

        [Test]
        public void GetLiveMaxWorkItemsPerFrame_NoSettingsFile_ReturnsFallback()
        {
            Assert.AreEqual(42, RuntimePipelineConfig.GetLiveMaxWorkItemsPerFrame(42));
        }

        [Test]
        public void GetLiveMaxWorkItemsPerFrame_SettingsFileExists_ReturnsCurrentValue()
        {
            var config = ScriptableObject.CreateInstance<RuntimePipelineConfig>();
            config.maxWorkItemsPerFrame = 33;
            config.Save();
            Object.DestroyImmediate(config);

            Assert.AreEqual(33, RuntimePipelineConfig.GetLiveMaxWorkItemsPerFrame(10));
        }

        [Test]
        public void GetLiveMaxWorkItemsPerFrame_FileChangedSinceLastRead_ReturnsUpdatedValue()
        {
            var config = ScriptableObject.CreateInstance<RuntimePipelineConfig>();
            config.maxWorkItemsPerFrame = 15;
            config.Save();
            Object.DestroyImmediate(config);
            var first = RuntimePipelineConfig.GetLiveMaxWorkItemsPerFrame(10);

            var updated = ScriptableObject.CreateInstance<RuntimePipelineConfig>();
            updated.maxWorkItemsPerFrame = 40;
            updated.Save();
            Object.DestroyImmediate(updated);
            var second = RuntimePipelineConfig.GetLiveMaxWorkItemsPerFrame(10);

            Assert.AreEqual(15, first);
            Assert.AreEqual(40, second, "A later Save() must be picked up on the next call");
        }

        [Test]
        public void Load_CorruptSettingsFile_ReturnsNullAndLogsWarning()
        {
            var path = Path.Combine(Path.GetDirectoryName(Application.dataPath), "ProjectSettings/Packages/com.unity.pipeline/RuntimePipelineConfig.json");
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, "{ not valid json");

            LogAssert.Expect(LogType.Warning, new Regex("Pipeline: could not read runtime settings.*"));
            var loaded = RuntimePipelineConfig.Load();

            Assert.IsNull(loaded);
        }
    }
}
