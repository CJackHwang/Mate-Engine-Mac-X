using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;
using UnityEngine.EventSystems;
using Kirurobo;
using TMPro;

public class AvatarSettingsMenu : MonoBehaviour
{
    public GameObject menuPanel;
    public Slider soundThresholdSlider, idleSwitchTimeSlider, idleTransitionTimeSlider, avatarSizeSlider, fpsLimitSlider;
    public Toggle enableAudioDetectionToggle, enableDancingToggle, enableMouseTrackingToggle;
    public Toggle isTopmostToggle;
    public Toggle enableParticlesToggle;
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

    public Slider petVolumeSlider;
    public Slider effectsVolumeSlider;
    public Slider menuVolumeSlider;

    public List<AudioSource> petAudioSources = new List<AudioSource>();
    public List<AudioSource> effectsAudioSources = new List<AudioSource>();
    public List<AudioSource> menuAudioSources = new List<AudioSource>();

    public TMP_Dropdown graphicsDropdown;



    public Button windowSizeButton;

    public static bool IsMenuOpen { get; private set; }

    private UniWindowController uniWindowController;
    private AvatarParticleHandler currentParticleHandler;


    [System.Serializable]
    public class AccessoryToggleEntry
    {
        public string ruleName;
        public Toggle toggle;
    }

    public List<AccessoryToggleEntry> accessoryToggleBindings = new List<AccessoryToggleEntry>();


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
            uniWindowController = FindFirstObjectByType<UniWindowController>();

        var particleHandlers = FindObjectsByType<AvatarParticleHandler>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        currentParticleHandler = particleHandlers.Length > 0 ? particleHandlers[0] : null;

        applyButton?.onClick.AddListener(ApplySettings);
        resetButton?.onClick.AddListener(ResetToDefaults);

        foreach (var slider in new[] { soundThresholdSlider, idleSwitchTimeSlider, idleTransitionTimeSlider, avatarSizeSlider, fpsLimitSlider })
            AddSliderListeners(slider);

        foreach (var toggle in new[] {
        enableAudioDetectionToggle, enableDancingToggle, enableMouseTrackingToggle,
        isTopmostToggle, enableParticlesToggle
    })
            toggle?.onValueChanged.AddListener(delegate { });

        if (graphicsDropdown != null)
        {
            graphicsDropdown.ClearOptions();
            graphicsDropdown.AddOptions(new List<string> {
            "ULTRA", "VERY HIGH", "HIGH", "NORMAL", "LOW"
        });

            graphicsDropdown.onValueChanged.AddListener((index) =>
            {
                QualitySettings.SetQualityLevel(index, true);
                SaveLoadHandler.Instance.data.graphicsQualityLevel = index;
                SaveLoadHandler.Instance.SaveToDisk();
            });

            graphicsDropdown.SetValueWithoutNotify(SaveLoadHandler.Instance.data.graphicsQualityLevel);
            QualitySettings.SetQualityLevel(SaveLoadHandler.Instance.data.graphicsQualityLevel, true);
        }
        LoadSettings();
        ApplySettings();
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
        if (Input.GetMouseButtonDown(1) && menuPanel != null)
        {
            bool newState = !menuPanel.activeSelf;
            menuPanel.SetActive(newState);
            IsMenuOpen = newState;

            if (newState)
            {
                var appManager = FindFirstObjectByType<AllowedAppsManager>();
                if (appManager != null)
                {
                    appManager.RefreshUI();
                }
            }
        }
    }

    public void LoadSettings()
    {

        foreach (var entry in accessoryToggleBindings)
        {
            if (string.IsNullOrEmpty(entry.ruleName) || entry.toggle == null) continue;

            if (SaveLoadHandler.Instance.data.accessoryStates.TryGetValue(entry.ruleName, out bool state))
            {
                entry.toggle.SetIsOnWithoutNotify(state);
            }
        }

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
        enableParticlesToggle?.SetIsOnWithoutNotify(data.enableParticles);

        fakeHDRToggle?.SetIsOnWithoutNotify(data.fakeHDR);
        bloomToggle?.SetIsOnWithoutNotify(data.bloom);
        dayNightToggle?.SetIsOnWithoutNotify(data.dayNight);

        petVolumeSlider?.SetValueWithoutNotify(data.petVolume);
        effectsVolumeSlider?.SetValueWithoutNotify(data.effectsVolume);
        menuVolumeSlider?.SetValueWithoutNotify(data.menuVolume);

        if (graphicsDropdown != null)
        {
            graphicsDropdown.SetValueWithoutNotify(data.graphicsQualityLevel);
            QualitySettings.SetQualityLevel(data.graphicsQualityLevel, true);
        }

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
        data.enableParticles = enableParticlesToggle?.isOn ?? true;

        data.fakeHDR = fakeHDRToggle?.isOn ?? true;
        data.bloom = bloomToggle?.isOn ?? true;
        data.dayNight = dayNightToggle?.isOn ?? true;

        data.petVolume = petVolumeSlider?.value ?? 1f;
        data.effectsVolume = effectsVolumeSlider?.value ?? 1f;
        data.menuVolume = menuVolumeSlider?.value ?? 1f;

        foreach (var entry in accessoryToggleBindings)
        {
            if (string.IsNullOrEmpty(entry.ruleName) || entry.toggle == null) continue;

            bool isOn = entry.toggle.isOn;
            SaveLoadHandler.Instance.data.accessoryStates[entry.ruleName] = isOn;

            // Apply directly to matching AccessoryRule
            foreach (var handler in AccessoiresHandler.ActiveHandlers)
            {
                foreach (var rule in handler.rules)
                {
                    if (rule.ruleName == entry.ruleName)
                    {
                        rule.isEnabled = isOn;
                        break;
                    }
                }
            }
        }



        if (graphicsDropdown != null)
        {
            data.graphicsQualityLevel = graphicsDropdown.value;
            QualitySettings.SetQualityLevel(graphicsDropdown.value, true);
        }

        if (fakeHDRObject != null) fakeHDRObject.SetActive(data.fakeHDR);
        if (bloomObject != null) bloomObject.SetActive(data.bloom);
        if (dayNightObject != null) dayNightObject.SetActive(data.dayNight);

        if (currentParticleHandler == null)
        {
            var particleHandlers = FindObjectsByType<AvatarParticleHandler>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            currentParticleHandler = particleHandlers.Length > 0 ? particleHandlers[0] : null;
        }

        if (currentParticleHandler != null)
        {
            currentParticleHandler.featureEnabled = data.enableParticles;
            currentParticleHandler.enabled = data.enableParticles;
        }

        if (uniWindowController != null)
            uniWindowController.isTopmost = data.isTopmost;

        foreach (var limiter in FindObjectsByType<FPSLimiter>(FindObjectsSortMode.None))
            limiter.SetFPSLimit(data.fpsLimit);

        UpdateAllCategoryVolumes();

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

        data.petVolume = 1f;
        data.effectsVolume = 1f;
        data.menuVolume = 1f;
        data.graphicsQualityLevel = 1;


        data.accessoryStates = new Dictionary<string, bool>();
        foreach (var entry in accessoryToggleBindings)
        {
            if (!string.IsNullOrEmpty(entry.ruleName))
                data.accessoryStates[entry.ruleName] = false;
        }


        SaveLoadHandler.Instance.data = data;

        foreach (var handler in AccessoiresHandler.ActiveHandlers)
        {
            handler.ResetAccessoryStatesToDefault();
            handler.ClearAccessoryStatesFromSave();
        }

        SaveLoadHandler.Instance.SaveToDisk(); 
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
        down.callback.AddListener((eventData) => { });
        trigger.triggers.Add(down);

        var up = new EventTrigger.Entry { eventID = EventTriggerType.PointerUp };
        up.callback.AddListener((eventData) => { });
        trigger.triggers.Add(up);
    }

    private void UpdateAllCategoryVolumes()
    {
        float petVolume = petVolumeSlider?.value ?? 1f;
        float effectsVolume = effectsVolumeSlider?.value ?? 1f;
        float menuVolume = menuVolumeSlider?.value ?? 1f;

        foreach (var src in petAudioSources)
            if (src != null) src.volume = GetBaseVolume(src) * petVolume;

        foreach (var src in effectsAudioSources)
            if (src != null) src.volume = GetBaseVolume(src) * effectsVolume;

        foreach (var src in menuAudioSources)
            if (src != null) src.volume = GetBaseVolume(src) * menuVolume;
    }

    private Dictionary<AudioSource, float> baseVolumes = new Dictionary<AudioSource, float>();

    private float GetBaseVolume(AudioSource src)
    {
        if (src == null) return 1f;
        if (!baseVolumes.TryGetValue(src, out float baseVol))
        {
            baseVol = src.volume;
            baseVolumes[src] = baseVol;
        }
        return baseVol;
    }

}
