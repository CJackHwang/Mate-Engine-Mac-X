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
    public GameObject uniWindowControllerObject;
    public Button applyButton, resetButton;
    public bool resetAlsoClearsAllowedApps = false;
    public VRMLoader vrmLoader;

    private bool isSliderBeingDragged;
    public static bool IsMenuOpen { get; private set; }

    private UniWindowController uniWindowController;

    private void Start()
    {
        if (menuPanel != null)
        {
            menuPanel.SetActive(false);
            IsMenuOpen = false;
        }

        if (uniWindowControllerObject != null)
            uniWindowController = uniWindowControllerObject.GetComponent<UniWindowController>();
        else
            uniWindowController = FindObjectOfType<UniWindowController>();

        LoadSettings();
        ApplySettings();

        applyButton?.onClick.AddListener(ApplySettings);
        resetButton?.onClick.AddListener(ResetToDefaults);

        foreach (var slider in new[] { soundThresholdSlider, idleSwitchTimeSlider, idleTransitionTimeSlider, avatarSizeSlider, fpsLimitSlider })
            AddSliderListeners(slider);

        foreach (var toggle in new[] { enableAudioDetectionToggle, enableDancingToggle, enableMouseTrackingToggle, isTopmostToggle })
            toggle?.onValueChanged.AddListener(delegate { });
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

        if (uniWindowController != null)
            uniWindowController.isTopmost = data.isTopmost;

        foreach (var limiter in FindObjectsByType<FPSLimiter>(FindObjectsSortMode.None))
            limiter.SetFPSLimit(data.fpsLimit);

        SaveLoadHandler.Instance.SaveToDisk();
        SaveLoadHandler.ApplyAllSettingsToAllAvatars(); // Apply globally
    }

    public void ResetToDefaults()
    {
        var data = new SaveLoadHandler.SettingsData();

        if (!resetAlsoClearsAllowedApps)
            data.allowedApps = new List<string>(SaveLoadHandler.Instance.data.allowedApps); // preserve list

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
