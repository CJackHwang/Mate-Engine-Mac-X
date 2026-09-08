using UnityEditor;
using UnityEngine;

namespace Unity.Pipeline.Editor
{
    /// <summary>
    /// Inspectable configuration and control surface for the live editor pipeline server. This is a
    /// settings asset, NOT the server's owner — <see cref="PipelineServerStartup"/> owns the server
    /// instance (a static owner survives domain reloads cleanly, whereas a ScriptableObject's
    /// lifetime does not track the server across editor events). The owner reads this asset's config
    /// when starting; the custom inspector drives Start/Stop through the owner and shows live status.
    ///
    /// The asset is optional: without it the owner uses the defaults below. "Window/Pipeline/Settings"
    /// creates it on demand.
    /// </summary>
    class EditorPipelineManager : ScriptableObject
    {
        [Tooltip("HTTP port for the editor server. 0 = auto-assign from the 7800-7849 range. Applies on next start.")]
        [SerializeField] private ushort m_Port = 0;

        [Tooltip("Start the server automatically when the editor loads. Applies on next editor load.")]
        [SerializeField] private bool m_AutoStart = true;

        [Tooltip("Self-heal: if the HTTP listener dies without a Stop(), re-open it on a timer. " +
                 "Keeps auto-tick on so the editor keeps ticking while unfocused (required for the watchdog).")]
        [SerializeField] private bool m_WatchdogEnabled = true;

        [Tooltip("How often the watchdog checks the listener, between 1 and 60 seconds.")]
        [SerializeField] private int m_WatchdogIntervalSeconds = 5;

        [Tooltip("Log every command request/response (raw JSON) handled by the editor server to " +
                 "<project>/Logs/pipeline.log. Editor only; applies live.")]
        [SerializeField] private bool m_LogRequestsResponses = false;

        [Tooltip("Record local eval-usage telemetry (fingerprints + shape, no raw source) to " +
                 "<project>/Library/Pipeline/eval-usage.jsonl. Read it back with the 'report_evals' " +
                 "command. Local-only; no data leaves the machine. Applies live.")]
        [SerializeField] private bool m_EvalTelemetryEnabled = true;

        [Tooltip("Also store the raw eval source in each eval-usage telemetry record. Off by default " +
                 "(privacy-first) — enable only for local debugging. Applies live.")]
        [SerializeField] private bool m_StoreEvalSource = false;

        [Tooltip("Accept requests from a client running in a sandboxed browser frame (Origin: null), " +
                 "such as a plugin hosted inside a web application. An ordinary web page is refused " +
                 "either way, and a sandboxed one still needs the bearer token. Applies on next start.")]
        [SerializeField] private bool m_AllowBrowserClients = false;

        // Hot Reload Watch config, drawn by a dedicated inspector section (not DrawDefaultInspector).
        [HideInInspector, SerializeField] private bool m_HotReloadRepushOnConnect = true;

        /// <summary>HTTP port for the editor server. 0 = auto-assign. Applies on next start.</summary>
        public int Port => m_Port;
        /// <summary>Start the server automatically when the editor loads.</summary>
        public bool AutoStart => m_AutoStart;
        /// <summary>Self-heal: re-open the listener on a timer if it dies without a Stop().</summary>
        public bool WatchdogEnabled => m_WatchdogEnabled;
        /// <summary>How often the watchdog checks the listener, in seconds.</summary>
        public int WatchdogIntervalSeconds => m_WatchdogIntervalSeconds;
        /// <summary>Log every command request/response to Logs/pipeline.log.</summary>
        public bool LogRequestsResponses => m_LogRequestsResponses;
        public bool EvalTelemetryEnabled => m_EvalTelemetryEnabled;
        public bool StoreEvalSource => m_StoreEvalSource;
        public bool AllowBrowserClients => m_AllowBrowserClients;

        /// <summary>Player-target watches: re-push the watched hot-reload state to any player that
        /// connects mid-watch, so a restarted player catches up without waiting for the next save.</summary>
        internal bool HotReloadRepushOnConnect => m_HotReloadRepushOnConnect;

        /// <summary>Whether the live server (owned by PipelineServerStartup) is actually running.</summary>
        public bool IsServerRunning => PipelineServerStartup.Server != null && PipelineServerStartup.Server.IsRunning;

        /// <summary>The port the live server is actually listening on, or 0 when stopped.</summary>
        public int ActualPort => PipelineServerStartup.Server?.Port ?? 0;

        /// <summary>Start the live server (delegates to the static owner, which reads this config).</summary>
        public void StartServer() => PipelineServerStartup.EnsureServerStarted();

        /// <summary>Stop the live server.</summary>
        public void StopServer() => PipelineServerStartup.StopServer();

        /// <summary>Restart the live server.</summary>
        public void RestartServer() => PipelineServerStartup.RestartServer();

        /// <summary>
        /// Load the single settings asset if one exists, otherwise null (the owner falls back to
        /// defaults). No caching — assets are cheap to look up and this is only read at start/inspect.
        /// </summary>
        /// <returns>The settings asset, or null if none exists.</returns>
        public static EditorPipelineManager Load()
        {
            var guids = AssetDatabase.FindAssets("t:EditorPipelineManager");
            if (guids.Length == 0)
                return null;
            return AssetDatabase.LoadAssetAtPath<EditorPipelineManager>(
                AssetDatabase.GUIDToAssetPath(guids[0]));
        }

        private void OnValidate()
        {
            m_WatchdogIntervalSeconds = Mathf.Clamp(m_WatchdogIntervalSeconds, 1, 60);
        }
    }
}
