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
    private readonly System.Collections.Generic.List<AvatarAnimatorController> cachedAvatars = new();
    private float refreshTimer;

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
    //
    // Cached: FindObjectsOfTypeAll is expensive, so we refresh the avatar list at
    // most every 0.5s instead of scanning the whole scene every frame (avoids GC /
    // frame hitches that make the app feel unresponsive).
    bool IsAnyAvatarBigScreen()
    {
        refreshTimer -= Time.unscaledDeltaTime;
        if (refreshTimer <= 0f)
        {
            refreshTimer = 0.5f;
            cachedAvatars.Clear();
            var all = Resources.FindObjectsOfTypeAll<AvatarAnimatorController>();
            for (int i = 0; i < all.Length; i++)
                if (all[i] != null) cachedAvatars.Add(all[i]);
        }

        for (int i = 0; i < cachedAvatars.Count; i++)
        {
            var a = cachedAvatars[i];
            if (a == null) continue; // destroyed avatar; refreshed on next tick
            if (a.BlockDraggingOverride) return true;
            if (a.animator != null && a.animator.GetBool("isBigScreen")) return true;
        }
        return false;
    }
}
