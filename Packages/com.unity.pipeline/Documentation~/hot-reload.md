# Hot reload

Apply edited C# to running code without restarting Play Mode or rebuilding the Player. Two flavors are supported: **in-place** reload of a tagged method body, and **override** reload that routes a method through a separately-compiled replacement.

Reloaded code runs on one of two **backends**:

| Backend | How it runs | Where | Language |
|---------|-------------|-------|----------|
| `Assembly.Load` (default) | Roslyn compiles the edit, the IL loads as a sibling assembly | Editor Play Mode, Mono dev builds | Full C# |
| Interpreter (IlInterpreter) | The compiled IL runs in a VM — no `Assembly.Load` | Everywhere, **including IL2CPP dev builds** | A C# subset (see [constraints](#interpreter-constraints)) |

Pick the interpreter with the `reload_file_editor_interpreter` command. The watch UI and device pushes always use it (`reload_file_player_interpreter` — IL2CPP has no Roslyn and no `Assembly.Load`; the editor compiles, the device interprets). Hot reload is gated to the editor and development builds (`UNITY_EDITOR || DEVELOPMENT_BUILD`).

## Flavor 1 — in-place (`reload_file`)

Tag the method you want to be reloadable with `[HotReload]`, then edit its body and apply the file. The running instance picks up the new body.

```csharp
using Unity.Pipeline.HotReload;
using UnityEngine;

public class Spinner : MonoBehaviour
{
    public float rotationSpeed = 90f;

    [HotReload]
    void Update()
    {
        transform.Rotate(Vector3.up, rotationSpeed * Time.deltaTime);
    }
}
```

Edit the body of `Update`, then apply it:

```bash
unity command reload_file "<absolute path>/Spinner.cs"

# Emit debug symbols so breakpoints bind (compiles unoptimized):
unity command reload_file "<absolute path>/Spinner.cs" --pdb
```

`reload_file` parameters:

| Arg | Default | Meaning |
|-----|---------|---------|
| `filename` | *(required)* | Source file containing `[HotReload]` methods. |
| `timeout` | `30000` | Compilation timeout (ms). |
| `assemblyDir` | `null` | Optional directory to also save the compiled assembly to disk (default: in-memory only). |
| `pdb` | `false` | Emit a portable PDB mapped to the original source so debugger breakpoints bind. Compiles unoptimized. |

`reload_file_editor_interpreter` takes the same parameters and runs the reloaded methods through the IlInterpreter VM instead of `Assembly.Load` (IL2CPP-safe; subset + host-surface limits apply).

**Constraints (in-place):** the `[HotReload]` method must be **`public`** (instance **or static**) and return **`void` or `System.Collections.IEnumerator`** (a coroutine). Other value-returning methods are skipped at build time with a warning.

**New methods:** a reload can declare a method the compiled type doesn't have yet and call it from a reloaded body (instance and static methods, expression bodies included; not generics, properties, or fields). A new method cannot be a reload *entry point* — nothing compiled calls it until the next real compile — so tagging it `[HotReload]` registers nothing for it (the reload response says so) while the rest of the file still applies.

**Method groups and delegates:** a reloaded body can pass a method as a callback — `_root.Add(new Button(Click))`, `button.clicked += Click`, `RegisterCallback<ClickEvent>(OnClick)` — whether the method already exists compiled or was just added, and event `-=` unsubscribes as expected. Lambdas and closures work too, and delegate *values* combine (`a += b` on an `Action` local/field). Limits: at most 4 delegate parameters and no ref/out parameters. One caveat for a method group over a *new* method: the receiver is evaluated when the delegate fires, not captured at subscription, and `-=` of such a group won't match the `+=`.

**Coroutines:** a `[HotReload] IEnumerator` method picks up the new body at iterator **creation** — each `StartCoroutine` call uses the latest reload, while coroutines already running keep executing the body they were created from (use `[OnHotReload]` to stop/restart long-lived ones). `yield return null`, `WaitForSeconds`, and nested coroutines all work; `IEnumerable`/generic iterator returns are not supported. Two caveats: an exception thrown inside a reloaded coroutine body surfaces from the coroutine scheduler with no automatic fallback to the original body (unlike void methods), and on the interpreter backend a `try/finally` around a `yield` runs its `finally` on the normal path and on `Dispose`, but **not** when the body throws.

## Flavor 2 — override (`reload_file_override`)

When you want to swap behavior from a *separate* file (leaving the original untouched), tag the method with `[HotReloadWithOverrides]` and route it through `HotReloadHelper.ExecuteWithHotReload`:

```csharp
using Unity.Pipeline.HotReload;
using UnityEngine;

public class BossController : MonoBehaviour
{
    [HotReloadWithOverrides]
    void Update()
    {
        HotReloadHelper.ExecuteWithHotReload(this, "Update", OriginalUpdate);
    }

    public void OriginalUpdate()
    {
        transform.Rotate(45 * Time.deltaTime, 0, 0);
    }
}
```

Put the tweaked behavior in a **separate file** as a **`public static` method** tagged `[HotReloadOverrideMethod("Type.Method")]`. The override **takes the target instance as its first parameter**:

```csharp
// BossOverrides.cs — a separate file
using Unity.Pipeline.HotReload;
using UnityEngine;

public static class BossOverrides
{
    [HotReloadOverrideMethod("BossController.Update")]
    public static void TweakedUpdate(BossController instance)
    {
        instance.transform.Rotate(0, 90 * Time.deltaTime, 0); // new behaviour
    }
}
```

Apply the override file:

```bash
unity command reload_file_override "<absolute path>/BossOverrides.cs"
```

`reload_file_override` parameters:

| Arg | Default | Meaning |
|-----|---------|---------|
| `filename` | *(required)* | The override source file to compile. |
| `timeout` | `30000` | Compilation timeout (ms). |
| `assemblyDir` | `null` | Optional directory to also save the compiled assembly to disk (default: in-memory only). |

The override method's signature must be `public static <ReturnType> Name(<TargetType> instance, ...)` — return type and trailing parameters matching the original. Do **not** redeclare the target type in the override file (a common cause of "signature mismatch"). Until an override is applied, `ExecuteWithHotReload` calls `OriginalUpdate`; once applied, dispatch routes to the override.

`ExecuteWithHotReload` has matching overloads for both value-returning (`Func<object>`) and void (`Action`) originals:

```csharp
public static object ExecuteWithHotReload<T>(T instance, string methodName, Func<object> originalMethod, params object[] parameters);
public static void   ExecuteWithHotReload<T>(T instance, string methodName, Action originalMethod,       params object[] parameters);
```

## Reload callbacks — `[OnHotReload]`

Tag a **parameterless instance method** with `[OnHotReload]` to run code after a reload lands — a place to re-initialize state the swapped code depends on (think a domain-reload-free `OnEnable`):

```csharp
[OnHotReload]
void OnCodeReloaded()
{
    StopAllCoroutines();
    StartCoroutine(MainLoop()); // restart so the loop picks up the new body
}
```

It fires once per reload that changes at least one method on the declaring type, on **every live instance** (`UnityEngine.Object`-derived types only), on the main thread. Exceptions are logged, not propagated. Both flavors and the watcher trigger it. Typical uses: restarting long-lived coroutines and refreshing caches computed by the old code.

## Interpreter constraints

The `Assembly.Load` backend runs plain C# — anything Roslyn compiles works. The interpreter backend does not: it executes a **C# subset** against a **fixed host surface**, and both limits bite at different times.

**Language subset.** The shipped Roslyn analyzer (diagnostics **MS002–MS023**) marks violations in your IDE, on `[HotReload]` method bodies only. Not supported:

- `await` / `Task`, `try`/`catch`/`finally`, `lock`, LINQ query syntax (`from x in …`), `checked`
- `decimal`, `dynamic`, reflection types (`Type`, `MethodInfo`, …), threading types
- `ref`/`out`/`in` parameters, `ref` returns and locals, multi-dimensional arrays (`int[,]` — jagged `int[][]` is fine), generic methods
- declaring static fields, value-type nullables (`T?`), new type declarations inside the file, `yield` in a local function

`long`, `ulong`, and `double` work (the VM runs them in 64-bit slots). Lambdas, closures, delegates, `goto`, string interpolation, and coroutine `yield` all work — see the delegate and coroutine notes above for their edges.

**Host API surface.** Interpreted code can only call the UnityEngine/BCL members the interpreter exposes, plus the reloaded type's own members. In the editor, missing members bind on demand; on a device they cannot (IL2CPP strips unreferenced code), so an unexposed member throws when the reloaded code reaches it. A device push checks this up front and lists the missing members as warnings in the push response.

## Watching for changes

Instead of running `reload_file` by hand, let the editor watch for saves: **Window → Pipeline → Settings… → Hot Reload Interpreter Watch → Start Watching**. The watch covers the whole Assets tree; a saved `.cs` only compiles if it mentions `HotReload`.

Each save picks its target automatically:

- **A development player is connected** (Autoconnect Profiler, or attached via Profiler/Console) → the save compiles in the editor and is pushed to every connected player over PlayerConnection, always on the interpreter backend.
- **No player** → the save applies in this editor process, also on the interpreter backend.

Plugging in or disconnecting a device mid-watch reroutes the next save; no restart needed.

The workflow: start the watch, enter Play Mode, edit `[HotReload]` bodies, save. Points to know:

- **Auto-refresh is off while watching**, so a save hot-reloads instead of triggering a domain reload. Other assets won't import until you refresh manually (Cmd/Ctrl+R). **Stop Watching** restores your setting.
- Only methods that **changed since the watch started** reload; untouched ones keep running compiled.
- The watch **survives domain reloads**, re-applies overrides that a Play-Mode reload wiped, and re-pushes state to a player that reconnects (a restarted player boots the original code) — governed by the **Re-push On Connect** setting.
- The inspector shows liveness: watcher event count, last apply, and the resolved target. Compile errors and binding warnings land in the Console.

To push once without a watch, use the command directly:

```bash
# Compile a file's (or folder's, recursively) [HotReload] methods and push to connected players
unity command reload_file_player_interpreter "Assets/Scripts/Player.cs"

# Target one player by id (-1 = broadcast, the default)
unity command reload_file_player_interpreter "Assets/Scripts" --player 2
```

The push compiles with the *player's* preprocessor defines, needs the runtime Pipeline enabled in the player, and acks asynchronously to the editor console.

## Supporting commands

- `hotreload_status` — list which methods currently have a reload applied.
- `cleanup_hotreload` — delete saved hot-reload assemblies and revert every method to its compiled body.
- `reload_file_editor_interpreter` — `reload_file` on the interpreter backend (same parameters).
- `reload_file_player_interpreter` — compile in the editor, push to connected players (see above).

## How it works

An applied reload compiles the edited source **in memory** — nothing is written to disk unless you pass `assemblyDir`. The result then either loads next to the original assembly (`Assembly.Load` backend, Mono only) or is executed by the interpreter (works everywhere, including IL2CPP). Every `[HotReload]` method gets a small check injected when your project builds: on each call it runs the latest reloaded body if one exists, otherwise the original. That is why edits apply without touching call sites — and why a reload only ever changes tagged methods.

## See also

- [Runtime connection & setup](runtime-setup.md) — enabling hot reload in a Player build.
- [Command reference](commands/runtime.md)
