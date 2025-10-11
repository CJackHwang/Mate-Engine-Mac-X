# [Mate Engine](https://github.com/shinyflvre/Mate-Engine) - Custom Dance Player

## Technical Documentation

For more detailed instructions, refer to [MateEngine-CustomDancePlayer](https://github.com/maoxig/MateEngine-CustomDancePlayer).

### Directory Structure

```
.
├── MATE ENGINE - Custom Dance Player
│   ├── Core        // Core scripts
│   ├── Prefab      // Prefabs
│   └── Utils       // Utility scripts
├── README_player.md
```

### Script Overview

The components of this player are clearly organized by functionality. Feel free to modify them as needed.

#### Core
- **DanceAvatarHelper.cs**: Provides helper methods for Avatar management, including: retrieving and caching the current Avatar and Animator, setting audio sources, detecting and updating Avatars, handling MMD facial BlendShapes, and more. Notable points::
  - `SetupDummyForDance()`
  - `SetupMMDBlendshapeSMR()`
    - Both methods enhance facial expressions during dances. Dance files follow the VRC MMD World standard, using the Japanese MMD facial curve to control the `BlendShape` of the model's `Body` (`SkinnedMeshRenderer`).  
    - To maximize compatibility, after switching characters, `SetupDummyForDance()` checks if the model already has a `SkinnedMeshRenderer` with Japanese MMD BlendShapes. If so, it renames the GameObject to "Body" for animation curve control. Otherwise, `SetupMMDBlendshapeSMR()` manually adds a prepared DummyMesh (with common Japanese MMD BlendShapes) to the character, names it "Body", and uses `DummyToUniversalSync.cs` to sync DummyMesh BlendShapes to `UniversalBlendshapes`, enabling broad facial expression compatibility.
  - `CustomDanceAvatarController`: This runtimeAnimatorController overrides the character's original AnimatorController. Using the dance file's controller directly caused Unity to repeatedly log warnings about missing parameters, leading to memory issues. Therefore, a custom AnimatorController with all required parameters is used for dance playback. Further improvements are being explored.

- **DancePlayerCore.cs**: The main player script. Notable points:
  - In `PlayDanceByIndex`, the presence of `TargetSMR` determines whether to disable `UniversalBlendshapes`. If not disabled, it may override animation-driven facial expressions in LateUpdate.
  - The `WaitForAudioThenStartAnimation` method ensures the audio file starts playing before the animation, preventing desynchronization due to DSF buffer issues. A manual sync slider is provided for precise adjustment.

- **DancePlayerUIManager.cs**: Manages all UI functions. Key methods:
  - `SetPanelVisible(bool visible)`: Controls UI panel visibility.
  - `AddMyUIToGameMenuList()`: Previously used to add UI to the game's circle menu; now mostly obsolete.
  - `HandleKeyToggleUI()`: Handles toggling UI visibility via hotkey; retained for convenience.

- **DanceResourceManager.cs**: Manages dance resources. The method for loading runtimeAnimatorController is commented out (not deleted) for future consideration, as loading the full controller may cause warnings.
- **DanceSettingsHandler.cs**: Handles saving/loading player settings. Follows SSOT principles; logic is straightforward.

#### Utils
- **DanceCameraDistKeeper.cs**: Maintains a fixed distance (z-axis) between the dance camera and character hips to prevent model clipping or disappearance.
- **DanceShadowFollower.cs**: Ensures the character's shadow follows movement, preventing shadow loss or panel occlusion during lateral or backward movement.
- **DanceWindowFollower.cs**: Keeps the Unity window, main camera, and related objects following the character's hips. This ensures the character remains in view during large movements, though it may cause window stuttering or out-of-bounds issues.
- **DummyToUniversalSync.cs**: Syncs DummyMesh BlendShapes to `UniversalBlendshapes`.
- **HipsFollower.cs** & **UIDragHandler.cs**: Allow the UI panel to follow character hips even when dragged.
- **SMRHandle.cs**: Handles `SkinnedMeshRenderer` functions. Currently, it enables `updateWhenOffscreen` for all renderers after character switch to reduce model part disappearance.
- **GlobalHotkeyListener.cs**: Adds a system-level global hotkey listener (default: `Ctrl+Alt+>` for play/pause). Useful for multi-character dances with multiple app instances. Hotkey customization is recommended but not yet implemented.

#### Prefab

- **CustomDancePlayer.prefab**: Contains all player components.
- **DummyBlendshapeMesh.asset**: Mesh with common Japanese MMD BlendShapes, dynamically added to the model's Body for facial animation compatibility, synced via `DummyToUniversalSync`.
- **CUSTOM_DANCE.anim**: Placeholder animation, replaced by dance file animation for playback.
- **DANCE_END.anim**: Placeholder animation played at dance end, triggers `OnAnimationEnd` for automatic next dance switching (see `DancePlayerAvatarProxy.cs`).
- **CustomDanceAvatarController.controller**: AnimatorController with all MateEngine AvatarAnimatorController parameters, overrides character's original controller; simple logic and state machine.

### Dance File Instructions

Download all converted dance files and a sample pre-packaged dance file here:  
https://drive.google.com/drive/folders/1YU7-Hz-O8-9B2E58mxQxexJTBTCT42jr?usp=sharing

Creating a dance file (.unity3d) is easy: In Unity, package a `.anim` animation file, a corresponding `.controller` file, and an `.mp3`/`.ogg`/`.wav` audio file into an AssetBundle.

#### Dance File Structure (.unity3d AssetBundle)
Each dance uses a `.unity3d` AssetBundle containing the following resources (consistent naming recommended):

| Resource Type       | File Naming Convention          | Description                                                                                                                                                                                                          |
| ------------------- | ------------------------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Animator Controller | `[DanceName].controller`        | Simple single-state controller: Create an Animator Controller, drag the `[DanceName].anim` file into the controller window. Unity auto-generates a state, and the "Entry" transitions unconditionally to this state. |
| Audio File          | `[DanceName].wav`/`.mp3`/`.ogg` | Supports 3 common formats; duration must match animation length to avoid desynchronization.                                                                                                                          |
| Animation           | `[DanceName].anim`              | Contains dance animation data.                                                                                                                                                                                       |

#### Additional Notes

For optimal MMD facial and camera support, follow these standards when creating dance files:

**Facial expressions**: Use standard Japanese MMD BlendShapes (e.g., `あいうえお`), controlled via animation curves on the model's `Body` (`SkinnedMeshRenderer`).

**Camera animation**: Create `Camera_root/Camera_root_1/Camera` under the model. `Camera_root` controls overall position, `Camera_root_1` controls distance (z-axis), and the Camera component manages FOV. After creating the camera hierarchy as above, add an initial 180° rotation on the y-axis to `Camera_root`.


For MMD dance file creation, see: https://github.com/maoxig/VroidMMDTools