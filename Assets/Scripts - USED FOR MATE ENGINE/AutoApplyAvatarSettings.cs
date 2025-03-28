using UnityEngine;

public class AutoApplyAvatarSettings : MonoBehaviour
{
    private void Start()
    {
        // Wait one frame to ensure all VRMs and components are in scene
        StartCoroutine(DelayedApply());
    }

    private System.Collections.IEnumerator DelayedApply()
    {
        yield return null; // Wait one frame

        AvatarSettingsMenu settingsMenu = FindObjectOfType<AvatarSettingsMenu>();
        if (settingsMenu != null)
        {
            Debug.Log("[AutoApplyAvatarSettings] Applying settings on startup...");
            settingsMenu.LoadSettings();   // Optional but safe
            settingsMenu.ApplySettings();  // Applies to all avatars
        }
        else
        {
            Debug.LogWarning("[AutoApplyAvatarSettings] AvatarSettingsMenu not found!");
        }
    }
}