# Runtime connection & setup

How to run the Pipeline server inside a built Player so a client can drive it the same way it drives the Editor. The runtime server only exists in **development builds**, so setup is mostly about building correctly.

## Configuring the runtime server

The runtime server is configured entirely through **Project Settings → Pipeline → Runtime**. No GameObject or component needs to be added to any scene — `RuntimePipelineBootstrap` creates the runtime driver automatically at boot (Player start, or entering Play Mode in the Editor) if settings exist and `enableInBuilds` is on.

Your settings are never written as a file under `Assets/` — they live in `ProjectSettings/Packages/com.unity.pipeline/RuntimePipelineConfig.json`, a normal, tracked part of `ProjectSettings/` (the same place Unity itself keeps most other project-wide settings). Merely viewing the Project Settings page, or reading via `get_runtime_pipeline_settings`, shows built-in defaults but never creates this file — it's written the first time you actually change a setting (through the page, or a confirmed `set_runtime_pipeline_settings`).

**Security warning:** whenever `enableInBuilds` is on, the settings page shows a persistent warning — a stronger error-styled one if Development Build is currently unchecked, since that means the *next* build would ship this (including remote code execution) as a release build.

### Settings

| Field | Default | Meaning |
|-------|---------|---------|
| `enableInBuilds` | `false` | Master switch. The server only starts when this is `true`. **Off by default for safety.** |
| `autoStart` | `true` | Start the server automatically when the Player boots (requires `enableInBuilds`). |
| `port` | `0` | Listen port. `0` = auto-assign from `7900`–`7949`. |
| `requestTimeoutMs` | `30000` | Per-request timeout. |
| `enableAuditLogging` | `true` | Log remote requests for auditing. |
| `maxWorkItemsPerFrame` | `10` | Dispatcher work items processed per frame. |

If `autoStart` is off, drive the server manually via `RuntimePipelineBootstrap.Instance` — the driver `RuntimeInitializeOnLoadMethod` already created at boot — and call `StartServer()`/`StopServer()` on it. `RuntimePipelineBootstrap.Bootstrap()` is idempotent (a second call returns the existing instance rather than creating a duplicate), so it's also safe to call directly if you're not sure whether bootstrap has run yet.

### Generated build-only assets

Each build generates two **transient** assets under `Assets/Settings/Pipeline/Resources/`, both deleted again once the build finishes:

- `RuntimePipelineConfig.asset` — your current settings, baked in so the Player can find them (the authored copy stays in `ProjectSettings/`, never here).
- `RuntimePipelineBuildInfo.asset` — the hot-reload allowlist: absolute paths to `Assets/` plus every loaded package's resolved location, captured at build time because a running Player can't resolve the project layout on its own. Read by `RuntimePipelineBootstrap` at boot; absent (e.g. Play Mode without ever having built) just means no hot-reload roots are allowed, not an error.

Neither should ever be committed — add this to your project's `.gitignore`:

```
/Assets/Settings/Pipeline/Resources/RuntimePipelineConfig.asset*
/Assets/Settings/Pipeline/Resources/RuntimePipelineBuildInfo.asset*
```

A build interrupted mid-way (crash, force-quit) can leave either behind; if you see them in `git status` after such an interruption, it's safe to delete them. The next build you run will also delete them itself, at the very start of preprocessing, before anything else — so a leftover from an interrupted build can never get packaged into a later Player, even one built with `enableInBuilds` off.

## Development-build gating

The runtime server, code evaluation, and hot reload are all gated behind a compile-time guard:

```csharp
#if UNITY_EDITOR || (UNITY_STANDALONE && DEBUG)
```

`DEBUG` is defined for **standalone Development Builds** only. In a non-development (release) build the compilation/eval/hot-reload code is compiled out entirely, so it cannot run — even if `enableInBuilds` is `true`. To use the runtime server in a Player you therefore need **both**:

1. A **Development Build** (defines `DEBUG`), built for a **standalone** target (Windows/macOS/Linux).
2. `enableInBuilds = true` in Project Settings → Pipeline → Runtime.

## Step-by-step: enable in a dev build

1. Open **Project Settings → Pipeline → Runtime**.
2. Tick **`enableInBuilds`**. Leave `autoStart` on and `port` at `0` (auto).
3. Open **File → Build Settings** and enable **Development Build** for a **Standalone** platform.
4. Build and run the Player.
5. On start, the bootstrap creates a `RuntimePipelineServer`, which binds the first free port in `7900`–`7949` and writes the runtime descriptor `.unity-pipeline-runtime-port` (with `port` and `evalToken`).
6. Connect a client using that port and token.

## Security

The runtime server applies the same protections as the editor server (see [Connectivity](connectivity.md)):

- It binds the **IPv4 loopback** (`http://127.0.0.1:<port>/`, plus the `localhost` hostname), so it is reachable only from the same machine and never exposed on a routable interface. Clients should connect to `127.0.0.1` explicitly — see [Loopback-only binding](connectivity.md#loopback-only-binding) for why `localhost` can resolve to the IPv6 loopback (`::1`), which Unity's Mono `HttpListener` cannot serve.
- Every request must present `Authorization: Bearer <evalToken>`; the token is generated at startup and published in the runtime descriptor.

Because the server exposes code evaluation and hot reload, only enable it in development/QA builds — never in a production build without additional safeguards.

## See also

- [Connectivity](connectivity.md) — ports, descriptor file, and auth in detail.
- [Hot reload](hot-reload.md) — applying live code changes to a running Player.
