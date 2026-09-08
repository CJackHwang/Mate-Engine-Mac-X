using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Unity.Pipeline.Security;
using Unity.Pipeline.Tests.Runtime;

namespace Unity.Pipeline.Tests.Editor
{
    /// <summary>
    /// Test fixture that owns an isolated <see cref="TestEditorPipelineServer"/> plus a
    /// <see cref="PipelineClient"/> pointed at it. ViaClient tests use this instead of the live
    /// editor server, so running the suite never disturbs the server agents drive over HTTP.
    ///
    /// Wrap in a `using` (or SetUp/TearDown) so the server is stopped after each test.
    /// </summary>
    sealed class PipelineTestServer : IDisposable
    {
        private readonly TestEditorPipelineServer m_Server;
        private readonly PipelineClient m_Client;
        // Appended from the HTTP thread, read by the test afterwards.
        private readonly ConcurrentQueue<CommandExecutionInfo> m_Reported = new ConcurrentQueue<CommandExecutionInfo>();
        private readonly ConcurrentQueue<CommandExecutionInfo> m_ReportedOnMainThread = new ConcurrentQueue<CommandExecutionInfo>();

        public int Port => m_Server.Port;
        public PipelineClient Client => m_Client;

        /// <summary>
        /// Everything the server reported when a command finished, oldest first — including a
        /// request rejected before any command ran, which carries a transaction and no command.
        /// </summary>
        public IReadOnlyList<CommandExecutionInfo> Reported => m_Reported.ToArray();

        /// <summary>
        /// What crossed to the main thread, which is the subset carrying a command. The crossing is
        /// a dispatcher post, normally pumped by the editor update loop; that loop does not tick
        /// inside a synchronous test, so <see cref="Execute"/> pumps it before returning.
        /// </summary>
        public IReadOnlyList<CommandExecutionInfo> ReportedOnMainThread => m_ReportedOnMainThread.ToArray();

        public PipelineTestServer()
        {
            m_Server = new TestEditorPipelineServer();
            m_Server.CommandDoneRecorder = info => m_Reported.Enqueue(info);
            m_Server.CommandDoneMainThreadRecorder = info => m_ReportedOnMainThread.Enqueue(info);
            m_Server.Start(); // auto-assigns a port in 7850-7899; writes no descriptor
            m_Client = new PipelineClient($"http://localhost:{m_Server.Port}", SecurityTokenManager.GetOrCreateToken());
        }

        /// <summary>
        /// Execute a command against this isolated server, pumping the server's own dispatcher
        /// while the HTTP call is in flight. This lets MainThreadRequired commands complete even
        /// when the editor update loop isn't ticking (e.g. during a synchronous run_tests that
        /// blocks the main thread), so it's safe to call from a plain [Test] without deadlocking.
        /// </summary>
        public PipelineResponse Execute(string command, object parameters = null, int timeoutMs = 30000)
        {
            // Run the whole client call on a threadpool thread (not the main thread) so none of its
            // async continuations capture Unity's SynchronizationContext. Otherwise they'd be queued
            // back to the main thread, which this method deliberately blocks below while pumping —
            // the task could never complete (sync-over-async deadlock). The server side still needs
            // the main thread, which the ProcessWorkQueue pump below provides.
            var task = Task.Run(() => m_Client.ExecuteCommandAsync(command, parameters));

            var start = DateTime.UtcNow;
            while (!task.IsCompleted)
            {
                m_Server.Dispatcher.ProcessWorkQueue();

                if ((DateTime.UtcNow - start).TotalMilliseconds > timeoutMs)
                    throw new TimeoutException($"Command '{command}' did not complete within {timeoutMs}ms");

                Thread.Sleep(1);
            }

            // The response is written before the post is queued, so the client task can complete
            // with the post still pending. Pump once more, standing in for the editor update tick,
            // so ReportedOnMainThread is current on return.
            m_Server.Dispatcher.ProcessWorkQueue(int.MaxValue);

            return task.GetAwaiter().GetResult();
        }

        public void Dispose()
        {
            m_Client?.Dispose();
            m_Server?.Stop();
        }
    }
}
