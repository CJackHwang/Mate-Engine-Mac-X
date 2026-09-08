# Script commands

Commands for creating C# script files, attaching MonoBehaviours to GameObjects, reading/writing serialized fields, and running compiled project entry points in memory. Writing a `.cs` file does not make its type available — Unity must import and compile it (a domain reload) first. The flow is: `create_script` → `recompile` → poll `recompile_status` (until completed/up_to_date) → `attach_script`. For bulk construction that does **not** need a new persistent project type, skip the recompile entirely: [`run_script`](#run_script) compiles a file in memory and runs it with no asset import and no domain reload.

### `create_script`
Create a new C# script (default base class MonoBehaviour) from a template under the authoring root. NOTE: the type does not exist until a recompile completes — to attach it, call recompile, poll recompile_status, then attach_script.

| Parameter | Required | Default | Description |
|-----------|----------|---------|-------------|
| `name` | yes | – | Class/file name without extension, e.g. PlayerController. Must be a valid C# identifier. |
| `path` | no | `–` | Folder (relative to the authoring root; the Assets/ prefix is optional) to write the .cs into. Defaults to the authoring root. |
| `namespace` | no | `–` | Optional namespace to wrap the class in. Omit for the global namespace. |
| `base_class` | no | `MonoBehaviour` | Base class to derive from. Defaults to MonoBehaviour. |
| `overwrite` | no | `false` | Overwrite the file if it already exists. Defaults to false (an existing file is an error). |

**Returns:** `AuthoringResult`
**Notes:** Does not trigger a recompile; the created type is not usable until a recompile completes (poll `recompile_status`).

### `attach_script`
Add a MonoBehaviour to a GameObject by its (compiled) type name OR by its script asset path. Provide exactly one of 'type' or 'script'. If the type isn't compiled yet, returns a recoverable error: recompile, poll recompile_status, then retry.

| Parameter | Required | Default | Description |
|-----------|----------|---------|-------------|
| `target` | yes | – | Reference to the GameObject to add the component to (globalId/path/guid/instanceId/hierarchyPath). |
| `type` | no | `–` | Component type name to add, e.g. PlayerController or Game.Player.PlayerController. Must already be compiled. Mutually exclusive with 'script'. |
| `script` | no | `–` | Script asset path, e.g. 'Assets/Pool/Scripts/CueShooter.cs'. The backing class is resolved via MonoScript.GetClass(), so the class name may differ from the filename. Mutually exclusive with 'type'. |

**Returns:** `AuthoringResult`
**Notes:** Undo-able. Provide exactly one of `type` or `script`. A not-yet-compiled type returns a recoverable error (recompile, poll `recompile_status`, then retry).

### `set_serialized_field`
Set a serialized field on a component/asset. Supports primitives, enums, Vector/Color/Rect/Bounds, object references (value = an ObjectRef: asset by guid/fileId/path or scene object by instanceId/hierarchyPath), and array elements via 'name.Array.data[i]' (or 'name.Array.size' to resize).

| Parameter | Required | Default | Description |
|-----------|----------|---------|-------------|
| `target` | yes | – | Reference to the component or asset to modify (globalId/path/guid/instanceId/hierarchyPath). May be a GameObject when 'component' is given. |
| `field` | yes | – | SerializedProperty path, e.g. 'speed', 'settings.speed', or 'waypoints.Array.data[0]'. |
| `value` | yes | – | JSON value to assign. For object references pass an ObjectRef object (or null to clear). For enums pass the value name. |
| `component` | no | `–` | Component type name on the target GameObject (e.g. 'Rigidbody'). Use when 'target' is a GameObject; omit when 'target' is already a component handle. |

**Returns:** `AuthoringResult`
**Notes:** Undo-able.

### `get_serialized_fields`
Read serialized fields of a component/asset. Returns each top-level field's name, type and value (object references are returned as re-usable handles). Pass 'field' to read a single SerializedProperty path.

| Parameter | Required | Default | Description |
|-----------|----------|---------|-------------|
| `target` | yes | – | Reference to the component or asset to read (globalId/path/guid/instanceId/hierarchyPath). May be a GameObject when 'component' is given. |
| `field` | no | `–` | Optional single SerializedProperty path to read (e.g. 'speed' or 'items.Array.data[0]'). Omit to read all top-level fields. |
| `component` | no | `–` | Component type name on the target GameObject (e.g. 'Rigidbody'). Use when 'target' is a GameObject; omit when 'target' is already a component handle. |

**Returns:** `object`

### `run_script`
Compile a single project `.cs` file in memory (no domain reload, no code carried through the
protocol) and execute a named `static` entry point. This is the **builder-pattern** path for bulk
construction — see the note below. Editor command; still arbitrary code execution, so it is grouped
under the same `scripts/eval` tag as `eval` and is disabled wherever the eval family is disabled.

| Parameter | Required | Default | Description |
|-----------|----------|---------|-------------|
| `file` | yes | `–` | Path to a `.cs` file. Relative paths resolve against the **project root** (the parent of `Assets/`), not the process working directory. May live **outside `Assets/`** (e.g. `AgentScripts/`) so writing it never triggers an asset import or domain reload. |
| `entry` | no | `–` | Entry point as `Namespace.Type.Method` (or `Type.Method`, or a bare `Method`). Nested types may be written with dots (`Outer.Inner.Method`). Default: the single public static method if unambiguous, else a method named `Main`. Static, non-generic methods only in v1. Ephemeral mode only — rejected with `mode=hotpatch`. |
| `args` | no | `–` | JSON array of arguments, coerced to the entry's parameter types: primitives, enum names, `string[]`, and a string/object handle (`ObjectRef`) for `UnityEngine.Object` parameters. JSON `null` is only valid for reference-type and nullable parameters (never a silent `default(T)`). Ephemeral mode only — rejected with `mode=hotpatch`. |
| `mode` | no | `ephemeral` | `ephemeral`: compile to an in-memory assembly, run the entry, discard. `hotpatch`: apply in-place `[HotReload]` method replacements to already-loaded types (delegates to `reload_file`). |
| `references` | no | `–` | Extra assembly name prefixes to reference. In the Editor all loaded assemblies are already referenced, so this is effectively a no-op there. Ephemeral mode only; **not applied** by hotpatch (the compile is `reload_file`'s). |
| `defines` | no | `–` | Extra scripting define symbols, **appended to the project's active editor defines** (`EditorUserBuildSettings.activeScriptCompilationDefines`) — so `UNITY_EDITOR`, version and platform symbols are already set and sources behave like project code; your defines extend that set rather than replace it. Ephemeral mode only; **not applied** by hotpatch. |
| `pdb` | no | `false` | Emit a portable PDB mapped to the source file so breakpoints bind. Ephemeral runs always emit source-mapped symbols so exception stack traces carry `file:line`. Compiles unoptimized. |
| `timeout_ms` | no | `30000` | Timeout in milliseconds (governs the main-thread wait budget end-to-end, like `eval`). |
| `dry_run` | no | `false` | Compile only: return diagnostics; the emitted assembly is **not loaded** into the domain and nothing executes (the response carries no `assemblyName`). Rejected with `mode=hotpatch` — a hotpatch "dry run" would still apply live method replacements. |

**Returns:** `RunScriptResponse` — `{ result, diagnostics, compileMs, executeMs, assemblyName }` on top
of the standard envelope. The result carries no echo of the source (path + diagnostics only). Compile
errors return full Roslyn diagnostics (with line/column) and execute nothing; runtime exceptions
surface as structured errors with `file:line` mapped to the source file.
**Notes:** `MainThreadRequired = true`. Editor command.

- Executing runs always compile **unoptimized (Debug codegen)** so the emitted symbols keep full
  sequence points for `file:line` mapping — expect the generated code to run somewhat slower than
  regular project assemblies.
- **Async entry points** (`async Task` / `Task<T>`) are awaited to completion; `Task<T>.Result`
  becomes the command result and exceptions in the async work surface as runtime errors. The await
  is **asynchronous** — the Editor main thread keeps pumping, so awaits that resume on Unity's
  main-thread context (`Task.Yield()`, `Task.Delay()`, Awaitable, …) work naturally; no
  `ConfigureAwait(false)` contortions are required. The wait is bounded by what remains of
  `timeout_ms`; on expiry the command returns a `Timeout` error and the task keeps running
  detached (its effects may still land), like an abandoned `eval`.
- **hotpatch specifics:** `entry`, `args` and `dry_run` are rejected (Bad Request); `references` and
  `defines` are not applied. Path resolution follows `reload_file`, which probes under `Assets/`
  for bare filenames (unlike ephemeral's project-root resolution). Timing: `executeMs` carries the
  whole reload duration (compile + apply, not split by `reload_file`); `compileMs` is `0`. Advisory
  reload diagnostics are surfaced as severity-`warning` entries on success.
- Under Unity's Mono runtime the in-memory assembly emitted by an executing `ephemeral` run is
  **not** collectible/unloadable, so each run retains one small assembly (the same behavior as
  `eval`); prefer a single builder entry over many tiny runs. `dry_run` loads nothing.

#### The builder pattern (bulk construction)

For bulk construction — creating many objects, wiring private fields, generating content — put the
logic in a **versioned project script** and invoke it with `run_script`. Write the file with
`write_text_file` (ideally under a non-`Assets/` folder such as `AgentScripts/` so the write triggers
no asset import), then call `run_script --file AgentScripts/Build.cs --entry Build.All`. Iterating the
script and re-running it costs an in-memory compile (< ~2s), **not** a 15–20s domain reload.

The anti-patterns this replaces: carrying construction code as escaped C# strings through `eval`
(verbose, unreviewable, shell-escaping hazards), and editing a builder under `Assets/` in a loop
(each edit pays a full domain reload). Hold the line: code goes in files on disk via `run_script`;
`eval` stays for genuinely ad-hoc one-liners.

See [Creating commands](../creating-commands.md) and [Connectivity](../connectivity.md).
