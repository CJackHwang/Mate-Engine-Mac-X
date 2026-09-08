using System;
using System.Threading.Tasks;
using NUnit.Framework;
using Newtonsoft.Json.Linq;
using Unity.Pipeline;
using Unity.Pipeline.Commands;
using Unity.Pipeline.Editor;
using Unity.Pipeline.Security;

namespace Unity.Pipeline.Tests.Editor
{
    /// <summary>
    /// Test-only command that raises a dialog shown+dismissed pair on a caller-supplied tracker
    /// from inside its own handler — lets DialogsDuringExecutionTests exercise the real
    /// execStartUtc-scoped attach logic without needing a real modal dialog.
    /// </summary>
    static class SimulateDialogDuringExecCommand
    {
        internal static DialogStateTracker TargetTracker;

        [CliCommand("test_simulate_dialog_during_exec", "Test-only: raises a dialog shown+dismissed pair during its own execution", MainThreadRequired = true)]
        public static bool Run()
        {
            TargetTracker?.OnShown(42, "native", "Mid-command dialog", "msg", new[] { "OK" }, "Info", DateTime.UtcNow);
            TargetTracker?.OnDismissed(42, DateTime.UtcNow);
            return true;
        }
    }

    class DialogsDuringExecutionTests
    {
        sealed class DialogTestServer : EditorPipelineServer
        {
            protected override bool WritesDescriptor => false;
            protected override (int basePort, int maxPort) GetPortRange() => (7850, 7899);
            protected override string GetToken() => SecurityTokenManager.GetOrCreateToken();
            protected override bool IsSettled => true;
        }

        DialogTestServer m_Server;
        Unity.Pipeline.Tests.Runtime.PipelineClient m_Client;

        [SetUp]
        public void SetUp()
        {
            CommandRegistry.SetDiscovery(new TypeCacheCommandDiscovery());
            m_Server = new DialogTestServer();
            m_Server.Start();
            m_Client = new Unity.Pipeline.Tests.Runtime.PipelineClient(m_Server);
            SimulateDialogDuringExecCommand.TargetTracker = m_Server.Dialogs;
        }

        [TearDown]
        public void TearDown()
        {
            SimulateDialogDuringExecCommand.TargetTracker = null;
            m_Client?.Dispose();
            m_Server?.Stop();
        }

        [Test]
        public async Task ApiExec_NoDialogRaised_OmitsDialogsDuringExecutionField()
        {
            var response = await m_Client.ExecuteCommandAsync("editor_status", new { });

            Assert.IsTrue(response.IsSuccess);
            Assert.IsNull(response.JsonResponse["dialogsDuringExecution"]);
        }

        [Test]
        public async Task ApiExec_DialogRaisedAndDismissedInsideHandler_AttachesIt()
        {
            var response = await m_Client.ExecuteCommandAsync("test_simulate_dialog_during_exec", new { });

            Assert.IsTrue(response.IsSuccess);
            var attached = (JArray)response.JsonResponse["dialogsDuringExecution"];
            Assert.IsNotNull(attached, "A dialog raised inside the command's own handler must be attached");
            Assert.AreEqual(42, attached[0]["id"].ToObject<int>());
        }

        [Test]
        public async Task ApiExec_DialogRaisedLongBeforeCommand_DoesNotLeakIntoResponse()
        {
            m_Server.Dialogs.OnShown(1, "native", "Old dialog", "msg", new[] { "OK" }, "Info",
                System.DateTime.UtcNow.AddMinutes(-5));
            m_Server.Dialogs.OnDismissed(1, System.DateTime.UtcNow.AddMinutes(-5));

            var response = await m_Client.ExecuteCommandAsync("editor_status", new { });

            Assert.IsTrue(response.IsSuccess);
            Assert.IsNull(response.JsonResponse["dialogsDuringExecution"],
                "A dialog that opened well before this call started must not leak into its response");
        }
    }
}
