using System.IO;
using UnityEditor;
using UnityEngine;

namespace Unity.Pipeline.Editor
{
    /// <summary>
    /// Custom inspector for the <see cref="EditorPipelineManager"/> settings asset: shows live server
    /// status (accurate running check + actual port, read from the static owner) and offers
    /// Start/Stop/Restart buttons, alongside the editable configuration. Watchdog edits are pushed to
    /// a running server immediately; port/autoStart apply on the next start.
    /// </summary>
    [CustomEditor(typeof(EditorPipelineManager))]
    class EditorPipelineManagerEditor : UnityEditor.Editor
    {
        // Repaint() from inside OnInspectorGUI re-triggers OnInspectorGUI on the next editor tick, so
        // a running server redrew this IMGUI-heavy inspector every frame (several ms) while merely
        // visible. Repaint from EditorApplication.update at a low rate instead.
        private const double StatusRepaintInterval = 0.25;
        private double m_NextStatusRepaint;

        private void OnEnable()
        {
            EditorApplication.update += ThrottledStatusRepaint;
        }

        private void OnDisable()
        {
            EditorApplication.update -= ThrottledStatusRepaint;
        }

        private void ThrottledStatusRepaint()
        {
            var mgr = target as EditorPipelineManager;
            if (mgr == null || !(mgr.IsServerRunning || EditorHotReloadWatcher.IsWatching))
                return;

            double now = EditorApplication.timeSinceStartup;
            if (now < m_NextStatusRepaint)
                return;
            m_NextStatusRepaint = now + StatusRepaintInterval;
            Repaint();
        }

        /// <summary>Draw the default inspector plus live server status and Start/Stop/Restart controls.</summary>
        public override void OnInspectorGUI()
        {
            var mgr = (EditorPipelineManager)target;

            EditorGUI.BeginChangeCheck();
            DrawDefaultInspector();
            if (EditorGUI.EndChangeCheck())
            {
                // Push edited watchdog config to the live server (owned by PipelineServerStartup), so
                // changes take effect without a restart.
                var server = PipelineServerStartup.Server;
                if (server != null)
                {
                    server.WatchdogEnabled = mgr.WatchdogEnabled;
                    server.WatchdogIntervalSeconds = mgr.WatchdogIntervalSeconds;
                    server.LogRequestsResponses = mgr.LogRequestsResponses;
                }

                // Eval-usage telemetry config lives on static fields (recording happens in the
                // Runtime command layer, not the server), so push edits there too — applies live.
                // Goes through the same ApplyEvalTelemetrySettings the startup/server-start paths
                // use, so there is exactly one place that knows the settings->statics mapping.
                PipelineServerStartup.ApplyEvalTelemetrySettings(mgr);
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Status", EditorStyles.boldLabel);
            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.Toggle("Running", mgr.IsServerRunning);
                EditorGUILayout.IntField("Actual Port", mgr.ActualPort);
            }

            EditorGUILayout.Space();
            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(mgr.IsServerRunning))
                {
                    if (GUILayout.Button("Start"))
                        mgr.StartServer();
                }
                using (new EditorGUI.DisabledScope(!mgr.IsServerRunning))
                {
                    if (GUILayout.Button("Stop"))
                        mgr.StopServer();
                }
                if (GUILayout.Button("Restart"))
                    mgr.RestartServer();
            }

            DrawHotReloadWatchSection();
            // Live status is kept current by ThrottledStatusRepaint — never Repaint() from here.
        }

        /// <summary>
        /// Watch the project's Assets folder and re-apply affected [HotReload] methods on every save,
        /// driving <see cref="EditorHotReloadWatcher"/>. Neither scope, target, nor backend is a
        /// choice: the whole Assets tree is watched, each save goes to the connected player(s) if any,
        /// else applies in-editor, and both paths run on the interpreter; a read-only row shows the
        /// current target resolution. Config is locked while a watch is active.
        /// </summary>
        private void DrawHotReloadWatchSection()
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Hot Reload Interpreter Watch", EditorStyles.boldLabel);

            bool watching = EditorHotReloadWatcher.IsWatching;

            serializedObject.Update();

            // Not a picker: each save is pushed to the connected player(s) if any, else applied
            // in-editor — resolved per save, so plugging in a device mid-watch reroutes the next save.
            int connected = EditorHotReloadWatcher.ConnectedPlayerCount;
            using (new EditorGUI.DisabledScope(true))
                EditorGUILayout.LabelField(
                    new GUIContent("Target", "Resolved automatically at each file change: development player(s) if connected, otherwise the reload applies in this editor process. Both run on the interpreter."),
                    new GUIContent(connected > 0
                        ? $"Player — {connected} connected"
                        : "In Editor — no player connected"));

            // Outside the watching-locked scope: read at each player-connect event, so toggling it
            // takes effect for the next connection even while a watch is active.
            EditorGUILayout.PropertyField(
                serializedObject.FindProperty("m_HotReloadRepushOnConnect"),
                new GUIContent("Re-push On Connect",
                    "Re-push the watched hot-reload state to any player that connects while the watch " +
                    "is active. A restarted player boots the original build without previously pushed " +
                    "overrides; this catches it up immediately instead of waiting for the next save."));

            serializedObject.ApplyModifiedProperties();

            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(watching))
                {
                    // The UI always watches the whole Assets tree; StartWatch itself accepts any
                    // file or folder for CLI/API callers.
                    if (GUILayout.Button("Start Watching"))
                        EditorHotReloadWatcher.StartWatch(Path.GetFullPath(Application.dataPath),
                            isFolder: true);
                }
                using (new EditorGUI.DisabledScope(!watching))
                {
                    if (GUILayout.Button("Stop Watching"))
                        EditorHotReloadWatcher.StopWatch();
                }
            }

            if (watching)
                EditorGUILayout.HelpBox(
                    "Asset auto-refresh is disabled while watching, so saved scripts hot-reload instead of " +
                    "triggering a domain reload. New or changed assets won't import until you refresh manually " +
                    "(Cmd/Ctrl+R); Stop Watching restores your auto-refresh setting.",
                    MessageType.Warning);

            using (new EditorGUI.DisabledScope(true))
            {
                // Read WatchPath once: it goes null the instant the watch stops (domain reload / StopWatch),
                // which can race the `watching` captured at the top of this method — guard against null here.
                var watchPath = EditorHotReloadWatcher.WatchPath;
                // The live Target row above shows where saves currently go; this line covers what is watched.
                string status = watching && !string.IsNullOrEmpty(watchPath)
                    ? $"{(EditorHotReloadWatcher.IsFolder ? "folder" : "file")} · {Path.GetFileName(watchPath.TrimEnd('/', '\\'))}"
                    : "idle";
                EditorGUILayout.LabelField("Watching", status);

                if (watching)
                {
                    // Liveness: saves applying while the event count stays flat means the OS file
                    // watcher is dead and the reconcile poll is carrying the watch.
                    EditorGUILayout.LabelField("Watcher Events",
                        $"{EditorHotReloadWatcher.FsEventCount}{Ago(EditorHotReloadWatcher.LastFsEventUtc)}");
                    EditorGUILayout.LabelField("Last Apply",
                        EditorHotReloadWatcher.LastApplyFile is string f
                            ? $"{f}{Ago(EditorHotReloadWatcher.LastApplyUtc)}"
                            : "none yet");
                }
            }
        }

        private static string Ago(System.DateTime? utc)
        {
            if (utc == null) return "";
            var s = (System.DateTime.UtcNow - utc.Value).TotalSeconds;
            return s < 1 ? " · just now"
                : s < 120 ? $" · {s:0}s ago"
                : s < 7200 ? $" · {s / 60:0}m ago"
                : $" · {s / 3600:0}h ago";
        }
    }
}
