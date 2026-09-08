using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Unity.Pipeline.Models;
using Unity.Pipeline.Commands;
using Unity.Pipeline.Security;
using Unity.Pipeline.Threading;
using UnityEngine;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Unity.Pipeline
{
    /// <summary>
    /// HTTP server that enables CLI tools to execute commands in Unity Editor.
    /// Represents an Instance that can serve as a Pipeline Element for automation.
    /// </summary>
    public abstract class BasePipelineServer
    {
        private const long DefaultMaxRequestBodyBytes = 1 * 1024 * 1024;

        /// <summary>
        /// Maximum accepted request body size. Larger requests are rejected with 413, bounding the memory
        /// one remote request can force the server to buffer. Settable so a server whose clients
        /// legitimately post larger documents can raise it without every other server paying for it.
        /// </summary>
        public long MaxRequestBodyBytes { get; set; } = DefaultMaxRequestBodyBytes;

        /// <summary>
        /// Upper bound on how many bytes we read-and-discard from an over-limit request body before
        /// sending the 413. HttpListener resets the TCP connection if we close the response while the
        /// client is still uploading, and the client then sees a connection error instead of the 413.
        /// Draining lets the client finish so it can read the response. Kept comfortably above
        /// <see cref="MaxRequestBodyBytes"/> to cover clients only slightly over the cap, while still
        /// bounding the work a single abusive request can force (a larger body simply resets).
        /// </summary>
        private const long MaxDrainBytes = 8 * 1024 * 1024;

        /// <summary>
        /// Maximum accepted body size for /api/job/cancel (4 KiB). The body is just an
        /// <c>{"id": "..."}</c> object, so it needs far less headroom than <see cref="MaxRequestBodyBytes"/>.
        /// </summary>
        private const long MaxCancelBodyBytes = 4 * 1024;

        /// <summary>
        /// Dispatcher wait for a detached job's main-thread execution. Nothing is synchronously
        /// waiting on this thread (the job's HTTP response was already sent), so this only needs
        /// to be larger than any command's own timeout (e.g. eval's 24h cap) rather than bounded
        /// by an HTTP-facing default.
        /// </summary>
        private const int UnboundedJobDispatcherTimeoutMs = int.MaxValue;

        private bool m_IsRunning;
        private int m_Port;

        /// <summary>
        /// Serializes /api/exec command execution now that requests are processed
        /// concurrently: execs queue exactly as they did when the accept loop was serial,
        /// while read-only endpoints (/api/status, /api/progress, …) answer immediately.
        /// </summary>
        private readonly SemaphoreSlim m_ExecGate = new SemaphoreSlim(1, 1);
        private HttpListener m_HttpListener;
        private readonly Dispatcher m_Dispatcher = new Dispatcher();

        /// <summary>This server's own progress-reporting state (see CliProgress).</summary>
        internal CliProgressState Progress { get; } = new CliProgressState();

        /// <summary>This server's own modal-dialog state (see EditorDialogStateMirror, Task 7).</summary>
        internal DialogStateTracker Dialogs { get; } = new DialogStateTracker();

        /// <summary>This server's own detached-job registry (see PipelineCancellation, /api/job).</summary>
        internal PipelineJobRegistry JobRegistry { get; } = new PipelineJobRegistry();

        /// <summary>
        /// The server instance currently executing a command on this thread, if any. Set only
        /// around the actual command invocation (see <see cref="ExecuteCommandDirect"/>) so
        /// CliProgress.Report/PipelineCancellation — called from arbitrary command code with no
        /// reference to "which server is running me" — resolve to the right instance. Correct
        /// regardless of which physical thread ends up running the command, since the push/read/pop
        /// all happen within one synchronous call frame; a command that awaits and resumes on a
        /// different thread before calling either API would not see it (no command does today).
        /// </summary>
        [ThreadStatic] private static BasePipelineServer m_CurrentServer;
        internal static BasePipelineServer CurrentServer => m_CurrentServer;

        private bool m_WatchdogEnabled;
        private bool m_WatchdogArmed;
        private DateTime m_LastWatchdogCheck;

        /// <summary>
        /// This server's own main-thread dispatcher. Each server instance owns one (no global
        /// singleton), so tests that start their own server never affect any other server's
        /// dispatch. Pump it via ProcessWorkQueue from the main thread (auto-pumped on
        /// EditorApplication.update in the editor; pumped by RuntimePipelineDriver.Update in a
        /// player; pumped explicitly by tests).
        /// </summary>
        public Dispatcher Dispatcher => m_Dispatcher;

        /// <summary>
        /// Whether the server is running AND its HTTP listener is actually listening. More accurate
        /// than the internal running flag alone: returns false if the listener was stopped/disposed
        /// (e.g. by a domain reload) even when the flag is stale-true. Cheap and non-blocking.
        ///
        /// A self-HTTP probe was considered for "does it actually respond" but rejected: the server
        /// processes requests strictly one-at-a-time (HandleRequests awaits ProcessRequest), so a
        /// self-probe deadlocks if issued from within a handler and false-negatives whenever a
        /// request is in flight — unsafe for a watchdog. IsListening reliably catches the realistic
        /// failure (listener stopped by a domain reload), which is what we need.
        /// </summary>
        public bool IsRunning => m_IsRunning && m_HttpListener != null && m_HttpListener.IsListening;

        /// <summary>
        /// When enabled, the server periodically checks its own HTTP listener and re-opens it in
        /// place if it died without going through <see cref="Stop"/> (e.g. an unexpected listener
        /// fault). The server instance survives such failures — only the listener dies — so it can
        /// self-heal without an external restart. Default OFF: transient/test servers must not
        /// watchdog. The editor owner enables it for the live server.
        ///
        /// Setting this while the server is running arms/disarms the watchdog immediately; otherwise
        /// it takes effect on the next <see cref="Start"/>. Domain reloads are NOT handled here (the
        /// instance is torn down and recreated by [InitializeOnLoad]); the watchdog only revives a
        /// listener that died while the instance is still alive.
        /// </summary>
        public bool WatchdogEnabled
        {
            get => m_WatchdogEnabled;
            set
            {
                if (m_WatchdogEnabled == value)
                    return;
                m_WatchdogEnabled = value;
                if (!m_IsRunning)
                    return;
                if (value)
                    ArmWatchdog();
                else
                    DisarmWatchdog();
            }
        }

        /// <summary>
        /// How often the watchdog checks the listener (seconds). Default 5.
        /// </summary>
        public double WatchdogIntervalSeconds { get; set; } = 5.0;

        /// <summary>
        /// Port number the server is listening on. 0 if not running.
        /// Range: 7800-7899 for Editor, 7900-7999 for Runtime (avoids unity-tools port range 37800-37899).
        /// </summary>
        public int Port => m_Port;

        /// <summary>UTC time this server instance started listening.</summary>
        public abstract DateTime StartedAt { get; }

        /// <summary>
        /// Whether this server writes/deletes the shared instance descriptor (.unity-pipeline-port).
        /// Test servers override this to false so they never clobber the live server's descriptor —
        /// the test already knows its port, so no discovery file is needed.
        /// </summary>
        protected virtual bool WritesDescriptor => true;

        /// <summary>
        /// Whether this server advertises commands marked [CliCommand(RuntimeOnly = true)] in
        /// its /api/commands listing. Runtime servers list them; Editor servers hide them so a
        /// client connected to an Editor only sees the Editor command surface.
        /// </summary>
        protected virtual bool IncludeRuntimeOnlyCommands => true;

        /// <summary>
        /// Whether requests from a sandboxed browser frame (Origin: null) are accepted. Off by default;
        /// an ordinary web page is refused either way, and a sandboxed one still needs the bearer token,
        /// which lives in a file no browser can read.
        /// </summary>
        public bool AllowSandboxedBrowserClients
        {
            get => m_AllowSandboxedBrowserClients;
            set => m_AllowSandboxedBrowserClients = value;
        }

        // Read on the listener thread, so volatile: a value set on the main thread must be seen there.
        private volatile bool m_AllowSandboxedBrowserClients;

        /// <summary>The only Origin a sandboxed frame reports; a real page sends its own origin.</summary>
        private const string SandboxedOrigin = "null";

        /// <summary>Write the shared instance descriptor file so clients can discover this server.</summary>
        protected abstract void CreateInstanceDescriptor();
        /// <summary>Remove the shared instance descriptor file on shutdown.</summary>
        protected abstract void DeleteInstanceDescriptor();
        /// <summary>Refresh the descriptor's heartbeat timestamp so discovery can detect a live server.</summary>
        protected abstract void UpdateHeartBeat();
        /// <summary>Build the host-specific payload for <c>/api/status</c> (Editor vs. Player fields differ).</summary>
        /// <returns>The status payload.</returns>
        protected abstract object GetServerStatus();
        /// <summary>The bearer token clients must present to authenticate requests.</summary>
        /// <returns>The current token.</returns>
        protected abstract string GetToken();

        /// <summary>
        /// Optional wire features this server understands, advertised on /api/status and in the
        /// port descriptor.
        ///
        /// One array feeds both carriers so they cannot drift: a server that claims a capability
        /// it lacks is worse than one that claims nothing, because a client's fallback is keyed on
        /// the claim. Absence of the key entirely means the server is too old to have it, and
        /// clients must then assume no raw command-line support.
        /// </summary>
        internal static readonly string[] Capabilities = { "exec.argv", "exec.commandLine" };

        /// <summary>Hook invoked once the listener is accepting requests. No-op by default.</summary>
        protected virtual void ServerStarted()
        {

        }

        /// <summary>
        /// Busy probe for the /api/exec gate: return a non-null, human-readable reason when the
        /// host cannot service <paramref name="command"/> right now (e.g. the Editor is still
        /// importing/compiling while settling after a cold start), or null to let it run. A busy
        /// command is rejected with HTTP 503 and a structured, retryable envelope — before sync
        /// execution and before a detached job is created — instead of executing into a
        /// half-ready host and failing opaquely (AUTHAPI-35). Called on the request thread, so
        /// implementations must only read thread-safe state. Default: never busy.
        /// </summary>
        /// <param name="command">The command about to be dispatched.</param>
        /// <returns>A human-readable busy reason, or null to let the command run.</returns>
        protected virtual string GetBusyReason(CommandInfo command) => null;

        /// <summary>
        /// For a command otherwise blocked by the dialog gate, an optional cached-but-still-valid
        /// result to serve as a normal 200 success instead of the 503 (e.g. editor_status: nothing
        /// about compilation/play-mode/domain-reload can change while the main thread is stuck
        /// inside the dialog's native message loop, so a snapshot taken just before it blocked
        /// stays correct for the whole window — see EditorPipelineServer's override). Called on the
        /// request thread, so implementations must only read thread-safe state. Default: no
        /// fallback exists, so every MainThreadRequired command stays hard-gated.
        ///
        /// Internal, not protected: DialogInfo is internal, and a protected member can't expose a
        /// less-accessible type in its signature — a subclass outside this assembly (without
        /// InternalsVisibleTo) couldn't reference DialogInfo to override it. EditorPipelineServer
        /// overrides this from Unity.Pipeline.Editor, which already has that grant (same as
        /// Dialogs and BuildDialogPayload above).
        /// </summary>
        internal virtual object TryGetDialogBlockedFallback(CommandInfo command, DialogInfo blockingDialog) => null;

        /// <summary>Hook invoked once the listener has stopped accepting requests. No-op by default.</summary>
        protected virtual void ServerStopped()
        {

        }

        /// <summary>
        /// This server's current auth token. Exposed to the test assembly (via InternalsVisibleTo)
        /// so the test client can authenticate without re-deriving the token.
        /// </summary>
        internal string Token => GetToken();

        /// <summary>
        /// Whether <see cref="Port"/> was picked from this server's range rather than requested by
        /// the caller. Recorded at bind time, so it still describes the running listener after the
        /// settings asset is edited.
        /// </summary>
        internal bool PortAutoAssigned { get; private set; }

        /// <summary>
        /// Start the HTTP server on the specified port or auto-assign from range.
        /// </summary>
        /// <param name="port">Port to bind to, or 0 for auto-assignment from server-specific range</param>
        public void Start(int port = 0)
        {
            if (m_IsRunning)
                return;

            m_Port = port == 0 ? FindAvailablePort() : port;
            PortAutoAssigned = port == 0;

            try
            {
                // Initialize this server's own dispatcher (captures the main thread; Start() is
                // always called from the main thread).
                m_Dispatcher.Initialize();

                // Warm the token cache on the main thread before the listener accepts requests:
                // per-request auth runs on background threads and the Editor token is SessionState-
                // backed (main-thread-only). Re-runs after every domain reload via [InitializeOnLoad].
                SecurityTokenManager.GetOrCreateToken();

                // Mark running before opening the listener so HandleRequests' loop guard stays true
                // as soon as it starts on the threadpool.
                m_IsRunning = true;
                OpenListener();

                System.Console.WriteLine($"Start HTTP server: port:{m_Port}");

                if (WritesDescriptor)
                    CreateInstanceDescriptor();

                ArmWatchdog();

                ServerStarted();
            }
            catch (Exception)
            {
                m_IsRunning = false;
                m_HttpListener?.Stop();
                m_HttpListener = null;
                throw;
            }
        }

        /// <summary>
        /// Create the HTTP listener, bind it, and start the request-handling loop. Extracted from
        /// <see cref="Start"/> so the watchdog can re-open a dead listener in place without
        /// re-running the rest of startup (dispatcher init, descriptor). Caller sets m_IsRunning.
        /// </summary>
        private void OpenListener()
        {
            m_HttpListener = new HttpListener();
            StartHttpListenerWithFallback(m_HttpListener, m_Port);

            // Start request handling
            _ = Task.Run(HandleRequests);
        }

        private static void StartHttpListenerWithFallback(HttpListener listener, int port)
        {
            string[] candidates = new string[]
            {
                $"http://*:{port}/",
                $"http://127.0.0.1:{port}/",
                $"http://+:{port}/"
            };

            foreach (var prefix in candidates)
            {
                try
                {
                    listener.Prefixes.Clear();
                    listener.Prefixes.Add(prefix);
                    if (prefix.Contains("127.0.0.1"))
                    {
                        listener.Prefixes.Add($"http://localhost:{port}/");
                    }
                    listener.Start();
                    return;
                }
                catch
                {
                    try { listener.Stop(); } catch {}
                    listener.Prefixes.Clear();
                }
            }

            throw new InvalidOperationException($"Unable to bind HttpListener on port {port}");
        }

        /// <summary>
        /// Stop the HTTP server and clean up resources.
        /// </summary>
        public void Stop()
        {
            if (!m_IsRunning)
                return;

            m_IsRunning = false;

            DisarmWatchdog();
            System.Console.WriteLine("Pipeline Server stopped");

            try
            {
                if (WritesDescriptor)
                    DeleteInstanceDescriptor();

                m_HttpListener?.Stop();
                m_HttpListener?.Close();

                ServerStopped();
            }
            catch
            {
                // Ignore cleanup errors
            }
            finally
            {
                m_HttpListener = null;
                m_Dispatcher.Shutdown();
            }
            // Note: Keep port value for diagnostic purposes
        }

        /// <summary>
        /// Arm the watchdog (no-op if disabled or already armed). In the editor the tick rides
        /// EditorApplication.update; in a player it is driven by RuntimePipelineDriver.Update via
        /// <see cref="WatchdogTick"/>.
        /// </summary>
        private void ArmWatchdog()
        {
            if (!m_WatchdogEnabled || m_WatchdogArmed)
                return;

            m_WatchdogArmed = true;
            m_LastWatchdogCheck = DateTime.UtcNow;
#if UNITY_EDITOR
            UnityEditor.EditorApplication.update += WatchdogTick;
#endif
        }

        private void DisarmWatchdog()
        {
            if (!m_WatchdogArmed)
                return;

            m_WatchdogArmed = false;
#if UNITY_EDITOR
            UnityEditor.EditorApplication.update -= WatchdogTick;
#endif
        }

        /// <summary>
        /// Watchdog heartbeat: throttled to <see cref="WatchdogIntervalSeconds"/>, re-opens the HTTP
        /// listener in place if it died while the server is still meant to be running. Safe to call
        /// every frame. In the editor it is subscribed to EditorApplication.update by ArmWatchdog;
        /// in a player RuntimePipelineDriver.Update calls it.
        /// </summary>
        public void WatchdogTick()
        {
            if (!m_WatchdogArmed)
                return;

#if UNITY_EDITOR
            // Don't fight domain reloads / asset updates — the listener is expected to be torn down
            // then, and [InitializeOnLoad] recreates the server afterwards.
            if (UnityEditor.EditorApplication.isCompiling || UnityEditor.EditorApplication.isUpdating)
                return;
#endif

            var now = DateTime.UtcNow;
            if ((now - m_LastWatchdogCheck).TotalSeconds < WatchdogIntervalSeconds)
                return;
            m_LastWatchdogCheck = now;

            if (m_HttpListener != null && m_HttpListener.IsListening)
                return; // healthy

            try
            {
                // Dispose the dead listener so it releases the port binding before we re-bind a new
                // one on the same port (a stopped-but-not-closed listener can keep the port held).
                try { m_HttpListener?.Close(); } catch { }
                m_HttpListener = null;

                // The instance is alive and the watchdog is armed → the server is meant to be
                // listening. Re-open the listener in place.
                m_IsRunning = true;
                OpenListener();
                Debug.Log($"Pipeline watchdog re-opened HTTP listener on port {m_Port}");
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"Pipeline watchdog failed to re-open listener on port {m_Port}: {ex.Message}");
            }
        }

        /// <summary>
        /// Handle incoming HTTP requests with routing to appropriate endpoints.
        /// </summary>
        private async Task HandleRequests()
        {
            while (m_IsRunning && m_HttpListener != null && m_HttpListener.IsListening)
            {
                try
                {
                    var context = await m_HttpListener.GetContextAsync();
                    // Process detached so the accept loop keeps serving while a long command
                    // holds its /api/exec connection open. Without this, ANY in-flight exec
                    // blocked every other request — including /api/status probes and the
                    // /api/progress polls that exist precisely for that situation (CLI-488;
                    // the "editor is unresponsive until the command finishes" report in
                    // CLI-335). Command execution itself stays strictly serialized via
                    // m_ExecGate below, so exec ordering semantics are unchanged.
                    _ = ProcessRequestDetached(context);
                }
                catch (ObjectDisposedException)
                {
                    // Listener was stopped, exit gracefully
                    break;
                }
                catch (HttpListenerException)
                {
                    // Listener was stopped, exit gracefully
                    break;
                }
                catch (Exception ex)
                {
                    Debug.LogError(ex);
                }
            }
            m_IsRunning = false;
        }

        /// <summary>
        /// Run ProcessRequest without the accept loop awaiting it; a per-request failure is
        /// logged and must never tear down the listener loop.
        /// </summary>
        private async Task ProcessRequestDetached(HttpListenerContext context)
        {
            try
            {
                await ProcessRequest(context);
            }
            catch (Exception ex)
            {
                Debug.LogError(ex);
            }
        }

        /// <summary>
        /// Process individual HTTP request and route to appropriate handler.
        /// </summary>
        /// <param name="context">The incoming HTTP request/response context.</param>
        /// <returns>A task that completes once the response has been written.</returns>
        protected virtual async Task ProcessRequest(HttpListenerContext context)
        {
            var request = context.Request;
            var response = context.Response;

            try
            {
                // Confine access to the local machine. The listener binds a wildcard host (see
                // AddLoopbackPrefixes) so it accepts any Host header, which also means it accepts
                // connections on non-loopback interfaces — reject anything that isn't loopback
                // before doing any other work. Bearer-token auth is still enforced below.
                var remoteAddress = request.RemoteEndPoint?.Address;
                if (remoteAddress == null || !IPAddress.IsLoopback(remoteAddress))
                {
                    await SendStatusResponse(response, 403, "Forbidden", "Only loopback connections are allowed");
                    return;
                }

                // Reject browser-originated requests. Legitimate non-browser clients (CLI, CI) never
                // send an Origin header; emitting no CORS headers and refusing any request that
                // carries one prevents a website in the developer's browser from reaching this
                // local server (and short-circuits CORS preflights). A server that opts into
                // AllowSandboxedBrowserClients additionally admits Origin: null.
                var origin = request.Headers["Origin"];
                if (!string.IsNullOrEmpty(origin))
                {
                    if (!AllowSandboxedBrowserClients || origin != SandboxedOrigin)
                    {
                        await SendStatusResponse(response, 403, "Forbidden", "Cross-origin requests are not allowed");
                        return;
                    }

                    // "*" rather than echoing "null": the gate above already decides who is served, no
                    // credentials are involved, and it is better supported for an opaque origin.
                    response.AddHeader("Access-Control-Allow-Origin", "*");
                    response.AddHeader("Vary", "Origin");
                }

                // Preflight is answered before auth by necessity: the browser sends OPTIONS with no
                // Authorization header, so requiring the token here would fail every real request.
                if (request.HttpMethod == "OPTIONS")
                {
                    if (string.IsNullOrEmpty(origin))
                    {
                        await SendStatusResponse(response, 405, "Method Not Allowed", "OPTIONS is only used for CORS preflight");
                        return;
                    }

                    response.AddHeader("Access-Control-Allow-Methods", "GET, POST, OPTIONS");
                    response.AddHeader("Access-Control-Allow-Headers", "Authorization, Content-Type");
                    response.AddHeader("Access-Control-Max-Age", "600");
                    // Chrome's Private Network Access: a page on the public internet reaching a loopback
                    // address must be granted it explicitly, or the preflight fails.
                    if (!string.IsNullOrEmpty(request.Headers["Access-Control-Request-Private-Network"]))
                        response.AddHeader("Access-Control-Allow-Private-Network", "true");
                    response.StatusCode = 204;
                    response.ContentLength64 = 0;
                    response.OutputStream.Close();
                    return;
                }

                // Authenticate every request with the bearer token before routing.
                if (!IsAuthorized(request))
                {
                    await SendStatusResponse(response, 401, "Unauthorized", "Missing or invalid authentication token");
                    return;
                }

                // Route to appropriate endpoint
                var path = request.Url.AbsolutePath.ToLowerInvariant();

                switch (path)
                {
                    case "/api/status":
                        await HandleStatusRequest(response);
                        break;
                    case "/api/editor_status":
                        await HandleEditorStatusRequest(response);
                        break;
                    case "/api/commands":
                        await HandleCommandsRequest(request, response);
                        break;
                    case "/api/exec":
                        if (request.HttpMethod == "POST")
                            await HandleExecRequest(request, response);
                        else
                            await HandleMethodNotAllowed(response, "POST");
                        break;
                    case "/api/test-status":
                        await HandleTestStatusRequest(response);
                        break;
                    case "/api/progress":
                        await HandleProgressRequest(request, response);
                        break;
                    case "/api/dialog":
                        await HandleDialogRequest(response);
                        break;
                    case "/api/job":
                        if (request.HttpMethod == "GET")
                            await HandleJobStatusRequest(request, response);
                        else
                            await HandleMethodNotAllowed(response, "GET");
                        break;
                    case "/api/job/cancel":
                        if (request.HttpMethod == "POST")
                            await HandleJobCancelRequest(request, response);
                        else
                            await HandleMethodNotAllowed(response, "POST");
                        break;
                    default:
                        await HandleNotFound(response);
                        break;
                }
            }
            catch (Exception e)
            {
                // A silent 500 leaves the client with nothing to go on; name the request and the fault.
                Debug.LogError($"Pipeline: unhandled error serving {request.HttpMethod} "
                    + $"{request.Url?.AbsolutePath}: {e}");

                // Ensure response is always closed
                try
                {
                    if (response.OutputStream.CanWrite)
                    {
                        response.StatusCode = 500;
                        response.OutputStream.Close();
                    }
                }
                catch { }
            }
        }

        /// <summary>
        /// Validate the request's bearer token against this server's token.
        /// </summary>
        private bool IsAuthorized(HttpListenerRequest request)
        {
            var header = request.Headers["Authorization"];
            if (string.IsNullOrEmpty(header))
                return false;

            const string prefix = "Bearer ";
            if (!header.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return false;

            var token = header.Substring(prefix.Length).Trim();
            return SecurityTokenManager.ConstantTimeEquals(token, GetToken());
        }

        /// <summary>
        /// Send a structured JSON error response with an explicit HTTP status code.
        /// </summary>
        private async Task SendStatusResponse(HttpListenerResponse response, int statusCode, string error, string details)
        {
            try
            {
                response.StatusCode = statusCode;
                response.ContentType = "application/json";
                var json = JsonConvert.SerializeObject(BaseResponse.Failure(error, details), Formatting.Indented);
                var buffer = Encoding.UTF8.GetBytes(json);
                response.ContentLength64 = buffer.Length;

                await response.OutputStream.WriteAsync(buffer, 0, buffer.Length);
                response.OutputStream.Close();
            }
            catch
            {
                try { response.OutputStream.Close(); } catch { }
            }
        }

        /// <summary>
        /// Parse the <c>omit_nulls</c> query parameter (snake_case like the other query
        /// parameters, e.g. group_by) — the GET-endpoint equivalent of exec's <c>omitNulls</c>
        /// JSON body flag, for /api/job, /api/job/cancel, and /api/progress. Accepted values:
        /// "true"/"false", case-insensitive; absent means false. Any other value (e.g.
        /// <c>omit_nulls=1</c>) is NOT silently coerced to false: it yields false plus a
        /// <paramref name="warning"/> the caller surfaces in the response's "warnings" array, so
        /// an agent learns the accepted spelling instead of guessing why nulls are still present.
        /// (Exec body requests don't need this: JSON deserialization coerces their boolean.)
        /// </summary>
        private static bool ParseOmitNulls(HttpListenerRequest request, out string warning)
        {
            warning = null;
            var raw = request.QueryString["omit_nulls"];
            if (raw == null)
            {
                // A bare "?omit_nulls" (no '=') is not the same as absent: .NET files valueless
                // query tokens under the null key, so look there to warn instead of silently
                // treating an intended opt-in as false.
                var valueless = request.QueryString.GetValues(null);
                if (valueless != null && Array.IndexOf(valueless, "omit_nulls") >= 0)
                    warning = "omit_nulls given without a value ignored; use omit_nulls=true";
                return false;
            }
            if (string.Equals(raw, "true", StringComparison.OrdinalIgnoreCase))
                return true;
            if (string.Equals(raw, "false", StringComparison.OrdinalIgnoreCase))
                return false;

            warning = $"omit_nulls='{raw}' ignored; expected 'true' or 'false'";
            return false;
        }

        /// <summary>
        /// Serialize <paramref name="payload"/> and send it as the JSON body. Null fields are
        /// INCLUDED by default — on /api/job and /api/progress every field is payload (there is
        /// no envelope/payload split like /api/exec's), and an absent key is indistinguishable
        /// from a nonexistent or misspelled one. Callers pass <paramref name="omitNulls"/> from
        /// the endpoint's <c>omit_nulls</c> query parameter (see <see cref="ParseOmitNulls"/>)
        /// to opt out (AUTHAPI-21).
        /// Shares <see cref="SendStatusResponse"/>'s write guard: a failure while writing
        /// (e.g. the client disconnected mid-response) is swallowed here instead of propagating
        /// into the caller's own catch block, which would otherwise attempt a second response on
        /// the now-dead connection.
        /// </summary>
        private async Task SendJsonResponse(HttpListenerResponse response, int statusCode, object payload, bool omitNulls = false)
        {
            try
            {
                var json = JsonConvert.SerializeObject(payload,
                    new JsonSerializerSettings
                    {
                        NullValueHandling = omitNulls ? NullValueHandling.Ignore : NullValueHandling.Include
                    });

                response.StatusCode = statusCode;
                response.ContentType = "application/json";
                var buffer = Encoding.UTF8.GetBytes(json);
                response.ContentLength64 = buffer.Length;

                await response.OutputStream.WriteAsync(buffer, 0, buffer.Length);
                response.OutputStream.Close();
            }
            catch
            {
                try { response.OutputStream.Close(); } catch { }
            }
        }

        /// <summary>
        /// Handle /api/status endpoint - returns basic server health information.
        /// No Editor API access required, always fast response.
        /// </summary>
        private async Task HandleStatusRequest(HttpListenerResponse response)
        {
            try
            {
                var basicStatus = GetServerStatus();
                var json = JsonConvert.SerializeObject(basicStatus, Formatting.Indented);

                response.StatusCode = 200;
                response.ContentType = "application/json";

                var buffer = Encoding.UTF8.GetBytes(json);
                response.ContentLength64 = buffer.Length;

                await response.OutputStream.WriteAsync(buffer, 0, buffer.Length);
                response.OutputStream.Close();
            }
            catch (Exception ex)
            {
                Debug.LogError($"HandleStatusRequest failed: {ex.Message}");
                await SendErrorResponse(response, "Status Error", $"Failed to get status: {ex.Message}");
            }
        }

        /// <summary>
        /// Handle /api/editor_status endpoint - returns detailed Editor state via command execution.
        /// Executes the "editor_status" command to get rich Editor information.
        /// </summary>
        private async Task HandleEditorStatusRequest(HttpListenerResponse response)
        {
            try
            {
                // TODO: should this be implemented as a command??

                // Queue behind the same gate as /api/exec so this doesn't run concurrently with
                // an in-flight exec (or another editor_status/test_status call): it dispatches
                // onto the main thread just like exec does, so left ungated it would race rather
                // than queue the way it did under the old serial accept loop.
                object result;
                await m_ExecGate.WaitAsync();
                try
                {
                    result = await ExecuteCommandByName("editor_status", new JObject());
                }
                finally
                {
                    m_ExecGate.Release();
                }

                // The editor_status command returns a StatusResponse directly
                var editorStatus = result as StatusResponse;
                if (editorStatus != null)
                {
                    // Update with server-specific information
                    UpdateStatusWithServerInfo(editorStatus);

                    var json = JsonConvert.SerializeObject(editorStatus, Formatting.Indented);

                    response.StatusCode = 200;
                    response.ContentType = "application/json";

                    var buffer = Encoding.UTF8.GetBytes(json);
                    response.ContentLength64 = buffer.Length;

                    await response.OutputStream.WriteAsync(buffer, 0, buffer.Length);
                    response.OutputStream.Close();
                }
                else
                {
                    Debug.LogError($"HandleEditorStatusRequest: editor_status command returned wrong type: {result?.GetType().FullName ?? "null"}");
                    await SendErrorResponse(response, "Editor Status Error", "editor_status command did not return valid StatusResponse");
                }
            }
            catch (Exception ex)
            {
                var errorMessage = !string.IsNullOrEmpty(ex.Message) ? ex.Message : "[EMPTY EXCEPTION MESSAGE]";
                Debug.LogError($"HandleEditorStatusRequest failed:");
                Debug.LogError($"  Exception Type: {ex.GetType().FullName}");
                Debug.LogError($"  Message: '{errorMessage}'");
                Debug.LogError($"  Full Exception: {ex}");

                await SendErrorResponse(response, "Editor Status Error", $"Failed to get editor status: {errorMessage}");
            }
        }

        /// <summary>
        /// Update StatusResponse with server-specific information.
        /// Used by /api/editor_status to add server metadata to command result.
        /// </summary>
        private void UpdateStatusWithServerInfo(StatusResponse editorStatus)
        {
            UpdateHeartBeat();
            // Ensure heartbeat is current
            editorStatus.LastHeartbeat = DateTime.UtcNow;
        }

        /// <summary>
        /// Handle /api/commands endpoint - returns the available CLI commands.
        ///
        /// Optional query parameters (filters combine with AND):
        ///  - detail: 'full' (default) includes parameters and the generated JSON schema;
        ///    'compact' returns a lightweight index (name, description, tags, package) so a
        ///    client can cheaply browse, then fetch full detail only for the commands it
        ///    intends to invoke.
        ///  - query: case-insensitive substring match on name, description, or any tag.
        ///  - tag: scope to a tag subtree via segment-aware prefix match ('assets' matches
        ///    'assets' and 'assets/import' but not 'assetsx').
        ///  - group_by: 'flat' (default) returns a 'commands' array; 'package' and 'tag'
        ///    return a 'groups' array instead ('tag' is a nested tree mirroring tag/subtag;
        ///    untagged commands land in a node with an empty tag).
        ///  - sort: 'name' (default) or 'package' (originating package, name as tiebreak);
        ///    order: 'asc' (default) or 'desc'. Sorting applies to the flat list before
        ///    pagination and grouping.
        ///  - offset / limit: paginate the filtered, sorted flat list (applied before
        ///    grouping, so pages are deterministic); 'total' reports the match count before
        ///    pagination while 'count' is the number of commands actually returned.
        /// </summary>
        private async Task HandleCommandsRequest(HttpListenerRequest request, HttpListenerResponse response)
        {
            try
            {
                var queryString = request.QueryString;

                var detail = queryString["detail"] ?? "full";
                bool fullDetail;
                if (string.Equals(detail, "full", StringComparison.OrdinalIgnoreCase))
                    fullDetail = true;
                else if (string.Equals(detail, "compact", StringComparison.OrdinalIgnoreCase))
                    fullDetail = false;
                else
                {
                    await SendStatusResponse(response, 400, "Invalid Request",
                        $"Unknown detail value '{detail}'. Accepted values: compact, full");
                    return;
                }

                var groupBy = queryString["group_by"] ?? "flat";
                var groupByPackage = string.Equals(groupBy, "package", StringComparison.OrdinalIgnoreCase);
                var groupByTag = string.Equals(groupBy, "tag", StringComparison.OrdinalIgnoreCase);
                if (!groupByPackage && !groupByTag && !string.Equals(groupBy, "flat", StringComparison.OrdinalIgnoreCase))
                {
                    await SendStatusResponse(response, 400, "Invalid Request",
                        $"Unknown group_by value '{groupBy}'. Accepted values: flat, package, tag");
                    return;
                }

                var sort = queryString["sort"] ?? "name";
                var sortByPackage = string.Equals(sort, "package", StringComparison.OrdinalIgnoreCase);
                if (!sortByPackage && !string.Equals(sort, "name", StringComparison.OrdinalIgnoreCase))
                {
                    await SendStatusResponse(response, 400, "Invalid Request",
                        $"Unknown sort value '{sort}'. Accepted values: name, package");
                    return;
                }

                var order = queryString["order"] ?? "asc";
                var descending = string.Equals(order, "desc", StringComparison.OrdinalIgnoreCase);
                if (!descending && !string.Equals(order, "asc", StringComparison.OrdinalIgnoreCase))
                {
                    await SendStatusResponse(response, 400, "Invalid Request",
                        $"Unknown order value '{order}'. Accepted values: asc, desc");
                    return;
                }

                var offset = 0;
                if (queryString["offset"] != null
                    && (!int.TryParse(queryString["offset"], out offset) || offset < 0))
                {
                    await SendStatusResponse(response, 400, "Invalid Request",
                        $"Invalid offset value '{queryString["offset"]}'. Expected a non-negative integer");
                    return;
                }

                int? limit = null;
                if (queryString["limit"] != null)
                {
                    if (!int.TryParse(queryString["limit"], out var parsedLimit) || parsedLimit < 0)
                    {
                        await SendStatusResponse(response, 400, "Invalid Request",
                            $"Invalid limit value '{queryString["limit"]}'. Expected a non-negative integer");
                        return;
                    }
                    limit = parsedLimit;
                }

                var query = queryString["query"];
                var tag = queryString["tag"];

                var filtered = CommandRegistry.DiscoverCommands()
                    .Where(c => IncludeRuntimeOnlyCommands || !c.RuntimeOnly)
                    .Where(c => MatchesQuery(c, query) && MatchesTagSubtree(c, tag));
                var matching = SortCommands(filtered, sortByPackage, descending).ToList();

                // Paginate the flat, sorted list before any grouping so pages are deterministic.
                IEnumerable<CommandInfo> window = matching.Skip(offset);
                if (limit.HasValue)
                    window = window.Take(limit.Value);
                var page = window.ToList();

                Func<CommandInfo, object> project = c =>
                    fullDetail ? BuildFullCommandResponse(c) : BuildCompactCommandResponse(c);

                var responseData = new Dictionary<string, object>();
                if (groupByPackage)
                    responseData["groups"] = BuildPackageGroups(page, project);
                else if (groupByTag)
                    responseData["groups"] = BuildTagTree(page, project);
                else
                    responseData["commands"] = page.Select(project).ToList();
                responseData["count"] = page.Count;
                responseData["total"] = matching.Count;
                responseData["offset"] = offset;
                responseData["limit"] = limit;
                responseData["server"] = new
                {
                    version = "0.0.1", // TODO: Get from package.json
                    port = m_Port,
                    startTime = StartedAt
                };

                var json = JsonConvert.SerializeObject(responseData, Formatting.Indented);

                response.StatusCode = 200;
                response.ContentType = "application/json";

                var buffer = Encoding.UTF8.GetBytes(json);
                response.ContentLength64 = buffer.Length;

                await response.OutputStream.WriteAsync(buffer, 0, buffer.Length);
                response.OutputStream.Close();
            }
            catch (Exception ex)
            {
                Debug.LogError($"Failed to handle /api/commands request: {ex.Message}");

                // Return error response
                response.StatusCode = 500;
                response.ContentType = "application/json";

                var errorResponse = new { error = "Internal server error", message = ex.Message };
                var errorJson = JsonConvert.SerializeObject(errorResponse);
                var errorBuffer = Encoding.UTF8.GetBytes(errorJson);
                response.ContentLength64 = errorBuffer.Length;

                await response.OutputStream.WriteAsync(errorBuffer, 0, errorBuffer.Length);
                response.OutputStream.Close();
            }
        }

        /// <summary>
        /// Build the lightweight index entry for a single command (detail=compact).
        /// </summary>
        private static object BuildCompactCommandResponse(CommandInfo command)
        {
            return new
            {
                name = command.Name,
                description = command.Description,
                tags = command.Tags,
                package = command.Package
            };
        }

        /// <summary>
        /// Build the complete JSON response object for a single command (detail=full).
        /// </summary>
        private object BuildFullCommandResponse(CommandInfo command)
        {
            var entry = BuildCommandEntry(command);
            entry["schema"] = JsonSchemaGenerator.GenerateCommandSchema(command);
            return entry;
        }

        /// <summary>
        /// One command's catalog entry: everything /api/commands serves for it except the
        /// generated JSON <c>schema</c>.
        ///
        /// Shared by the catalog and by an argument-error envelope, so a client's entry renderer
        /// consumes both unchanged and the two cannot drift. The error path takes this directly
        /// rather than building the full response and dropping the schema, because generating and
        /// serializing a schema nobody reads is pure waste on the interactive retry path.
        /// </summary>
        private JObject BuildCommandEntry(CommandInfo command)
        {
            return JObject.FromObject(new
            {
                name = command.Name,
                description = command.Description,
                tags = command.Tags,
                package = command.Package,
                mainThreadRequired = command.MainThreadRequired,
                runtimeOnly = command.RuntimeOnly,
                parameters = command.Parameters.Select(p => new
                {
                    name = p.Name,
                    description = p.Description,
                    type = p.ParameterType.Name,
                    required = p.Required,
                    defaultValue = p.DefaultValue
                }).ToList()
            });
        }

        /// <summary>
        /// Attaches the bound-parameter echo for raw-form requests, and only for those, leaving
        /// the structured path's envelope unchanged.
        ///
        /// The value is exactly what the binder stored, so the echo cannot disagree with what the
        /// command executes with. A client that does not bind locally has no other way to report
        /// what the command received.
        /// </summary>
        private static CommandExecutionResponse WithBoundParameters(
            CommandExecutionResponse response, CommandExecutionRequest request)
        {
            if (request.IsRawForm)
                response.BoundParameters = request.Parameters ?? new JObject();
            return response;
        }

        /// <summary>
        /// Renders argument problems as English prose for <c>errorDetails</c>.
        ///
        /// This is the fallback, not the primary channel: a client that understands
        /// <c>argProblems</c> renders and localizes them itself. This text serves curl, MCP, and
        /// any client that meets a <c>kind</c> it does not recognize, so it must stay useful on
        /// its own.
        /// </summary>
        private static string DescribeArgProblems(CommandInfo command, List<ArgProblem> problems)
        {
            if (problems == null || problems.Count == 0)
                return "Invalid arguments";

            var parts = new List<string>(problems.Count);
            foreach (var problem in problems)
            {
                switch (problem.Kind)
                {
                    case ArgProblemKind.EmptyName:
                        parts.Add($"'{problem.Token}' is not a valid flag: the name is empty.");
                        break;
                    case ArgProblemKind.EmptyValue:
                        parts.Add($"--{problem.Name} needs a value.");
                        break;
                    case ArgProblemKind.SingleDash:
                        parts.Add($"'{problem.Token}' is not a valid flag: single-dash flags are not supported. Use a double dash, or pass it after -- to send it as a value.");
                        break;
                    case ArgProblemKind.Duplicate:
                        parts.Add($"--{problem.Name} was given more than once.");
                        break;
                    case ArgProblemKind.UnknownName:
                        parts.Add(problem.Suggestion != null
                            ? $"{command.Name} has no parameter --{problem.Name}. Did you mean --{problem.Suggestion}?"
                            : $"{command.Name} has no parameter --{problem.Name}. Valid parameters: {DescribeParameterNames(command)}.");
                        break;
                    case ArgProblemKind.BareAssignment:
                        parts.Add($"'{problem.Token}' looks like a flag written without dashes. Use --name value instead.");
                        break;
                    case ArgProblemKind.ExcessPositional:
                        parts.Add($"{command.Name} takes {problem.Capacity} argument(s) but {problem.Given} were given (starting at '{problem.Token}').");
                        break;
                    case ArgProblemKind.PositionalConflict:
                        parts.Add($"--{problem.Name} is already set by an explicit flag, so there is no slot for '{problem.Token}'.");
                        break;
                    case ArgProblemKind.TypeMismatch:
                        parts.Add($"--{problem.Name} expects {problem.ExpectedType}, but got '{problem.Token}'.{DescribeValidValues(command, problem.Name)}");
                        break;
                    default:
                        parts.Add($"{problem.Kind}: {problem.Token ?? problem.Name}");
                        break;
                }
            }

            return string.Join(" ", parts);
        }

        /// <summary>Every declared parameter of the command, flag-spelled, or "(none)".</summary>
        private static string DescribeParameterNames(CommandInfo command)
        {
            if (command.Parameters.Count == 0)
                return "(none)";
            return string.Join(", ", command.Parameters.Select(p => "--" + p.Name));
        }

        /// <summary>
        /// For an enum parameter, the legal names, following the same enumerate-the-valid-set
        /// convention used for unknown command names and query values. Empty for every other type.
        /// </summary>
        private static string DescribeValidValues(CommandInfo command, string parameterName)
        {
            foreach (var parameter in command.Parameters)
            {
                if (!string.Equals(parameter.Name, parameterName, StringComparison.OrdinalIgnoreCase))
                    continue;
                var type = Nullable.GetUnderlyingType(parameter.ParameterType) ?? parameter.ParameterType;
                if (type.IsEnum)
                    return " Valid values: " + string.Join(", ", Enum.GetNames(type)) + ".";
                break;
            }
            return string.Empty;
        }

        /// <summary>
        /// Order the filtered command list: by name, or by originating package with name as
        /// tiebreak. Both keys follow the requested direction.
        /// </summary>
        private static IEnumerable<CommandInfo> SortCommands(IEnumerable<CommandInfo> commands, bool byPackage, bool descending)
        {
            if (byPackage)
            {
                var byPkg = descending
                    ? commands.OrderByDescending(c => c.Package ?? string.Empty, StringComparer.Ordinal)
                    : commands.OrderBy(c => c.Package ?? string.Empty, StringComparer.Ordinal);
                return descending
                    ? byPkg.ThenByDescending(c => c.Name, StringComparer.Ordinal)
                    : byPkg.ThenBy(c => c.Name, StringComparer.Ordinal);
            }
            return descending
                ? commands.OrderByDescending(c => c.Name, StringComparer.Ordinal)
                : commands.OrderBy(c => c.Name, StringComparer.Ordinal);
        }

        /// <summary>
        /// Case-insensitive substring match on the command's name, description, or any tag.
        /// A null/empty query matches everything.
        /// </summary>
        private static bool MatchesQuery(CommandInfo command, string query)
        {
            if (string.IsNullOrEmpty(query))
                return true;
            return command.Name.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0
                || command.Description.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0
                || command.Tags.Any(t => t.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        /// <summary>
        /// Segment-aware tag subtree match: 'assets' matches tags 'assets' and 'assets/import'
        /// but not 'assetsx'. A null/empty tag matches everything.
        /// </summary>
        private static bool MatchesTagSubtree(CommandInfo command, string tag)
        {
            if (string.IsNullOrEmpty(tag))
                return true;
            return command.Tags.Any(t => t.Equals(tag, StringComparison.OrdinalIgnoreCase)
                || t.StartsWith(tag + "/", StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Group a page of commands by originating package, sorted by package name.
        /// </summary>
        private static List<object> BuildPackageGroups(List<CommandInfo> commands, Func<CommandInfo, object> project)
        {
            return commands
                .GroupBy(c => c.Package ?? string.Empty)
                .OrderBy(g => g.Key, StringComparer.Ordinal)
                .Select(g => (object)new
                {
                    package = g.Key,
                    count = g.Count(),
                    commands = g.Select(project).ToList()
                })
                .ToList();
        }

        /// <summary>
        /// Node of the group_by=tag tree. A command appears in the node of each tag it carries;
        /// untagged commands land in a top-level node with an empty tag.
        /// </summary>
        private class TagTreeNode
        {
            public readonly List<CommandInfo> Commands = new List<CommandInfo>();
            public readonly SortedDictionary<string, TagTreeNode> Children =
                new SortedDictionary<string, TagTreeNode>(StringComparer.Ordinal);
        }

        /// <summary>
        /// Build the nested group_by=tag tree mirroring the tag/subtag hierarchy. Each node's
        /// 'count' covers its whole subtree (a command tagged with several tags under the same
        /// node counts once per tag entry).
        /// </summary>
        private static List<object> BuildTagTree(List<CommandInfo> commands, Func<CommandInfo, object> project)
        {
            var root = new TagTreeNode();
            foreach (var command in commands)
            {
                if (command.Tags.Count == 0)
                {
                    GetOrAddChild(root, string.Empty).Commands.Add(command);
                    continue;
                }
                foreach (var tag in command.Tags)
                {
                    var node = root;
                    foreach (var segment in tag.Split('/'))
                        node = GetOrAddChild(node, segment);
                    node.Commands.Add(command);
                }
            }
            return root.Children
                .Select(kv => ToTagGroup(kv.Key, kv.Value, project))
                .ToList();
        }

        private static TagTreeNode GetOrAddChild(TagTreeNode node, string key)
        {
            if (!node.Children.TryGetValue(key, out var child))
            {
                child = new TagTreeNode();
                node.Children[key] = child;
            }
            return child;
        }

        private static object ToTagGroup(string path, TagTreeNode node, Func<CommandInfo, object> project)
        {
            return new
            {
                tag = path,
                count = SubtreeCommandCount(node),
                commands = node.Commands.Select(project).ToList(),
                children = node.Children
                    .Select(kv => ToTagGroup(path.Length == 0 ? kv.Key : path + "/" + kv.Key, kv.Value, project))
                    .ToList()
            };
        }

        private static int SubtreeCommandCount(TagTreeNode node)
        {
            return node.Commands.Count + node.Children.Values.Sum(SubtreeCommandCount);
        }

        /// <summary>
        /// Handle 404 Not Found responses.
        /// </summary>
        private async Task HandleNotFound(HttpListenerResponse response)
        {
            response.StatusCode = 404;
            response.ContentType = "text/plain";

            var responseText = "Not Found";
            var buffer = Encoding.UTF8.GetBytes(responseText);
            response.ContentLength64 = buffer.Length;

            await response.OutputStream.WriteAsync(buffer, 0, buffer.Length);
            response.OutputStream.Close();
        }

        /// <summary>
        /// Handle 405 Method Not Allowed responses.
        /// </summary>
        private async Task HandleMethodNotAllowed(HttpListenerResponse response, string allowedMethod)
        {
            await SendErrorResponse(response, "Method Not Allowed",
                $"This endpoint only supports {allowedMethod} requests", statusCode: 405);
        }

        /// <summary>
        /// Handle /api/test-status endpoint - returns status of async test execution.
        /// </summary>
        private async Task HandleTestStatusRequest(HttpListenerResponse response)
        {
            try
            {
                // Queue behind the same gate as /api/exec/editor_status — see the comment in
                // HandleEditorStatusRequest. (In practice this rarely blocks: the async-test-status
                // workflow polls this only once run_tests --async_tests has already returned and
                // released the gate.)
                object result;
                await m_ExecGate.WaitAsync();
                try
                {
                    result = await ExecuteCommandByName("test_status", new JObject());
                }
                finally
                {
                    m_ExecGate.Release();
                }

                string jsonResponse;
                if (result is string statusString)
                {
                    // test_status command returns JSON string directly
                    jsonResponse = statusString;
                }
                else
                {
                    // Fallback - serialize whatever was returned
                    jsonResponse = JsonConvert.SerializeObject(result ?? new { status = "no_tests", message = "No test run in progress" });
                }

                response.StatusCode = 200;
                response.ContentType = "application/json";

                var buffer = Encoding.UTF8.GetBytes(jsonResponse);
                response.ContentLength64 = buffer.Length;

                await response.OutputStream.WriteAsync(buffer, 0, buffer.Length);
                response.OutputStream.Close();
            }
            catch (Exception ex)
            {
                Debug.LogError($"HandleTestStatusRequest failed: {ex.Message}");
                await SendErrorResponse(response, "Test Status Error", $"Failed to get test status: {ex.Message}");
            }
        }

        /// <summary>
        /// Runs <paramref name="body"/> under the exec gate: tracks the in-flight count for
        /// /api/progress ("active" while any exec runs) via CliProgress's own atomic
        /// BeginExecutionCount/EndExecutionCount, owns CliProgress for the duration
        /// (BeginExecution/EndExecution) so a queued execution can't surface the previous one's
        /// stale progress, and resets the explicit report when the last in-flight execution
        /// completes. Shared by the synchronous /api/exec path and <see cref="RunJobDetached"/> so
        /// the two copies of this scaffold can't drift apart.
        /// </summary>
        /// <param name="executionId">Id CliProgress.Report calls during this execution are attributed to.</param>
        /// <param name="body">The gated work to run once the exec gate is acquired.</param>
        /// <param name="preStartCheck">Runs after the gate is acquired but before
        /// CliProgress.BeginExecution; returning false skips <paramref name="body"/> entirely
        /// (used by the job path to honor a cancellation requested while still queued).</param>
        private async Task ExecuteGated(string executionId, Func<Task> body, Func<bool> preStartCheck = null)
        {
            Progress.BeginExecutionCount();
            try
            {
                await m_ExecGate.WaitAsync();
                try
                {
                    if (preStartCheck != null && !preStartCheck())
                        return;

                    Progress.BeginExecution(executionId);
                    try
                    {
                        await body();
                    }
                    finally
                    {
                        Progress.EndExecution(executionId);
                    }
                }
                finally
                {
                    m_ExecGate.Release();
                }
            }
            finally
            {
                Progress.EndExecutionCount();
            }
        }

        /// <summary>
        /// Execute a detached job (CLI-335). A cancellation requested while the job is still
        /// queued prevents it from starting; a running job is only cooperatively cancelable (see
        /// PipelineCancellation).
        /// </summary>
        private async Task RunJobDetached(PipelineJobRecord record, CommandInfo command, CommandExecutionRequest commandRequest)
        {
            // Set once the command actually starts, so a job canceled while still queued reports
            // nothing at all.
            var ran = false;
            var succeeded = false;
            var timer = new System.Diagnostics.Stopwatch();
            try
            {
                await ExecuteGated(record.Id, async () =>
                {
                    JobRegistry.MarkRunning(record);
                    ran = true;
                    timer.Start();
                    try
                    {
                        var result = await ExecuteCommandByName(command, commandRequest.Parameters,
                            UnboundedJobDispatcherTimeoutMs);
                        if (record.CancellationRequested)
                        {
                            // The code honored (or raced) a cancellation request; report
                            // canceled rather than a half-relevant result.
                            JobRegistry.MarkCanceled(record);
                        }
                        else
                        {
                            succeeded = IsSuccessfulResult(result);
                            JobRegistry.MarkCompleted(record, result);
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        JobRegistry.MarkCanceled(record);
                    }
                    catch (ArgumentException ex)
                    {
                        // Match the synchronous handler, which classifies ArgumentException as
                        // "Parameter Validation Failed": without this arm a client polling
                        // GET /api/job cannot tell bad input from a command that ran and failed.
                        if (record.CancellationRequested)
                        {
                            JobRegistry.MarkCanceled(record);
                        }
                        else
                        {
                            JobRegistry.MarkFailed(record, "Parameter Validation Failed", ex.Message);
                        }
                    }
                    catch (Exception ex)
                    {
                        // The command layer wraps thrown exceptions (an
                        // OperationCanceledException from PipelineCancellation surfaces as
                        // a wrapped execution failure) — after a cancellation request, any
                        // failure is reported as the cancellation taking effect.
                        if (record.CancellationRequested)
                        {
                            JobRegistry.MarkCanceled(record);
                        }
                        else
                        {
                            JobRegistry.MarkFailed(record, "Command Execution Failed", ex.Message);
                        }
                    }
                    finally
                    {
                        timer.Stop();
                    }
                },
                preStartCheck: () =>
                {
                    if (record.CancellationRequested)
                    {
                        JobRegistry.MarkCanceled(record);
                        return false;
                    }
                    return true;
                });
            }
            catch (Exception ex)
            {
                // The runner is fire-and-forget; a failure here must be recorded, never thrown.
                Debug.LogError($"RunJobDetached failed: {ex.Message}");
                JobRegistry.MarkFailed(record, "Internal Server Error", ex.Message);
            }

            // The submission already reported its own transaction, which could only say the job was
            // queued. This is the point where the command's real result is known.
            if (ran)
                OnCommandDone(CommandExecutionInfo.ForDetachedJob(command, succeeded, timer.ElapsedMilliseconds,
                    commandRequest.Parameters));
        }

        /// <summary>
        /// Project a progress snapshot to the <c>{title,info,current,total,pct}</c> wire shape
        /// shared by /api/progress and a running job's <c>progress</c> field — kept as one helper
        /// so the two can't drift out of sync with each other or the documented contract.
        /// </summary>
        private static object BuildProgressPayload(CliProgressState.Snapshot snapshot)
        {
            if (!snapshot.HasReport)
                return null;

            return new
            {
                title = snapshot.Title,
                info = snapshot.Info,
                current = snapshot.Current,
                total = snapshot.Total,
                pct = snapshot.Progress01
            };
        }

        /// <summary>Serialize one job record to the wire shape shared by the job endpoints.</summary>
        private object BuildJobResponse(PipelineJobRecord record, string warning = null)
        {
            lock (record.Gate)
            {
                var running = record.State == PipelineJobState.Running;
                var progress = running ? BuildProgressPayload(Progress.Current) : null;

                return new
                {
                    jobId = record.Id,
                    command = record.Command,
                    state = record.State.ToString().ToLowerInvariant(),
                    cancellationRequested = record.CancellationRequested,
                    enqueuedAt = record.EnqueuedAt,
                    startedAt = record.StartedAt,
                    completedAt = record.CompletedAt,
                    result = record.Result,
                    error = record.Error,
                    errorDetails = record.ErrorDetails,
                    progress,
                    // Corrective guidance for the caller (e.g. an unparseable omit_nulls value);
                    // null when there is nothing to say.
                    warnings = warning == null ? null : new[] { warning }
                };
            }
        }

        /// <summary>
        /// Handle GET /api/job?id=… — a detached job's state, progress, and (once terminal)
        /// its retained result (CLI-335 poll/reattach). Served on the listener thread.
        /// </summary>
        private async Task HandleJobStatusRequest(HttpListenerRequest request, HttpListenerResponse response)
        {
            try
            {
                var id = request.QueryString["id"];
                if (string.IsNullOrEmpty(id))
                {
                    await SendStatusResponse(response, 400, "Bad Request", "Query parameter 'id' is required");
                    return;
                }
                if (!JobRegistry.TryGet(id, out var record))
                {
                    await SendStatusResponse(response, 404, "Job Not Found", $"No job with id '{id}' (jobs do not survive domain reloads and are pruned after retention)");
                    return;
                }

                var omitNulls = ParseOmitNulls(request, out var omitNullsWarning);
                await SendJsonResponse(response, 200, BuildJobResponse(record, omitNullsWarning), omitNulls);
            }
            catch (Exception ex)
            {
                Debug.LogError($"HandleJobStatusRequest failed: {ex.Message}");
                await SendErrorResponse(response, "Job Status Error", $"Failed to get job status: {ex.Message}");
            }
        }

        /// <summary>
        /// Handle POST /api/job/cancel — request cancellation of a detached job (CLI-335).
        /// A queued job never starts; a running job gets the cooperative
        /// PipelineCancellation flag (synchronous code cannot be aborted from outside).
        /// </summary>
        private async Task HandleJobCancelRequest(HttpListenerRequest request, HttpListenerResponse response)
        {
            try
            {
                if (request.ContentLength64 > MaxCancelBodyBytes)
                {
                    await DrainRequestBody(request, MaxDrainBytes);
                    await SendStatusResponse(response, 413, "Payload Too Large",
                        $"Request body exceeds the maximum allowed size of {MaxCancelBodyBytes} bytes");
                    return;
                }

                string body;
                try
                {
                    using (var limited = new MaxLengthStream(request.InputStream, MaxCancelBodyBytes, leaveOpen: true))
                    using (var reader = new StreamReader(limited))
                    {
                        body = await reader.ReadToEndAsync();
                    }
                }
                catch (RequestTooLargeException ex)
                {
                    await DrainRequestBody(request, MaxDrainBytes);
                    await SendStatusResponse(response, 413, "Payload Too Large", ex.Message);
                    return;
                }
                finally
                {
                    request.InputStream.Dispose();
                }

                string id = null;
                try
                {
                    var parsed = JObject.Parse(string.IsNullOrEmpty(body) ? "{}" : body);
                    id = parsed["id"]?.ToString();
                }
                catch (JsonException)
                {
                    // Fall through to the missing-id error below.
                }

                if (string.IsNullOrEmpty(id))
                {
                    await SendStatusResponse(response, 400, "Bad Request", "Request body must be JSON with an 'id' field");
                    return;
                }
                if (!JobRegistry.RequestCancel(id, out var record))
                {
                    await SendStatusResponse(response, 404, "Job Not Found", $"No job with id '{id}'");
                    return;
                }

                var omitNulls = ParseOmitNulls(request, out var omitNullsWarning);
                await SendJsonResponse(response, 200, BuildJobResponse(record, omitNullsWarning), omitNulls);
            }
            catch (Exception ex)
            {
                Debug.LogError($"HandleJobCancelRequest failed: {ex.Message}");
                await SendErrorResponse(response, "Job Cancel Error", $"Failed to cancel job: {ex.Message}");
            }
        }

        /// <summary>
        /// Handle /api/progress endpoint — the currently executing command's task progress
        /// (CLI-488): the server side of the CLI's terminal progress bars, the pipeline
        /// equivalent of EditorUtility.DisplayProgressBar.
        ///
        /// Served entirely on the listener thread from CliProgress's lock-protected snapshot —
        /// never marshaled to the main thread — so it stays responsive while a long synchronous
        /// command has the main thread blocked (exactly when progress matters most).
        ///
        /// Contract (all progress fields optional; pct is 0–1; an idle server returns
        /// <c>{"active":false,"progress":null}</c>):
        /// <code>{"active":true,"progress":{"title":"…","info":"…","current":42,"total":100,"pct":0.42}}</code>
        /// </summary>
        private async Task HandleProgressRequest(HttpListenerRequest request, HttpListenerResponse response)
        {
            try
            {
                var active = Progress.IsActive;
                var progress = active ? BuildProgressPayload(Progress.Current) : null;

                var omitNulls = ParseOmitNulls(request, out var omitNullsWarning);
                await SendJsonResponse(response, 200, new
                {
                    active,
                    progress,
                    // Corrective guidance for the caller (e.g. an unparseable omit_nulls value);
                    // null when there is nothing to say.
                    warnings = omitNullsWarning == null ? null : new[] { omitNullsWarning }
                }, omitNulls);
            }
            catch (Exception ex)
            {
                Debug.LogError($"HandleProgressRequest failed: {ex.Message}");
                await SendErrorResponse(response, "Progress Error", $"Failed to get progress: {ex.Message}");
            }
        }

        /// <summary>
        /// Handle /api/dialog endpoint — currently-open modal dialogs, mirroring /api/progress's
        /// shape and thread-safety: served entirely from DialogStateTracker's lock-protected
        /// state, never marshaled to the main thread, so it answers even while a synchronous
        /// command has the main thread blocked inside a modal dialog (exactly when it matters
        /// most).
        ///
        /// Contract: {"active":true,"dialogs":[{"id","source","title","message","level","buttons","openedAt","dismissedAt"}]}
        ///
        /// "active": false means no dialog of a COVERED kind is open, not that the Editor isn't
        /// blocked by some other popup — coverage is mechanism-based (see
        /// UnityEditor.EditorDialogEvents' remarks in trunk): native message boxes and EditorWindow
        /// modals are covered; OS file/folder pickers and any other dialog mechanism are not. A
        /// caller still needs a fallback signal (e.g. a command timeout) for the uncovered cases.
        /// </summary>
        private async Task HandleDialogRequest(HttpListenerResponse response)
        {
            try
            {
                var dialogs = Dialogs.CurrentlyOpen;
                await SendJsonResponse(response, 200, new
                {
                    active = dialogs.Count > 0,
                    dialogs = dialogs.Select(BuildDialogPayload).ToList()
                });
            }
            catch (Exception ex)
            {
                Debug.LogError($"HandleDialogRequest failed: {ex.Message}");
                await SendErrorResponse(response, "Dialog Error", $"Failed to get dialog state: {ex.Message}");
            }
        }

        /// <summary>
        /// Shared wire projection of a DialogInfo, used by /api/dialog, dialogsDuringExecution, the
        /// busy-gate, and (internal, via InternalsVisibleTo) EditorPipelineServer's /api/status.
        /// </summary>
        internal static object BuildDialogPayload(DialogInfo info) => new
        {
            id = info.Id,
            source = info.Source,
            title = info.Title,
            message = info.Message,
            level = info.Level,
            buttons = info.Buttons,
            openedAt = info.OpenedAtUtc,
            dismissedAt = info.DismissedAtUtc
        };

        /// <summary>Human-readable one-line summary of a dialog for the busy-gate message. Message/Level are null for a ManagedCustomWindow source, so both are omitted when absent.</summary>
        private static string FormatDialogSummary(DialogInfo info)
        {
            var levelPrefix = string.IsNullOrEmpty(info.Level) ? "" : $"[{info.Level}] ";
            var messageSuffix = string.IsNullOrEmpty(info.Message) ? "" : $": {info.Message}";
            return $"{levelPrefix}{info.Title}{messageSuffix}";
        }

        /// <summary>
        /// Handle /api/exec endpoint - execute CLI commands with parameters.
        /// </summary>
        private async Task HandleExecRequest(HttpListenerRequest request, HttpListenerResponse response)
        {
            var cmd = "";
            // Stay false for pre-parse failures (413/empty/invalid JSON), which cannot honor the
            // request's flags; set from the request as soon as it deserializes — before structure
            // validation, so a verbose request gets a verbose validation failure. (AUTHAPI-21)
            var verbose = false;
            var omitNulls = false;
            string requestBody = null;
            // The execution half of the info reported to OnCommandDone. Both stay at their
            // defaults on every branch that answers without running a command, and the timer is
            // readable from the catch blocks below because it is started inside the gated body.
            CommandInfo executed = null;
            JObject executedParameters = null;
            var timer = new System.Diagnostics.Stopwatch();
            try
            {
                // Reject oversized bodies up front via Content-Length (cheap, before reading anything).
                if (request.ContentLength64 > MaxRequestBodyBytes)
                {
                    // Drain the body first so the client can read the 413 (see DrainRequestBody).
                    await DrainRequestBody(request, MaxDrainBytes);
                    await SendExecResponse(response, 413,
                        BaseResponse.Failure("Payload Too Large",
                            $"Request body exceeds the maximum allowed size of {MaxRequestBodyBytes} bytes"), null);
                    return;
                }

                // Read request body, enforcing the same cap while reading in case Content-Length is
                // absent or untruthful (e.g. chunked transfer-encoding).
                try
                {
                    // leaveOpen: on an over-limit read we still need the raw InputStream open to drain
                    // the unsent remainder before responding, so it must outlive the reader/wrapper.
                    using (var limited = new MaxLengthStream(request.InputStream, MaxRequestBodyBytes, leaveOpen: true))
                    using (var reader = new StreamReader(limited))
                    {
                        requestBody = await reader.ReadToEndAsync();
                    }
                }
                catch (RequestTooLargeException ex)
                {
                    // Drain the body first so the client can read the 413 (see DrainRequestBody).
                    await DrainRequestBody(request, MaxDrainBytes);
                    await SendExecResponse(response, 413,
                        BaseResponse.Failure("Payload Too Large", ex.Message), null);
                    return;
                }
                finally
                {
                    // MaxLengthStream left the InputStream open; dispose it now that any read/drain is done.
                    request.InputStream.Dispose();
                }

                if (string.IsNullOrEmpty(requestBody))
                {
                    await SendExecResponse(response, 400,
                        BaseResponse.Failure("Bad Request", "Request body is required"), requestBody);
                    return;
                }

                // Parse command request
                CommandExecutionRequest commandRequest;
                try
                {
                    commandRequest = JsonConvert.DeserializeObject<CommandExecutionRequest>(requestBody);
                }
                catch (JsonException ex)
                {
                    await SendExecResponse(response, 400,
                        BaseResponse.Failure("Invalid JSON", $"Failed to parse request body: {ex.Message}"), requestBody);
                    return;
                }

                // A body of literal JSON "null" parses without error but deserializes to null —
                // reject it here so everything below can rely on a non-null request (previously
                // this slid through the null-conditional validation and NRE'd at the Command read).
                if (commandRequest == null)
                {
                    await SendExecResponse(response, 400,
                        BaseResponse.Failure("Invalid Request", "Request body must be a JSON object"), requestBody);
                    return;
                }

                // Honor the reply-shape flags from here on: the request has deserialized, so even
                // the structure-validation failure below can respect them.
                verbose = commandRequest.Verbose;
                omitNulls = commandRequest.OmitNulls;

                // Validate request structure
                var requestValidationError = commandRequest.Validate();
                if (!string.IsNullOrEmpty(requestValidationError))
                {
                    await SendExecResponse(response, 400,
                        BaseResponse.Failure("Invalid Request", requestValidationError), requestBody, verbose, omitNulls);
                    return;
                }

                // Raw command-line forms. Tokenize `commandLine`, or take `argv` as-is (it is
                // already split; re-splitting would corrupt any value containing a space), then
                // normalize the request IN PLACE by assigning the command name. Normalizing here
                // rather than deeper down is what leaves the rest of this method unchanged:
                // RunJobDetached re-reads commandRequest, so `job` needs no special handling, and
                // the CmdSuccess command echo stays correct.
                //
                // Placed after Validate() so transport concerns do not depend on the command
                // registry, after the reply-shape flags are captured so a malformed line still
                // honours verbose/omitNulls, and before ResolveCommand so an unknown raw command
                // name produces the same Command Not Found envelope as the structured path.
                List<string> rawTokens = null;
                if (commandRequest.IsRawForm)
                {
                    if (commandRequest.Argv != null)
                    {
                        rawTokens = commandRequest.Argv;
                    }
                    else if (!CommandLineTokenizer.TryTokenize(commandRequest.CommandLine, out rawTokens, out var tokenizeError))
                    {
                        // Reported as a malformed request shape, not as an argument error: no
                        // command was resolved, so there is no schema and no argProblems to carry.
                        // INVALID_COMMAND_ARGS promises a client both of those.
                        await SendExecResponse(response, 400,
                            BaseResponse.Failure("Invalid Request", tokenizeError),
                            requestBody, verbose, omitNulls);
                        return;
                    }

                    // A blank first token is as malformed as no token at all, and it is reachable:
                    // a commandLine of `"" --message hi` is a NONBLANK source string, so Validate()
                    // passes, yet it tokenizes to an empty command name. Left unchecked that became
                    // a Command Not Found, telling the caller an empty command is merely
                    // unavailable. `argv` with an empty first element is already rejected by
                    // Validate(), so both raw forms answer the same way here.
                    if (rawTokens.Count == 0 || string.IsNullOrWhiteSpace(rawTokens[0]))
                    {
                        await SendExecResponse(response, 400,
                            BaseResponse.Failure("Invalid Request", "Command name is required"),
                            requestBody, verbose, omitNulls);
                        return;
                    }

                    commandRequest.Command = rawTokens[0];
                }

                cmd = commandRequest.Command;

                // Resolve the command once — the busy gate and the execution path share this
                // single registry lookup instead of scanning twice per request. An unknown name
                // throws here and surfaces as the usual Command Not Found via the catch below
                // (for a "job": true submission that now happens BEFORE a job is created, so a
                // misnamed command fails fast instead of producing a job that fails later).
                var command = ResolveCommand(cmd);

                // Bind the remaining tokens and normalize in place, so everything from
                // ExtractCommandParameters downwards sees exactly one shape.
                //
                // Before the busy gate: a malformed command line is a deterministic client fault,
                // and answering 503-retryable first would send clients into a retry loop over an
                // error that will never clear. Also before job creation, so a mistyped parameter
                // can never return a job id and exit 0.
                if (rawTokens != null)
                {
                    var args = rawTokens.GetRange(1, rawTokens.Count - 1);
                    if (!CommandLineBinder.TryBind(command, args, out var bound, out var argProblems))
                    {
                        await SendExecResponse(response, 400,
                            CommandExecutionResponse.CmdInvalidArgs(cmd,
                                DescribeArgProblems(command, argProblems), argProblems,
                                BuildCommandEntry(command)),
                            requestBody, verbose, omitNulls);
                        return;
                    }

                    commandRequest.Parameters = bound;

                    // Refuse a known-invalid raw submission before the busy gate and before job
                    // creation. Binding SUCCEEDS when a required parameter simply was not supplied
                    // — that rule belongs to parameter validation, not the binder — so without
                    // this check `{"argv":["log_editor"],"job":true}` reached job creation, was
                    // acknowledged with a job id and a 200, and only failed later inside the
                    // detached run. The client would be told a deterministically invalid request
                    // had been accepted.
                    var missingRequired = ValidateRequiredParametersBound(command, bound);
                    if (!string.IsNullOrEmpty(missingRequired))
                    {
                        await SendExecResponse(response, 400,
                            CommandExecutionResponse.CmdFailure(cmd, "Parameter Validation Failed", missingRequired),
                            requestBody, verbose, omitNulls);
                        return;
                    }
                }

                // Dialog busy gate: checked before the settling gate below. A modal dialog genuinely
                // blocks the main thread inside a nested OS message loop (unlike settling, which
                // merely delays), so it's true regardless of settle state and is the more actionable
                // fact for a caller — settling resolves on its own, a dialog needs a human.
                // TryGetDialogBlockedFallback is the one exception: a command whose result can be
                // served from a cached snapshot instead of running for real (editor_status).
                if (command.MainThreadRequired)
                {
                    var openDialogs = Dialogs.CurrentlyOpen;
                    if (openDialogs.Count > 0)
                    {
                        object fallback = TryGetDialogBlockedFallback(command, openDialogs[0]);
                        if (fallback != null)
                        {
                            await SendExecResponse(response, 200,
                                WithBoundParameters(
                                    CommandExecutionResponse.CmdSuccess(cmd, fallback), commandRequest),
                                requestBody, verbose, omitNulls);
                            return;
                        }

                        var summary = FormatDialogSummary(openDialogs[0]);
                        var separator = summary.EndsWith(".") || summary.EndsWith("!") || summary.EndsWith("?") ? " " : ". ";
                        var busyResponse = CommandExecutionResponse.CmdBusy(cmd,
                            $"A modal dialog is currently open and blocking the main thread: {summary}{separator}" +
                            "Poll GET /api/dialog for details, or retry once it is dismissed.",
                            "blocked_by_dialog");
                        busyResponse.Dialogs = openDialogs.Select(BuildDialogPayload).ToList();
                        await SendExecResponse(response, 503, busyResponse, requestBody, verbose, omitNulls);
                        return;
                    }
                }

                // Busy gate (AUTHAPI-35): while the host is settling after startup, reject
                // not-yet-serviceable commands up front — before sync execution AND before a
                // detached job is created (a queued job would otherwise run into the half-ready
                // Editor in the background). 503 with a retryable envelope, distinguishable from
                // a genuine command failure. Only the exec endpoint gates; the status endpoints
                // keep working so the busy state itself stays observable.
                var busyReason = GetBusyReason(command);
                if (busyReason != null)
                {
                    // The busy reply is a standard exec envelope, so it follows the request's
                    // reply-shape flags like every other branch: lean drops the command echo and
                    // envelope metadata; verbose restores them (AUTHAPI-21 x AUTHAPI-35).
                    await SendExecResponse(response, 503,
                        CommandExecutionResponse.CmdBusy(cmd, busyReason, "settling"), requestBody, verbose, omitNulls);
                    return;
                }

                // Detached job (CLI-335): reply with a job id immediately and run the command
                // in the background — the client polls GET /api/job?id=… to reattach and
                // collect the result after its own HTTP timeout would have expired.
                if (commandRequest.Job)
                {
                    if (!JobRegistry.TryCreate(commandRequest.Command, out var jobRecord))
                    {
                        await SendExecResponse(response, 429,
                            CommandExecutionResponse.CmdFailure(commandRequest.Command, "Too Many Queued Jobs",
                                "Too many jobs are queued or running; wait for some to finish and retry."), requestBody, verbose, omitNulls);
                        return;
                    }
                    _ = RunJobDetached(jobRecord, command, commandRequest);
                    // Standard exec envelope; the job handle is the command's "result".
                    await SendExecResponse(response, 200,
                        WithBoundParameters(
                            CommandExecutionResponse.CmdSuccess(commandRequest.Command,
                                new { jobId = jobRecord.Id, state = "queued" }), commandRequest),
                        requestBody, verbose, omitNulls);
                    return;
                }

                // Execute command using shared execution logic (see ExecuteGated: one command at
                // a time, exactly as when the accept loop was serial).
                DateTime execStartUtc = default;
                object result = null;
                await ExecuteGated(Guid.NewGuid().ToString("N"), async () =>
                {
                    // Claimed here rather than before ExecuteGated, so that a request which never
                    // gets past the gate reports no execution at all, and so that the wait for the
                    // one-at-a-time gate counts as queueing rather than as time the command ran.
                    executed = command;
                    executedParameters = commandRequest.Parameters;
                    execStartUtc = DateTime.UtcNow;
                    timer.Start();
                    try
                    {
                        result = await ExecuteCommandByName(command, commandRequest.Parameters,
                            commandRequest.Timeout ?? 60000);
                    }
                    finally
                    {
                        timer.Stop();
                    }
                });

                // Send success response, attaching any dialog(s) that opened during this call
                // (dialogsDuringExecution) so a caller learns about them even without polling
                // /api/dialog concurrently while the call was in flight.
                var successResponse = WithBoundParameters(
                    CommandExecutionResponse.CmdSuccess(commandRequest.Command, result), commandRequest);
                var dialogEvents = Dialogs.EventsSince(execStartUtc);
                if (dialogEvents.Count > 0)
                    successResponse.DialogsDuringExecution = dialogEvents.Select(BuildDialogPayload).ToList();

                await SendExecResponse(response, 200, successResponse, requestBody, verbose, omitNulls,
                    executed, IsSuccessfulResult(result), timer.ElapsedMilliseconds, commandRequest.Parameters);
            }
            catch (ArgumentException ex)
            {
                // Parameter validation errors
                await SendExecResponse(response, 400,
                    CommandExecutionResponse.CmdFailure(cmd, "Parameter Validation Failed", ex.Message), requestBody, verbose, omitNulls,
                    executed, false, timer.ElapsedMilliseconds, executedParameters);
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("No command named"))
            {
                // Command not found errors
                await SendExecResponse(response, 400,
                    CommandExecutionResponse.CmdFailure(cmd, "Command Not Found", ex.Message), requestBody, verbose, omitNulls,
                    executed, false, timer.ElapsedMilliseconds, executedParameters);
            }
            catch (InvalidOperationException ex)
            {
                // Command execution errors
                await SendExecResponse(response, 400,
                    CommandExecutionResponse.CmdFailure(cmd, "Command Execution Failed", ex.Message), requestBody, verbose, omitNulls,
                    executed, false, timer.ElapsedMilliseconds, executedParameters);
            }
            catch (Exception ex)
            {
                Debug.LogError($"Failed to handle /api/exec request: {ex.Message}");
                await SendExecResponse(response, 400,
                    CommandExecutionResponse.CmdFailure(cmd, "Internal Server Error", ex.Message), requestBody, verbose, omitNulls,
                    executed, false, timer.ElapsedMilliseconds, executedParameters);
            }
        }

        /// <summary>
        /// Read and discard the remaining request body, up to <paramref name="maxDrainBytes"/> bytes.
        /// When we reject a request early (e.g. 413) the client may still be uploading; if we close the
        /// response while unread data is in flight, HttpListener resets the TCP connection and the client
        /// sees a connection error instead of our HTTP response. Draining lets the upload complete so the
        /// response is delivered. Best-effort and bounded: a body larger than the budget stops draining
        /// (and may reset), which is acceptable for an abusive request.
        /// </summary>
        private static async Task DrainRequestBody(HttpListenerRequest request, long maxDrainBytes)
        {
            try
            {
                var input = request.InputStream;
                var buffer = new byte[16 * 1024];
                long drained = 0;
                while (drained < maxDrainBytes)
                {
                    var read = await input.ReadAsync(buffer, 0, buffer.Length);
                    if (read <= 0)
                        break;
                    drained += read;
                }
            }
            catch
            {
                // Best-effort: if the client already closed the connection or the stream is gone,
                // there is nothing left to drain and the response send below will handle the rest.
            }
        }

        /// <summary>
        /// Send error response with structured JSON format.
        /// </summary>
        private async Task SendErrorResponse(HttpListenerResponse response, string error, string details = null, int statusCode = 400)
        {
            try
            {
                var errorResponse = BaseResponse.Failure(error, details);
                await SendResponse(response, statusCode, errorResponse);
            }
            catch
            {
                // Ignore errors in error handling
            }
        }

        private async Task SendResponse(HttpListenerResponse response, int statusCode, BaseResponse pipelineResponse)
        {
            response.StatusCode = statusCode;
            response.ContentType = "application/json";
            var json = JsonConvert.SerializeObject(pipelineResponse, Formatting.Indented);
            var buffer = Encoding.UTF8.GetBytes(json);
            response.ContentLength64 = buffer.Length;

            await response.OutputStream.WriteAsync(buffer, 0, buffer.Length);
            response.OutputStream.Close();
        }

        /// <summary>
        /// Send an /api/exec response and report the finished interaction to
        /// <see cref="OnCommandDone"/>. Single send point for the exec handler so every branch
        /// (success and error) is captured uniformly.
        ///
        /// The executed* arguments carry the execution half of the info. Branches that reject a
        /// request before any command runs (413, malformed body, busy host, job queue full) leave
        /// them at their defaults, which reports an info with no command.
        /// </summary>
        private async Task SendExecResponse(HttpListenerResponse response, int statusCode,
                                            BaseResponse body, string requestJson, bool verbose = false, bool omitNulls = false,
                                            CommandInfo executedCommand = null, bool executedSuccess = false, long executedDurationMs = 0,
                                            JObject executedParameters = null)
        {
            response.StatusCode = statusCode;
            response.ContentType = "application/json";
            // Lean, compact serialization by default; the request's `verbose` flag opts into the
            // full envelope and `omitNulls` drops payload nulls. Single contract shared with the
            // byte-size regression test. (AUTHAPI-21)
            var json = ExecResponseSerializer.Serialize(body, verbose, omitNulls);
            var buffer = Encoding.UTF8.GetBytes(json);
            response.ContentLength64 = buffer.Length;

            await response.OutputStream.WriteAsync(buffer, 0, buffer.Length);
            response.OutputStream.Close();

            OnCommandDone(new CommandExecutionInfo(requestJson, json, executedCommand, executedSuccess, executedDurationMs,
                executedParameters));
        }

        /// <summary>
        /// Hook invoked once per finished /api/exec interaction, and once more when a detached job
        /// completes. The single place to hang post-command work: the Editor server writes the
        /// transaction log here and queues its analytics from here. No-op by default (the Player
        /// does neither).
        ///
        /// Runs on the background HTTP thread, so an implementation must either stick to
        /// thread-safe work or marshal to the main thread itself. It must also never throw —
        /// an exception here would surface as a failed request for work the caller never asked for.
        /// </summary>
        /// <param name="info">The finished interaction: its transaction, its execution, or both.</param>
        protected virtual void OnCommandDone(in CommandExecutionInfo info) { }

        /// <summary>
        /// Whether a command reported success. A command fails in two ways: by throwing, which the
        /// caller has already accounted for, or by RETURNING a response that carries its own
        /// failure — eval, hot reload, run_script and test runs all do the latter, so nothing
        /// throws and the outer envelope says success while the inner result says otherwise.
        /// </summary>
        private static bool IsSuccessfulResult(object result)
        {
            switch (result)
            {
                case CommandExecutionResponse commandResponse:
                    return commandResponse.Success;
                case BaseResponse baseResponse:
                    return string.IsNullOrEmpty(baseResponse.Error);
                default:
                    return true;
            }
        }

        /// <summary>
        /// Convert a single JSON parameter token to the command's parameter type.
        ///
        /// Agents and the CLI frequently pass structured parameters (e.g. <c>float[]</c> position,
        /// <c>JObject</c> properties) as a JSON-ENCODED STRING — e.g. position <c>"[0,0,0]"</c> or
        /// properties <c>"{\"m_Mass\":0.17}"</c>. Newtonsoft's <c>JValue(string).ToObject(float[])</c>
        /// (and likewise for <c>JObject</c>) returns null, so the parameter silently drops out:
        /// set_transform applies nothing and set_component_properties fails Required-parameter
        /// validation (CLI-219 / CLI-220). To fix this generally — without special-casing any command —
        /// when the token is a string but the target type is NOT string AND the trimmed string starts
        /// with '{' or '[', we re-parse it as a JSON document before converting.
        ///
        /// The '{'/'[' guard is deliberate and narrow: ordinary string params (and ObjectRef string
        /// handles like "/Player", "Assets/Foo.prefab", "guid:..." — none of which start with '{'/'[')
        /// fall straight through to <c>token.ToObject</c>, so the class-level
        /// <see cref="Unity.Pipeline.Models.ObjectRefConverter"/> and plain-string parameters keep
        /// working unchanged. A re-parse that fails falls through to the normal conversion path (the
        /// caller's try/catch then handles any remaining failure).
        /// </summary>
        /// <remarks>
        /// Internal rather than private so <see cref="CommandLineBinder"/> can DRY-RUN the exact
        /// converter the executor will use. Binding against a different conversion would let a
        /// command line pass validation and then fail during execution.
        /// </remarks>
        internal static object ConvertParameterToken(Newtonsoft.Json.Linq.JToken token, System.Type targetType)
        {
            if (token == null)
                return null;

            if (token.Type == Newtonsoft.Json.Linq.JTokenType.String
                && targetType != typeof(string)
                && !targetType.IsPrimitive
                && !targetType.IsEnum)
            {
                var s = token.Value<string>();
                if (!string.IsNullOrWhiteSpace(s))
                {
                    var t = s.TrimStart();
                    if (t.Length > 0 && (t[0] == '{' || t[0] == '['))
                    {
                        try { return Newtonsoft.Json.Linq.JToken.Parse(s).ToObject(targetType); }
                        catch (Newtonsoft.Json.JsonException) { /* fall through to the direct conversion */ }
                    }
                }
            }

            return token.ToObject(targetType);
        }

        /// <summary>
        /// Extract command parameters from JSON request and convert to appropriate types.
        /// <para><paramref name="conversionError"/> reports arguments that could not be converted to
        /// the parameter's type. Substituting the default there gives the caller a different result
        /// than it asked for with no way to detect it — an ignored <c>limit</c> returns more than was
        /// asked, an ignored <c>timeout</c> waits longer than allowed. The slot still gets the default
        /// so the array stays well-formed, but the callers reject the request.</para>
        /// </summary>
        private object[] ExtractCommandParameters(CommandInfo command, Newtonsoft.Json.Linq.JObject parametersJson, out string conversionError)
        {
            StringBuilder conversionErrors = null;
            var parameterValues = new object[command.Parameters.Count];

            for (int i = 0; i < command.Parameters.Count; i++)
            {
                var paramInfo = command.Parameters[i];
                var paramName = paramInfo.Name;

                // Try to get value from JSON parameters
                object jsonValue = null;
                if (parametersJson != null && parametersJson.ContainsKey(paramName))
                {
                    var token = parametersJson[paramName];
                    try
                    {
                        jsonValue = ConvertParameterToken(token, paramInfo.ParameterType);

                        // A converter can DECLINE a token by returning null instead of throwing —
                        // ObjectRefConverter does exactly that for unsupported kinds, so
                        // {"parent": true} would otherwise read as an omitted argument and the
                        // command would run against the default. Anything that converts to null
                        // without being one of the intentional null forms is a conversion failure.
                        if (jsonValue == null && !IsIntentionalNullToken(token))
                        {
                            RecordConversionError(ref conversionErrors, paramName, paramInfo,
                                $"unsupported JSON value ({token.Type})");
                        }
                    }
                    catch (Exception ex)
                    {
                        // Record it rather than logging and moving on: a console warning is invisible
                        // to the HTTP caller, which is the only party that can act on it.
                        RecordConversionError(ref conversionErrors, paramName, paramInfo, ex.Message);
                    }
                }

                // Use provided value or default value
                if (jsonValue != null)
                {
                    parameterValues[i] = jsonValue;
                }
                else if (paramInfo.DefaultValue != null)
                {
                    parameterValues[i] = paramInfo.DefaultValue;
                }
                else
                {
                    // Use type's default value for value types, null for reference types
                    parameterValues[i] = paramInfo.ParameterType.IsValueType
                        ? Activator.CreateInstance(paramInfo.ParameterType)
                        : null;
                }
            }

            conversionError = conversionErrors?.ToString();
            return parameterValues;
        }

        /// <summary>
        /// The token forms a converter may legitimately turn into null, as opposed to declining a
        /// value it cannot represent: an explicit JSON null, and an empty or whitespace-only string.
        /// The latter is a documented contract, not a quirk — <c>ObjectRefConverter.FromString</c>
        /// returns null for it, and <c>set_parent</c> advertises "Omit (or empty) to move the object
        /// to the scene root", so <c>{"parent":""}</c> must keep detaching rather than 400.
        /// </summary>
        private static bool IsIntentionalNullToken(JToken token)
        {
            if (token == null || token.Type == JTokenType.Null)
                return true;

            return token.Type == JTokenType.String && string.IsNullOrWhiteSpace(token.Value<string>());
        }

        /// <summary>Appends one parameter-conversion failure to the accumulated report.</summary>
        private static void RecordConversionError(
            ref StringBuilder errors, string paramName, CommandParameterInfo paramInfo, string detail)
        {
            if (errors == null)
                errors = new StringBuilder();
            else
                errors.Append("; ");
            errors.Append(
                $"Parameter '{paramName}' could not be converted to {paramInfo.ParameterType.Name}: {detail}");
        }

        /// <summary>
        /// The one wording for a missing required parameter. Shared by the two validators below so
        /// the structured and raw request forms cannot diagnose the same mistake differently.
        /// </summary>
        private static string MissingRequiredParameterMessage(string parameterName)
        {
            return $"Required parameter '{parameterName}' is missing or empty";
        }

        /// <summary>
        /// Validates required parameters against a BOUND parameter object, before anything runs.
        ///
        /// <see cref="ValidateCommandParameters"/> applies the same rule to already-extracted CLR
        /// values, but it runs inside command execution — which for a detached job is after the job
        /// id has already been handed to the client. A raw submission missing a required parameter
        /// would be acknowledged as accepted and only fail later, in the background. Checking the
        /// bound object first lets that request be refused up front, with the same envelope the
        /// synchronous path produces.
        ///
        /// The rule itself is shared, not reimplemented: both this and the extracted-value check
        /// normalize to a CLR value and call <see cref="IsMissingRequiredValue"/>, so the raw and
        /// structured paths cannot drift on what counts as missing.
        /// </summary>
        private static string ValidateRequiredParametersBound(CommandInfo command, JObject parameters)
        {
            for (var i = 0; i < command.Parameters.Count; i++)
            {
                var paramInfo = command.Parameters[i];
                if (!paramInfo.Required)
                    continue;

                if (IsMissingRequiredValue(AsRequiredValue(parameters?[paramInfo.Name])))
                {
                    return MissingRequiredParameterMessage(paramInfo.Name);
                }
            }

            return null;
        }

        /// <summary>
        /// The one rule for "this required parameter was not supplied": absent, null, or the empty
        /// string. Both the bound-JSON and extracted-CLR checks normalize to a CLR value and share
        /// this, so a future change to what counts as missing cannot be applied to one path and
        /// forgotten on the other.
        /// </summary>
        private static bool IsMissingRequiredValue(object value)
        {
            return value == null || (value is string text && string.IsNullOrEmpty(text));
        }

        /// <summary>
        /// A bound JSON value as the CLR value <see cref="IsMissingRequiredValue"/> expects. A JSON
        /// null is a CLR null; a JSON string is its text; anything else is present by definition
        /// and stands in for itself.
        /// </summary>
        private static object AsRequiredValue(JToken value)
        {
            if (value == null || value.Type == JTokenType.Null)
                return null;
            return value.Type == JTokenType.String ? value.Value<string>() : (object)value;
        }

        /// <summary>
        /// Validate that all required command parameters are provided.
        /// </summary>
        private string ValidateCommandParameters(CommandInfo command, object[] parameters)
        {
            for (int i = 0; i < command.Parameters.Count; i++)
            {
                var paramInfo = command.Parameters[i];

                if (paramInfo.Required && IsMissingRequiredValue(parameters[i]))
                {
                    return MissingRequiredParameterMessage(paramInfo.Name);
                }
            }

            return null; // No validation errors
        }

        /// <summary>
        /// Resolve a command name to its <see cref="CommandInfo"/>, or throw the standard
        /// Command Not Found error. Single registry scan — /api/exec resolves once and shares
        /// the result between the busy gate and execution.
        /// </summary>
        private static CommandInfo ResolveCommand(string commandName)
        {
            var commands = CommandRegistry.DiscoverCommands().ToList();
            var command = commands.FirstOrDefault(c => c.Name == commandName);
            if (command == null)
            {
                var availableCommands = string.Join(", ", commands.Select(c => c.Name));
                var errorMessage = $"No command named '{commandName}' is available. Available: [{availableCommands}]";
                Debug.LogError($"ExecuteCommandByName: {errorMessage}");
                throw new InvalidOperationException(errorMessage);
            }
            return command;
        }

        /// <summary>
        /// Execute a command by name with JSON parameters.
        /// Handles command lookup, parameter validation, and execution.
        /// Used by callers that only hold a name (status endpoints, detached job runner); the
        /// /api/exec handler resolves the command itself and uses the CommandInfo overload.
        /// </summary>
        private async Task<object> ExecuteCommandByName(string commandName, JObject parametersJson, int dispatcherTimeoutMs = 60000)
        {
            return await ExecuteCommandByName(ResolveCommand(commandName), parametersJson, dispatcherTimeoutMs);
        }

        /// <summary>
        /// Execute a pre-resolved command with JSON parameters (parameter extraction, required
        /// validation, threading).
        /// </summary>
        private async Task<object> ExecuteCommandByName(CommandInfo command, JObject parametersJson, int dispatcherTimeoutMs = 60000)
        {
            // Extract parameters
            var parameters = ExtractCommandParameters(command, parametersJson, out var conversionError);

            // Reject unconvertible arguments rather than silently running with the default
            if (!string.IsNullOrEmpty(conversionError))
            {
                Debug.LogError($"ExecuteCommandByName: Parameter conversion failed: {conversionError}");
                throw new ArgumentException(conversionError);
            }

            // Validate required parameters
            var validationError = ValidateCommandParameters(command, parameters);
            if (!string.IsNullOrEmpty(validationError))
            {
                Debug.LogError($"ExecuteCommandByName: Parameter validation failed: {validationError}");
                throw new ArgumentException(validationError);
            }

            // Execute command with appropriate threading
            return await ExecuteCommand(command, parameters, dispatcherTimeoutMs);
        }

        /// <summary>
        /// Execute the command method with provided parameters.
        /// Handles main thread execution if required.
        /// </summary>
        private async Task<object> ExecuteCommand(CommandInfo command, object[] parameters, int dispatcherTimeoutMs = 60000)
        {
            object raw;
            if (command.MainThreadRequired)
            {
                // Execute on the main thread using THIS server's dispatcher.
                if (m_Dispatcher.IsMainThread())
                {
                    raw = ExecuteCommandDirect(command, parameters);
                }
                else
                {
                    // If the command declares its own int "timeout" parameter (e.g. eval/eval_file),
                    // honor it as the wait budget instead of the caller-provided dispatcher budget
                    // (UUM-148641) — that's the only mechanism that actually enforces such a
                    // command's requested deadline end-to-end, in both directions: a request below
                    // the dispatcher default must time out early, not wait it out. Detached jobs
                    // are the one exception: they pass an unbounded budget (CLI-335) and rely on
                    // cooperative cancellation, so the requested value must not re-bound them.
                    var requestedTimeoutMs = ResolveRequestedTimeoutMs(command, parameters);
                    var waitBudgetMs = dispatcherTimeoutMs == UnboundedJobDispatcherTimeoutMs
                        ? dispatcherTimeoutMs
                        : requestedTimeoutMs ?? dispatcherTimeoutMs;
                    raw = await Task.Run(() => m_Dispatcher.Invoke(() => ExecuteCommandDirect(command, parameters), waitBudgetMs));
                }
            }
            else
            {
                // Execute on background thread
                raw = await Task.Run(() => ExecuteCommandDirect(command, parameters));
            }

            return await UnwrapResult(raw);
        }

        /// <summary>
        /// Commands whose own "timeout" parameter is documented in milliseconds and is meant to bound
        /// the whole main-thread call. Other commands happen to also declare a "timeout" parameter
        /// (e.g. run_tests, in seconds) with different semantics, so this can't be a blanket
        /// name/type match — it has to be opted into per command.
        /// </summary>
        private static readonly HashSet<string> CommandsWithMillisecondTimeoutParameter = new HashSet<string> { "eval", "eval_file", "run_script" };

        /// <summary>
        /// For commands in <see cref="CommandsWithMillisecondTimeoutParameter"/>, return the value the
        /// caller passed for their own "timeout" parameter. Returns null otherwise, in which case the
        /// caller should fall back to Dispatcher.Invoke's own default wait.
        /// </summary>
        private static int? ResolveRequestedTimeoutMs(CommandInfo command, object[] parameters)
        {
            if (!CommandsWithMillisecondTimeoutParameter.Contains(command.Name))
                return null;

            for (int i = 0; i < command.Parameters.Count; i++)
            {
                // eval/eval_file name their budget "timeout"; run_script names it "timeout_ms".
                var pName = command.Parameters[i].Name;
                if ((pName == "timeout" || pName == "timeout_ms") && command.Parameters[i].ParameterType == typeof(int))
                {
                    // A non-positive value cannot be a meaningful wait budget. Forwarding it would
                    // make Dispatcher.Invoke fail with its own opaque timeout before the command
                    // ever runs — fall back to the default budget instead, so the command's own
                    // friendly "timeout must be between …" validation is what answers the caller.
                    var requested = (int)parameters[i];
                    return requested > 0 ? requested : (int?)null;
                }
            }

            return null;
        }

        /// <summary>
        /// Commands declared as `async Task`/`Task&lt;T&gt;` return their Task from reflection Invoke.
        /// Await it here (on the calling background thread, leaving the main thread free to pump the
        /// dispatcher) so the actual value is serialized rather than the Task itself, and so a faulted
        /// command surfaces its real exception to the request handler instead of being masked when
        /// Newtonsoft reads Task.Result during serialization.
        /// </summary>
        private static async Task<object> UnwrapResult(object result)
        {
            if (result is Task task)
            {
                await task.ConfigureAwait(false);
                // Task&lt;T&gt; exposes a Result property; non-generic Task does not.
                var resultProperty = task.GetType().GetProperty("Result");
                return resultProperty?.GetValue(task);
            }

            return result;
        }

        /// <summary>
        /// Extract, validate, and invoke a pre-resolved command with JSON parameters synchronously on
        /// the current main thread, returning its unwrapped result. This is the exact parameter
        /// extraction, required-parameter validation, reflection invoke, and Task unwrap that the
        /// <c>/api/exec</c> path (<see cref="ExecuteCommandByName(CommandInfo, JObject, int)"/>) uses,
        /// exposed for composite commands (e.g. <c>batch</c>) that re-dispatch other registered
        /// commands from inside their own main-thread execution — so each sub-operation behaves, and
        /// its result is shaped, identically to a standalone call.
        ///
        /// MUST be called on the main thread. A composite command is itself
        /// <c>MainThreadRequired</c>, so it already runs there; calling off the main thread throws.
        /// A command that throws surfaces exactly as it would through <c>/api/exec</c>
        /// (<see cref="ArgumentException"/> preserved, other exceptions wrapped in
        /// <see cref="InvalidOperationException"/>) so the caller can record a per-operation failure.
        /// </summary>
        internal object DispatchCommandOnMainThread(CommandInfo command, JObject parametersJson)
        {
            if (command == null)
                throw new ArgumentNullException(nameof(command));
            if (!m_Dispatcher.IsMainThread())
                throw new InvalidOperationException(
                    "DispatchCommandOnMainThread must be called on the main thread.");

            var parameters = ExtractCommandParameters(command, parametersJson, out var conversionError);

            // Same contract as ExecuteCommandByName: an argument that could not be converted is a
            // rejected request, not a silent fallback to the parameter's default.
            if (!string.IsNullOrEmpty(conversionError))
                throw new ArgumentException(conversionError);

            var validationError = ValidateCommandParameters(command, parameters);
            if (!string.IsNullOrEmpty(validationError))
                throw new ArgumentException(validationError);

            var raw = ExecuteCommandDirect(command, parameters);
            // Unwrap a Task result inline. WARNING: this GetResult() blocks the main thread, so it is
            // only safe for results that are already complete (synchronous commands, or async methods
            // that happened to complete synchronously). An async command driven to completion by
            // EditorApplication.update callbacks (run_tests/list_tests) could NEVER finish here —
            // update cannot pump while this call blocks — and the Editor would freeze permanently.
            // That is why composite callers (batch) must reject commands with Task return types
            // before dispatching them through this path.
            return UnwrapResult(raw).GetAwaiter().GetResult();
        }

        /// <summary>
        /// Direct command execution using reflection. Sets <see cref="CurrentServer"/> for the
        /// duration so CliProgress/PipelineCancellation calls inside the command body resolve to
        /// this server, regardless of which thread this ends up running on.
        /// </summary>
        private object ExecuteCommandDirect(CommandInfo command, object[] parameters)
        {
            var previousServer = m_CurrentServer;
            m_CurrentServer = this;
            try
            {
                return command.Method.Invoke(null, parameters);
            }
            catch (System.Reflection.TargetInvocationException tie) when (tie.InnerException != null)
            {
                // Reflection wraps the command's own exception in a TargetInvocationException; surface
                // the inner message so callers (and agents) get an actionable error instead of the
                // generic "Exception has been thrown by the target of an invocation."
                var inner = tie.InnerException;
                Debug.LogError($"Command '{command.Name}' failed: {inner.Message}");

                // Preserve validation-oriented exceptions so HandleExecRequest's dedicated
                // catch (ArgumentException) classifies them as "Parameter Validation Failed" rather than
                // collapsing them into "Command Execution Failed". Rethrow the original with its stack.
                if (inner is ArgumentException)
                    System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(inner).Throw();

                throw new InvalidOperationException($"Command '{command.Name}' failed: {inner.Message}", inner);
            }
            catch (Exception ex)
            {
                Debug.LogError($"Command execution failed: {ex.Message}");
                throw new InvalidOperationException($"Command '{command.Name}' failed: {ex.Message}", ex);
            }
            finally
            {
                m_CurrentServer = previousServer;
            }
        }


        /// <summary>
        /// Get the port range for this server type.
        /// Editor servers use 7800-7899, Runtime servers use 7900-7999.
        /// </summary>
        /// <returns>The inclusive port range to try when binding the listener.</returns>
        protected virtual (int basePort, int maxPort) GetPortRange()
        {
            return (7800, 7849); // Editor production (test editor servers use 7850-7899)
        }

        /// <summary>
        /// Find an available port in the pipeline server range.
        /// </summary>
        private int FindAvailablePort()
        {
            var (basePort, maxPort) = GetPortRange();

            for (int port = basePort; port <= maxPort; port++)
            {
                if (IsPortAvailable(port))
                {
                    return port;
                }
            }

            throw new InvalidOperationException($"No available ports in range {basePort}-{maxPort}");
        }

        /// <summary>
        /// Check if a specific port is available for binding.
        /// </summary>
        private bool IsPortAvailable(int port)
        {
            try
            {
                using (var listener = new HttpListener())
                {
                    StartHttpListenerWithFallback(listener, port);
                    listener.Stop();
                    return true;
                }
            }
            catch
            {
                return false;
            }
        }
    }
}
