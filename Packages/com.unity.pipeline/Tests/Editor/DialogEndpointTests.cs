using System.Threading.Tasks;
using NUnit.Framework;
using Newtonsoft.Json.Linq;
using Unity.Pipeline;
using Unity.Pipeline.Editor;
using Unity.Pipeline.Security;

namespace Unity.Pipeline.Tests.Editor
{
    class DialogEndpointTests
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
            m_Server = new DialogTestServer();
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
        public async Task ApiDialog_NoDialogOpen_ReportsInactiveEmptyList()
        {
            var http = await m_Client.GetHttpAsync("/api/dialog");
            var json = JObject.Parse(await http.Content.ReadAsStringAsync());

            Assert.IsFalse(json["active"].ToObject<bool>());
            Assert.IsEmpty((JArray)json["dialogs"]);
        }

        [Test]
        public async Task ApiDialog_OneDialogOpen_ReportsItsFields()
        {
            m_Server.Dialogs.OnShown(7, "native", "Save changes?", "You have unsaved changes.",
                new[] { "Save", "Cancel" }, "Warning", System.DateTime.UtcNow);

            var http = await m_Client.GetHttpAsync("/api/dialog");
            var json = JObject.Parse(await http.Content.ReadAsStringAsync());

            Assert.IsTrue(json["active"].ToObject<bool>());
            var dialog = (JObject)((JArray)json["dialogs"])[0];
            Assert.AreEqual(7, dialog["id"].ToObject<int>());
            Assert.AreEqual("native", dialog["source"].ToString());
            Assert.AreEqual("Save changes?", dialog["title"].ToString());
            Assert.AreEqual("Warning", dialog["level"].ToString());
            CollectionAssert.AreEqual(new[] { "Save", "Cancel" }, dialog["buttons"].ToObject<string[]>());
        }

        [Test]
        public async Task ApiDialog_DismissedDialog_NoLongerReportedAsActive()
        {
            m_Server.Dialogs.OnShown(7, "native", "Title", "Message", new[] { "OK" }, "Info", System.DateTime.UtcNow);
            m_Server.Dialogs.OnDismissed(7, System.DateTime.UtcNow);

            var http = await m_Client.GetHttpAsync("/api/dialog");
            var json = JObject.Parse(await http.Content.ReadAsStringAsync());

            Assert.IsFalse(json["active"].ToObject<bool>());
            Assert.IsEmpty((JArray)json["dialogs"]);
        }
    }
}
