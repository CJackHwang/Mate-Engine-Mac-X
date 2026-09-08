using System;
using Unity.Pipeline.Editor;
using Unity.Pipeline.Security;

namespace Unity.Pipeline.Tests.Editor
{
    /// <summary>
    /// Editor pipeline server for tests. Binds to an isolated port range (7850-7899) and never
    /// writes the shared instance descriptor, so it can never collide with or clobber the live
    /// editor server (port 7800, descriptor .unity-pipeline-port) that agents drive over HTTP.
    ///
    /// Because no descriptor is created, the token comes straight from SecurityTokenManager
    /// (the same source the descriptor would use), so token-gated commands still validate.
    /// </summary>
    internal sealed class TestEditorPipelineServer : EditorPipelineServer
    {
        protected override bool WritesDescriptor => false;

        /// <summary>
        /// Observes everything the server reports, including requests rejected before a command
        /// ran. Invoked on the HTTP thread, so an observer must be thread-safe.
        /// </summary>
        internal Action<CommandExecutionInfo> CommandDoneRecorder;

        /// <summary>
        /// Observes what made it across to the main thread — the subset that carries a command.
        /// Null by default, and that default is the point: the suite builds one of these per test,
        /// so inheriting the real main-thread work would report a command execution and mint a
        /// pipeline analytics session out of every test run.
        /// </summary>
        internal Action<CommandExecutionInfo> CommandDoneMainThreadRecorder;

        protected override void OnCommandDone(in CommandExecutionInfo info)
        {
            // Keep the real behaviour (transaction log, and the dispatcher post this test server
            // exists to let a fixture observe); only the main-thread half below is stubbed out.
            base.OnCommandDone(info);
            CommandDoneRecorder?.Invoke(info);
        }

        protected override void OnCommandDoneMainThread(in CommandExecutionInfo info)
        {
            CommandDoneMainThreadRecorder?.Invoke(info);
        }

        protected override (int basePort, int maxPort) GetPortRange() => (7850, 7899);

        protected override string GetToken() => SecurityTokenManager.GetOrCreateToken();

        // The base ties /api/status readiness to the instance descriptor, which this server
        // intentionally never writes. It is genuinely running once Start() returns, so report ready.
        protected override object GetServerStatus() =>
            new { status = "ready", lastHeartbeat = DateTime.UtcNow, capabilities = Capabilities };
    }
}
