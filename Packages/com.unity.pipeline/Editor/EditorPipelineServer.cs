using System;
using System.Collections.Generic;
using Unity.Pipeline.Commands;
using Unity.Pipeline.Editor.Commands;
using Unity.Pipeline.Models;
using Unity.Pipeline.Security;
using UnityEditor;
using UnityEngine;
#if UNITY_6000_5_OR_NEWER
using Unity.Scripting.LifecycleManagement;
#endif

namespace Unity.Pipeline.Editor
{
#if UNITY_6000_5_OR_NEWER
    [NoAutoStaticsCleanup]
#endif
    class EditorPipelineServer : BasePipelineServer
    {
        private InstanceDescriptor m_InstanceDescriptor;

        /// <summary>
        /// Guards mutate-then-write of m_InstanceDescriptor: /api/status and /api/editor_status
        /// both reach UpdateHeartBeat concurrently now, and InstanceDescriptor.WriteToProjectRoot's
        /// own lock only covers the write, not the field mutations feeding it.
        /// </summary>
        private readonly object m_HeartbeatLock = new object();

        /// <summary>
        /// One-way settle latch (AUTHAPI-35). On a cold project import the server starts (and
        /// writes its descriptor) while the Editor is still importing assets / compiling scripts,
        /// so early main-thread commands land in a not-quite-ready Editor and fail opaquely. The
        /// latch stays false until the Editor is first seen idle after startup, and is scoped to
        /// the EDITOR SESSION via <see cref="SettledSessionKey"/>: once any server instance has
        /// seen the Editor idle, every later instance (domain-reload restart, test server) starts
        /// settled — a server coming up mid-session while a compile/import happens to be in
        /// flight (script-creating test fixtures, recompile loops) must NOT re-arm the gate.
        /// Volatile: set on the main thread, read from HTTP request threads.
        /// </summary>
        private volatile bool m_Settled;
        private bool m_SettleTickSubscribed;

        /// <summary>
        /// SessionState marker: the Editor has been seen idle at least once this session, i.e.
        /// the post-startup settle window is over for the whole session. SessionState survives
        /// domain reloads and dies with the Editor process — exactly the settle window's scope.
        /// Read/written on the main thread only (Start and the update tick); request threads
        /// only read the volatile <see cref="m_Settled"/> snapshot.
        /// </summary>
        private const string SettledSessionKey = "Unity.Pipeline.EditorSessionSettled";

        /// <summary>
        /// Commands that must stay servable while the Editor is settling: editor_status is the
        /// status surface callers poll to observe the busy/settling state itself.
        /// Background (MainThreadRequired = false) commands are always exempt and don't need
        /// listing here.
        /// </summary>
        private static readonly HashSet<string> CommandsServiceableWhileSettling = new HashSet<string> { "editor_status" };

        /// <summary>
        /// editor_status's fields as of the instant a dialog was about to be shown — see
        /// <see cref="SnapshotStatusForDialogGate"/>. Once the dialog actually blocks the main
        /// thread, editor_status can no longer run for real (unlike the settle case above, where
        /// the thread is merely busy, not stuck inside a native message loop), so the dialog gate
        /// serves this snapshot instead. Nothing it describes (compiling/domain reload/play mode)
        /// can change while the thread that would update it is stuck, so it stays correct for the
        /// whole blocked window. Null until the first dialog of the session is about to be shown.
        /// </summary>
        private StatusResponse m_LastStatusBeforeDialog;

        /// <summary>When true, every command transaction is written to the project transaction log.</summary>
        public bool LogRequestsResponses { get; set; }

        /// <summary>
        /// Whether /api/status's ordinary (non-settling, non-dialog-blocked) case should report
        /// "ready" rather than "error". Defaults to whether the instance descriptor was actually
        /// created; a <see cref="WritesDescriptor"/>-false test double that is genuinely running
        /// can override this instead of faking a descriptor just to exercise that branch.
        /// </summary>
        protected virtual bool HasInstanceDescriptor => m_InstanceDescriptor != null;

        /// <summary>UTC time this server instance started listening.</summary>
        public override DateTime StartedAt => m_InstanceDescriptor == null ? new DateTime() : m_InstanceDescriptor.StartedAt;

        /// <summary>
        /// Whether the Editor has been seen idle (not importing, not compiling) at least once
        /// this editor session. Until then the server is "settling": main-thread commands are
        /// rejected with a retryable busy signal instead of executing into a half-ready Editor.
        /// Virtual so tests can pin the unsettled state.
        /// </summary>
        protected virtual bool IsSettled => m_Settled;

        /// <summary>
        /// Whether this Editor session has already completed its settle window (the SessionState
        /// marker any settling server sets on first idle). Main thread only. Exposed protected so
        /// tests can assert it as a precondition before probing the latch's immediate-settle path.
        /// </summary>
        protected static bool IsSessionSettled => SessionState.GetBool(SettledSessionKey, false);

        /// <summary>Arm the post-startup settle latch.</summary>
        protected override void ServerStarted()
        {
            InitializeSettleLatch();
        }

        /// <summary>
        /// Whether the Editor was launched with -automated. A Unity console warning for this fact
        /// is unactionable (nobody watches the console in an agent-driven workflow) and, worse, was
        /// logged once per constructed server instance — the isolated test servers built per test
        /// (see TestEditorPipelineServer) spammed it dozens of times per suite run. The fact is
        /// instead surfaced as structured data on the instance descriptor (see
        /// <see cref="CreateInstanceDescriptor"/>) — the one thing every client reads before it can
        /// connect at all (UUM-149977).
        /// </summary>
        internal static bool DetectAutomatedMode(string[] commandLineArgs)
        {
            foreach (var arg in commandLineArgs)
            {
                if (arg == "-automated")
                    return true;
            }
            return false;
        }

        /// <summary>Unsubscribe the settle-tick handler.</summary>
        protected override void ServerStopped()
        {
            UnsubscribeSettleTick();
        }

        /// <summary>
        /// Arm or immediately release the settle latch. Start() runs on the main thread, so the
        /// Editor state and SessionState are readable directly. A server starts settled when the
        /// session has already settled once (mid-session restarts after domain reloads, test
        /// servers started while a test's own compile/import is in flight) or when the Editor is
        /// idle right now — zero behavior change in both cases. Only a server started before the
        /// session's first idle moment (cold import: [InitializeOnLoad] runs inside the startup
        /// AssetDatabase refresh) arms the gate, and settles on the first update tick that sees
        /// the Editor idle.
        /// </summary>
        private void InitializeSettleLatch()
        {
            if (SessionState.GetBool(SettledSessionKey, false)
                || (!EditorApplication.isCompiling && !EditorApplication.isUpdating))
            {
                MarkSettled();
                return;
            }

            m_SettleTickSubscribed = true;
            EditorApplication.update += SettleTick;
        }

        private void SettleTick()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
                return;

            MarkSettled();
            UnsubscribeSettleTick();
        }

        /// <summary>
        /// Release this instance's latch and mark the whole session settled (main thread only).
        /// </summary>
        private void MarkSettled()
        {
            m_Settled = true;
            SessionState.SetBool(SettledSessionKey, true);
        }

        private void UnsubscribeSettleTick()
        {
            if (!m_SettleTickSubscribed)
                return;

            m_SettleTickSubscribed = false;
            EditorApplication.update -= SettleTick;
        }

        /// <summary>
        /// Busy gate (see <see cref="BasePipelineServer.GetBusyReason"/>): while settling, reject
        /// main-thread commands with a retryable busy signal — during the post-start import/compile
        /// window they would otherwise execute into a half-ready Editor and fail with an opaque
        /// null-data envelope (AUTHAPI-35). Background commands (status polling: recompile_status,
        /// package_status, console, ...) and editor_status stay servable so callers can observe
        /// progress. Reads only the volatile latch — safe on the request thread.
        /// </summary>
        /// <param name="command">The command about to be dispatched.</param>
        /// <returns>A busy reason while settling, or null once the Editor has been seen idle.</returns>
        protected override string GetBusyReason(CommandInfo command)
        {
            if (IsSettled)
                return null;

            if (!command.MainThreadRequired || CommandsServiceableWhileSettling.Contains(command.Name))
                return null;

            return "The Editor is still settling after startup (importing assets / compiling scripts), " +
                   "so main-thread commands are not serviceable yet. Retry shortly, or poll /api/status until it reports 'ready'.";
        }

        /// <summary>
        /// Called by EditorDialogStateMirror right before a dialog blocks the main thread (while
        /// it's still free to read live Editor state) — captures editor_status's fields for
        /// <see cref="TryGetDialogBlockedFallback"/> to serve while the dialog is open.
        /// </summary>
        internal void SnapshotStatusForDialogGate()
        {
            m_LastStatusBeforeDialog = EditorStatusCommand.GetEditorStatus();
        }

        /// <summary>
        /// editor_status is the one MainThreadRequired command the dialog gate doesn't hard-fail:
        /// it can't run for real (see <see cref="m_LastStatusBeforeDialog"/>'s doc), but its answer
        /// is a snapshot taken the instant before the block started, which stays accurate for the
        /// whole window. No snapshot exists only if a dialog opened before this server ever saw one
        /// coming (should not happen in practice — EditorDialogStateMirror snapshots on every
        /// dialogWillShow), in which case this falls back to the normal 503.
        /// </summary>
        internal override object TryGetDialogBlockedFallback(CommandInfo command, DialogInfo blockingDialog)
        {
            if (command.Name != "editor_status" || m_LastStatusBeforeDialog == null)
                return null;

            return new StatusResponse
            {
                Status = "blocked_by_dialog",
                Compiling = m_LastStatusBeforeDialog.Compiling,
                DomainReloadInProgress = m_LastStatusBeforeDialog.DomainReloadInProgress,
                PlayMode = m_LastStatusBeforeDialog.PlayMode,
                LastHeartbeat = DateTime.UtcNow,
                ProjectPath = m_LastStatusBeforeDialog.ProjectPath,
                UnityVersion = m_LastStatusBeforeDialog.UnityVersion,
                Dialog = BuildDialogPayload(blockingDialog)
            };
        }

        /// <summary>
        /// Post-command work, on the HTTP thread.
        ///
        /// The transaction log runs here directly rather than crossing to the main thread with the
        /// analytics: its append is thread-safe file I/O by design (see
        /// <see cref="PipelineTransactionLog"/>), and driving it from the editor update loop would
        /// put a read-modify-write of the log on every frame that served a command. An info with no
        /// request JSON is a detached job reporting its real result, which was never part of a
        /// transaction.
        /// </summary>
        protected override void OnCommandDone(in CommandExecutionInfo info)
        {
            if (LogRequestsResponses && info.RequestJson != null)
                PipelineTransactionLog.Append(info.RequestJson, info.ResponseJson);

            // Requests rejected before anything ran carry no command, and nothing on the main-thread
            // side has anything to say about them.
            if (info.Command == null)
                return;

            // Copied out of the `in` parameter because a lambda cannot capture one.
            var posted = info;
            Dispatcher.Post(() => OnCommandDoneMainThread(posted));
        }

        /// <summary>
        /// Post-command work that needs the main thread, run on the next dispatcher pump. Analytics
        /// lives here, and so does anything else added later that has to touch Unity APIs once a
        /// command is done. Virtual so a test server can opt out wholesale.
        /// </summary>
        protected virtual void OnCommandDoneMainThread(in CommandExecutionInfo info)
        {
            PipelineAnalytics.RecordCommandExecuted(info);
        }

        // Runtime-only commands (eval, reload_file_override, …) are part of the Player surface; don't
        // advertise them when a client is connected to the Editor.
        /// <summary>False: Editor clients don't see runtime-only commands (they belong to the Player surface).</summary>
        protected override bool IncludeRuntimeOnlyCommands => false;
        /// <summary>Write the shared instance descriptor file so clients can discover this server.</summary>
        protected override void CreateInstanceDescriptor()
        {
            // Create and write instance descriptor for CLI discovery
            var automated = DetectAutomatedMode(System.Environment.GetCommandLineArgs());
            m_InstanceDescriptor = InstanceDescriptor.CreateCurrent(Port, automated);
            InstanceDescriptor.WriteToProjectRoot(m_InstanceDescriptor);
        }

        /// <summary>Remove the shared instance descriptor file on shutdown.</summary>
        protected override void DeleteInstanceDescriptor()
        {
            // Clean up instance descriptor file
            if (m_InstanceDescriptor != null)
            {
                InstanceDescriptor.RemoveFromProjectRoot(m_InstanceDescriptor.ProjectPath);
            }
            m_InstanceDescriptor = null;
        }

        /// <summary>Refresh the descriptor's heartbeat timestamp and current token.</summary>
        protected override void UpdateHeartBeat()
        {
            // Update heartbeat in instance descriptor
            if (m_InstanceDescriptor == null)
                return;

            lock (m_HeartbeatLock)
            {
                // Keep the port file's token current for discovery only — GetToken() validates the
                // live token, so this just catches up to a rotation on the next heartbeat.
                m_InstanceDescriptor.EvalToken = SecurityTokenManager.GetOrCreateToken();
                m_InstanceDescriptor.LastHeartbeat = DateTime.UtcNow;
                try
                {
                    InstanceDescriptor.WriteToProjectRoot(m_InstanceDescriptor);
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"Failed to update instance descriptor: {ex.Message}");
                }
            }
        }

        /// <summary>Build the Editor-specific status/heartbeat payload for /api/status, including the settle state.</summary>
        /// <returns>The status payload.</returns>
        protected override object GetServerStatus()
        {
            UpdateHeartBeat();
            // "blocked_by_dialog" is checked first: a modal dialog blocks the main thread
            // regardless of settle state, and is the more actionable fact for a caller — settling
            // resolves on its own, a dialog needs a human. Read off DialogStateTracker's lock, same
            // as /api/dialog, so this stays fast and needs no main-thread access. "settling" is
            // checked next: on a cold import the settle window precedes normal operation, and
            // clients waiting for "ready" (instead of firing commands blindly) land after the
            // Editor is actually serviceable (AUTHAPI-35).
            var openDialogs = Dialogs.CurrentlyOpen;
            string status = openDialogs.Count > 0 ? "blocked_by_dialog"
                : !IsSettled ? "settling"
                : (HasInstanceDescriptor ? "ready" : "error");

            // Keep the common case's shape unchanged (no "dialog" key at all) rather than
            // always emitting "dialog": null — this is a widely-polled endpoint, not worth
            // changing its byte shape for every request just to cover the rare blocked case.
            // Gated on status itself, not openDialogs.Count, so a dialog that opens during the
            // settle window doesn't attach a "dialog" key to a "settling" response (StatusResponse's
            // dialog field is documented as present only when status is "blocked_by_dialog").
            if (status == "blocked_by_dialog")
            {
                return new
                {
                    status,
                    lastHeartbeat = m_InstanceDescriptor?.LastHeartbeat,
                    capabilities = Capabilities,
                    dialog = BasePipelineServer.BuildDialogPayload(openDialogs[0])
                };
            }
            return new
            {
                status,
                lastHeartbeat = m_InstanceDescriptor?.LastHeartbeat,
                capabilities = Capabilities
            };
        }

        /// <summary>The bearer token clients must present to authenticate requests.</summary>
        /// <returns>The current live token.</returns>
        protected override string GetToken()
        {
            // Validate the live token so a rotation/revocation takes effect on the next request (not
            // the next heartbeat). The descriptor's EvalToken is discovery-only.
            return SecurityTokenManager.GetOrCreateToken();
        }
    }
}
