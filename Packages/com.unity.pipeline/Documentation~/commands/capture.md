# Capture commands

Commands that render a camera, the Scene View, or a UI Toolkit element to a PNG and return it base64-encoded so an agent can "see" the editor (or a running player) without a display.

### `capture_game_view`
Render the game view to a PNG. Returns it inline as base64, unless `save_path` is set — then the result is **path-only** (no base64) so agent tool results stay small; pass `include_inline_image=true` to get both.

`source` selects what is captured. `camera` (default) renders a camera and **does not include Screen Space - Overlay UI** (HUDs, menus, damage numbers on overlay canvases) — overlay canvases composite straight to the screen and never render through a camera. `screen` captures the composited game view backbuffer, so overlay UI is included; because overlay UI only exists at runtime, `source=screen` **requires Play Mode** (in Edit Mode it returns a clear error — use `source=camera`). The screen capture reads the game view at its true pixel resolution — independent of OS display scaling — and preserves the on-screen color tone, so the PNG matches what the Game window shows.

Parameter interactions: `include_inline_image` is only meaningful together with `save_path` (without one, the inline image is always returned). `max_resolution` only applies when an inline image is actually returned — i.e. no `save_path`, or `save_path` + `include_inline_image=true`; it has **no effect** when `save_path` is set without `include_inline_image=true`. `camera` is ignored when `source=screen`.

| Parameter | Required | Default | Description |
|-----------|----------|---------|-------------|
| `width` | no | `1280` | Output width in px (default 1280; capped 4096). |
| `height` | no | `720` | Output height in px (default 720; capped 4096). |
| `camera` | no | `–` | Optional camera name (`source=camera` only); defaults to Camera.main, else the first enabled camera. |
| `save_path` | no | `–` | Optional project-relative path to write the PNG (e.g. Screenshots/foo.png). When set, the result omits the inline base64 image unless `include_inline_image=true`. |
| `include_inline_image` | no | `false` | Also return the image inline as base64 when `save_path` is set. |
| `max_resolution` | no | `0` | Cap on the inline image's longest edge (e.g. 512), preserving aspect ratio. No effect when `save_path` is set without `include_inline_image=true`. The `save_path` file keeps the requested resolution; a downscaled inline copy is reported via `inlineWidth`/`inlineHeight`. |
| `source` | no | `camera` | `camera` renders a camera (misses Screen Space - Overlay UI); `screen` captures the composited game view incl. overlay canvases (**Play Mode only**). |

**Returns:** `CaptureResult`

### `capture_scene_view`
Render the active Scene View to a PNG. Same result contract and parameter interactions as `capture_game_view`: inline base64, or path-only when `save_path` is set (opt back in with `include_inline_image=true`; `max_resolution` has no effect when `save_path` is set without it).

| Parameter | Required | Default | Description |
|-----------|----------|---------|-------------|
| `width` | no | `1280` | Output width in px (default 1280; capped 4096). |
| `height` | no | `720` | Output height in px (default 720; capped 4096). |
| `save_path` | no | `–` | Optional project-relative path to write the PNG (e.g. Screenshots/foo.png). When set, the result omits the inline base64 image unless `include_inline_image=true`. |
| `include_inline_image` | no | `false` | Also return the image inline as base64 when `save_path` is set. |
| `max_resolution` | no | `0` | Cap on the inline image's longest edge (e.g. 512), preserving aspect ratio. No effect when `save_path` is set without `include_inline_image=true`. The `save_path` file keeps the requested resolution; a downscaled inline copy is reported via `inlineWidth`/`inlineHeight`. |

**Returns:** `CaptureResult`

### `capture_editor_element`
Capture a UI Toolkit VisualElement (by selector) from an EditorWindow to a PNG; returns path + base64.

| Parameter | Required | Default | Description |
|-----------|----------|---------|-------------|
| `window` | yes | `–` | EditorWindow type name (e.g. InspectorWindow) or window title to capture from. |
| `selector` | yes | `–` | Element selector: '#name', '.class', a type name (e.g. Button), descendant (space) / child ('>') chains, optional pseudo-states (:checked,:hover,:focus,:active,:enabled,:disabled,:not(...)). |
| `output` | no | `–` | Output PNG path (absolute, or relative to the project root). Defaults to a timestamped file under <project>/Temp/pipeline-screenshots/. |

**Returns:** `CaptureElementResponse`
**Notes:** `MainThreadRequired = true`. Unity 6000.7+ only.

### `capture_runtime_element`
Capture a UI Toolkit VisualElement (by selector) from a live runtime panel (UIDocument or PanelRenderer) to a PNG; returns path + base64.

| Parameter | Required | Default | Description |
|-----------|----------|---------|-------------|
| `panel` | no | `–` | Name of the target panel: matches the PanelSettings asset name or the host GameObject name (UIDocument or PanelRenderer). Optional when exactly one panel exists. |
| `selector` | yes | `–` | Element selector: '#name', '.class', a type name (e.g. Button), descendant (space) / child ('>') chains, optional pseudo-states (:checked,:hover,:focus,:active,:enabled,:disabled,:not(...)). |
| `output` | no | `–` | Output PNG path (absolute, or relative to Application.persistentDataPath). Defaults to a timestamped file under Application.persistentDataPath. |

**Returns:** `CaptureElementResponse`
**Notes:** `MainThreadRequired = true`, `RuntimeOnly`. Unity 6000.7+ only.

See [Creating commands](../creating-commands.md) and [Connectivity](../connectivity.md).
