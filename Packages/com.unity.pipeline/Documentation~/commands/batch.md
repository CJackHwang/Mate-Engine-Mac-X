# Batch command

The `batch` command runs several existing pipeline commands in a single request, with cross-operation result references, atomic Undo, and dry-run validation. It exists to collapse the round-trips of a "create → add components → wire fields" job (otherwise 10–90 sequential calls) into one request, and to add what `eval` structurally lacks: **transactionality and rollback**, so a failure mid-way never leaves partial state on a user-owned scene.

**Scope guard:** `batch` is transactional object CRUD with references. It intentionally has **no** `invoke`/loop/conditional op types — arbitrary logic belongs in code-on-disk (`run_script`/`eval`), not in JSON. This keeps the command surface auditable and allowlistable.

### `batch`
Run multiple registered commands in one transactional request.

| Parameter | Required | Default | Description |
|-----------|----------|---------|-------------|
| `operations` | yes | – | Ordered list of operations, each `{ id?, command, params }`. Max 200. |
| `transactional` | no | `true` | Group every op into one Editor Undo step and revert all applied ops if any op fails. Forced `false` when `on_error=continue`. Rejects commands whose mutations Undo cannot revert (see [Transactional restrictions](#transactional-restrictions)). |
| `on_error` | no | `abort` | `abort`: stop at the first failing op. `continue`: run every op, collecting per-op errors (forces `transactional=false`). |
| `dry_run` | no | `false` | Validate command names, parameters, reference topology, and excluded/non-batchable commands without mutating anything. |
| `result_fields` | no | – | Optional per-op result projection: a map of op id-or-index → array of result field paths to keep (context economy). |
| `time_budget_ms` | no | `50000` | Cooperative time budget for the whole batch, in milliseconds (max 3600000). Checked before each op after the first; when exhausted, the remaining ops are skipped and a transactional batch rolls back. |

**Returns:** `BatchResult` — `{ results: [{ id?, command, success, result? | error?, skipped?, resultTruncated?, revertible? }], applied, reverted, transactional, onError, error?, undoGroup?, dryRun?, valid? }`. The batch-level `error` is set only for aborts not attributable to a single op ("batch time budget exceeded", "batch canceled").

**Notes:** Runs on the main thread. Each sub-operation goes through the same dispatch path as `/api/exec`, so its result is shaped identically to running that command standalone.

#### Operation shape

Each entry of `operations` is:

```json
{ "id": "createHead", "command": "add_component", "params": { "target": "$root.instanceId", "type": "Rigidbody" } }
```

- `id` (optional) — a name for referencing this op's result from a later op. Must be unique within the batch and match `^[A-Za-z_][A-Za-z0-9_-]*$` (purely numeric ids would be ambiguous with index selectors, and `.` collides with the reference path separator).
- `command` — the registered command to run (any command except the non-batchable ones below).
- `params` — the command's parameters, same shape as `/api/exec` `parameters`. Operations are **always** `command` + `params`; the `argv`/`commandLine` raw forms are exec-only and are not accepted inside a batch operation.

#### Cross-operation references

Any **string** value in a later op's `params` may reference an earlier op's result:

```
"$<id-or-index>.<jsonPath>"
```

- `<id-or-index>` names an earlier op by its `id` or its 0-based index (an `id` match takes precedence).
- `<jsonPath>` is a Newtonsoft SelectToken path into that op's result, e.g. `instanceId`, `components[0].instanceId`.
- `"$$"` escapes a literal `$` (so `"$$5"` is the literal string `"$5"`).
- References are **backward-only** and resolved as **whole values** (the entire string must be the reference). The substituted JSON token keeps its type, so a numeric `instanceId` deserializes into an `ObjectRef` parameter exactly as a literal would. References may appear anywhere in the parameter tree (including inside nested objects/arrays).
- Referencing an op that **failed** or was **skipped** errors explicitly (this can happen under `on_error=continue`); referencing a succeeded op that **returned no result** errors with "returned no result". A reference never silently resolves to `null`.
- References always resolve against the **full** result of the earlier op, even when the reported copy was truncated or projected via `result_fields`.
- **Result size bounds** (both measured in serialized UTF-8 bytes, i.e. wire size): a single op's reported result above **16 KiB** is replaced by a truncation marker (`resultTruncated: true` + a preview), and the **aggregate** of all reported results in one batch is bounded to **256 KiB** — past that, later results are elided the same way (the ops still execute; only the echoed copy is dropped). Use `result_fields` to project results down when a batch reads large objects.
- A malformed `result_fields` path never fails its op (the op has already executed); the bad path is reported inside the projection as `invalidResultFields`.

Example — create a GameObject, add a Rigidbody to it, then set the Rigidbody's mass:

```json
{
  "operations": [
    { "command": "create_gameobject", "params": { "name": "Ball" } },
    { "command": "add_component", "params": { "target": "$0.instanceId", "type": "Rigidbody" } },
    { "command": "set_serialized_field", "params": { "target": "$1.instanceId", "field": "m_Mass", "value": 2.5 } }
  ]
}
```

#### Atomicity

The whole batch runs inside one `AuthoringUndoScope`, so every op (each of which registers its own Undo operations) collapses into a **single** Editor Undo step. When `transactional` (the default) and the batch aborts — an op fails under `on_error=abort`, the time budget runs out, or the job is canceled — every applied op is reverted with `Undo.RevertAllDownToGroup` (including partial mutations registered by the failing op itself), the scene returns to its pre-batch state, and nothing is left on the Undo stack. `reverted: true` and the failing op's `error` are reported.

#### Transactional restrictions

Rollback is only real for **Undo-tracked** mutations (scene objects, components, serialized fields). Commands that mutate state **outside** the Undo system — asset and file writes, scene saves, Build Settings, project settings, baked data — cannot be rolled back, so a transactional batch **rejects** them with a structured `not_batchable_transactional` error. Run them with `transactional: false`, or call them standalone. Their per-op result reports `revertible: false` so the limitation is visible.

The non-revertible set (v1 — a curated list, pending per-command `Revertible` metadata):

- **Assets & files:** `create_asset`, `import_asset`, `move_asset`, `copy_asset`, `rename_asset`, `delete_asset`, `create_folder`, `set_import_settings`, `write_text_file`, `create_script`
- **Prefab assets:** `create_prefab`, `create_prefab_variant`, `apply_prefab_overrides`, `save_prefab_contents` (scene-side prefab ops — `instantiate_prefab`, `unpack_prefab`, `revert_prefab_overrides` — are Undo-tracked and stay fully batchable)
- **Scene persistence & build list:** `save_scene`, `save_all`, `add_scene_to_build`, `remove_scene_from_build`, `set_build_settings`
- **Settings:** `set_audio_settings`, `set_graphics_settings`, `set_input_settings`, `set_lighting_settings`, `set_navmesh_settings`, `set_physics_settings`, `set_player_settings`, `set_quality_settings`, `set_tags_layers`, `set_time_settings`, `set_authoring_root`
- **Animation / Timeline assets:** `create_animation_clip`, `set_animation_curve`, `remove_animation_curve`, `create_animator_controller`, `add_animator_layer`, `add_animator_parameter`, `add_animator_state`, `add_animator_transition`, `create_timeline`, `add_timeline_track`, `add_timeline_clip`
- **Materials:** `set_material_properties` (persists the `.mat` asset to disk)
- **Baking:** `bake_lighting`, `clear_baked_lighting`, `bake_navmesh`, `bake_navmesh_surfaces`, `clear_navmesh`, `bake_occlusion_culling`, `clear_occlusion_culling`

`open_scene` and `create_scene` are **always** rejected inside a batch (even with `transactional: false`): they clear the Editor Undo stack, which would silently destroy the batch's ability to revert anything that ran before them.

#### Timing, budget and cancellation

The batch executes in a **single main-thread turn** — the Editor UI is frozen for its duration, bounded by `time_budget_ms` (frame-spanning execution is future work). The elapsed time is checked cooperatively before each op after the first; when the budget is exhausted, the remaining ops are reported `skipped: true`, the batch-level `error` is set to "batch time budget exceeded", and a transactional batch rolls back. For long batches, submit the request as a detached job (`"job": true` on `/api/exec`) and raise `time_budget_ms` — jobs also make the batch cancelable: cooperative cancellation (`POST /api/job/cancel`) is checked between ops and aborts the batch the same way ("batch canceled"). Per-op progress is reported (`op i/n: <command>`) and visible via `GET /api/progress`.

#### Dry run

`dry_run: true` validates without executing anything: it catches unknown commands, excluded/non-batchable (`not_batchable`) commands, transactional rejections (`not_batchable_transactional`), unknown/missing parameters, and unresolvable reference topology (unknown or forward references). It mutates nothing and reports `valid` plus a per-op `success`/`error`.

#### Excluded commands

The following are rejected with a structured `not_batchable` error:

- **Out of scope:** `build`, `switch_build_target`, `package_*`, `editor_play`/`editor_stop`/`editor_pause`, `recompile`
- **Arbitrary code:** `eval`, `eval_file`, `run_script`, and nested `batch`
- **Modal-dialog hazard:** `menu` (menu items can open modal dialogs that wedge an unattended batch)
- **Undo-stack wipe:** `open_scene`, `create_scene`
- **Async commands** (`run_tests`, `list_tests`, and any command returning `Task`): they are completed by `EditorApplication.update` callbacks, which cannot fire while the batch holds the main thread — dispatching one would freeze the Editor permanently
- **Runtime-only commands** (e.g. `set_timescale`): they belong to the Player server surface and cannot execute on the editor server
