using UnityEditor;
using UnityEngine;
using Unity.Pipeline.Models;
using Unity.Pipeline.Commands;
using Unity.Pipeline.Telemetry;
using Unity.Pipeline.Threading;
using UnityEditor.MPE;
#if UNITY_6000_5_OR_NEWER
using Unity.Scripting.LifecycleManagement;
#endif

namespace Unity.Pipeline.Editor
{
    /// <summary>
    /// Automatically starts Pipeline HTTP server when Unity Editor loads.
    /// Handles startup, domain reload persistence, and cleanup.
    ///
    /// Static owner of the live editor server: a static owner survives domain reloads cleanly
    /// (re-created by [InitializeOnLoad]), whereas a ScriptableObject's lifetime does not track the
    /// server across editor events. <see cref="EditorPipelineManager"/> is an optional, inspectable
    /// settings asset whose config is read here at start.
    /// </summary>
    [InitializeOnLoad]
#if UNITY_6000_5_OR_NEWER
    [NoAutoStaticsCleanup]
#endif
    static class PipelineServerStartup
    {
        private static EditorPipelineServer m_Server;

        /// <summary>
        /// The live editor pipeline server instance (null when stopped). Exposed so the test guard
        /// can disable its watchdog for a test run and the EditorPipelineManager inspector can read
        /// live status.
        /// </summary>
        public static EditorPipelineServer Server => m_Server;

        static PipelineServerStartup()
        {
            // Seed the compile service's define snapshot while we're guaranteed on the main
            // thread — a background compile fired before any main-thread parse would otherwise
            // run with an empty define set (wrong #if branches). Before the worker-process
            // early-out: costs one editor API call and holds everywhere.
            Compilation.RoslynCompilationService.SnapshotProjectDefines();

            // Don't start server in AssetImportWorker processes
            if (!IsMainProcess())
                return;
            // Setup command discovery using TypeCache for fast Editor performance
            CommandRegistry.SetDiscovery(new TypeCacheCommandDiscovery());

            // Clean up any stale instance descriptor files from previous sessions
            CleanupStaleDescriptors();

            var cfg = EditorPipelineManager.Load();

            // Apply the local eval-usage telemetry config on EVERY domain reload, not only when
            // the server starts (AUTHAPI-29): recording hooks eval itself, not the server
            // lifecycle, so an AutoStart=false session must still honor the configured opt-outs
            // instead of falling back to the hardcoded defaults.
            ApplyEvalTelemetrySettings(cfg);

            // Start the pipeline server (respecting the settings asset's autoStart if one exists)
            if (cfg?.AutoStart ?? true)
                StartServer();

            // Handle domain reloads and editor shutdown
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            EditorApplication.quitting += OnEditorQuitting;

            // Handle domain reload detection
            AssemblyReloadEvents.beforeAssemblyReload += OnBeforeAssemblyReload;
            AssemblyReloadEvents.afterAssemblyReload += OnAfterAssemblyReload;
        }

        /// <summary>Start the server if it isn't already running.</summary>
        public static void EnsureServerStarted()
        {
            StartServer();
        }

        /// <summary>
        /// Force a clean restart of the editor pipeline server. Unlike EnsureServerStarted, this
        /// works even when the current server's listener has died but still reports IsRunning
        /// (e.g. after a test disrupted it). Used by tests to revive the live server they disrupted.
        /// </summary>
        public static void RestartServer()
        {
            StopServer();
            StartServer();
        }

        [MenuItem("Window/Pipeline/Start Server")]
        private static void MenuStartServer()
        {
            StartServer();
            if (m_Server != null && m_Server.IsRunning)
                Debug.Log($"Pipeline Server started on port {m_Server.Port}");
            else
                Debug.LogWarning("Pipeline Server failed to start");
        }

        [MenuItem("Window/Pipeline/Start Server", true)]
        private static bool MenuStartServerValidate() => m_Server == null || !m_Server.IsRunning;

        [MenuItem("Window/Pipeline/Stop Server")]
        private static void MenuStopServer()
        {
            StopServer();
        }

        [MenuItem("Window/Pipeline/Stop Server", true)]
        private static bool MenuStopServerValidate() => m_Server != null && m_Server.IsRunning;

        /// <summary>
        /// Select the EditorPipelineManager settings asset, creating it under Assets/Settings/Pipeline
        /// on first use (the live server otherwise runs from built-in defaults).
        /// </summary>
        [MenuItem("Window/Pipeline/Settings...")]
        private static void OpenSettings()
        {
            var mgr = EditorPipelineManager.Load();
            if (mgr == null)
            {
                const string folder = "Assets/Settings/Pipeline";
                if (!AssetDatabase.IsValidFolder(folder))
                {
                    if (!AssetDatabase.IsValidFolder("Assets/Settings"))
                        AssetDatabase.CreateFolder("Assets", "Settings");
                    AssetDatabase.CreateFolder("Assets/Settings", "Pipeline");
                }

                mgr = ScriptableObject.CreateInstance<EditorPipelineManager>();
                AssetDatabase.CreateAsset(mgr, folder + "/EditorPipelineManager.asset");
                AssetDatabase.SaveAssets();
            }
            Selection.activeObject = mgr;
            EditorGUIUtility.PingObject(mgr);
        }

        internal static bool IsMainProcess()
        {
            // Check command line arguments for asset import worker indicators
            var args = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "-readonly" || args[i] == "--virtual-project-clone")
                    return true;
            }

            if (AssetDatabase.IsAssetImportWorkerProcess())
                return false;

            if (ProcessService.level != ProcessLevel.Main)
                return false;

            return true;
        }

        /// <summary>
        /// Apply the local eval-usage telemetry config (AUTHAPI-29). Local-only, no transmission;
        /// disabling it stops even local recording. StoreSource is the explicit opt-in for
        /// persisting raw eval source (off by default). Resolving the directory primes the
        /// dataPath-derived default on the main thread, so off-main-thread readers (report_evals)
        /// and the background record task never touch Unity APIs.
        ///
        /// Internal (not private): this is the ONE place that knows how to turn the settings asset
        /// into the static telemetry config, so every push site — the domain-reload static ctor,
        /// server start, and <see cref="EditorPipelineManagerEditor"/>'s live inspector push — calls
        /// through here rather than re-deriving the two-field mapping independently, which would
        /// silently drift the moment a fourth push site (or a third telemetry field) showed up.
        /// </summary>
        internal static void ApplyEvalTelemetrySettings(EditorPipelineManager cfg)
        {
            EvalUsageTelemetry.Enabled = cfg?.EvalTelemetryEnabled ?? true;
            EvalUsageTelemetry.StoreSource = cfg?.StoreEvalSource ?? false;
            EvalUsageTelemetry.ResolveDirectory();
        }

        /// <summary>
        /// Start the Pipeline HTTP server, reading configuration from the EditorPipelineManager
        /// settings asset if one exists (otherwise using defaults).
        /// </summary>
        private static void StartServer()
        {
            if (m_Server != null && m_Server.IsRunning)
                return;

            try
            {
                var cfg = EditorPipelineManager.Load();
                m_Server = new EditorPipelineServer
                {
                    // Self-healing watchdog: if the listener dies without a Stop() (an unexpected fault
                    // outside a domain reload), the watchdog re-opens it so the dogfood loop doesn't
                    // wedge. Set before Start() (Start arms the watchdog).
                    WatchdogEnabled = cfg?.WatchdogEnabled ?? true,
                    WatchdogIntervalSeconds = cfg?.WatchdogIntervalSeconds ?? 5,
                    LogRequestsResponses = cfg?.LogRequestsResponses ?? false,
                    AllowSandboxedBrowserClients = cfg?.AllowBrowserClients ?? false,
                    MaxRequestBodyBytes = 32L * 1024 * 1024
                };

                // Rotate the transaction log once per Unity session (main thread; SessionState-gated).
                // The append path runs off-thread and can't touch SessionState.
                PipelineTransactionLog.RotateForNewSession();

                // Re-applied on every server start so inspector edits made this session stick
                // even without a domain reload (the static ctor covers the reload path).
                ApplyEvalTelemetrySettings(cfg);

                m_Server.Start(cfg?.Port ?? 0); // 0 auto-assigns from the 7800-7849 range.

                // Restore whatever auto-tick state the user last set this session (survives the
                // domain reload that just wiped AutoTickCommand's statics). Only a session with no
                // prior explicit set_autotick call falls back to a default, and that default is "on"
                // when the watchdog is enabled: the watchdog rides EditorApplication.update, but a
                // backgrounded/idle editor stops ticking once the listener dies (no requests left to
                // wake it) — the exact moment the watchdog must run. Keeping auto-tick on by default
                // keeps the update loop spinning regardless of focus, which keeps both the watchdog
                // AND the dispatcher message pump alive.
                Commands.AutoTickCommand.RestoreFromSession(defaultEnabled: m_Server.WatchdogEnabled);
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"Failed to start Pipeline Server: {ex.Message}");
            }
        }

        /// <summary>
        /// Stop the Pipeline HTTP server.
        /// </summary>
        public static void StopServer()
        {
            if (m_Server != null)
            {
                try
                {
                    m_Server.Stop();
                }
                catch (System.Exception ex)
                {
                    Debug.LogWarning($"Error stopping Pipeline Server: {ex.Message}");
                }
                finally
                {
                    m_Server = null;
                }
            }
        }

        /// <summary>
        /// Clean up stale instance descriptor files from previous Editor sessions.
        /// </summary>
        private static void CleanupStaleDescriptors()
        {
            var projectPath = System.IO.Path.GetDirectoryName(Application.dataPath);

            // Try to read existing descriptor
            var existing = InstanceDescriptor.ReadFromProjectRoot(projectPath);
            if (existing != null)
            {
                // Check if process is still running
                try
                {
                    var process = System.Diagnostics.Process.GetProcessById(existing.Pid);
                    if (process.HasExited)
                    {
                        // Process is dead, remove stale file
                        InstanceDescriptor.RemoveFromProjectRoot(projectPath);
                    }
                }
                catch
                {
                    // Process doesn't exist or access denied, remove stale file
                    InstanceDescriptor.RemoveFromProjectRoot(projectPath);
                }
            }
        }

        /// <summary>
        /// Handle play mode state changes.
        /// </summary>
        private static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            // Server continues running through play mode changes
            // Status endpoint will reflect current play mode via EditorApplication.isPlaying

            // RuntimePipelineDriver.Awake() switches CommandRegistry to reflection-based discovery
            // on every Play Mode entry (a Player has no TypeCache). With Fast Enter Play Mode
            // (domain reload disabled), nothing else runs this static constructor to switch it back
            // on the way out — pre-FEPM, re-entering Edit mode via a domain reload did this for
            // free. Restore TypeCache discovery explicitly here instead of relying on that reload.
            if (state == PlayModeStateChange.EnteredEditMode)
                CommandRegistry.SetDiscovery(new TypeCacheCommandDiscovery());
        }

        /// <summary>
        /// Handle Editor shutdown.
        /// </summary>
        private static void OnEditorQuitting()
        {
            StopServer(); // Stop() shuts down the server's own dispatcher.

            // After StopServer, so the session is closed last. A command that finished in the same
            // frame as the quit may still have its report queued on the dispatcher, and Shutdown
            // drops it — a lost report is preferable to running arbitrary queued work while the
            // Editor tears down.
            PipelineAnalytics.SendSessionStoppedIfStarted();
        }

        /// <summary>
        /// Handle before assembly reload (domain reload).
        /// </summary>
        private static void OnBeforeAssemblyReload()
        {
            // Server will be automatically recreated after reload due to [InitializeOnLoad]
            // Instance descriptor file will be cleaned up and recreated

        }

        /// <summary>
        /// Handle after assembly reload (domain reload).
        /// </summary>
        private static void OnAfterAssemblyReload()
        {
            // Server should already be restarted via [InitializeOnLoad]
            // This is mainly for logging/verification
        }
    }
}
