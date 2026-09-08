# Connectivity

How a client reaches a running Unity instance: the loopback-only HTTP servers, their port ranges, the port descriptor file used for discovery, and the bearer-token authentication every request must carry.

The rest of this page covers the implementation. If you just want to connect, start with the two sections below.

## Connecting to a running Editor

Connections go through the `unity` CLI. Run `unity command` with no command name to connect to a Unity instance and list its available commands:

```bash
# Auto-discover a Unity instance from the current directory
unity command

# Connect to a specific project
unity command --project-path /path/to/your/unity/project
```

Once connected, run any command by name, e.g. `unity command editor_status`.

## Connecting to a running Player (game)

To target a running development Player instead of the Editor, use `--runtime` (by process name) or `--runtime-path` (by the location of the runtime port file). These options go **after** `command` and **before** the command name:

```bash
# By Player process/executable name
unity command --runtime MyGame.exe runtime_status

# By the folder/bundle where the runtime port file lives
unity command --runtime-path "C:\Builds\MyGame" runtime_status             # Windows: next to the .exe
unity command --runtime-path "/Users/me/Builds/MyGame.app" runtime_status  # macOS: the .app bundle
```

> The Runtime server only runs in a **development Player build** with the runtime manager enabled — see [Runtime connection & setup](runtime-setup.md).

## Finding the port in the Unity logs

The [port descriptor file](#port-descriptor-file) is the primary discovery channel, but the server also reports itself to the Unity log when it starts and stops — useful when you can't read the descriptor file directly. Look for `Pipeline`-prefixed lines.

- **Runtime (Player)** — logs its port **and** the descriptor file location on start, and a line on stop:

  ```
  Pipeline: Runtime server started successfully on port 7901
  Pipeline: Runtime descriptor written to /path/to/MyGame/.unity-pipeline-runtime-port
  Pipeline: Runtime server stopped
  ```

- **Editor** — logs its port when the server is (re)started from the **Pipeline ▸ Start Server** menu or re-opened by its watchdog (`Pipeline Server started on port 7801`). Its descriptor always lives at the fixed `Library/Pipeline/.unity-pipeline-port` path.

### Where the logs live

**Editor log** (`Editor.log`):

| OS | Path |
|----|------|
| Windows | `C:\Users\<user>\AppData\Local\Unity\Editor\Editor.log` |
| macOS | `~/Library/Logs/Unity/Editor.log` |

**Player log** (`Player.log`):

| OS | Path |
|----|------|
| Windows | `C:\Users\<user>\AppData\LocalLow\<company>\<product>\Player.log` |
| macOS | `~/Library/Logs/<company>/<product>/Player.log` |

(`<company>` and `<product>` are the project's **Company Name** and **Product Name** from Player Settings.)

## Loopback-only binding

Both the Editor and Runtime servers bind to the **IPv4 loopback** (`127.0.0.1`) — plus the `localhost` hostname for compatibility:

```csharp
if (Socket.OSSupportsIPv4)
    m_HttpListener.Prefixes.Add($"http://127.0.0.1:{m_Port}/");
m_HttpListener.Prefixes.Add($"http://localhost:{m_Port}/");
```

The server is never exposed on a routable interface — it is reachable only from the same machine.

Clients should connect to **`127.0.0.1`** explicitly rather than `localhost`. Unity's Mono `HttpListener` only reliably serves the IPv4 loopback: a request arriving over the IPv6 loopback (`::1`) is answered with `400` because Mono mis-parses the bracketed `[::1]` host. Since `localhost` resolves to `::1` or `127.0.0.1` non-deterministically (notably on Windows with Node's default DNS order), dialing `localhost` caused intermittent connection failures — dialing `127.0.0.1` avoids the IPv6 path entirely.

In addition, the server refuses any request that carries an `Origin` header (legitimate CLI/CI clients never send one), which blocks a browser page from reaching the local server and short-circuits CORS preflights. The Editor server can opt into the single exception: enabling **Allow Browser Clients** in the pipeline settings asset (`Window/Pipeline/Settings...`, applies on next start) admits `Origin: null`, the opaque origin a sandboxed browser frame reports, and answers that client's CORS preflight. An ordinary page sends its real origin and is still refused, and a sandboxed one still needs the bearer token.

## Port ranges

Each server type picks the first free port in its range (or you can pin one explicitly):

| Server | Production range | Test range |
|--------|------------------|------------|
| **Editor** | `7800`–`7849` | `7850`–`7899` |
| **Runtime** (Player) | `7900`–`7949` | `7950`–`7999` |

If no port in the range is free, startup throws (`No available ports in range …`).

## Port Descriptor File

When a server starts, it writes a small JSON **instance descriptor** so clients can discover it. This is the only discovery channel — there is no broadcast or registry.

| Server | Descriptor path |
|--------|-----------------|
| **Editor** | `<projectPath>/Library/Pipeline/.unity-pipeline-port` |
| **Runtime** | `<workingDirectory>/.unity-pipeline-runtime-port` |

The Editor descriptor lives under the git-ignored `Library/` folder. The file is created with permissions restricted to the current user (it carries the auth token). The server rewrites it on every heartbeat to refresh `lastHeartbeat`, and deletes it on shutdown.

The Runtime descriptor is written next to the Player, at `{Application.dataPath}/..`:

- **Windows / Linux** — beside the executable, e.g. `C:\Builds\MyGame\.unity-pipeline-runtime-port`.
- **macOS** — `Application.dataPath` is `<Game>.app/Contents`, so the descriptor lands at the **`.app` bundle root**: `MyGame.app/.unity-pipeline-runtime-port`. (The file is inside the `.app` bundle.) This is why `unity command --runtime-path` takes the `.app` bundle path on macOS — see [Runtime connection & setup](runtime-setup.md).

> Test servers do **not** write a descriptor (they override `WritesDescriptor => false`) — the test already knows its port, so it never clobbers the live server's file. See [Tests architecture](testing.md).

### Editor descriptor fields

```json
{
  "pid": 12345,
  "port": 7800,
  "projectPath": "/path/to/Project",
  "projectName": "Project",
  "unityVersion": "6000.x.y",
  "mode": "editor",
  "startedAt": "2026-06-25T10:00:00Z",
  "lastHeartbeat": "2026-06-25T10:05:00Z",
  "evalToken": "<base64 token>",
  "capabilities": ["exec.argv", "exec.commandLine"],
  "info": "This editor has not been opened in automated mode and therefore could get stuck on modal dialogs. Human intervention may be required."
}
```

| Field | Meaning |
|-------|---------|
| `pid` | Process id of the Unity Editor. |
| `port` | Port the server is listening on. |
| `projectPath` | Absolute path to the Unity project. |
| `projectName` | Project folder name. |
| `unityVersion` | Editor version string. |
| `mode` | `"editor"` or `"batchmode"`. |
| `startedAt` | When the instance started (UTC). |
| `lastHeartbeat` | Last heartbeat (UTC), refreshed on status calls. |
| `evalToken` | Bearer token for authenticating requests. |
| `capabilities` | Optional wire features this server understands (see [Capability negotiation](#capability-negotiation)). **An absent key means the server predates the field** — treat it as supporting none of them. |
| `info` | Corrective guidance for the client, omitted when there is nothing to say. Currently only set when the Editor is interactive and wasn't launched with `-automated`: a blocking modal dialog can stall the Editor's main-thread update loop (and therefore all pipeline command processing) until a human dismisses it. Read this before issuing commands — it's the one field every client sees, since reading the descriptor is a mandatory first step just to connect. |

### Runtime descriptor fields

The runtime descriptor shares `pid`, `port`, `unityVersion`, `startedAt`, `lastHeartbeat`, and `evalToken`, and adds:

| Field | Meaning |
|-------|---------|
| `platform` | Unity runtime platform (e.g. `WindowsPlayer`). |
| `buildGuid` | Unique build identifier (`Application.buildGUID`). |
| `workingDirectory` | Directory the Player is running from. |

The runtime descriptor also carries `capabilities`, with the same meaning and the same absent-key rule.

(The runtime descriptor carries `platform`/`buildGuid`/`workingDirectory` in place of the editor's `projectPath`/`projectName`/`mode`.)

## Authentication

Every request must authenticate with a bearer token:

```
Authorization: Bearer <evalToken>
```

- The server **generates the token at startup** (`SecurityTokenManager.GetOrCreateToken()` — 256 bits of CSPRNG output, base64-encoded). In the Editor the token is persisted in `SessionState`, so it **survives domain reloads** within an editor session (recompiles, entering play mode) — long-lived clients (MCP sessions, IDE integrations) keep working instead of getting `401` after every reload. It is regenerated only when the Editor **restarts**, or on an explicit rotation (`SecurityTokenManager.ClearCache()` / `RotateToken()`). Player builds use a per-process token.
- The token is published in the descriptor's `evalToken` field. The descriptor is **re-advertised with the live token on every heartbeat**, so the port file never advertises a token the server would reject (e.g. after a reload or rotation).
- The server validates the bearer token on **every** request (before routing) using a constant-time comparison. A missing or wrong token returns `401 Unauthorized`.

## Discovering and calling an instance

A client connects by:

1. Reading the descriptor file (editor: `Library/Pipeline/.unity-pipeline-port`).
2. Taking the `port` and `evalToken` from it.
3. Sending requests to `http://127.0.0.1:<port>/...` with `Authorization: Bearer <evalToken>`.

Endpoints exposed by the server include `/api/status`, `/api/editor_status`, `/api/dialog`, `/api/commands` (lists available commands), `/api/exec` (POST — runs a command), `/api/test-status`, and `/api/progress`.

### Command progress (`GET /api/progress`)

While a command is executing over `/api/exec`, clients can poll `GET /api/progress` for the
task's live progress — the `unity` CLI uses this to render terminal progress bars, mirroring
`EditorUtility.DisplayProgressBar`. The endpoint is served off the Editor main thread, so it
answers even while a long synchronous command has the Editor blocked. Response
(`pct` is 0–1; all `progress` fields optional; `progress` is omitted when nothing is reported):

```json
{
  "active": true,
  "progress": {
    "title": "Generating World",
    "info": "Processing 42/100",
    "current": 42,
    "total": 100,
    "pct": 0.42
  }
}
```

Progress sources, in order of precedence:

1. `CliProgress.Report(title, info, current, total, progress)` — explicit reporting from
   command code; thread-safe and works from a blocked main thread.
2. Running `UnityEditor.Progress` items, mirrored automatically.

### Detached jobs (`/api/exec` with `"job": true`, `/api/job`, `/api/job/cancel`)

A long command can outlive the client's HTTP timeout. Submitting it as a **detached job**
returns a job id immediately; the command runs in the background (still one at a time, in
arrival order) and the client polls for the result — reattaching at any point:

1. `POST /api/exec` with body `{"command": "…", "parameters": {…}, "job": true}` → the
   standard exec envelope, with the job handle as its `result`:
   `{"success": true, "result": {"jobId": "…", "state": "queued"}}` (lean by default —
   add `"verbose": true` to get the `command`/`executedAt` metadata back, see AUTHAPI-21).
2. `GET /api/job?id=<jobId>` → `{"jobId", "command", "state":
   "queued|running|completed|failed|canceled", "progress": {…}, "result", "error", …}`.
   `progress` mirrors `/api/progress` while the job runs; `result` is retained after
   completion (last 100 terminal jobs, 1 hour) so it can be fetched repeatedly.
3. `POST /api/job/cancel` with body `{"id": "<jobId>"}` — a queued job is canceled before it
   starts; a running job gets a cooperative cancellation flag that command or `eval` code can
   observe via `Unity.Pipeline.PipelineCancellation.ThrowIfCancellationRequested()` (arbitrary
   synchronous code cannot be aborted from outside).

At most 100 jobs may be queued or running at once (jobs execute strictly serially, so a deeper
backlog is pure queued work with no benefit) — submitting one beyond that returns `429`.

Jobs live in Editor memory: they do not survive domain reloads (script recompilation).

Editor code that already calls `EditorUtility.DisplayProgressBar` can switch to the drop-in
`CliEditorProgress.DisplayProgressBar` / `ClearProgressBar` wrappers to keep the Editor dialog
and gain CLI visibility. See [Creating commands](creating-commands.md).

### Readiness and the settle window

On a **cold project import** the editor server comes up (and its descriptor is written) while the Editor is still importing assets and compiling scripts, so the Editor is not yet able to service commands. Until the Editor is first seen idle after server start:

- `/api/status` reports `"status": "settling"` instead of `"ready"`. Wait for `ready` before issuing commands.
- `/api/exec` rejects **main-thread** commands with **HTTP 503** and a structured, retryable envelope — distinguishable from a genuine command failure. The gate applies before execution *and* before a detached job (`"job": true`) is created, so a job can't run into the half-ready Editor in the background either. The busy reply is a standard exec envelope and follows the request's reply-shape flags: lean (the default, shown here) drops the envelope metadata; `"verbose": true` restores the `command` echo and `executedAt`:

  ```json
  {
    "success": false,
    "error": "Server Busy",
    "errorDetails": "The Editor is still settling after startup (importing assets / compiling scripts), so main-thread commands are not serviceable yet. Retry shortly, or poll /api/status until it reports 'ready'.",
    "status": "busy",
    "busyReason": "settling",
    "retryable": true
  }
  ```

  `busyReason` is the specific cause, matching `/api/status`'s own `status` values for the same conditions (`"settling"`, `"blocked_by_dialog"`) — branch on it directly rather than inferring the cause from whether `dialogs` happens to be populated.

- Background commands (`recompile_status`, `package_status`, `console`, ...) and `editor_status` stay servable throughout, so progress remains observable.

The settle gate is one-way and scoped to the **editor session**: once the Editor has been idle once after startup, the server reports `ready` and the gate never arms again for that session — including for server instances recreated by domain reloads and for servers started while a mid-session compile/import happens to be in flight. Warm starts settle immediately; only the cold-import window gates.

### Modal dialogs and the dialog-busy gate

A native modal dialog (e.g. "Save changes before quitting?") or an `EditorWindow` modal blocks the Editor's main thread inside a nested OS message loop, so no main-thread command can run until it's dismissed. The server mirrors these dialogs from `UnityEditor.EditorDialogEvents`:

- `GET /api/dialog` reports the currently open dialog(s): `{"active": bool, "dialogs": [...]}`, each entry shaped `{id, source, title, message, level, buttons, openedAt, dismissedAt}`.
- `/api/status` reports `"status": "blocked_by_dialog"` (instead of `"ready"`) with a `"dialog"` key carrying that same shape for the first open dialog — so the cheapest, most commonly polled probe doesn't claim readiness while every main-thread command is actually gated.
- `/api/exec` rejects any **main-thread** command with **HTTP 503**, the same busy envelope shape as the settle gate's but with `"busyReason": "blocked_by_dialog"` and a `"dialogs"` array attached.

  **Exception: `editor_status`.** Unlike every other main-thread command, `editor_status` still returns a normal **200** while a dialog is open, served from a snapshot taken the instant before the dialog blocked the thread — nothing it reports (compiling, domain reload, play mode) can change while the thread that would update it is stuck. The response carries `"status": "blocked_by_dialog"` and the same `"dialog"` payload as `/api/status`, so a caller can still tell it apart from a genuinely idle Editor.

- A successful `/api/exec` response may carry `"dialogsDuringExecution"`: dialog(s) that opened and/or closed while the command was actually *executing* (not while it sat queued behind another command), so a caller learns about them without polling `/api/dialog` concurrently.

Coverage is mechanism-based: native message boxes and `EditorWindow` modals are covered; OS file/folder pickers and other dialog mechanisms are not — a caller still needs a fallback signal (e.g. a command timeout) for uncovered cases.

`/api/commands` accepts optional query parameters for discovery:

- `detail` — `full` (the **default**) returns the complete command metadata including `parameters` and the generated `schema`; `compact` returns a lightweight index per command (`name`, `description`, `tags`, `package`). The recommended discovery flow is to browse the compact index first, then request full detail only for the few commands you intend to invoke.
- `query` — case-insensitive substring match on a command's name, description, or any tag.
- `tag` — scope results to a tag subtree via segment-aware prefix match (`tag=assets` matches `assets` and `assets/import`, not `assetsx`).
- `group_by` — `flat` (the default) returns a `commands` array; `package` or `tag` return a `groups` array instead (`tag` is a nested tree mirroring the tag/subtag hierarchy; untagged commands land in a node with an empty tag).
- `sort` — `name` (the default) or `package` (originating package, with name as tiebreak).
- `order` — `asc` (the default) or `desc`. Applies to the chosen `sort`; sorting happens on the flat list before pagination and grouping.
- `offset` / `limit` — paginate the filtered, sorted list. Both apply to the flat list *before* grouping, so pages stay deterministic. `offset` skips that many matches (default `0`); `limit` caps how many are returned (default: no cap). A client has seen everything once `offset + count` reaches `total`.

Filters combine with AND; a filter that matches nothing returns an empty result, not an error. An invalid `detail`, `group_by`, `sort`, `order`, `offset`, or `limit` value is rejected with `400` naming the accepted values.

### `/api/commands` response shape

Every response carries the pagination counters plus a `server` block. The command payload is either a flat `commands` array or — under `group_by` — a `groups` array in its place:

| Field | Meaning |
|---|---|
| `commands` | The page of commands. Present only when `group_by=flat` (the default). |
| `groups` | Present *instead of* `commands` when `group_by=package` or `group_by=tag`. |
| `count` | How many commands this page actually returned. |
| `total` | How many commands matched the filters, **before** `offset`/`limit` were applied. |
| `offset` | Echo of the requested `offset` (`0` when not supplied). |
| `limit` | Echo of the requested `limit` (`null` when not supplied — no cap). |
| `server` | The responding server's `version`, `port`, and `startTime`. |

`GET /api/commands?detail=compact&tag=baking/lighting&limit=2` — `count` is the 2 returned here, while `total` is all 6 commands under `baking/lighting`, so the next page is `offset=2`:

```json
{
  "commands": [
    {
      "name": "bake_lighting",
      "description": "Trigger an async lightmap bake of the open scene(s) via Lightmapping.BakeAsync(). Returns immediately; poll lighting_bake_status until completed.",
      "tags": ["baking/lighting"],
      "package": "Unity.Pipeline.Editor"
    },
    {
      "name": "cancel_lighting_bake",
      "description": "Cancel an in-progress lighting bake (Lightmapping.Cancel()).",
      "tags": ["baking/lighting"],
      "package": "Unity.Pipeline.Editor"
    }
  ],
  "count": 2,
  "total": 6,
  "offset": 0,
  "limit": 2,
  "server": {
    "version": "0.0.1",
    "port": 54321,
    "startTime": "2026-07-29T09:14:22.113Z"
  }
}
```

Under `group_by=tag` the same envelope carries a nested `groups` tree instead. Two things to note. A node's `commands` holds only the commands tagged *exactly* at that node — so `baking`, whose commands all live in subtags, reports an empty array — while its `count` covers the node's whole **subtree**. And because pagination happens before grouping, group counts describe the returned page rather than every match: `GET /api/commands?detail=compact&tag=baking&group_by=tag&limit=2` groups only the 2 commands on this page, while `total` still reports all 17 matches under `baking`:

```json
{
  "groups": [
    {
      "tag": "baking",
      "count": 2,
      "commands": [],
      "children": [
        {
          "tag": "baking/lighting",
          "count": 1,
          "commands": [
            {
              "name": "bake_lighting",
              "description": "Trigger an async lightmap bake of the open scene(s) via Lightmapping.BakeAsync(). Returns immediately; poll lighting_bake_status until completed.",
              "tags": ["baking/lighting"],
              "package": "Unity.Pipeline.Editor"
            }
          ],
          "children": []
        },
        {
          "tag": "baking/navmesh",
          "count": 1,
          "commands": [
            {
              "name": "bake_navmesh",
              "description": "Trigger an async legacy NavMesh bake of the open scene(s) via UnityEditor.AI.NavMeshBuilder. Returns immediately; poll navmesh_bake_status until completed.",
              "tags": ["baking/navmesh"],
              "package": "Unity.Pipeline.Editor"
            }
          ],
          "children": []
        }
      ]
    }
  ],
  "count": 2,
  "total": 17,
  "offset": 0,
  "limit": 2,
  "server": {
    "version": "0.0.1",
    "port": 54321,
    "startTime": "2026-07-29T09:14:22.113Z"
  }
}
```

`group_by=package` uses the same envelope with flatter nodes — `{ "package": "Unity.Pipeline.Editor", "count": 2, "commands": [ ... ] }`, with no `children`.

## Executing a command (`POST /api/exec`)

A request carries the command in **exactly one** of three forms:

```jsonc
{"command": "reload_file", "parameters": {"path": "Assets/A.cs"}}  // structured
{"commandLine": "reload_file Assets/A.cs"}                          // server tokenizes, then binds
{"argv": ["eval", "return 2+2;"]}                                   // server binds directly
```

For the two raw forms the **first token is the command name**, and the server binds the rest
against the command's declared parameters. This is what lets a client execute a command in a
single request without knowing its schema — the structured form needs `GET /api/commands` first,
the raw forms need nothing.

**Prefer `argv` whenever you already have split tokens.** On POSIX a CLI never holds the original
command *string* — the shell already split and unquoted argv — so re-quoting those tokens for the
server to re-split is a lossy round-trip across two independently-versioned quoting dialects. Use
`commandLine` only when you genuinely hold raw text (a chat message, an MCP argument, a pasted
line).

Supplying more than one form is an error, and so is sending `parameters` alongside `commandLine`
or `argv`. The latter is **rejected rather than ignored**: silently dropping a payload makes "you
sent it and we discarded it" indistinguishable from "you never sent it".

`argv` and `commandLine` are exec-only. `batch` operations keep `command` + `params`.

### Tokenizer dialect (`commandLine` only)

This is a **versioned contract**, not an implementation detail. It is the pipeline's own dialect —
identical on every OS, and deliberately *not* the caller's shell — so the same string tokenizes
the same way from cmd.exe, bash, an MCP client, or a chat box.

| Rule | Example | Tokens |
|---|---|---|
| Whitespace separates tokens; runs collapse | `a   b` | `a`, `b` |
| `"…"` groups, `\` escapes the next character | `"a b"`, `"a\"b"` | `a b`, `a"b` |
| `'…'` groups, with **no** escapes at all | `'a\b'` | `a\b` |
| `\` escapes outside quotes | `a\ b` | `a b` |
| Adjacent spans concatenate | `a"b c"d` | `ab cd` |
| `""` yields one **empty** token | `--message ""` | `--message`, *(empty)* |
| Unbalanced quote or dangling `\` | `"oops` | **error** |

There is **no expansion of any kind** — no `$VAR`, globbing, `~`, comments, or operators. `&&`,
`|` and `>` are ordinary characters.

Two consequences worth stating explicitly:

- **Single quotes are load-bearing.** They pass double quotes through untouched, which is the only
  practical way to send `eval 'obj.name = "x";'` or `batch --operations '[{…}]'`.
- **Windows `CommandLineToArgvW` rules do not apply.** `"a""b"` is `ab` here, not `a"b`. The usual
  argument for Windows rules — that `\` is the path separator — does not hold, because every path
  this API accepts is a forward-slashed Unity asset path.

### Argument grammar (both raw forms)

- `--key value`, `--key=value`, or a bare `--key` (which means `true`, and is an error on a
  non-boolean parameter).
- A follower is consumed as the value **unless it starts with `--`**, so `--path -foo` works.
- `--` ends flag parsing; everything after it is a positional value, exempt from the `-foo` and
  `key=value` checks.
- Positionals fill **required parameters in declaration order, then optional parameters in
  declaration order**. A slot already filled by an explicit flag is skipped, not overwritten.
- Not supported, by design: short flags, `-abc` bundling, `--no-x`, repeated-flag-as-array,
  flag-name abbreviation, and `@argfile`.

> **Declaration order is wire API for raw clients.** Reordering a command's parameters — or
> flipping one between required and optional — silently changes what a positional binds to. Append
> new optional parameters at the end. See [Creating commands](creating-commands.md).

### Successful replies echo the bound parameters

```json
{"success": true, "result": "pong", "parameters": {"message": "hi"}}
```

`parameters` is present **only** for `argv`/`commandLine` requests, so structured replies are
unchanged. It reports exactly what the binder stored, so a client that no longer binds locally can
still show the user what the server understood.

### Argument errors

```json
{
  "success": false,
  "error": "Invalid Command Arguments",
  "errorCode": "INVALID_COMMAND_ARGS",
  "errorDetails": "log_editor has no parameter --mesage. Did you mean --message?",
  "argProblems": [{"kind": "unknownName", "name": "mesage", "suggestion": "message"}],
  "commandSchema": {"name": "log_editor", "parameters": [{"name": "message", "type": "String", "required": true}]}
}
```

**`errorCode` is the discriminator, not the HTTP status.** Every `/api/exec` failure is already
`400`, so status alone cannot separate "your arguments are wrong" from "the command threw".

| `errorCode` | Meaning |
|---|---|
| `INVALID_COMMAND_ARGS` | The command resolved, but its arguments could not be bound. Nothing executed. |
| *(absent)* | Any other failure — an unknown command, a validation error, or the command itself throwing. |

A malformed *request shape* — two command forms at once, `parameters` beside `argv`, an empty
`argv`, a `commandLine` that does not tokenize (unbalanced quote, dangling backslash), or a body
using none of the three forms — is a `400` with `error: "Invalid Request"` and **no** `errorCode`,
because no command was ever resolved. Such a reply carries neither `argProblems` nor
`commandSchema`: there is no command to describe.

`argProblems` is machine-readable and **accumulated**: every defect is reported, not just the
first, so a user fixing a command line sees all of its problems at once. It is deliberately not
pre-rendered English — the `unity` CLI renders *and localizes* these itself. Use `errorDetails` as
the English fallback, and for any `kind` your client does not recognize.

| `kind` | Meaning |
|---|---|
| `emptyName` | A flag with no name: `--=` or `--=value`. A bare `--` is the end-of-flags separator, never this. |
| `emptyValue` | `--key=` with nothing after it, or a bare `--key` on a non-boolean parameter. |
| `singleDash` | A `-x` token in positional position. |
| `duplicate` | The same `--key` supplied more than once. |
| `unknownName` | No such parameter. Carries `suggestion` when a close match exists. |
| `bareAssignment` | A `key=value` positional whose left side names *the parameter that positional fills* — a misremembered `--key value`. A left side naming some other parameter is a value: `eval "timeout=5;"` binds as `code`. |
| `excessPositional` | More positionals than there are slots left for them. `capacity` is the slots still free after the flags took theirs and `given` is the positionals supplied; `token` is the first one with no slot. |
| `positionalConflict` | A positional whose own slot a flag already filled. Carries `name` (that parameter) and `token`. |
| `typeMismatch` | The declared type cannot accept the token; carries `expectedType`. |

`commandSchema` is the command's catalog entry, shaped like a `/api/commands` `commands[]`
element (without the generated `schema`). It lets a client print usage without a separate schema
fetch.

### Capability negotiation

Servers advertise optional wire features in `capabilities`, both on `GET /api/status` and in the
**port descriptor**:

```json
"capabilities": ["exec.argv", "exec.commandLine"]
```

The descriptor is the carrier that matters: clients already read it locally to obtain `evalToken`,
so negotiation costs **zero extra requests**.

The rule for clients:

- Token present → send that form.
- Descriptor present but **no `capabilities` key at all** → the server predates this feature. Do
  not send raw forms; tell the user to update the package.
- Never sniff first. An older server receiving `{"argv":[…]}` fails deserialization (its `command`
  field is required) and answers `400 {"error":"Invalid JSON"}` — which a client would otherwise
  surface as a generic internal failure, making version skew look like a client bug.

Because the descriptor file is writable by anything running as the user, treat a capability claim
as a hint rather than a guarantee: a `400` carrying `error: "Invalid JSON"` or `"Invalid Request"`
with **no** `errorCode`, in response to a request that used `argv`, means the same thing as an
absent key. Degrade, don't crash.

## See also

- [Runtime connection & setup](runtime-setup.md) — enabling the server in a Player build.
- [Creating commands](creating-commands.md) — authoring the commands clients call.
