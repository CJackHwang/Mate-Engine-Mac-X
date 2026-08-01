using UnityEngine;
using UnityEngine.Rendering.PostProcessing;

// Disables the global ambient-occlusion post-processing Volume while the avatar
// is enlarged on screen ("big screen" / screensaver / alarm), where the AO
// shadows show ugly moiré patterns on the enlarged model. It blends the Volume's
// weight down to 0 (and disables it) so AO fully disappears, then restores it as
// soon as the enlarged state ends.
public class AmbientOcclusionAutoHide : MonoBehaviour
{
    private PostProcessVolume volume;
    private bool lastEnlarged;

    void Awake()
    {
        if (volume == null) volume = GetComponent<PostProcessVolume>();
        UnityEngine.Debug.Log("[AmbientOcclusionAutoHide] volume=" + (volume != null ? "ok" : "null") + ", originalWeight=" + (volume != null ? volume.weight.ToString() : "?"));
    }

    void Update()
    {
        if (volume == null) volume = GetComponent<PostProcessVolume>();
        if (volume == null) return;

        bool enlarged = IsAnyAvatarBigScreen();
        if (enlarged != lastEnlarged)
        {
            lastEnlarged = enlarged;
            UnityEngine.Debug.Log("[AmbientOcclusionAutoHide] enlarged=" + enlarged);
        }
        bool want = !enlarged;
        // PPv2 blends a Volume by its weight; weight 0 fully removes its effect.
        if (Mathf.Abs(volume.weight - (want ? 1f : 0f)) > 0.01f)
        {
            volume.weight = want ? 1f : 0f;
            if (!want) volume.enabled = false; else volume.enabled = true;
            UnityEngine.Debug.Log("[AmbientOcclusionAutoHide] -> volume.weight=" + volume.weight);
        }
    }

    // The big-screen flag may be set on any avatar's animator (there can be more
    // than one), and AvatarBigScreenHandler also sets BlockDraggingOverride while
    // enlarged — check both signals so detection is reliable.
    bool IsAnyAvatarBigScreen()
    {
        var all = Resources.FindObjectsOfTypeAll<AvatarAnimatorController>();
        for (int i = 0; i < all.Length; i++)
        {
            if (all[i] == null) continue;
            if (all[i].BlockDraggingOverride) return true;
            if (all[i].animator != null && all[i].animator.GetBool("isBigScreen")) return true;
        }
        return false;
    }
}
