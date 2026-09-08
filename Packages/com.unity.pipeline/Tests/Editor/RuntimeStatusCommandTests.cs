using NUnit.Framework;
using Unity.Pipeline.Config;
using Unity.Pipeline.Runtime.Commands;
using Unity.Pipeline.Tests.Editor.ServerLifecyle;
using UnityEngine;

namespace Unity.Pipeline.Tests.Editor
{
    /// <summary>
    /// Tests for the runtime_status command (RuntimeStatusCommand), exercised both directly
    /// (static call) and through the HTTP server via PipelineClient.
    /// </summary>
    class RuntimeStatusCommandTests
    {
        #region Direct

        [Test]
        public void GetRuntimeStatus_ReturnsValidData()
        {
            var response = RuntimeStatusCommand.GetRuntimeStatus();

            Assert.IsNotNull(response);
            Assert.IsNotEmpty(response.UnityVersion, "Should have Unity version");
            Assert.IsNotEmpty(response.Platform, "Should have platform info");
            Assert.IsTrue(response.IsPlaying, "runtime_status reports IsPlaying = true");
            Assert.IsNotNull(response.LoadedLevelName, "Should have scene name");
        }

        [Test]
        public void GetRuntimeStatus_NoDriverBootstrapped_PipelineDriverIsNull()
        {
            GlobalServerStateGuard.Capture();
            RuntimePipelineResourceTestGuard.AssertNoConfigAssetExists();

            try
            {
                var response = RuntimeStatusCommand.GetRuntimeStatus();

                Assert.IsNull(response.PipelineDriver, "No driver was ever bootstrapped in this test, so there is nothing to report");
            }
            finally
            {
                RuntimePipelineResourceTestGuard.DeleteConfigIfExists();
                GlobalServerStateGuard.Restore();
            }
        }

        [Test]
        public void GetRuntimeStatus_DriverBootstrapped_ReportsDriverStatus()
        {
            GlobalServerStateGuard.Capture();
            RuntimePipelineResourceTestGuard.AssertNoConfigAssetExists();

            GameObject driverGameObject = null;
            try
            {
                var config = ScriptableObject.CreateInstance<RuntimePipelineConfig>();
                config.enableInBuilds = true;
                config.requestTimeoutMs = 12345;
                config.enableAuditLogging = false;
                config.autoStart = false;
                config.maxWorkItemsPerFrame = 17;
                config.Save();
                Object.DestroyImmediate(config);

                var driver = RuntimePipelineBootstrap.Bootstrap();
                driverGameObject = driver != null ? driver.gameObject : null;

                var response = RuntimeStatusCommand.GetRuntimeStatus();

                Assert.IsNotNull(response.PipelineDriver);
                Assert.IsTrue(response.PipelineDriver.EnableInBuilds);
                Assert.AreEqual(12345, response.PipelineDriver.RequestTimeoutMs);
                Assert.IsFalse(response.PipelineDriver.EnableAuditLogging);
                Assert.IsFalse(response.PipelineDriver.AutoStart);
                Assert.AreEqual(17, response.PipelineDriver.MaxWorkItemsPerFrame);
                Assert.IsFalse(response.PipelineDriver.IsServerRunning, "autoStart is false, so the server itself never started");
            }
            finally
            {
                if (driverGameObject != null)
                    Object.DestroyImmediate(driverGameObject);
                RuntimePipelineBootstrap.Instance = null;
                RuntimePipelineResourceTestGuard.DeleteConfigIfExists();
                GlobalServerStateGuard.Restore();
            }
        }

        #endregion

        #region ViaClient

        [Test]
        public void RuntimeStatus_ViaClient_ReturnsValidJson()
        {
            using (var server = new PipelineTestServer())
            {
                var response = server.Execute("runtime_status", null);

                Assert.IsTrue(response.IsSuccess, $"Command should succeed: {response.Error}");
                Assert.IsTrue(response.HasValidJson, "Response should have valid JSON");
                Assert.IsTrue(response.JsonResponse.ContainsKey("result"), "Should have result field");
            }
        }

        #endregion
    }
}
