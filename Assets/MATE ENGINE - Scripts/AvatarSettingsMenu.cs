using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;
using UnityEngine.EventSystems;
using Kirurobo; // For UniWindowController

public class AvatarSettingsMenu : MonoBehaviour
{
    public GameObject menuPanel;
    public Slider soundThresholdSlider, idleSwitchTimeSlider, idleTransitionTimeSlider, totalIdleAnimationsSlider, avatarSizeSlider, fpsLimitSlider;
    public Toggle enableAudioDetectionToggle, enableDraggingToggle, enableDancingToggle, enableHeadTrackingToggle, enableEyeTrackingToggle;
    // NEW FIELDS for UniWindowController and Is Topmost
    public Toggle isTopmostToggle;
    public GameObject uniWindowControllerObject;
    public Button applyButton, resetButton;
    public AudioSource audioSource;
    public List<AudioClip> uiSounds;
    public VRMLoader vrmLoader; // Drag your VRMLoader object in the Inspector
    private bool isSliderBeingDragged;
    public static bool IsMenuOpen { get; private set; }


    // We'll hold a reference to the actual UniWindowController
    private UniWindowController uniWindowController;

    private void Start()
    {
        if (menuPanel != null)
        {
            menuPanel.SetActive(false);
            IsMenuOpen = false;
        }


        LoadSettings();
        ApplySettings();
        applyButton?.onClick.AddListener(() => { ApplySettings(); PlayUISound(); });
        resetButton?.onClick.AddListener(() => { ResetToDefaults(); PlayUISound(); });

        foreach (var slider in new[] { soundThresholdSlider, idleSwitchTimeSlider, idleTransitionTimeSlider, totalIdleAnimationsSlider, avatarSizeSlider, fpsLimitSlider })
        {
            AddSliderListeners(slider);
        }

        // Include isTopmostToggle in the toggles that produce a UI sound
        foreach (var toggle in new[] { enableAudioDetectionToggle, enableDraggingToggle, enableDancingToggle, isTopmostToggle })
        {
            toggle?.onValueChanged.AddListener(delegate { PlayUISound(); });
        }

        // Set up the FPS limit slider's range and default
        if (fpsLimitSlider != null)
        {
            fpsLimitSlider.minValue = 10;
            fpsLimitSlider.maxValue = 240;
            fpsLimitSlider.value = PlayerPrefs.GetInt("FPSLimit", 90);
            fpsLimitSlider.onValueChanged.AddListener(delegate { UpdateFPSLimit(); PlayUISound(); });
        }
    }

    private void Update()
    {
        if ((Input.GetKeyDown(KeyCode.M) || Input.GetMouseButtonDown(1)) && menuPanel != null)
        {
            bool newState = !menuPanel.activeSelf;
            menuPanel.SetActive(newState);
            IsMenuOpen = newState;
            PlayUISound();
        }
    }


    public void UpdateFPSLimit()
    {
        foreach (var fpsLimiter in FindObjectsByType<FPSLimiter>(FindObjectsSortMode.None))
        {
            fpsLimiter.SetFPSLimit((int)fpsLimitSlider.value);
        }
        PlayerPrefs.SetInt("FPSLimit", (int)fpsLimitSlider.value);
        PlayerPrefs.Save();
    }
    public void LoadSettings()
    {
        foreach (var avatar in FindObjectsByType<AvatarAnimatorController>(FindObjectsSortMode.None))
        {
            if (soundThresholdSlider != null) soundThresholdSlider.value = PlayerPrefs.GetFloat("SoundThreshold", avatar.SOUND_THRESHOLD);
            if (idleSwitchTimeSlider != null) idleSwitchTimeSlider.value = PlayerPrefs.GetFloat("IdleSwitchTime", avatar.IDLE_SWITCH_TIME);
            if (idleTransitionTimeSlider != null) idleTransitionTimeSlider.value = PlayerPrefs.GetFloat("IdleTransitionTime", avatar.IDLE_TRANSITION_TIME);
            if (totalIdleAnimationsSlider != null) totalIdleAnimationsSlider.value = PlayerPrefs.GetInt("TotalIdleAnimations", avatar.totalIdleAnimations);
            if (avatarSizeSlider != null) avatarSizeSlider.value = PlayerPrefs.GetFloat("AvatarSize", avatar.transform.localScale.x);
            if (fpsLimitSlider != null) fpsLimitSlider.value = PlayerPrefs.GetInt("FPSLimit", 90);
            if (enableAudioDetectionToggle != null) enableAudioDetectionToggle.isOn = PlayerPrefs.GetInt("EnableAudioDetection", avatar.enableAudioDetection ? 1 : 0) == 1;
            if (enableDraggingToggle != null) enableDraggingToggle.isOn = PlayerPrefs.GetInt("EnableDragging", avatar.enableDragging ? 1 : 0) == 1;
            if (enableDancingToggle != null) enableDancingToggle.isOn = PlayerPrefs.GetInt("EnableDancing", avatar.enableDancing ? 1 : 0) == 1;
            if (enableHeadTrackingToggle != null)
                enableHeadTrackingToggle.isOn = PlayerPrefs.GetInt("EnableHeadTracking", 1) == 1;
            if (enableEyeTrackingToggle != null)
                enableEyeTrackingToggle.isOn = PlayerPrefs.GetInt("EnableEyeTracking", 1) == 1;
        }

        if (isTopmostToggle != null)
        {
            isTopmostToggle.isOn = PlayerPrefs.GetInt("IsTopmost", 1) == 1;
        }
    }
    public void ApplySettings()
    {
        foreach (var avatar in FindObjectsByType<AvatarAnimatorController>(FindObjectsSortMode.None))
        {
            avatar.SOUND_THRESHOLD = soundThresholdSlider?.value ?? avatar.SOUND_THRESHOLD;
            avatar.IDLE_SWITCH_TIME = idleSwitchTimeSlider?.value ?? avatar.IDLE_SWITCH_TIME;
            avatar.IDLE_TRANSITION_TIME = idleTransitionTimeSlider?.value ?? avatar.IDLE_TRANSITION_TIME;
            avatar.totalIdleAnimations = (int)(totalIdleAnimationsSlider?.value ?? avatar.totalIdleAnimations);
            if (avatarSizeSlider != null) avatar.transform.localScale = Vector3.one * avatarSizeSlider.value;
            avatar.enableAudioDetection = enableAudioDetectionToggle?.isOn ?? avatar.enableAudioDetection;
            avatar.enableDragging = enableDraggingToggle?.isOn ?? avatar.enableDragging;
            avatar.enableDancing = enableDancingToggle?.isOn ?? avatar.enableDancing;
            if (enableHeadTrackingToggle != null)
            {
                foreach (var headTracker in avatar.GetComponentsInChildren<AvatarControllerHeadTracking>())
                {
                    headTracker.enableHeadTracking = enableHeadTrackingToggle.isOn;
                }
            }
            if (enableEyeTrackingToggle != null)
            {
                foreach (var eyeTracker in avatar.GetComponentsInChildren<AvatarControllerEyeTracking>())
                {
                    eyeTracker.enableEyeTracking = enableEyeTrackingToggle.isOn;
                }
            }
        }

        if (uniWindowController != null && isTopmostToggle != null)
        {
            uniWindowController.isTopmost = isTopmostToggle.isOn;
        }

        UpdateFPSLimit();
        SaveSettings();
    }
    public void ResetToDefaults()
    {
        soundThresholdSlider?.SetValueWithoutNotify(0.2f);
        idleSwitchTimeSlider?.SetValueWithoutNotify(10f);
        idleTransitionTimeSlider?.SetValueWithoutNotify(1f);
        totalIdleAnimationsSlider?.SetValueWithoutNotify(5);
        avatarSizeSlider?.SetValueWithoutNotify(1.0f);
        enableAudioDetectionToggle?.SetIsOnWithoutNotify(true);
        enableDraggingToggle?.SetIsOnWithoutNotify(false);
        enableDancingToggle?.SetIsOnWithoutNotify(true);
        enableHeadTrackingToggle?.SetIsOnWithoutNotify(true);
        enableEyeTrackingToggle?.SetIsOnWithoutNotify(true);
        fpsLimitSlider?.SetValueWithoutNotify(90);

        // Also default IsTopmost to true
        isTopmostToggle?.SetIsOnWithoutNotify(true);

        UpdateFPSLimit();
        SaveSettings();

        // NEW: Re-inject the default model
        if (vrmLoader != null)
        {
            vrmLoader.LoadDefaultModel();
        }
    }

    private void SaveSettings()
    {
        PlayerPrefs.SetFloat("SoundThreshold", soundThresholdSlider?.value ?? 0.2f);
        PlayerPrefs.SetFloat("IdleSwitchTime", idleSwitchTimeSlider?.value ?? 10f);
        PlayerPrefs.SetFloat("IdleTransitionTime", idleTransitionTimeSlider?.value ?? 1f);
        PlayerPrefs.SetInt("TotalIdleAnimations", (int)(totalIdleAnimationsSlider?.value ?? 5));
        PlayerPrefs.SetFloat("AvatarSize", avatarSizeSlider?.value ?? 1.0f);
        PlayerPrefs.SetInt("EnableAudioDetection", enableAudioDetectionToggle?.isOn == true ? 1 : 0);
        PlayerPrefs.SetInt("EnableDragging", enableDraggingToggle?.isOn == true ? 1 : 0);
        PlayerPrefs.SetInt("EnableDancing", enableDancingToggle?.isOn == true ? 1 : 0);
        PlayerPrefs.SetInt("EnableHeadTracking", enableHeadTrackingToggle?.isOn == true ? 1 : 0);
        PlayerPrefs.SetInt("EnableEyeTracking", enableEyeTrackingToggle?.isOn == true ? 1 : 0);
        PlayerPrefs.SetInt("FPSLimit", (int)(fpsLimitSlider?.value ?? 90));
        // Save the IsTopmost state
        PlayerPrefs.SetInt("IsTopmost", isTopmostToggle?.isOn == true ? 1 : 0);
        PlayerPrefs.Save();
    }

    private void PlayUISound()
    {
        if (audioSource != null && uiSounds.Count > 0)
        {
            audioSource.PlayOneShot(uiSounds[Random.Range(0, uiSounds.Count)]);
        }
    }

    private void AddSliderListeners(Slider slider)
    {
        if (slider == null) return;
        var trigger = slider.gameObject.GetComponent<EventTrigger>() ?? slider.gameObject.AddComponent<EventTrigger>();
        trigger.triggers.Add(new EventTrigger.Entry { eventID = EventTriggerType.PointerDown, callback = new EventTrigger.TriggerEvent() });
        trigger.triggers[0].callback.AddListener((eventData) => {
            if (!isSliderBeingDragged)
            {
                PlayUISound();
                isSliderBeingDragged = true;
            }
        });
        trigger.triggers.Add(new EventTrigger.Entry { eventID = EventTriggerType.PointerUp, callback = new EventTrigger.TriggerEvent() });
        trigger.triggers[1].callback.AddListener((eventData) => isSliderBeingDragged = false);
    }
}
