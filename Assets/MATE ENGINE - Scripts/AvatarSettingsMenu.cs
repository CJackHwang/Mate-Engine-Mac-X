using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;
using UnityEngine.EventSystems;
using Kirurobo;

public class AvatarSettingsMenu : MonoBehaviour
{
    public GameObject menuPanel;
    public Slider soundThresholdSlider, idleSwitchTimeSlider, idleTransitionTimeSlider, avatarSizeSlider, fpsLimitSlider;
    public Toggle enableAudioDetectionToggle, enableDancingToggle, enableMouseTrackingToggle;
    public Toggle isTopmostToggle;
    public Toggle enableParticlesToggle; // NEW
    public GameObject uniWindowControllerObject;
    public Button applyButton, resetButton;
    public bool resetAlsoClearsAllowedApps = false;
    public VRMLoader vrmLoader;

    public Toggle fakeHDRToggle;
    public Toggle bloomToggle;
    public Toggle dayNightToggle;

    public GameObject fakeHDRObject;
    public GameObject bloomObject;
    public GameObject dayNightObject;

    public Button windowSizeButton;

    private bool isSliderBeingDragged;
    public static bool IsMenuOpen { get; private set; }

    private UniWindowController uniWindowController;
    private AvatarParticleHandler currentParticleHandler; // NEW

    private void Start()
    {
        if (menuPanel != null)
        {
            menuPanel.SetActive(false);
            IsMenuOpen = false;
        }

        windowSizeButton?.onClick.AddListener(CycleWindowSize);

        if (uniWindowControllerObject != null)
            uniWindowController = uniWindowControllerObject.GetComponent<UniWindowController>();
        else
            uniWindowController = FindObjectOfType<UniWindowController>();

        currentParticleHandler = FindObjectOfType<AvatarParticleHandler>(true); // Find active avatar handler

        LoadSettings();
        ApplySettings();

        applyButton?.onClick.AddListener(ApplySettings);
        resetButton?.onClick.AddListener(ResetToDefaults);

        foreach (var slider in new[] { soundThresholdSlider, idleSwitchTimeSlider, idleTransitionTimeSlider, avatarSizeSlider, fpsLimitSlider })
            AddSliderListeners(slider);

        foreach (var toggle in new[] {
            enableAudioDetectionToggle, enableDancingToggle, enableMouseTrackingToggle,
            isTopmostToggle, enableParticlesToggle
        })
            toggle?.onValueChanged.AddListener(delegate { });

        RestoreWindowSize();
    }

    private void CycleWindowSize()
    {
        var data = SaveLoadHandler.Instance.data;
        var controller = uniWindowController ?? UniWindowController.current;

        switch (data.windowSizeState)
        {
            case SaveLoadHandler.SettingsData.WindowSizeState.Normal:
                data.windowSizeState = SaveLoadHandler.SettingsData.WindowSizeState.Big;
                controller.windowSize = new Vector2(2048, 1536);
                break;

            case SaveLoadHandler.SettingsData.WindowSizeState.Big:
                data.windowSizeState = SaveLoadHandler.SettingsData.WindowSizeState.Small;
                controller.windowSize = new Vector2(768, 512);
                break;

            case SaveLoadHandler.SettingsData.WindowSizeState.Small:
                data.windowSizeState = SaveLoadHandler.SettingsData.WindowSizeState.Normal;
                controller.windowSize = new Vector2(1536, 1024);
                break;
        }

        SaveLoadHandler.Instance.SaveToDisk();
    }

    private void Update()
    {
        if ((Input.GetKeyDown(KeyCode.M) || Input.GetMouseButtonDown(1)) && menuPanel != null)
        {
            bool newState = !menuPanel.activeSelf;
            menuPanel.SetActive(newState);
            IsMenuOpen = newState;

            if (newState)
            {
                var appManager = FindObjectOfType<AllowedAppsManager>();
                if (appManager != null)
                {
                    appManager.RefreshUI();
                }
            }
        }
    }

    public void LoadSettings()
    {
        var data = SaveLoadHandler.Instance.data;

        soundThresholdSlider?.SetValueWithoutNotify(data.soundThreshold);
        idleSwitchTimeSlider?.SetValueWithoutNotify(data.idleSwitchTime);
        idleTransitionTimeSlider?.SetValueWithoutNotify(data.idleTransitionTime);
        avatarSizeSlider?.SetValueWithoutNotify(data.avatarSize);
        fpsLimitSlider?.SetValueWithoutNotify(data.fpsLimit);
        enableAudioDetectionToggle?.SetIsOnWithoutNotify(data.enableAudioDetection);
        enableDancingToggle?.SetIsOnWithoutNotify(data.enableDancing);
        enableMouseTrackingToggle?.SetIsOnWithoutNotify(data.enableMouseTracking);
        isTopmostToggle?.SetIsOnWithoutNotify(data.isTopmost);
        enableParticlesToggle?.SetIsOnWithoutNotify(data.enableParticles); // NEW

        fakeHDRToggle?.SetIsOnWithoutNotify(data.fakeHDR);
        bloomToggle?.SetIsOnWithoutNotify(data.bloom);
        dayNightToggle?.SetIsOnWithoutNotify(data.dayNight);

        RestoreWindowSize();
    }

    public void ApplySettings()
    {
        var data = SaveLoadHandler.Instance.data;

        data.soundThreshold = soundThresholdSlider?.value ?? 0.2f;
        data.idleSwitchTime = idleSwitchTimeSlider?.value ?? 10f;
        data.idleTransitionTime = idleTransitionTimeSlider?.value ?? 1f;
        data.avatarSize = avatarSizeSlider?.value ?? 1.0f;
        data.fpsLimit = (int)(fpsLimitSlider?.value ?? 90);
        data.enableAudioDetection = enableAudioDetectionToggle?.isOn ?? true;
        data.enableDancing = enableDancingToggle?.isOn ?? true;
        data.enableMouseTracking = enableMouseTrackingToggle?.isOn ?? true;
        data.isTopmost = isTopmostToggle?.isOn ?? true;
        data.enableParticles = enableParticlesToggle?.isOn ?? true; // NEW

        data.fakeHDR = fakeHDRToggle?.isOn ?? true;
        data.bloom = bloomToggle?.isOn ?? true;
        data.dayNight = dayNightToggle?.isOn ?? true;

        if (fakeHDRObject != null) fakeHDRObject.SetActive(data.fakeHDR);
        if (bloomObject != null) bloomObject.SetActive(data.bloom);
        if (dayNightObject != null) dayNightObject.SetActive(data.dayNight);

        if (currentParticleHandler == null)
            currentParticleHandler = FindObjectOfType<AvatarParticleHandler>(true);

        if (currentParticleHandler != null)
        {
            currentParticleHandler.featureEnabled = data.enableParticles;
            currentParticleHandler.enabled = data.enableParticles;
        }

        if (uniWindowController != null)
            uniWindowController.isTopmost = data.isTopmost;

        foreach (var limiter in FindObjectsByType<FPSLimiter>(FindObjectsSortMode.None))
            limiter.SetFPSLimit(data.fpsLimit);

        SaveLoadHandler.Instance.SaveToDisk();
        SaveLoadHandler.ApplyAllSettingsToAllAvatars();
        RestoreWindowSize();
    }

    private void RestoreWindowSize()
    {
        var data = SaveLoadHandler.Instance.data;
        var controller = uniWindowController ?? UniWindowController.current;

        switch (data.windowSizeState)
        {
            case SaveLoadHandler.SettingsData.WindowSizeState.Small:
                controller.windowSize = new Vector2(768, 512);
                break;
            case SaveLoadHandler.SettingsData.WindowSizeState.Big:
                controller.windowSize = new Vector2(2048, 1536);
                break;
            default:
                controller.windowSize = new Vector2(1536, 1024);
                break;
        }
    }

    public void ResetToDefaults()
    {
        var oldSizeState = SaveLoadHandler.Instance.data.windowSizeState;
        var data = new SaveLoadHandler.SettingsData();
        data.windowSizeState = oldSizeState;

        if (!resetAlsoClearsAllowedApps)
            data.allowedApps = new List<string>(SaveLoadHandler.Instance.data.allowedApps);

        SaveLoadHandler.Instance.data = data;

        LoadSettings();
        ApplySettings();

        if (vrmLoader != null)
            vrmLoader.ResetModel();
    }

    private void AddSliderListeners(Slider slider)
    {
        if (slider == null) return;

        var trigger = slider.gameObject.GetComponent<EventTrigger>() ?? slider.gameObject.AddComponent<EventTrigger>();

        var down = new EventTrigger.Entry { eventID = EventTriggerType.PointerDown };
        down.callback.AddListener((eventData) => isSliderBeingDragged = true);
        trigger.triggers.Add(down);

        var up = new EventTrigger.Entry { eventID = EventTriggerType.PointerUp };
        up.callback.AddListener((eventData) => isSliderBeingDragged = false);
        trigger.triggers.Add(up);
    }
}
