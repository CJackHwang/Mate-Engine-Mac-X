# Hot Reload - Minics

Demonstrates hot reloading with the minics vm. This allows hot reloading cs
scripts on all platforms (even on il2cpp players) with the constraint the the
script must only contain a subset of csharp.

## What's in this sample

| File | Purpose |
|------|---------|
| `pong.cs` | `PongScript`, a self-playing Pong game that can be hot reloaded. |
| `SampleScene.unity` | Scene with a GameObject carrying `PongScript`. |

`PongScript` is a self-playing arena Pong: two paddles orbit a circle and rally a
ball that switches colour on each hit.

## Try it

1. Import this sample and add `PongScene` to the built scene list.
2. Enable **developement build** and **autoconnect profiler** in the build settings (minics uses the profiler connection to reload the scripts.)
2. You may also want to enable run in background in the player settings if you are building for web.
3. Build and run the player. The paddles start rallying the ball automatically, and the profiler begins collecting data.
3. Make a change to pong.cs and push it with `unity command reload_file_player_interpreter "Assets/Samples/Unity Pipeline/0.2.0-exp.2/Hot Reload Examples/HotReload_Minics/pong.cs"` to see the reload live. (For automatic re-push on every save, use the Hot Reload Interpreter Watch section of the Pipeline Settings inspector.)

## How the hot reload works

- **`[HotReload]` on the class** opts every eligible method (`Start`, `Update`,
  `PlacePaddle`, `OnDisable`, …) into in-place reload. Editing any of them takes effect on the next `reload_file`.
- **`[OnHotReload]` on `OnReloaded()`** runs once after each reload, on every
  live instance. Here it tears down and rebuilds the scene (`OnDisable()` + `Start()`)
  so edits to setup code apply immediately.
- **Verification markers** `updateTicks` and `marker` are public fields you can read
  back over eval to confirm the freshly reloaded `Update` body is actually running.

Because in-place reloaded bodies may only touch **public** members of the target type,
every field and helper on `PongScript` is public.

## Minics

Minics is a subset of all C# features. This means that not all scripts will be able to
be hot reloaded. Consult the package documentation for more information on which
features are supported.
