using System;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Unity.Pipeline.Commands;
using Unity.Pipeline.Editor;
#if UNITY_6000_5_OR_NEWER
using Unity.Scripting.LifecycleManagement;
#endif

namespace Unity.Pipeline.Tests.Editor
{
    /// <summary>
    /// Tests for GET /api/progress — the server side of the CLI's terminal progress bars
    /// (CLI-488). Contract: <c>{"active":bool,"progress":{title,info,current,total,pct}}</c>
    /// with all progress fields optional and <c>pct</c> in 0–1. The endpoint is served on the
    /// listener thread from CliProgress's snapshot, so it must answer while a command is
    /// executing — that concurrency is exactly what these tests exercise.
    /// </summary>
#if UNITY_6000_5_OR_NEWER
    [NoAutoStaticsCleanup]
#endif
    class ProgressEndpointTests
    {
        private EditorPipelineServer m_Server;
        private Unity.Pipeline.Tests.Runtime.PipelineClient m_PipelineClient;

        /// <summary>Gates progress_test_wait so tests control exactly when the exec completes.</summary>
        private static readonly ManualResetEventSlim m_ReleaseWaitCommand = new ManualResetEventSlim(false);

        [SetUp]
        public void SetUp()
        {
            CommandRegistry.SetDiscovery(new TypeCacheCommandDiscovery());

            m_ReleaseWaitCommand.Reset();

            // Isolated test server (ports 7850-7899, no descriptor) — never touches the live server.
            m_Server = new TestEditorPipelineServer();
            m_Server.Start();
            m_Server.Progress.Clear();

            m_PipelineClient = new Unity.Pipeline.Tests.Runtime.PipelineClient(m_Server);
        }

        [TearDown]
        public void TearDown()
        {
            // Unblock a still-waiting command so its exec request can finish before server stop.
            m_ReleaseWaitCommand.Set();
            m_PipelineClient?.Dispose();
            m_Server?.Progress.Clear();
            m_Server?.Stop();
        }

        // Off the main thread on purpose: EditMode tests run on the main thread, and the whole
        // point is polling /api/progress while this command is still executing.
        [CliCommand("progress_test_wait", "Test command: report progress and wait for release", MainThreadRequired = false)]
        public static string ProgressTestWait()
        {
            CliProgress.Report("Progress Test", "Working", 1, 2, 0.5);
            m_ReleaseWaitCommand.Wait(TimeSpan.FromSeconds(15));
            return "done";
        }

        private async Task<JObject> GetProgressAsync()
        {
            var httpResponse = await m_PipelineClient.GetHttpAsync("/api/progress");
            Assert.IsTrue(httpResponse.IsSuccessStatusCode,
                $"/api/progress should return success, got: {httpResponse.StatusCode}");
            Assert.AreEqual("application/json", httpResponse.Content.Headers.ContentType.MediaType,
                "/api/progress should return JSON content type");
            var jsonContent = await httpResponse.Content.ReadAsStringAsync();
            return JObject.Parse(jsonContent);
        }

        /// <summary>
        /// Assert the progress field is EXPLICITLY null: nulls are included by default (AUTHAPI-21
        /// review), so an idle endpoint answers {"active":false,"progress":null} — a present key
        /// with a JSON null, never an absent key (an empty object is maximally ambiguous).
        /// </summary>
        private static void AssertProgressExplicitlyNull(JObject json, string message)
        {
            var token = json["progress"];
            Assert.IsNotNull(token, $"{message} — and the 'progress' key must be present (nulls are explicit by default)");
            Assert.AreEqual(JTokenType.Null, token.Type, message);
        }

        [Test]
        public async Task ApiProgress_NoExecInFlight_ReportsInactive()
        {
            var json = await GetProgressAsync();

            Assert.AreEqual(false, json["active"]?.Value<bool>(), "No exec in flight — active must be false");
            AssertProgressExplicitlyNull(json, "No exec in flight — progress must be explicitly null");
        }

        [Test]
        public async Task ApiProgress_ExplicitReportWithoutExec_StaysInactive()
        {
            // A stray report with no command executing must not claim an active task —
            // the CLI only polls during its own exec, and "active" mirrors exec state.
            m_Server.Progress.Report("Orphan", "Not attached to any exec");

            var json = await GetProgressAsync();

            Assert.AreEqual(false, json["active"]?.Value<bool>(), "No exec in flight — active must be false");
            AssertProgressExplicitlyNull(json, "Progress must be explicitly null while inactive");
        }

        [Test]
        public async Task ApiProgress_DuringExec_ServesSnapshotAndResetsAfter()
        {
            // Start a gated command and DON'T await it yet — the endpoint must answer while
            // the exec is in flight.
            var execTask = m_PipelineClient.ExecuteCommandAsync("progress_test_wait", new { });

            JObject during = null;
            for (var attempt = 0; attempt < 100; attempt++)
            {
                var json = await GetProgressAsync();
                // `is JObject`, not `!= null`: nulls are explicit now, so between exec start and
                // the command's first CliProgress.Report the endpoint answers
                // {"active":true,"progress":null} — a present JSON-null token, which `!= null`
                // would wrongly accept and then NRE on the field asserts below (seen on CI).
                if (json["active"]?.Value<bool>() == true && json["progress"] is JObject)
                {
                    during = json;
                    break;
                }
                await Task.Delay(50);
            }

            Assert.IsNotNull(during, "Endpoint never reported the in-flight command's progress");
            var progress = during["progress"] as JObject;
            Assert.AreEqual("Progress Test", progress["title"]?.ToString());
            Assert.AreEqual("Working", progress["info"]?.ToString());
            Assert.AreEqual(1, progress["current"]?.Value<long>());
            Assert.AreEqual(2, progress["total"]?.Value<long>());
            Assert.AreEqual(0.5, progress["pct"]?.Value<double>(), 1e-9, "pct must be the reported 0–1 value");

            // Release the command and let the exec finish.
            m_ReleaseWaitCommand.Set();
            var execResponse = await execTask;
            Assert.IsTrue(execResponse.IsSuccess, $"progress_test_wait should succeed: {execResponse.Error}");

            // After the last in-flight exec completes the state is reset: inactive, no leak
            // of this command's progress into the next.
            JObject after = null;
            for (var attempt = 0; attempt < 100; attempt++)
            {
                var json = await GetProgressAsync();
                if (json["active"]?.Value<bool>() == false)
                {
                    after = json;
                    break;
                }
                await Task.Delay(50);
            }
            Assert.IsNotNull(after, "Endpoint should report inactive after the exec completed");
            AssertProgressExplicitlyNull(after, "Completed command's progress must not leak");
        }
    }
}
