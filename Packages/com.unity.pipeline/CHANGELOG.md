# Changelog

All notable changes to this package will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/)
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [0.6.0-exp.1] - 2026-08-31

### Fixed
- [UUM-149991] AutoStaticsCleanup: Fixed usage-analysis findings.
- [UUM-149991] Fixed `CommandRegistry` staying on reflection-based command discovery after exiting Play Mode without a domain reload; `PipelineServerStartup` now restores TypeCache discovery on `EnteredEditMode`.
- [UUM-149977] Removed the "Editor is not in automated mode" console warning. Now surfaced as `info` on the instance descriptor instead.
- [AUTHAPI-36] Fixed `TestResultCollector.RunFinished` throwing `InvalidOperationException` (double-complete) when a run had already been completed by `SetError`/`Cancel` (e.g. a timed-out or errored run): a late `RunFinished` is now a no-op and the original error/cancellation is preserved.

### Added
- [AUTHAPI-68] `POST /api/exec` accepts an unparsed command line as `commandLine` (a string the server tokenizes) or `argv` (pre-split tokens), binding positional and `--flag` arguments against the command's declared parameters server-side. Exactly one of `command`, `commandLine` or `argv` may be present, and `parameters` cannot accompany the raw forms. Argument failures return `400` with `errorCode: "INVALID_COMMAND_ARGS"`, a machine-readable `argProblems` array, and the command's `commandSchema`; successful raw-form replies echo the bound `parameters`. Servers advertise `exec.argv` / `exec.commandLine` in `capabilities` on `/api/status` and in the port descriptor. `command` is no longer a JSON-required property, so a body carrying none of the three forms is now rejected by request validation as `400 {"error":"Invalid Request","errorDetails":"Command name is required"}` instead of by deserialization as `400 {"error":"Invalid JSON"}`.
- [AUTHAPI-64] Added a new endpoint reporting currently-open modal dialogs, and command execution now reports any dialogs shown while it ran. Main-thread commands are rejected with a busy status while a dialog is open, and status polling reports that a dialog is blocking it. Requires a recent enough trunk build.
- [AUTHAPI-29] Instrument `eval`/`eval_file` usage: each call logs a JSONL record (timing, success, statement/expression shape, and an API fingerprint of top-level member accesses) to `<project>/Library/Pipeline/eval-usage.jsonl`, editor-only and local-first — raw source isn't stored unless `Store Eval Source` is enabled. Toggle via `Eval Telemetry Enabled`.
- [AUTHAPI-29] New `report_evals` command: ranks logged eval fingerprints by frequency and cross-references the command catalog to surface patterns not yet covered by an existing command.
- [AUTHAPI-49] Editor analytics: `Pipeline_SessionStarted` on the first command a client executes, `Pipeline_CommandExecuted` per command executed, and `Pipeline_SessionStopped` when the Editor quits. `isEvalWithExistingCommandAvailable` reuses the AUTHAPI-29 eval fingerprints and coverage map, so it agrees with `report_evals` by construction. Only commands shipped in the package report their name and tags; a project-declared command reports `<customUserCommand>` and no tags. See `Documentation~/analytics.md`.
- [UUM-149580] `runtime_status` now reports `pipelineDriver` (isServerRunning, actualPort, and the enableInBuilds/requestTimeoutMs/enableAuditLogging/autoStart/maxWorkItemsPerFrame values actually governing the running driver, not just what Project Settings currently shows) — `null` if no driver was ever bootstrapped.
- [UUM-149580] New `get_runtime_pipeline_settings`/`set_runtime_pipeline_settings` commands: structured, `confirm`/`dry_run`-gated read/write access to Pipeline Runtime settings (Project Settings > Pipeline > Runtime), matching the existing Audio/Graphics/Input/Physics/Player/Quality/Time settings command family. Refused while in Play Mode.
- [AUTHAPI-27] New `batch` command: run up to 200 ordered operations in one request, with later ops referencing earlier results. Transactional by default (single Undo step, rolled back on failure); supports `on_error`, `dry_run`, `time_budget_ms`, and detached `job` execution. See `Documentation~/commands/batch.md` for details.
- [AUTHAPI-26] New `run_script` command: compile a single C# file in memory (no domain reload) and execute a named static entry point. Supports `ephemeral` (compile→run→discard) and `hotpatch` modes, JSON-coerced args, custom references/defines, and `dry_run` compilation. Returns `{ result, diagnostics, compileMs, executeMs, assemblyName }`; runtime errors include source `file:line`.
- [AUTHAPI-21] Leaner `/api/exec` responses by default: compact JSON, no null envelope keys, no always-on metadata — a minimal success is just `{"success":true,"result":...}`. Use `"verbose": true` to restore the full envelope, or `"omitNulls": true` to also drop nulls inside `result`. Added `format="value"` to `get_serialized_fields` to return raw values instead of a full descriptor.
- [AUTHAPI-21] New `warnings` array on responses for non-fatal corrective guidance (e.g. an ignored/invalid query parameter).
- [AUTHAPI-21] `/api/job`, `/api/job/cancel`, and `/api/progress` now include null keys explicitly; pass `omit_nulls=true` to drop them.
- [AUTHAPI-21] `POST /api/exec` now rejects a literal JSON `null` body with a structured 400 instead of an internal error.
- Add an IL interpreter (IlInterpreter) that works in IL2CPP players and the editor. Opt in with the `reload_file_editor_interpreter` command or by pushing to a connected device with `reload_file_player_interpreter`; the hot-reload watch always uses it. A shipped Roslyn analyzer (MS002–MS023) marks the supported C# subset in `[HotReload]` method bodies.
- Add an `[OnHotReload]` attribute to react to code being hot-reloaded.
- Add a `.cs` change watcher, enabled from the Pipeline settings, that hot-reloads changed code without a domain reload — in editor play mode, or in a player connected via the profiler connection.

### Changed
- [AUTHAPI-49] `BasePipelineServer.OnTransactionProcessed(requestJson, responseJson)` is replaced by `OnCommandDone(in CommandExecutionInfo)`, which also carries the command that ran, whether it succeeded, and how long it took. A detached job now reports when it actually completes, not only when its job handle was returned.
- [UUM-149580] Reorganized the Pipeline Runtime settings page (Project Settings > Pipeline > Runtime) into "Server" and "Runtime Behavior" groups, moved the status/security HelpBox to the top, and added an always-visible feature description when there's nothing to warn about. All fields except Max Work Items Per Frame are now read-only while in Play Mode, since edits to them would silently not apply until the next Play session or build; Max Work Items Per Frame stays live-editable — `RuntimePipelineDriver` polls it fresh every frame (cheaply, gated on the settings file's timestamp), matching the pre-redesign component's behavior for that field.
- [UUM-149580] Every field except Enable In Builds itself is now greyed out on the Pipeline Runtime settings page while Enable In Builds is off, instead of staying editable for a server that will not run.
- `reload_file` result contract, stated: a reload that compiles but applies NO methods now fails with the per-method reasons (previously a silent success); a file whose `[HotReload]` methods all match the compiled baseline reports an explicit "up to date" success — naming any stale overrides it reverted — instead of "hot reload successful with 0 methods".
- [AUTHAPI-26] A non-positive `timeout`/`timeout_ms` on `eval`, `eval_file`, or `run_script` now returns a clear `400 Bad Request` instead of an opaque dispatcher timeout.
- Reformatted CHANGELOG.md to more accurately track [Keep a Changelog](https://keepachangelog.com/en/1.1.0/)
- [AUTHAPI-59] Test fixtures and test-only helper classes under `Tests/` are now `internal` instead of `public` — they were never part of the package's public API surface.
- [AUTHAPI-59] Command handler classes and their result/DTO types under `Editor/Commands/` and `Runtime/Commands/` are now `internal` instead of `public`. Command registration and dispatch work via reflection regardless of accessibility (see `Documentation~/creating-commands.md`), so this was never functionally required.
- [AUTHAPI-59] `EditorPipelineManager`, `EditorPipelineManagerEditor`, `PipelineServerStartup`, `EditorPipelineServer`, `PipelineRuntimeBuildProcessor`, `HotReloadInPlaceILPostProcessor`, and `SecurityTokenManager` are now `internal` instead of `public`. `[CustomEditor]`, `[InitializeOnLoad]`, the build pipeline, and `ILPostProcessor` discovery all work via reflection regardless of accessibility, and none of these types were referenced outside the package.
- [AUTHAPI-59] `RuntimePipelineConfigEditor`, `RuntimePipelineSettingsProvider`, `PushEnvelope`, `PushVerifier`, `PushVerifyStatus`, `RotatingFileBackup`, and the `Unity.Pipeline.Telemetry` eval-usage helper types are now `internal` instead of `public` — same rationale: `[CustomEditor]`/`[SettingsProvider]` discovery is reflection-based, and none of these are referenced outside the package.

### Fixed
- [UUM-149580] The Pipeline server no longer permanently enables Player Settings "Run In Background" on start; the runtime driver now saves and restores the project's original value around start/stop.
- [UUM-149580] Transient `RuntimePipelineConfig`/`RuntimePipelineBuildInfo` assets left behind by an interrupted build are now purged at the start of every build, so a disabled build can no longer ship the runtime Pipeline server.
- [UUM-149580] Reading Runtime settings (Project Settings page, `get_runtime_pipeline_settings`, or a refused/dry-run `set_runtime_pipeline_settings`) no longer writes the settings file.
- [UUM-149580] `set_runtime_pipeline_settings` now rejects a `port`, `requestTimeoutMs`, or `maxWorkItemsPerFrame` outside the same bounds the Project Settings page enforces, instead of silently persisting an invalid value (a `maxWorkItemsPerFrame` of 0 permanently starved the dispatcher's work queue).

## [0.5.0-exp.1] - 2026-08-10

### Added
- [CLI-488] New `GET /api/progress` endpoint reporting the currently executing command's progress, served off the main thread so it responds even during a blocking command. Command authors report via `CliProgress.Report(...)` or `CliEditorProgress.DisplayProgressBar`; `UnityEditor.Progress` items are mirrored automatically. Powers live progress bars in the `unity` CLI. (with CLI-335)
- [CLI-335] Detached command jobs: `POST /api/exec` with `"job": true` runs in the background and returns a job id immediately; poll `GET /api/job?id=…` for state/progress/result. `POST /api/job/cancel` cancels a queued job or requests cooperative cancellation of a running one via the new `PipelineCancellation` API. Results are retained for 1 hour / last 100 jobs.
- [CLI-488] New `audit`/`audit_status` commands: run a Project Auditor static-analysis scan and poll it, producing an issue CSV. Works via reflection so the package still runs without Project Auditor installed; reports `unavailable` with install guidance when analysis rules are missing.
- [AUTHAPI-35] Report `settling` on `/api/status` and reject main-thread commands with `503 Server Busy` while the Editor is still importing/compiling after a cold start. Background commands (`recompile_status`, `package_status`, `console`, `editor_status`) stay servable throughout.

### Changed
- [CLI-335] Raised the `eval` timeout cap from 30 seconds to 24 hours.
- [CLI-335] The HTTP server now processes requests concurrently — `/api/status` and `/api/progress` answer immediately even during a long-running command. Command execution itself remains strictly serialized.
- [CLI-335] Hardening: a command's own timeout (not a fixed 60s) governs `/api/exec`'s wait; `editor_status`/`test_status` queue behind the exec gate; progress/job state is scoped per server instance; detached jobs are capped at 100 concurrent (further submissions get `429`).

### Fixed
- Fixed `executedAt` returning a zero-value instead of the actual execution time for `eval`/`eval_file`, `hot-reload`, and `run_tests`/`test_status`.
- [UUM-148802] Fixed `get_console_logs`/`clear_console`/`console` losing console capture after exiting Play Mode by re-subscribing to Unity's log callback on every play-mode transition.
- [UUM-148605] `set_autotick`'s enabled/interval state now persists across domain reloads via SessionState. Added a `persist` flag (default `true`) to opt out.
- [UUM-149016] Fixed `InvalidOperationException`s after repeated `run_tests` runs caused by a stale `TestResultCollector` still registered with Unity's TestRunnerApi.

## [0.4.0-exp.1] - 2026-07-23
- Persist the pipeline server auth token across editor domain reloads so long-lived clients (MCP/IDE) no longer get `401` after a recompile. (CLI-412)
- `capture_game_view` gains a `source` parameter: `camera` (default, unchanged) or `screen`. `source=screen` captures the composited game view backbuffer so Screen Space - Overlay UI (HUDs, menus) is included — the camera path never sees overlay canvases. Screen capture requires Play Mode; in Edit Mode it returns a clear error. (AUTHAPI-10)
- `capture_game_view`/`capture_scene_view` with `save_path` now return a path-only result (no base64) so agent tool results stay small; pass `include_inline_image=true` for the old behavior. Add `max_resolution` to cap the inline image size. (AUTHAPI-8)
- Object-reference string handles now accept authoring-root-relative asset paths (e.g. `Materials/Floor.mat`, not just `Assets/Materials/Floor.mat`): a relative string with a file extension is treated as an asset path and normalized under the authoring root, and a failed lookup now reports every strategy tried instead of a misleading hierarchy-path-only error. (AUTHAPI-9)

## [0.3.1-exp.1] - 2026-07-16
- Update docs

## [0.3.0-exp.1] - 2026-07-13

- Improve security by ensuring token usage and enforcing read control on the token.
- Fix all upm-pvp warnings.
- Fix Samples installation.
- All server works if the App is minimized (in the RunInBackground).
- Rework all docs.
- Improve connectivity regaridng IPv4 vs IPv6 support.
- Add `eval_file` command: evaluate C# code read from a `.cs` file on disk, as a file-based alternative to `eval` (which takes inline `code`). Both commands run the source through the same evaluation path.
- Add a large set of Editor automation commands for agentic content-pipeline control:
  - **Assets & files:** `create_asset`, `import_asset`, `move_asset`, `copy_asset`, `rename_asset`, `delete_asset`, `find_assets`, `create_folder`, `get_import_settings`, `set_import_settings`, `read_text_file`, `write_text_file`.
  - **Scenes:** `create_scene`, `open_scene`, `save_scene`, `save_all`, `list_open_scenes`, `set_active_scene`, `get_scene_hierarchy`, `add_scene_to_build`, `remove_scene_from_build`.
  - **GameObjects & components:** `create_gameobject`, `create_gameobjects`, `delete_gameobject`, `find_gameobjects`, `rename_gameobject`, `set_parent`, `set_transform`, `set_active`, `set_tag`, `set_layer`, `add_component`, `remove_component`, `get_component_properties`, `set_component_properties`.
  - **Prefabs:** `create_prefab`, `create_prefab_variant`, `instantiate_prefab`, `apply_prefab_overrides`, `revert_prefab_overrides`, `unpack_prefab`, `save_prefab_contents`.
  - **Scripts & serialized fields:** `create_script`, `attach_script`, `get_serialized_fields`, `set_serialized_field`.
  - **Selection & search:** `get_selection`, `set_selection`, `search`.
  - **Capture & screenshots:** `screenshot`, `capture_game_view`, `capture_scene_view`, `capture_editor_element`, `capture_runtime_element`.
  - **Build:** `build`, `build_status`.
  - **Console & diagnostics:** `console`, `clear_console`, `get_console_logs`, `get_performance_stats`.
  - **Editor menus & authoring root:** `menu`, `get_authoring_root`, `set_authoring_root`.
  - **Materials & shaders:** `get_material_properties`, `set_material_properties`, `get_shader_properties`, `list_shaders`.
  - **Animation & Animator:** `create_animation_clip`, `get_animation_clip`, `set_animation_curve`, `remove_animation_curve`, `create_animator_controller`, `get_animator_controller`, `add_animator_layer`, `add_animator_parameter`, `add_animator_state`, `add_animator_transition`.
  - **Timeline:** `create_timeline`, `get_timeline`, `add_timeline_track`, `add_timeline_clip`.
  - **Lighting:** `bake_lighting`, `cancel_lighting_bake`, `lighting_bake_status`, `clear_baked_lighting`, `get_lighting_settings`, `set_lighting_settings`.
  - **NavMesh:** `bake_navmesh`, `bake_navmesh_surfaces`, `cancel_navmesh_bake`, `navmesh_bake_status`, `clear_navmesh`, `get_navmesh_settings`, `set_navmesh_settings`.
  - **Occlusion culling:** `bake_occlusion_culling`, `cancel_occlusion_bake`, `occlusion_bake_status`, `clear_occlusion_culling`.
- Update Wrench
- Warn user if Unity Editor is started in non-automated mode.

## [0.2.0-exp.2] - 2026-06-24

- Fix security audit flaws
- First official published version

## [0.1.0-exp.1] - 2026-06-09

### This is the first release of _Unity Pipeline_.
