using System;
using System.Collections.Generic;
using System.Reflection;
using Unity.Pipeline.Config;
using Unity.Pipeline.Commands;
using Unity.Pipeline.HotReload;
using Unity.Pipeline.Runtime.Telemetry;
using UnityEngine;
#if (UNITY_EDITOR || DEVELOPMENT_BUILD)
using UnityEngine.Networking.PlayerConnection;
#endif

namespace Unity.Pipeline
{
    /// <summary>
    /// Drives the runtime Pipeline server. Created exactly once, in code, by
    /// <see cref="RuntimePipelineBootstrap"/> — never authored in a scene. Hidden from Add
    /// Component (nothing to configure via the Inspector; see <see cref="Initialize"/> instead)
    /// and guarded against stacking multiple instances on one GameObject, in case it's ever added
    /// manually or via a script despite that.
    /// </summary>
    [AddComponentMenu("")]
    [DisallowMultipleComponent]
    public class RuntimePipelineDriver : MonoBehaviour
    {
        // Not yet exposed via RuntimePipelineConfig/the runtime settings page — always on. Wire up
        // through m_Config alongside the other Runtime Behavior settings if a project needs to
        // disable the overlay.
        private const bool ShowReloadOverlay = true;

        private RuntimePipelineServer m_Server;
        private RuntimePipelineConfig m_Config;
        private IReadOnlyList<string> m_AllowedReloadRoots = Array.Empty<string>();

        // Public half of the project's push-signing key. Baked at build time into the
        // RuntimePipelineBuildInfo asset (from Library/Pipeline/signing.key in the editor) and
        // supplied via Initialize — a player build rejects every hot-reload push that is not signed
        // by the matching private key; empty means "reject all pushes" (strict policy, no fallback).
        private string m_PushPublicKey = "";
        private bool m_OwnsSampler;

        // Restored by StopServer so Play Mode exit doesn't leave the user's setting overridden.
        private bool m_PreviousRunInBackground;

        // True only between something overriding Application.runInBackground (StartServer, or
        // DrainPendingReloads reacting to a hot-reload push that arrived with no HTTP server
        // running) and StopServer restoring it. Both writers check this before saving
        // m_PreviousRunInBackground, so whichever gets there first "owns" the saved value — the
        // other must not clobber it with the already-overridden "true". Gating the restore on this
        // (rather than m_Server.IsRunning) also means it still happens even if the listener already
        // died on its own before StopServer runs — otherwise runInBackground would stay stuck on,
        // reproducing a narrower version of the bug this ownership move fixed in the first place.
        private bool m_RunInBackgroundOverridden;

        /// <summary>Get the runtime server instance if it's running.</summary>
        public RuntimePipelineServer Server => m_Server;

        /// <summary>Whether the server is currently running.</summary>
        public bool IsServerRunning => m_Server != null && m_Server.IsRunning;

        /// <summary>Get the actual port the server is running on.</summary>
        public int ActualPort => m_Server?.Port ?? 0;

        /// <summary>The configuration this driver was initialized with.</summary>
        public RuntimePipelineConfig Config => m_Config;

        /// <summary>Absolute roots this build may hot reload source files from. Baked at build time.</summary>
        public IReadOnlyList<string> AllowedReloadRoots => m_AllowedReloadRoots;

        /// <summary>
        /// Supply configuration and baked hot-reload roots. Must be called once, immediately after
        /// AddComponent (before the end of the current frame, i.e. before Start runs).
        /// </summary>
        /// <param name="config">The configuration to run with.</param>
        /// <param name="allowedReloadRoots">Absolute roots this build may hot reload source files from.</param>
        /// <param name="pushPublicKey">Public half of the push-signing key pushes must be signed with; empty rejects all pushes.</param>
        public void Initialize(RuntimePipelineConfig config, IReadOnlyList<string> allowedReloadRoots,
            string pushPublicKey = "")
        {
            m_Config = config;
            m_AllowedReloadRoots = allowedReloadRoots ?? Array.Empty<string>();
            m_PushPublicKey = pushPublicKey ?? "";
        }

        /// <summary>Public half of the project push-signing key. Baked at build time.</summary>
        public string PushPublicKey => m_PushPublicKey;

        void Awake()
        {
            // Persistent across scene loads; exactly one of these ever exists (created by
            // RuntimePipelineBootstrap), so there is no duplicate-instance case to defend against.
            // DontDestroyOnLoad only functions (and is only legal to call) in Play Mode; invoking it
            // from an Edit Mode context (e.g. an EditMode test, or this driver created via eval)
            // throws, which would otherwise abort the rest of Awake below.
            if (Application.isPlaying)
                DontDestroyOnLoad(gameObject);

            // Set up command discovery (the server owns its own dispatcher, initialized on Start).
            CommandRegistry.SetDiscovery(null); // Triggers reflection-based discovery

            // Auto-discover and register hot-reloadable methods. Hot reload requires this driver to
            // be present (it owns the communication workflow), so discovery lives here rather than in
            // each gameplay script's Awake.
            RegisterDiscoveredHotReloadMethods();

            // Own the process-wide frame-stats sampler. A Player has no profiler window or
            // EditorApplication.update to drive sampling, so the driver feeds it from Update (below) and
            // the runtime_status telemetry command reads FrameStatsSampler.Shared. Created here (rather than
            // on server start) so fps history is already warm by the time an agent connects. Only claim
            // ownership if nothing already created one — a second, transient driver (e.g. a test creating
            // its own driver alongside one RuntimeInitializeOnLoadMethod already created) must not steal or
            // later tear down a sibling driver's sampler.
            if (FrameStatsSampler.Shared == null)
            {
                FrameStatsSampler.Shared = new FrameStatsSampler();
                m_OwnsSampler = true;
            }
        }

        void Start()
        {
            if (m_Config != null && m_Config.autoStart && m_Config.enableInBuilds)
            {
                StartServer();
            }

#if (UNITY_EDITOR || DEVELOPMENT_BUILD)
            // Independent of the HTTP server: hot-reload IL pushed from the editor arrives over
            // PlayerConnection (the Profiler's channel — no open port needed, tunnels over USB).
            RegisterReloadReceiver();

            if (ShowReloadOverlay && GetComponent<HotReloadStatusOverlay>() == null)
                gameObject.AddComponent<HotReloadStatusOverlay>();
#endif
        }

        /// <summary>
        /// Scan loaded user assemblies for methods tagged [HotReload] (in-place workflow) and
        /// [HotReloadWithOverrides] (helper workflow) and register them as reload targets.
        /// </summary>
        private static void RegisterDiscoveredHotReloadMethods()
        {
            const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic
                | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

            int registered = 0;

            foreach (var assembly in PipelineUtils.GetLoadedAssemblies())
            {
                if (!ShouldScanForHotReload(assembly))
                    continue;

                Type[] types;
                try
                {
                    types = assembly.GetTypes();
                }
                catch (ReflectionTypeLoadException ex)
                {
                    types = ex.Types; // best effort: keep the types that did load
                }

                foreach (var type in types)
                {
                    if (type == null)
                        continue;

                    foreach (var method in type.GetMethods(flags))
                    {
                        var inPlace = method.GetCustomAttribute<HotReloadAttribute>();
                        if (inPlace != null)
                        {
                            // The weaver keys dispatch on TypeName.MethodName, so register with the
                            // default id (no custom Id) to match.
                            HotReloadRegistry.RegisterReloadableMethod(
                                method, new HotReloadWithOverridesAttribute { RequireMainThread = inPlace.RequireMainThread });
                            registered++;
                            continue;
                        }

                        var reloadable = method.GetCustomAttribute<HotReloadWithOverridesAttribute>();
                        if (reloadable != null)
                        {
                            HotReloadRegistry.RegisterReloadableMethod(method, reloadable);
                            registered++;
                        }
                    }
                }
            }

            // Always log, including 0: a zero count on device is the primary stripping/attribute
            // diagnostic, and its absence is ambiguous with the log line being suppressed.
            Debug.Log($"Pipeline: Auto-discovered and registered {registered} hot-reloadable method(s)." +
                (registered == 0
                    ? " If methods were expected: are the [HotReload] attributes in this build, and did the owning assembly survive stripping?"
                    : ""));
        }

        /// <summary>Skip engine/framework assemblies that cannot contain user hot-reload methods.</summary>
        private static bool ShouldScanForHotReload(Assembly assembly)
        {
            if (assembly.IsDynamic)
                return false;

            var name = assembly.GetName().Name;
            if (string.IsNullOrEmpty(name))
                return false;

            foreach (var prefix in s_HotReloadSkipPrefixes)
            {
                if (name.StartsWith(prefix, StringComparison.Ordinal))
                    return false;
            }

            return true;
        }

        private static readonly string[] s_HotReloadSkipPrefixes =
        {
            "System", "mscorlib", "netstandard", "Microsoft", "Mono.", "nunit",
            "UnityEngine", "UnityEditor", "Unity.Collections", "Unity.Burst",
            "Unity.Mathematics", "Newtonsoft", "log4net", "ICSharpCode",
        };

        /// <summary>
        /// The maxWorkItemsPerFrame value actually governing the dispatcher right now — live from
        /// the settings file in the Editor, frozen to the build-time value in a Player. Exposed
        /// (rather than reading m_Config.maxWorkItemsPerFrame directly) so status reporting and
        /// Update() can never disagree about which value is actually in effect.
        /// </summary>
        public int CurrentMaxWorkItemsPerFrame
        {
            get
            {
                var fallback = m_Config != null ? m_Config.maxWorkItemsPerFrame : 10;
#if UNITY_EDITOR
                return RuntimePipelineConfig.GetLiveMaxWorkItemsPerFrame(fallback);
#else
                return fallback;
#endif
            }
        }

        void Update()
        {
            // Feed the frame-stats sampler once per frame on the main thread. Uses unscaled delta so
            // reported fps reflects real frame pacing regardless of Time.timeScale.
            FrameStatsSampler.Shared?.Sample(Time.unscaledDeltaTime);

            // Pump this server's own dispatcher for main-thread operations (player builds have no
            // EditorApplication.update to auto-pump it).
            m_Server?.Dispatcher.ProcessWorkQueue(CurrentMaxWorkItemsPerFrame);

            // Drive the watchdog (player builds have no EditorApplication.update). No-op unless the
            // server's watchdog is enabled and armed.
            m_Server?.WatchdogTick();

#if (UNITY_EDITOR || DEVELOPMENT_BUILD)
            DrainPendingReloads();
#endif
        }

        void OnApplicationQuit()
        {
            StopServer();
        }

        void OnDestroy()
        {
            StopServer();

            if (RuntimePipelineBootstrap.Instance == this)
                RuntimePipelineBootstrap.Instance = null;

            // Only the driver that actually created the shared sampler tears it down — see Awake().
            if (m_OwnsSampler)
            {
                FrameStatsSampler.Shared?.Dispose();
                FrameStatsSampler.Shared = null;
            }
#if (UNITY_EDITOR || DEVELOPMENT_BUILD)
            UnregisterReloadReceiver();
#endif
        }

#if (UNITY_EDITOR || DEVELOPMENT_BUILD)
        // ---- PlayerConnection hot-reload receiver (editor pushes compiled override IL to this player) ----

        // Pushes arrive on the connection message pump (not guaranteed main thread); enqueue here and
        // apply in Update() where the interpreter + Unity APIs are safe to touch.
        private readonly List<byte[]> m_PendingReloads = new List<byte[]>();
        private bool m_ReloadReceiverRegistered;

        // Push-signing session state. The nonce is fresh per receiver lifetime and announced to the
        // editor via HandshakeMsg; every accepted push must be signed over it (see PushEnvelope).
        private byte[] m_PushSessionNonce;
        private PushVerifier m_PushVerifier;

        private void RegisterReloadReceiver()
        {
            try
            {
                PlayerConnection.instance.Register(PipelineHotReloadConnect.ApplyReloadMsg, OnApplyReloadMessage);
                PlayerConnection.instance.Register(PipelineHotReloadConnect.ReloadPendingMsg, OnReloadPendingMessage);
                PlayerConnection.instance.Register(PipelineHotReloadConnect.ReloadFailedMsg, OnReloadFailedMessage);
                PlayerConnection.instance.Register(PipelineHotReloadConnect.RequestHandshakeMsg, OnRequestHandshakeMessage);
                PlayerConnection.instance.RegisterConnection(OnPushPeerConnected);
                m_ReloadReceiverRegistered = true;

                m_PushSessionNonce = PushEnvelope.CreateNonce();
#if UNITY_EDITOR
                // Editor play mode is not a network surface for pushes (they originate in this same
                // process, and remote access goes through the token-authenticated HTTP server), so
                // signing is not enforced here — see DrainPendingReloads.
                Debug.Log("Pipeline: PlayerConnection hot-reload receiver ready (editor play mode — push signing not enforced).");
#else
                if (!string.IsNullOrEmpty(m_PushPublicKey))
                {
                    m_PushVerifier = new PushVerifier(m_PushPublicKey, m_PushSessionNonce);
                    var fingerprint = PushEnvelope.Fingerprint(m_PushPublicKey);
                    if (PushEnvelope.VerifySelfTest(m_PushPublicKey, out var selfTestError))
                        Debug.Log($"Pipeline: PlayerConnection hot-reload receiver ready — signed pushes only (key {fingerprint}, verify self-test OK). Push IL from the editor via 'reload_file_player_interpreter'.");
                    else
                        Debug.LogError($"Pipeline: push-signature verify self-test FAILED on this platform ({selfTestError}) — every push will be rejected as CryptoUnavailable.");
                }
                else
                {
                    // Strict policy: a receiver without a baked key accepts nothing. The only way to
                    // get here is a build whose RuntimePipelineBuildInfo carries no key (e.g. an older
                    // build, or one produced without the build processor running).
                    Debug.LogWarning("Pipeline: hot-reload push receiver has NO baked signing key — all pushes will be rejected. " +
                        "Rebuild with the Pipeline build processor enabled (it generates the key and bakes the public half into RuntimePipelineBuildInfo).");
                }
#endif
                SendPushHandshake(); // the editor may already be connected (e.g. autoconnect raced us)
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"Pipeline: PlayerConnection register failed: {ex.Message}");
            }
        }

        private void UnregisterReloadReceiver()
        {
            if (!m_ReloadReceiverRegistered) return;
            try
            {
                PlayerConnection.instance.Unregister(PipelineHotReloadConnect.ApplyReloadMsg, OnApplyReloadMessage);
                PlayerConnection.instance.Unregister(PipelineHotReloadConnect.ReloadPendingMsg, OnReloadPendingMessage);
                PlayerConnection.instance.Unregister(PipelineHotReloadConnect.ReloadFailedMsg, OnReloadFailedMessage);
                PlayerConnection.instance.Unregister(PipelineHotReloadConnect.RequestHandshakeMsg, OnRequestHandshakeMessage);
                PlayerConnection.instance.UnregisterConnection(OnPushPeerConnected);
            }
            catch { }
            m_ReloadReceiverRegistered = false;
        }

        /// <summary>Announce this session (protocol version, expected key fingerprint, session nonce)
        /// to the editor. Sent on receiver start, on every new connection, and on RequestHandshakeMsg.</summary>
        private void SendPushHandshake()
        {
            try
            {
                var fingerprint = string.IsNullOrEmpty(m_PushPublicKey) ? "" : PushEnvelope.Fingerprint(m_PushPublicKey);
                PlayerConnection.instance.Send(PipelineHotReloadConnect.HandshakeMsg,
                    PipelineHotReloadConnect.EncodeHandshake(PushEnvelope.ProtocolVersion, fingerprint, m_PushSessionNonce));
            }
            catch { /* no connection yet — the connect callback will re-announce */ }
        }

        // Both may fire on the connection thread; SendPushHandshake only touches immutable session state.
        private void OnRequestHandshakeMessage(MessageEventArgs args) => SendPushHandshake();
        private void OnPushPeerConnected(int playerId) => SendPushHandshake();

        private void OnApplyReloadMessage(MessageEventArgs args)
        {
            if (args?.data == null) return;
            lock (m_PendingReloads) m_PendingReloads.Add(args.data);
        }

        // Connection-thread handlers: HotReloadActivity is thread-safe, no marshalling needed.
        private void OnReloadPendingMessage(MessageEventArgs args) =>
            HotReloadActivity.ReportCompileStarted(PipelineHotReloadConnect.DecodeText(args?.data));

        private void OnReloadFailedMessage(MessageEventArgs args) =>
            HotReloadActivity.ReportFailed(PipelineHotReloadConnect.DecodeText(args?.data));

        private void DrainPendingReloads()
        {
            byte[][] batch;
            lock (m_PendingReloads)
            {
                if (m_PendingReloads.Count == 0) return;
                batch = m_PendingReloads.ToArray();
                m_PendingReloads.Clear();
            }

            // A player that received a hot-reload push must keep ticking while unfocused, otherwise
            // Update() (which drains + applies these pushes, and runs the reloaded code) pauses the
            // moment the window loses focus. Force it on once, on first receipt — reusing StartServer/
            // StopServer's own save/restore bookkeeping (rather than writing Application.runInBackground
            // directly) so OnDestroy's StopServer() still restores the user's original value even when
            // the HTTP server itself was never started (PlayerConnection needs no open port).
            if (!m_RunInBackgroundOverridden)
            {
                m_PreviousRunInBackground = Application.runInBackground;
                Application.runInBackground = true;
                m_RunInBackgroundOverridden = true;
                Debug.Log("Pipeline: forced Application.runInBackground = true (hot-reload push received)");
            }

            foreach (var data in batch)
            {
                bool ok = false;
                string summary;
                try
                {
                    // ---- authentication gate (before anything touches the bytes) ----
                    byte[] payload = null;
                    string rejectAck = null;   // generic wire ack; the device log carries the detail
#if UNITY_EDITOR
                    // Editor play mode: strip a signed envelope without verification (no key is baked
                    // in play mode), accept legacy raw payloads as-is. Not a security gate — pushes to
                    // the editor originate from the editor itself.
                    if (PushEnvelope.LooksLikeEnvelope(data))
                    {
                        if (!PushEnvelope.TryParse(data, out _, out _, out payload))
                            rejectAck = "malformed hot-reload push";
                    }
                    else
                    {
                        payload = data;
                    }
#else
                    if (m_PushVerifier == null)
                    {
                        LogPushReject("no signing key baked in this build — rebuild with the Pipeline build processor enabled");
                        rejectAck = "push rejected (authentication)";
                    }
                    else
                    {
                        var status = m_PushVerifier.Verify(data, out payload);
                        if (status != PushVerifyStatus.Ok)
                        {
                            // Q10 diagnostics: a legacy (pre-signing) editor sends a raw payload, which
                            // is the one failure the sender can actually fix without touching the build.
                            if (status == PushVerifyStatus.Malformed && !PushEnvelope.LooksLikeEnvelope(data))
                            {
                                LogPushReject("unsigned legacy push — the sending editor's com.unity.pipeline package predates push signing");
                                rejectAck = "push rejected: unsigned legacy push — update the com.unity.pipeline package in the editor";
                            }
                            else
                            {
                                LogPushReject($"{status} (session nonce/counter and signature details are in this device log only)");
                                rejectAck = "push rejected (authentication)";
                            }
                        }
                    }
#endif
                    if (rejectAck != null)
                    {
                        summary = rejectAck;
                    }
                    else if (!PipelineHotReloadConnect.TryDecode(payload, out var typeName, out var methodNames, out var il))
                    {
                        summary = "malformed hot-reload push";
                    }
                    else
                    {
                        var registered = Unity.Pipeline.Compilation.InterpreterHotReloadExecutor.Register(
                            il, typeName, methodNames, out var skipped, out var warnings);
                        HotReloadRegistry.InvokeReloadCallbacks(registered);
                        // Full warning text (unbound host members) goes to the device log; the ack
                        // summary the editor shows carries just the count so it stays one line.
                        foreach (var w in warnings)
                            Debug.LogWarning($"Pipeline: hot-reload {typeName} — {w}");
                        // Same for per-method skip reasons: the summary carries only the count, but a
                        // method that didn't apply (e.g. SetupUI referencing a member the running
                        // build stripped) is only diagnosable if the reason reaches the device log —
                        // "skipped 1" alone gives the user nothing to act on.
                        foreach (var s in skipped)
                            Debug.LogWarning($"Pipeline: hot-reload {typeName} — skipped {s}");
                        ok = registered.Count > 0;
                        summary = ok
                            ? $"applied {registered.Count} method(s) on {typeName}" +
                                (skipped.Count > 0 ? $"; skipped {skipped.Count}" : "") +
                                (warnings.Count > 0 ? $"; {warnings.Count} binding warning(s)" : "")
                            : $"no methods applied on {typeName}" +
                                (skipped.Count > 0 ? $": {string.Join("; ", skipped)}" : "");
                    }
                }
                catch (Exception ex)
                {
                    // The interpreter binds via reflection, so the real cause (e.g.
                    // ExecutionEngineException from a generic instantiation IL2CPP never
                    // AOT-compiled) arrives wrapped in TargetInvocationException, whose Message is
                    // just "Exception has been thrown by the target of an invocation." Unwrap for
                    // the ack the editor shows; keep the full chain + stack in the device log.
                    var root = ex;
                    while (root is System.Reflection.TargetInvocationException && root.InnerException != null)
                        root = root.InnerException;
                    Debug.LogException(ex);
                    summary = $"apply failed: {root.GetType().Name}: {root.Message}";
                }

                Debug.Log($"Pipeline: hot-reload push — {summary}");
                if (ok) HotReloadActivity.ReportApplied(summary);
                else HotReloadActivity.ReportFailed(summary);
                try
                {
                    PlayerConnection.instance.Send(
                        PipelineHotReloadConnect.ResultMsg, PipelineHotReloadConnect.EncodeResult(ok, summary));
                }
                catch { }
            }
        }

        // Reject-log rate limiting: full detail for the first few failures, then one summary line per
        // minute — an attacker probing the port must not be able to spam the device log into
        // uselessness, but "signature failures are happening" stays visible (it IS the intrusion signal).
        private const int RejectLogBurst = 10;
        private const float RejectWindowSeconds = 60f;
        private int m_RejectsInWindow;
        private float m_RejectWindowStart = float.NegativeInfinity;
        private int m_SuppressedRejects;
        private float m_LastRejectSummaryTime = float.NegativeInfinity;

        private void LogPushReject(string detail)
        {
            float now = Time.realtimeSinceStartup;
            if (now - m_RejectWindowStart > RejectWindowSeconds)
            {
                m_RejectWindowStart = now;
                m_RejectsInWindow = 0;
            }

            m_RejectsInWindow++;
            if (m_RejectsInWindow <= RejectLogBurst)
            {
                Debug.LogWarning($"Pipeline: hot-reload push rejected — {detail}");
                return;
            }

            m_SuppressedRejects++;
            if (now - m_LastRejectSummaryTime >= RejectWindowSeconds)
            {
                Debug.LogWarning($"Pipeline: {m_SuppressedRejects} further hot-reload push(es) rejected in the last minute " +
                    $"(possible probing on this network); most recent: {detail}");
                m_LastRejectSummaryTime = now;
                m_SuppressedRejects = 0;
            }
        }
#endif

        /// <summary>Manually start the Pipeline server.</summary>
        public void StartServer()
        {
            if (m_Server != null && m_Server.IsRunning)
            {
                Debug.LogWarning("Pipeline: Server already started or initialization attempted.");
                return;
            }

            try
            {
                System.Console.WriteLine("Pipeline: Starting runtime server from RuntimePipelineDriver...");

                if (m_Config == null || !m_Config.enableInBuilds)
                {
                    Debug.LogWarning("Pipeline: Runtime server disabled in configuration (enableInBuilds = false)");
                    return;
                }

                var validation = m_Config.Validate();
                if (!validation.IsValid)
                {
                    Debug.LogError($"Pipeline: Invalid runtime configuration: {validation.Message}");
                    return;
                }

                if (validation.Level == "warning")
                {
                    Debug.LogWarning($"Pipeline: Runtime configuration warning: {validation.Message}");
                }

                m_Server = new RuntimePipelineServer(m_Config);
                m_Server.Start(m_Config.port);

                if (m_Server.IsRunning)
                {
                    // Keeps Update -> Dispatcher.ProcessWorkQueue/WatchdogTick running while unfocused.
                    // Guarded on m_RunInBackgroundOverridden (rather than unconditional) because
                    // DrainPendingReloads can already have overridden it — a hot-reload push over
                    // PlayerConnection needs no HTTP server, so it can arrive and force this on before
                    // StartServer ever runs. Saving unconditionally here would clobber the real
                    // original value with the already-overridden "true", and StopServer would then
                    // restore to "true" instead of the user's setting.
                    if (!m_RunInBackgroundOverridden)
                    {
                        m_PreviousRunInBackground = Application.runInBackground;
                        Application.runInBackground = true;
                        m_RunInBackgroundOverridden = true;
                    }

                    // The hot-reload registry marshals main-thread overrides through a dispatcher
                    // when hot-reloaded code is invoked from a background thread at runtime. Inject
                    // this server's dispatcher here (a player has exactly one driver), so editor and
                    // per-test servers never clobber the global registry's dispatcher.
                    HotReloadRegistry.Dispatcher = m_Server.Dispatcher;

                    // Publish the build-baked allowed roots so reload commands can validate that
                    // incoming files are inside the project before compiling and injecting them.
                    HotReloadRegistry.AllowedReloadRoots = m_AllowedReloadRoots;

                    System.Console.WriteLine($"Pipeline: Runtime server started successfully on port {m_Server.Port}");
                }
                else
                {
                    Debug.LogError("Pipeline: Failed to start runtime server");
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"Pipeline: Runtime initialization failed: {ex.Message}");
                Debug.LogError($"Pipeline: Stack trace: {ex.StackTrace}");
            }
        }

        /// <summary>Manually stop the Pipeline server.</summary>
        public void StopServer()
        {
            try
            {
                if (m_Server != null && m_Server.IsRunning)
                {
                    System.Console.WriteLine("Pipeline: Stopping runtime server...");
                    // Drop the registry's reference to this server's (about to be shut down) dispatcher.
                    if (HotReloadRegistry.Dispatcher == m_Server.Dispatcher)
                        HotReloadRegistry.Dispatcher = null;
                    m_Server.Stop(); // Stop() shuts down the server's own dispatcher.
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"Pipeline: Runtime shutdown error: {ex.Message}");
            }

            // Independent of whether the listener was still alive above: if we overrode
            // runInBackground, restore it regardless, so a listener that already died on its own
            // (e.g. the watchdog never got to it) can't leave the setting stuck on.
            if (m_RunInBackgroundOverridden)
            {
                Application.runInBackground = m_PreviousRunInBackground;
                m_RunInBackgroundOverridden = false;
            }
        }

        /// <summary>Validate the current configuration.</summary>
        /// <returns>The validation result.</returns>
        public ValidationResult ValidateConfiguration()
        {
            return m_Config != null ? m_Config.Validate() : ValidationResult.Error("No RuntimePipelineConfig assigned");
        }
    }
}
