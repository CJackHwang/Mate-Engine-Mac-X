# Creating commands

A *command* is a `static` method that the Pipeline server can invoke over HTTP. The method's accessibility does not matter — `public`, `internal`, and `private` static methods can all be registered. This page covers the command-authoring API: how to declare a command, describe its parameters, return a result, and have it discovered automatically.

## The handler + response pattern

Authoring a command has two halves:

1. **The handler** — your `static` method, tagged `[CliCommand]`. It does the work and returns a value.
2. **The response** — the server wraps whatever your handler returns in a [`CommandExecutionResponse`](#the-commandexecutionresponse-envelope) and serializes it to JSON for the client.

You never build the HTTP response yourself. Return a `string`, a number, an anonymous object, a typed model, or `null`; the server takes care of the envelope, timing, and error reporting.

## Declaring a command

Tag a `static` method with `[CliCommand]` (the examples below use `public`, but any accessibility works):

```csharp
[CliCommand("name", "description", MainThreadRequired = true, RuntimeOnly = false)]
public static <ReturnType> Handler(...);
```

| Argument | Meaning |
|----------|---------|
| `name` | Unique command name used by the client (`unity command <name>`). |
| `description` | Human-readable text shown in help and the `/api/commands` listing. |
| `MainThreadRequired` | Whether the handler must run on Unity's main thread. **Default `true`.** |
| `RuntimeOnly` | Whether the command is hidden from an Editor server's command listing. **Default `false`.** |
| `Tags` | Optional hierarchical tags for grouping/browsing commands. Path-style: a `/` separates tag from subtag (e.g. `"assets"`, `"assets/import"`). **Default: empty.** |

Every discovered command also carries the **package** it originates from (derived from the declaring assembly, e.g. `Unity.Pipeline.Editor`). Tags and package appear in both the compact and full `/api/commands` listings — `detail=full` (the default) includes the parameters and generated JSON schema, while `detail=compact` returns just the lightweight index (`name`, `description`, `tags`, `package`). Tags also drive the endpoint's `tag` filter and its `group_by=tag` tree (see [Connectivity](connectivity.md)).

The method **must be `static`**, but its accessibility does not matter: `public`, `internal`, and `private` static methods can all be registered. `CommandRegistry` invokes handlers through reflection, so a `private` handler runs exactly like a `public` one. Only a non-static (instance) method fails to register — `CommandRegistry` skips it and logs a warning.

### Tag taxonomy

Every shipped command carries at least one tag (enforced by `CommandRegistrationTests.CommandRegistry_DiscoverCommands_AllShippedCommandsCarryTags`). Tags are lowercase `/`-separated paths. Pick from the established top-level tags before inventing a new one, and add a subtag when a family is large enough to browse on its own:

| Top-level tag | Covers | Subtags in use |
|---------------|--------|----------------|
| `animation` | Animation authoring | `animator`, `clip`, `timeline` |
| `assets` | Asset CRUD and search | `import`, `text` |
| `authoring` | Authoring-root configuration | — |
| `batch` | Transactional multi-command execution (`batch`) | — |
| `baking` | Bake pipelines | `lighting`, `navmesh`, `occlusion` |
| `build` | Player builds | `settings`, `targets` |
| `capture` | Screenshots and view/element capture | — |
| `editor` | Editor application control | `playmode` |
| `gameobjects` | Scene GameObject manipulation | `components` |
| `materials` | Materials | `shaders` |
| `navigation` | Selection and search | — |
| `observability` | Logs and diagnostics | `console`, `performance` |
| `packages` | UPM package management | — |
| `prefabs` | Prefab workflows | — |
| `runtime` | Player-only application control | `application`, `input` |
| `scenes` | Scene lifecycle and hierarchy | — |
| `scripts` | C# source workflows | `compile`, `eval`, `hotreload` |
| `settings` | Project settings | `audio`, `graphics`, `input`, `physics`, `player`, `quality`, `tags_layers`, `time` |
| `tests` | Test runner | — |
| `ui` | UI element workflows | — |

A command may carry multiple tags when it genuinely belongs to two families (e.g. `add_scene_to_build` is tagged `scenes` and `build/settings`).

### Describing parameters

Tag each parameter with `[CliArg]`:

```csharp
[CliArg("name", "description", Required = false, DefaultValue = null)]
```

| Property | Meaning |
|----------|---------|
| `name` | Parameter name as it appears in client arguments (`--name value`). |
| `description` | Human-readable description for help text. |
| `Required` | Whether the parameter must be supplied. Defaults to `false` — but if the parameter has no C# default value it is treated as required. |
| `DefaultValue` | Value used when the client omits the parameter (a C# default value takes precedence). |

`[CliArg]` is optional metadata. A parameter without it still works: its name defaults to the C# parameter name and `Required` defaults to "does this parameter lack a C# default value?".

#### Declaration order and `Required` are wire API

Clients may send an unparsed command line (`argv`/`commandLine` on `/api/exec`), and the server
binds positionals into **required parameters in declaration order, then optional parameters in
declaration order**. Three consequences for command authors:

- **Reordering parameters, or flipping one between required and optional, is a breaking change**
  for those clients even though the C# signature still compiles. Append new optional parameters at
  the end.
- **Enum parameters are validated by name** (case-insensitively), and an invalid value is rejected
  with the legal set echoed back — you get that for free, no attribute needed. A `[Flags]` enum
  also takes a comma-separated combination (`--channels Info,Warning`), and a numeric value is
  accepted, because validation defers to the same converter the executor uses.
- **Structured (`IStructuredCommandInput` / `JObject`) parameters are reachable from a command
  line only as a JSON-valued flag**, e.g. `--payload '{"name":"x"}'`. Single quotes are the
  practical form; see the tokenizer dialect in [Connectivity](connectivity.md).

A token the declared type cannot accept is now a hard `400` on the raw path, rather than the
parameter silently dropping out and the command running with a default.

## Worked example 1 — returning a string

A command can return a plain `string`. The server places it in the response `result` field.

```csharp
using Unity.Pipeline.Commands;
using UnityEditor;
using UnityEngine;

public static class PlayModeCommands
{
    [CliCommand("editor_play", "Enter Unity Editor play mode")]
    public static string EnterPlayMode()
    {
        if (EditorApplication.isPlaying)
            return "Already in play mode";

        EditorApplication.isPlaying = true;
        return "Entered play mode";
    }
}
```

Calling `editor_play` yields a `CommandExecutionResponse` whose `result` is `"Entered play mode"`.

## Worked example 2 — returning a response model

For richer results, return a model. Extending `CommandExecutionResponse` lets you populate response fields directly; you can also return any plain serializable model (like `AuthoringResult`) and let the server wrap it.

```csharp
using System;
using Unity.Pipeline.Commands;
using Unity.Pipeline.Models;

public class WordCountResponse : CommandExecutionResponse
{
    public int WordCount { get; set; }

    public static WordCountResponse Counted(int wordCount) => new WordCountResponse
    {
        Success = true,
        WordCount = wordCount
    };

    public static WordCountResponse Failed(string error) => new WordCountResponse
    {
        Success = false,
        Error = error
    };
}

public static class TextCommands
{
    [CliCommand("word_count", "Count the words in a string")]
    public static WordCountResponse CountWords(
        [CliArg("text", "Text to count words in", Required = true)] string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return WordCountResponse.Failed("text parameter is required and cannot be empty");

        return WordCountResponse.Counted(text.Split((char[])null, StringSplitOptions.RemoveEmptyEntries).Length);
    }
}
```

`WordCountResponse` adds `wordCount` on top of the standard envelope. Another common shape is a domain model such as `AuthoringResult` — the canonical identity (asset path, GUID, instance id, hierarchy path) of an object a command created, returned so the client can reference it in a follow-up call.

## Structured (multi-field) parameters

When a command needs a structured argument with several fields, don't spread them across many `[CliArg]` parameters — declare a small DTO that implements `IStructuredCommandInput` and take it as a single parameter. The type is advertised to clients as a nested JSON **object** schema in `GET /api/commands` (instead of collapsing to `string`), and the value is deserialized automatically via Newtonsoft — no extra wiring.

```csharp
[CliCommand("set_time_settings", "Change Time settings. Requires confirm=true; use dry_run to preview.")]
public static CommandExecutionResponse Set(
    [CliArg("settings", "Fields to change; omitted fields are left unchanged.")] TimeSettingsArgs settings = null,
    [CliArg("confirm", "Apply the change. Without it the call is refused.")] bool confirm = false,
    [CliArg("dry_run", "Preview the change without applying it.")] bool dryRun = false)
{ /* ... */ }

public class TimeSettingsArgs : IStructuredCommandInput
{
    [CliArg("fixedDeltaTime", "Fixed timestep in seconds (e.g. 0.02).")]
    public float? FixedDeltaTime { get; set; }

    [CliArg("timeScale", "Time scale (1 = real-time).")]
    public float? TimeScale { get; set; }
}
```

`JsonSchemaGenerator` reflects over the type's public, writable fields and properties to emit `{ "type": "object", "properties": { ... } }`, recursing into nested `IStructuredCommandInput` members and arrays/lists of them. Member metadata mirrors command parameters:

- `[CliArg(name, description, Required = ...)]` controls the property name, description, and whether it appears in the schema's `required` array.
- Without a `[CliArg]`, the member (or its Newtonsoft `[JsonProperty]`) name is used and it is optional. Use nullable types (`float?`) for "omitted = leave unchanged" semantics.
- `[JsonIgnore]` members are omitted from the schema.

`[CliArg]` is valid on parameters, fields, and properties, so the same attribute annotates DTO members. Most commands that take an `IStructuredCommandInput` are mutations — pair the DTO with `confirm`/`dry_run` and follow the [safety conventions](safety-and-mutations.md) (inline gate + `AuthoringUndoScope`).

## The `CommandExecutionResponse` envelope

Whatever your handler returns, the client receives a `CommandExecutionResponse`:

| Field | Type | Meaning | In lean reply? |
|-------|------|---------|----------------|
| `success` | `bool` | Whether the command ran without throwing. | always |
| `result` | `object` | Your handler's return value (a string, model, anonymous object, or `null`). | always on success, explicitly `null` when null; omitted on failure |
| `error` | `string` | Error summary when `success` is `false`. | when non-null |
| `errorDetails` | `string` | Extra error context. | when non-null |
| `warnings` | `string[]` | Corrective guidance the server wants the caller to see (e.g. an unrecognized option value). | when non-null (omitted when there is nothing to say, in every mode) |
| `command` | `string` | The command name. | verbose only |
| `executedAt` | `DateTime` | When the reply was produced. | verbose only |
| `executionTimeMs` | `long?` | How long the command took. | verbose only |

The envelope is **lean by default** (AUTHAPI-21): serialized compact (no indentation), with the
envelope's own null keys omitted and the always-on metadata (`command`, `executedAt`,
`executionTimeMs`) dropped. A minimal success is just `{"success":true,"result":...}`. Agents
consume the JSON directly; a client can pretty-print for a human if needed.

The boundaries of the lean contract, worth knowing:

- **Only the envelope's nulls are omitted; your `result` payload keeps explicit nulls.** The
  envelope's null keys are redundant (`success` already disambiguates: `error` is null on success),
  so they are dropped. Inside `result` the opposite holds — a scene object's `AuthoringResult`
  still carries `"assetPath": null` — because an absent payload key would be indistinguishable
  from a nonexistent or misspelled one.
- **A successful reply always carries `result`, even when its value is null — in every mode.** A
  null result on success is the command's actual value (e.g. a `format="value"` read of an
  unassigned object-reference field), never a droppable envelope null. Only a *failure* omits the
  `result` key.
- **The metadata gating applies to the whole graph.** A command whose result is itself a response
  model (e.g. `eval` returns an `EvalResponse` nested under `result`) has its nested
  `executedAt`/`executionTimeMs` stripped in lean mode too — and restored by verbose along with
  everything else.

Two independent request flags adjust the shape:

- `"verbose": true` — full fidelity back: every envelope field, explicit nulls, and nested response
  metadata, for debugging or correlation. Honored on request-validation failures too.
- `"omitNulls": true` — drop null keys from the whole reply **inside `result`** (the success
  `result` key itself always survives, see above). Opt in only when the result schema is already
  known and the nulls are pure bytes — the payoff case is bulk list reads (e.g. `list_shaders`
  over hundreds of built-ins repeating `"assetPath": null` per item).

The `GET`-style endpoints (`/api/job`, `/api/progress`) have no envelope/payload split — every field
is payload — so they include null keys explicitly by default and accept `omit_nulls=true` as a query
parameter instead. An unrecognized value (e.g. `omit_nulls=1`) is not silently coerced: the reply
keeps its nulls and carries a `warnings` array naming the accepted values, so an agent can correct
itself instead of guessing.

If your handler throws, the server catches it and returns a failure envelope with `success = false` and the exception message in `error` — you do not need to catch-and-wrap yourself unless you want a tailored message.

## `MainThreadRequired`

- **Default: `true`.** Most Unity APIs (scene, GameObject, asset, play-mode access) must run on the main thread. The server marshals these handlers onto the main thread via its dispatcher.
- **Set `false` only** for handlers that are thread-safe and read-only / pollable (e.g. a status or buffer-read command). These run on a background thread so they never block the main thread or deadlock against a busy editor.

When in doubt, leave it `true`.

## `RuntimeOnly`

- **Default: `false`** — the command is advertised in an Editor server's `/api/commands` listing.
- **Set `true`** to hide a command from the Editor command listing. It remains **executable**; it is simply not advertised when a client is connected to an Editor (Runtime/Player servers still list it). Use this for commands that only make sense against a running Player.

## Discovery

Commands are discovered by `CommandRegistry`, which scans for `[CliCommand]`-tagged methods through a pluggable `ICommandDiscovery`:

- In the **Editor**, `TypeCacheCommandDiscovery` provides fast `TypeCache`-based discovery.
- In a **Player**, the registry falls back to reflection over loaded assemblies.

Results are cached until the next domain reload. **A newly added command becomes available after the next recompile** — no registration call is needed; just declare it and recompile.

## Minimal custom command template

```csharp
using Unity.Pipeline.Commands;

public static class MyCommands
{
    [CliCommand("my_command", "What this command does")]
    public static object MyCommand(
        [CliArg("text", "Some input", Required = true)] string text,
        [CliArg("count", "How many times")] int count = 1)
    {
        // Do work on the main thread (MainThreadRequired defaults to true).
        return new { echoed = text, count };   // anonymous object → response.result
    }
}
```

Recompile, then invoke it from your client (`unity command my_command --text hello --count 3`).

## See also

- [Command reference](commands/runtime.md)
- [Connectivity](connectivity.md) — how clients reach the server and authenticate.
