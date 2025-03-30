using UnityEngine;

public class FPSLimiter : MonoBehaviour
{
    [Range(15, 120)] // Slider in Inspector (Min: 10 FPS, Max: 240 FPS)
    public int targetFPS = 60;

    private int previousFPS;

    void Start()
    {
        // Load FPS from PlayerPrefs
        targetFPS = PlayerPrefs.GetInt("FPSLimit", 60);
        ApplyFPSLimit();
    }

    void Update()
    {
        if (targetFPS != previousFPS) // Detect changes in Inspector
        {
            ApplyFPSLimit();
        }
    }

    public void ApplyFPSLimit()
    {
        Application.targetFrameRate = targetFPS;
        QualitySettings.vSyncCount = 0; // Disable VSync to enforce FPS cap
        previousFPS = targetFPS;
        PlayerPrefs.SetInt("FPSLimit", targetFPS);
        PlayerPrefs.Save();
        Debug.Log("FPS set to: " + targetFPS);
    }

    // New public method for AvatarSettingsMenu to change FPS dynamically
    public void SetFPSLimit(int fps)
    {
        targetFPS = Mathf.Clamp(fps, 10, 240);
        ApplyFPSLimit();
    }
}
