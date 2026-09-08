# Analytics

The Editor server reports how the pipeline is used through Unity's editor analytics
(`EditorAnalytics.SendAnalytic`). The data answers questions the package has no other way to
answer: whether sessions are interactive or automated, which commands clients actually reach for,
how often they fail, how long they take, and how much traffic is raw `eval` rather than a
concrete command.

Three things bound what is collected:

- **Editor only.** A server running in a development Player reports nothing — `EditorAnalytics`
  does not exist there.
- **The editor's own opt-out applies.** With analytics disabled in Unity preferences,
  `SendAnalytic` does nothing and no event leaves the machine. There is no separate pipeline
  switch to turn off.
- **Nothing about a command's content is collected.** Not its parameters, not the code passed to
  `eval`, not paths, not results.
- **Nothing a project declared about its own commands is collected.** Only commands shipped in the
  package report their name and tags; anything a project declared itself reports
  `<customUserCommand>` and no tags — see
  [What is and is not collected](#what-is-and-is-not-collected).

## Events

### `Pipeline_SessionStarted`

Sent once when a client executes its first command, which is the moment the pipeline actually
started handling work — not when the Editor launched or the server bound its port.

| Field | Meaning |
|-------|---------|
| `batchmode` | The Editor was launched with `-batchmode`. |
| `automated` | The Editor was launched with `-automated`. |
| `browserAllowed` | The server accepts sandboxed browser clients (the limited-CORS path). Off unless enabled in the Pipeline settings. |
| `dynamicPortAssignation` | The port was auto-assigned from the server's range rather than pinned in the settings asset. |
| `hotReloadWatcherEnabled` | The `.cs` change watcher is running. |

A session spans a Unity **process**. The latch lives in `SessionState`, which survives domain
reloads, so a script recompile does not open a second session.

### `Pipeline_CommandExecuted`

Sent once per command a client executes.

| Field | Meaning |
|-------|---------|
| `commandName` | The registered command name — but only for commands that ship with the package. A project-declared command reports the literal `<customUserCommand>` instead. |
| `commandTags` | The command's tags, in declaration order — but only for commands that ship with the package. Empty for an untagged command, and always empty for a project-declared one. |
| `commandSuccess` | Whether the command succeeded. False both when it threw and when it returned a result carrying its own failure — `eval`, `reload_file`, `run_script` and `run_tests` all report failure the second way. |
| `commandDuration` | Milliseconds spent inside the command, excluding the wait for the one-command-at-a-time gate. |
| `isUserDefinedCommand` | The command was declared by the project rather than shipped in the package. |
| `isEval` | The command is a code evaluation (tagged `scripts/eval`: `eval`, `eval_file`, `run_script`). |
| `isEvalWithExistingCommandAvailable` | Whether the evaluated snippet touches an API that a shipped command already covers — the "you did not need eval for this" signal. Same judgement `report_evals` makes, applied to one invocation: see [How the eval-coverage flag is decided](#how-the-eval-coverage-flag-is-decided). |

What counts as one execution:

- One event per `/api/exec` call, including a `job: true` submission — reported when the job
  actually **finishes**, not when its job handle is returned.
- A `batch` reports one event, for `batch` itself. Its sub-operations are not reported
  individually.
- A request rejected before anything ran (oversized body, malformed JSON, unknown command,
  settling host, full job queue) reports nothing: no command executed.
- The internal executions behind `/api/editor_status` and `/api/test-status` report nothing.
  Clients poll those endpoints, and the polling would swamp everything else.

### `Pipeline_SessionStopped`

Sent when the Editor quits, and only if `Pipeline_SessionStarted` was sent — an Editor that never
served a command closes no session.

| Field | Meaning |
|-------|---------|
| `sessionDuration` | Milliseconds between the session starting and the Editor quitting. |

## How the eval-coverage flag is decided

`isEvalWithExistingCommandAvailable` reuses the eval instrumentation from AUTHAPI-29 rather than
inventing a second opinion. Per invocation:

1. `EvalUsageFingerprinter.Analyze` turns the snippet into API fingerprints — dotted member-access
   paths, with no literals or raw source in them.
2. Each fingerprint is looked up in `EvalCoverageMap.Default`, a small curated hint map from API to
   the command that would cover it.
3. The flag is true only if a mapped command is **actually in the live catalog**. A mapping to a
   command that does not exist yet (`AssetDatabase.Refresh` → `refresh_assets`) is a gap for
   `report_evals` to surface, not an avoidable eval.

This is deliberately the same rule as `report_evals`' `covered` list, so the two can never disagree.
Two consequences worth knowing when reading the data:

- **It is a floor, not a measure.** The map has a handful of entries and matching is on the whole
  fingerprint, so `PlayerSettings.GetScriptingBackend(...)` matches while
  `UnityEditor.PlayerSettings.GetScriptingBackend(...)` and
  `PlayerSettings.GetScriptingBackend(...).ToString()` do not — they fingerprint as different paths.
  A `false` means "no mapped-and-present command matched", never "no command could cover this".
- **`run_script` always reports `false`.** It runs a project file through a named entry point rather
  than a snippet, and the fingerprinter parses script-kind bodies, so it is not fingerprinted.
  `eval` (inline body) and `eval_file` (body read back from its file) are judged.

## What is and is not collected

A command's name and tags are text the project's own authors wrote, so either can name an
unannounced feature or an internal system. Tags are constrained in shape
(`^[a-z0-9_]+(/[a-z0-9_]+)*$`) and shipped commands must use the documented taxonomy, but nothing
holds a project to it, so a tag carries a project-chosen word exactly as a name does.

So both are withheld. `commandName` and `commandTags` describe only commands that ship in the
package (`Unity.Pipeline`, `Unity.Pipeline.Editor`); every other `[CliCommand]` reports the literal
`<customUserCommand>` and an empty tag array. The remaining fields — success, duration, `isEval` —
are facts about the execution, not about what the project called it, so they are reported for every
command.

What this costs: project-declared traffic is countable but not breakable down by area. That is the
deliberate trade — `isUserDefinedCommand` answers how much of the surface is project-declared, which
is the question worth asking, and nothing identifying is needed to answer it.

Command **arguments** are never collected. Neither is anything a command reads or writes.

## Adding post-command work

Analytics is one consumer of a single seam rather than a special case. When a command finishes,
`BasePipelineServer.OnCommandDone` receives a `CommandExecutionInfo` describing both the raw
transaction and the execution. It runs on the HTTP thread, so `EditorPipelineServer` splits by what
each consumer needs: the transaction log is appended there directly (thread-safe file I/O by
design), and anything needing Unity APIs is handed to the server dispatcher with
`Dispatcher.Post`, which runs it in `OnCommandDoneMainThread` on the next main-thread pump.

New post-command work goes in one of those two, depending on which thread it needs. `Post` is the
general fire-and-forget counterpart to `Dispatcher.Invoke`: it returns immediately instead of
parking the request thread until the next pump, and swallows-and-logs failures because there is no
caller left to throw to. Work still queued when the server stops is dropped, so a report can be
lost in the frame the Editor quits or reloads its domain.

## See also

- [Creating commands](creating-commands.md) — where a command's name and tags come from.
- [Connectivity](connectivity.md) — the port and CORS settings the session event reports.
- [Hot reload](hot-reload.md) — the watcher whose state the session event reports.
