using System.Threading.Tasks;
using NUnit.Framework;
using Newtonsoft.Json.Linq;
using Unity.Pipeline.Editor;
using Unity.Pipeline.Security;

namespace Unity.Pipeline.Tests.Editor.ServerLifecycle
{
    class DialogGateTests
    {
        sealed class DialogGateTestServer : EditorPipelineServer
        {
            /// <summary>true (the default) simulates a settled server; false the post-cold-start
            /// settle window, for the settling-vs-dialog priority tests below.</summary>
            public bool Settled { get; set; } = true;

            protected override bool WritesDescriptor => false;
            protected override (int basePort, int maxPort) GetPortRange() => (7850, 7899);
            protected override string GetToken() => SecurityTokenManager.GetOrCreateToken();
            protected override bool IsSettled => Settled;

            // WritesDescriptor is false, so the instance descriptor is never created; this
            // server is genuinely running once Start() returns, so /api/status's ordinary case
            // should still be "ready", not "error" — needed for the ApiStatus_* tests below.
            protected override bool HasInstanceDescriptor => true;
        }

        DialogGateTestServer m_Server;
        Unity.Pipeline.Tests.Runtime.PipelineClient m_Client;

        [SetUp]
        public void SetUp()
        {
            m_Server = new DialogGateTestServer();
            m_Server.Start();
            m_Client = new Unity.Pipeline.Tests.Runtime.PipelineClient(m_Server);
        }

        [TearDown]
        public void TearDown()
        {
            m_Client?.Dispose();
            m_Server?.Stop();
        }

        [Test]
        public async Task ApiExec_MainThreadCommand_WhileDialogOpen_Returns503BusyEnvelopeWithDialogInfo()
        {
            m_Server.Dialogs.OnShown(7, "native", "Save changes?", "msg", new[] { "Save", "Cancel" }, "Warning", System.DateTime.UtcNow);

            var response = await m_Client.ExecuteCommandAsync("log_editor", new { message = "should not run while a dialog is open" });

            Assert.AreEqual(503, response.StatusCode);
            var json = response.JsonResponse;
            Assert.IsFalse(json["success"].ToObject<bool>());
            Assert.AreEqual("busy", json["status"]?.ToString());
            Assert.AreEqual("blocked_by_dialog", json["busyReason"]?.ToString());
            Assert.IsTrue(json["retryable"].ToObject<bool>());
            var dialogs = (JArray)json["dialogs"];
            Assert.AreEqual(7, dialogs[0]["id"].ToObject<int>());
        }

        // EditorDialogStateMirror.OnDialogWillShow always calls SnapshotStatusForDialogGate()
        // immediately before Dialogs.OnShown(...), so in a live Editor a snapshot exists by the
        // time any request can observe a dialog as open — editor_status then gets served from
        // that snapshot (200) instead of the generic busy envelope. The no-snapshot case (503)
        // can only occur before the mirror has ever snapshotted a dialog this session; here it's
        // reproduced by driving Dialogs.OnShown directly, bypassing the mirror.
        [TestCase(false, 503, TestName = "ApiExec_EditorStatus_WhileDialogOpen_NoSnapshotTaken_Returns503BusyEnvelope")]
        [TestCase(true, 200, TestName = "ApiExec_EditorStatus_WhileDialogOpen_SnapshotTakenFirst_Returns200WithBlockedStatus")]
        public async Task ApiExec_EditorStatus_WhileDialogOpen(bool snapshotTakenFirst, int expectedStatusCode)
        {
            if (snapshotTakenFirst)
                m_Server.SnapshotStatusForDialogGate();

            m_Server.Dialogs.OnShown(7, "native", "Title", "msg", new[] { "OK" }, "Info", System.DateTime.UtcNow);

            var response = await m_Client.ExecuteCommandAsync("editor_status", new { });

            Assert.AreEqual(expectedStatusCode, response.StatusCode);
            if (snapshotTakenFirst)
            {
                Assert.AreEqual("blocked_by_dialog", response.JsonResponse["result"]?["status"]?.ToString());
                Assert.AreEqual(7, response.JsonResponse["result"]?["dialog"]?["id"]?.ToObject<int>());
            }
        }

        [Test]
        public async Task ApiExec_BackgroundCommand_WhileDialogOpen_ExecutesNormally()
        {
            m_Server.Dialogs.OnShown(7, "native", "Title", "msg", new[] { "OK" }, "Info", System.DateTime.UtcNow);

            var response = await m_Client.ExecuteCommandAsync("recompile_status", new { });

            Assert.IsTrue(response.IsSuccess,
                "MainThreadRequired=false commands must stay servable while a dialog is open");
        }

        [Test]
        public async Task ApiExec_AfterDialogDismissed_PreviouslyGatedCommandSucceeds()
        {
            m_Server.Dialogs.OnShown(7, "native", "Title", "msg", new[] { "OK" }, "Info", System.DateTime.UtcNow);
            m_Server.Dialogs.OnDismissed(7, System.DateTime.UtcNow);

            var response = await m_Client.ExecuteCommandAsync("log_editor", new { message = "runs after dismissal" });

            Assert.IsTrue(response.IsSuccess);
        }

        [Test]
        public async Task ApiStatus_WhileDialogOpen_ReportsBlockedByDialogWithDetails()
        {
            // /api/status is the cheapest, most-likely-to-be-polled-first probe — it must not
            // report "ready" while every MainThreadRequired command is actually gated.
            m_Server.Dialogs.OnShown(7, "native", "Save changes?", "msg", new[] { "Save", "Cancel" }, "Warning", System.DateTime.UtcNow);

            var http = await m_Client.GetHttpAsync("/api/status");
            var json = JObject.Parse(await http.Content.ReadAsStringAsync());

            Assert.AreEqual("blocked_by_dialog", json["status"]?.ToString());
            Assert.AreEqual(7, json["dialog"]?["id"]?.ToObject<int>());
            Assert.AreEqual("Save changes?", json["dialog"]?["title"]?.ToString());
            Assert.AreEqual("Warning", json["dialog"]?["level"]?.ToString());
        }

        [Test]
        public async Task ApiStatus_WhileSettlingAndDialogOpen_ReportsBlockedByDialogNotSettling()
        {
            // A dialog blocks the main thread regardless of settle state, and needs a human —
            // settling resolves on its own. blocked_by_dialog must win over settling.
            m_Server.Settled = false;
            m_Server.Dialogs.OnShown(7, "native", "Title", "msg", new[] { "OK" }, "Info", System.DateTime.UtcNow);

            var http = await m_Client.GetHttpAsync("/api/status");
            var json = JObject.Parse(await http.Content.ReadAsStringAsync());

            Assert.AreEqual("blocked_by_dialog", json["status"]?.ToString());
            Assert.AreEqual(7, json["dialog"]?["id"]?.ToObject<int>());
        }

        [Test]
        public async Task ApiExec_MainThreadCommand_WhileSettlingAndDialogOpen_ReportsBlockedByDialogNotSettling()
        {
            m_Server.Settled = false;
            m_Server.Dialogs.OnShown(7, "native", "Title", "msg", new[] { "OK" }, "Info", System.DateTime.UtcNow);

            var response = await m_Client.ExecuteCommandAsync("log_editor", new { message = "should be gated by the dialog, not settling" });

            Assert.AreEqual(503, response.StatusCode);
            Assert.AreEqual("blocked_by_dialog", response.JsonResponse["busyReason"]?.ToString());
        }

        [Test]
        public async Task ApiStatus_NoDialogOpen_ReportsReadyWithNoDialogKey()
        {
            var http = await m_Client.GetHttpAsync("/api/status");
            var json = JObject.Parse(await http.Content.ReadAsStringAsync());

            Assert.AreEqual("ready", json["status"]?.ToString());
            Assert.IsNull(json["dialog"], "The common case must not carry a \"dialog\" key at all, not even null.");
        }

        [Test]
        public async Task ApiStatus_AfterDialogDismissed_ReportsReadyAgain()
        {
            m_Server.Dialogs.OnShown(7, "native", "Title", "msg", new[] { "OK" }, "Info", System.DateTime.UtcNow);
            m_Server.Dialogs.OnDismissed(7, System.DateTime.UtcNow);

            var http = await m_Client.GetHttpAsync("/api/status");
            var json = JObject.Parse(await http.Content.ReadAsStringAsync());

            Assert.AreEqual("ready", json["status"]?.ToString());
        }
    }
}
