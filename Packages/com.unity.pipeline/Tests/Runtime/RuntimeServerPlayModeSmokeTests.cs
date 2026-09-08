using System.Collections;
using System.Threading.Tasks;
using NUnit.Framework;
using Unity.Pipeline.Runtime.Commands;
using Unity.Pipeline.Security;
using UnityEngine;
using UnityEngine.TestTools;

namespace Unity.Pipeline.Tests.Runtime
{
    /// <summary>
    /// Thin PlayMode smoke suite for paths that genuinely require a running player:
    /// the autoStart lifecycle (MonoBehaviour Start) and quit (DontDestroyOnLoad, play-mode only).
    /// General command behavior is covered by the EditMode *CommandTests — this only covers
    /// play-mode-only paths and one end-to-end server check.
    /// </summary>
    class RuntimeServerPlayModeSmokeTests
    {
        [UnityTest]
        public IEnumerator AutoStart_StartsServer_AndServesRequest()
        {
            var config = ScriptableObject.CreateInstance<Unity.Pipeline.Config.RuntimePipelineConfig>();
            config.enableInBuilds = true;
            config.autoStart = true;

            var go = new GameObject("SmokeRuntimeDriver");
            var driver = go.AddComponent<RuntimePipelineDriver>();
            driver.Initialize(config, null);

            // Start() -> autoStart -> StartServer runs on the next frame (MonoBehaviour lifecycle), not synchronously here.
            float t = 0f;
            while (!driver.IsServerRunning && t < 5f) { t += Time.unscaledDeltaTime; yield return null; }
            Assert.IsTrue(driver.IsServerRunning, "autoStart should start the runtime server");

            var serve = ServeStatus(driver.ActualPort, SecurityTokenManager.GetOrCreateToken());
            while (!serve.IsCompleted) yield return null;
            Assert.IsTrue(serve.Result, "runtime server should serve a runtime_status request");

            driver.StopServer();
            Object.Destroy(go);
            Object.Destroy(config);
        }

        static async Task<bool> ServeStatus(int port, string token)
        {
            using (var client = new PipelineClient($"http://localhost:{port}", token))
            {
                var response = await client.ExecuteCommandAsync("runtime_status", null);
                return response.IsSuccess;
            }
        }

        [Test]
        public void Quit_SchedulesQuit()
        {
            // QuitApplication uses DontDestroyOnLoad, which is play-mode only. Application.Quit is a
            // no-op in the editor, but destroy the scheduler anyway so nothing lingers.
            var result = RuntimeApplicationCommand.QuitApplication(0);
            Assert.That(result, Does.Contain("Application quit scheduled with exit code 0"));

            foreach (var go in PipelineUtils.FindObjectsByType<GameObject>())
            {
                if (go.name.Contains("QuitScheduler")) { Object.Destroy(go); break; }
            }
        }
    }
}
