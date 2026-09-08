# com.unity.pipeline

Unity package for **remote-controlling a running Unity Editor (or dev Player) over HTTP**.
A client — CLI, CI, or an agent — connects and executes registered commands: recompile,
run tests, eval C#, hot-reload files, play-mode control, status/heartbeat.

## Layout (standard UPM package)

| Folder | Assembly | Notes |
|--------|----------|-------|
| `Runtime/` | `Unity.Pipeline` | Ships in player builds. HTTP server base, command registry, models, runtime commands, hot reload, player server. |
| `Runtime/IlInterpreter/` | `Unity.Pipeline.IlInterpreter` | IL interpreter (IlInterpreter VM) that executes eval snippets and hot-reloaded method bodies where `Reflection.Emit` is unavailable (IL2CPP). Referenced by both `Unity.Pipeline` and `Unity.Pipeline.Editor`. |
| `Runtime/Analyzers/` | — | Shipped `IlInterpreterAnalyzer.dll` — Roslyn analyzer (MS002–MS023) surfacing the interpreter's C# subset on `[HotReload]` method bodies. Built from `IlInterpreter/analyzer/` at the repo root (see its `README.md`); auto-synced on Release builds — never edit the DLL by hand. |
| `Editor/` | `Unity.Pipeline.Editor` | Editor-only. Live server + its owner, settings asset, editor commands, test runner, hot-reload watcher, build processors (`Editor/BuildProcessors/`, incl. the hot-reload `link.xml` generator). |
| `CodeGen/` | `Unity.Pipeline.CodeGen` | ILPostProcessor (Mono.Cecil) for in-place hot reload. Root-level by Unity convention (cf. Burst/Entities) — leave as-is. |
| `Tests/Editor/` | `Unity.Pipeline.Tests.Editor` | **EditMode** suite (the main one). |
| `Tests/Runtime/` | `Unity.Pipeline.Tests.Runtime` | **PlayMode** suite. |
| `Samples~/` | — | Hot-reload sample scenes. |

## Architecture entry points

- **`Runtime/Common/BasePipelineServer.cs`** — shared HTTP listener + routing (`/api/exec`,
  `/api/status`, `/api/commands`, …). Subclassed by `Editor/EditorPipelineServer.cs` and
  `Runtime/PlayerSupport/RuntimePipelineServer.cs`.
- **`Editor/EditorPipelineStartup.cs`** (`PipelineServerStartup`) — `[InitializeOnLoad]` static
  owner of the live editor server; survives domain reloads; auto-enables autotick.
- **`Editor/EditorPipelineManager.cs`** — inspectable settings asset (`Window/Pipeline/Settings...` menu).
- Commands are discovered via `[CliCommand]` attributes. Editor command groups live under
  `Editor/Commands/` (Assets, Scenes, GameObjects, Prefabs, Scripts, Build, PackageManager,
  ProjectSettings, …); runtime commands under `Runtime/`.
- **Interpreter path (IL2CPP-safe eval & hot reload)** — `Runtime/IlInterpreter/ScriptInterpreter.cs`
  is the VM core; `Runtime/Compilation/IlInterpreterHostBindings.cs` (`CreateStandard()`) builds the
  host surface it may call — consumed by eval, hot reload, and the `link.xml` generator
  (`Editor/BuildProcessors/HotReloadLinkXmlGenerator.cs`); see the repo-root `CONTEXT.md` glossary
  for the drift invariant. `Runtime/Compilation/InterpreterHotReloadExecutor.cs` routes reloaded
  method bodies into the VM; `Editor/EditorHotReloadWatcher.cs` owns the file-watch → push-reload
  loop.
- **`Editor/Authoring/`** — helpers shared by state-changing commands: `AuthoringUndoScope`
  (collapses a command's `Undo`-registered mutations into one step) and `ProjectPaths`/`ObjectResolver`
  (authoring-root path resolution + object handles).
  Destructive/overwriting commands gate on `confirm`/`dry_run` inline (see `delete_asset`). Structured
  multi-field command args implement `IStructuredCommandInput` (`Runtime/Common/`). See
  `Documentation~/safety-and-mutations.md`, `Documentation~/authoring-commands.md`, and
  `Documentation~/creating-commands.md`.

## Driving & verifying (agents)

Use the **`unity-pipeline` skill**, which drives the live editor via the `unity` CLI.
Canonical verb is `command`; the auth token is the `evalToken` field inside the port file
`<liveProject>/Library/Pipeline/.unity-pipeline-port`, sent as `Authorization: Bearer <token>`.

Edit→verify loop: make a logical change (may span several files) → `command recompile` →
poll `command recompile_status` → `command run_tests --filter <TestClass>`. The server keeps the
editor ticking while unfocused, so compiles proceed even when focus is elsewhere.

## Conventions

- Private instance fields: `m_PascalCase`. Private static fields: `s_PascalCase`. Consts: `PascalCase`.
  Exception: `Runtime/IlInterpreter/` keeps its `_camelCase` style.
- Don't `git commit`/`push` without an explicit request.
- Changelog entries: `[JIRA-KEY] <one short clause>` in Unreleased. No justification/why — state what changed, stop.
  Plain English, minimal code snippets/API names — describe the change, don't quote the implementation.

