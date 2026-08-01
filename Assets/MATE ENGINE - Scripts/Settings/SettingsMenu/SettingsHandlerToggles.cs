using Kirurobo;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Localization.Components;

public class SettingsHandlerToggles : MonoBehaviour
{
    [Header("Toggles")]
    public Toggle enableDancingToggle;
    public Toggle enableMouseTrackingToggle;
    public Toggle isTopmostToggle;
    public Toggle enableParticlesToggle;
    public Toggle bloomToggle;
    public Toggle dayNightToggle;
    public Toggle enableWindowSittingToggle;
    public Toggle enableDiscordRPCToggle;
    public Toggle enableHandHoldingToggle;
    public Toggle ambientOcclusionToggle;
    public Toggle enableIKToggle;
    public Toggle enableDanceSwitchToggle;
    public Toggle followMusicToggle;
    public Toggle enableRandomMessagesToggle;
    public Toggle enableHusbandoModeToggle;
    public Toggle enableAutoMemoryTrimToggle;
    public Toggle enableMinecraftMessagesToggle;
    public Toggle enableFeedSystemToggle;
    public Toggle enableRandomAvatarToggle;
    public Toggle enableLocomotionToggle;

    [Header("External Objects")]
    public GameObject bloomObject;
    public GameObject dayNightObject;
    public GameObject ambientOcclusionObject;
    public GameObject uniWindowControllerObject;

    private UniWindowController uniWindowController;
    private AvatarParticleHandler currentParticleHandler;

    void Start()
    {
        if (uniWindowControllerObject != null)
            uniWindowController = uniWindowControllerObject.GetComponent<UniWindowController>();
        else
            uniWindowController = FindAnyObjectByType<UniWindowController>();
        enableDancingToggle?.onValueChanged.AddListener(OnEnableDancingChanged);
        enableMouseTrackingToggle?.onValueChanged.AddListener(OnEnableMouseTrackingChanged);
        isTopmostToggle?.onValueChanged.AddListener(OnIsTopmostChanged);
        enableParticlesToggle?.onValueChanged.AddListener(OnEnableParticlesChanged);
        bloomToggle?.onValueChanged.AddListener(OnBloomChanged);
        dayNightToggle?.onValueChanged.AddListener(OnDayNightChanged);
        enableWindowSittingToggle?.onValueChanged.AddListener(OnEnableWindowSittingChanged);
        enableDiscordRPCToggle?.onValueChanged.AddListener(OnEnableDiscordRPCChanged);
        enableHandHoldingToggle?.onValueChanged.AddListener(OnEnableHandHoldingChanged);
        ambientOcclusionToggle?.onValueChanged.AddListener(OnAmbientOcclusionChanged);
        enableIKToggle?.onValueChanged.AddListener(OnEnableIKChanged);
        enableDanceSwitchToggle?.onValueChanged.AddListener(OnEnableDanceSwitchChanged);
        EnsureFollowMusicToggle();
        followMusicToggle?.onValueChanged.AddListener(OnFollowMusicChanged);
        enableRandomMessagesToggle?.onValueChanged.AddListener(OnEnableRandomMessagesChanged);
        enableHusbandoModeToggle?.onValueChanged.AddListener(OnEnableHusbandoModeChanged);
        enableAutoMemoryTrimToggle?.onValueChanged.AddListener(OnEnableAutoMemoryTrimChanged);
        enableMinecraftMessagesToggle?.onValueChanged.AddListener(OnEnableMinecraftMessagesChanged);
        enableFeedSystemToggle?.onValueChanged.AddListener(OnEnableFeedSystemChanged);
        enableRandomAvatarToggle?.onValueChanged.AddListener(OnEnableRandomAvatarChanged);
        enableLocomotionToggle?.onValueChanged.AddListener(OnEnableLocomotionChanged);
        LoadSettings();
        ApplySettings();
    }

    #region Toggle Callbacks

    private void OnEnableDancingChanged(bool v) { SaveLoadHandler.Instance.data.enableDancing = v; ApplySettings(); Save(); }
    private void OnEnableMouseTrackingChanged(bool v) { SaveLoadHandler.Instance.data.enableMouseTracking = v; ApplySettings(); Save(); }
    private void OnIsTopmostChanged(bool v) { SaveLoadHandler.Instance.data.isTopmost = v; ApplySettings(); Save(); }
    private void OnEnableParticlesChanged(bool v) { SaveLoadHandler.Instance.data.enableParticles = v; ApplySettings(); Save(); }
    private void OnBloomChanged(bool v) { SaveLoadHandler.Instance.data.bloom = v; ApplySettings(); Save(); }
    private void OnDayNightChanged(bool v) { SaveLoadHandler.Instance.data.dayNight = v; ApplySettings(); Save(); }
    private void OnEnableWindowSittingChanged(bool v) { SaveLoadHandler.Instance.data.enableWindowSitting = v; ApplySettings(); if (!v) { var handlers = FindObjectsByType<AvatarWindowHandler>(FindObjectsInactive.Include); foreach (var handler in handlers) handler.ForceExitWindowSitting(); } Save(); }
    private void OnEnableDiscordRPCChanged(bool v) { SaveLoadHandler.Instance.data.enableDiscordRPC = v; ApplySettings(); Save(); }
    private void OnEnableHandHoldingChanged(bool v) { SaveLoadHandler.Instance.data.enableHandHolding = v; ApplySettings(); Save(); }
    private void OnAmbientOcclusionChanged(bool v) { SaveLoadHandler.Instance.data.ambientOcclusion = v; ApplySettings(); Save(); }
    private void OnEnableIKChanged(bool v) { SaveLoadHandler.Instance.data.enableIK = v; ApplySettings(); Save(); }
    private void OnEnableDanceSwitchChanged(bool v) { SaveLoadHandler.Instance.data.enableDanceSwitch = v; Save(); }
    private void OnFollowMusicChanged(bool v)
    {
        SaveLoadHandler.Instance.data.followMusic = v;
        Save(); // pushes avatar.followMusic via ApplyAllSettingsToAllAvatars
    }
    private void OnEnableAutoMemoryTrimChanged(bool v) { SaveLoadHandler.Instance.data.enableAutoMemoryTrim = v; ApplySettings(); Save(); }
    private void OnEnableRandomMessagesChanged(bool v)
    {
        SaveLoadHandler.Instance.data.enableRandomMessages = v;
        ApplySettings();
        Save();
    }
    private void OnEnableHusbandoModeChanged(bool v)
    {
        SaveLoadHandler.Instance.data.enableHusbandoMode = v;
        ApplySettings();
        Save();
    }
    private void OnEnableMinecraftMessagesChanged(bool v)
    {
        SaveLoadHandler.Instance.data.enableMinecraftMessages = v;
        ApplySettings();
        Save();
    }
    private void OnEnableFeedSystemChanged(bool v)
    {
        SaveLoadHandler.Instance.data.enableFeedSystem = v;
        ApplySettings();
        Save();
    }
    private void OnEnableRandomAvatarChanged(bool v) { SaveLoadHandler.Instance.data.enableRandomAvatar = v; Save(); }
    private void OnEnableLocomotionChanged(bool v) { SaveLoadHandler.Instance.data.enableLocomotion = v; ApplySettings(); Save(); }

    #endregion

    // The dance "follow music" row is cloned at runtime from the existing
    // "EnableDanceTransitions" toggle row — same approach as EnsureCliffOffsetSlider
    // in SettingsHandlerSliders — to avoid hand-editing the fragile scene YAML.
    private void EnsureFollowMusicToggle()
    {
        if (followMusicToggle != null || enableDanceSwitchToggle == null) return;
        Transform row = enableDanceSwitchToggle.transform;
        if (row == null || row.parent == null) return;

        GameObject clone = Instantiate(row.gameObject, row.parent);
        clone.transform.SetSiblingIndex(row.GetSiblingIndex() + 1);
        clone.name = "FollowMusic";

        RectTransform rt = clone.GetComponent<RectTransform>();
        RectTransform srcRT = row as RectTransform;
        if (rt != null && srcRT != null)
        {
            // Source sits at y = -140; next left-column row is at -240, so -200 is free.
            rt.anchoredPosition = new Vector2(srcRT.anchoredPosition.x, srcRT.anchoredPosition.y - 60f);
        }

        Toggle t = clone.GetComponent<Toggle>();
        if (t != null)
        {
            t.onValueChanged.RemoveAllListeners(); // drop the cloned handlers
            followMusicToggle = t;
        }

        const string label = "跟随音乐自动跳舞";
        foreach (var tmp in clone.GetComponentsInChildren<TMPro.TextMeshProUGUI>(true))
        {
            // Disable the cloned LocalizedStringEvent so it can't overwrite our label.
            var lse = tmp.GetComponent<LocalizeStringEvent>();
            if (lse != null) lse.enabled = false;
            tmp.text = label;
            break;
        }
        foreach (var txt in clone.GetComponentsInChildren<UnityEngine.UI.Text>(true))
        {
            var lse = txt.GetComponent<LocalizeStringEvent>();
            if (lse != null) lse.enabled = false;
            txt.text = label;
            break;
        }

        var tooltip = clone.GetComponent<UiTooltip>();
        if (tooltip != null)
        {
            tooltip.locKey = "";
            tooltip.tooltipText = "开启时，角色随系统播放器播放的音乐自动跳舞；关闭时，打开“跳舞”开关即直接跳舞。";
        }
    }

    public void LoadSettings()
    {
        var data = SaveLoadHandler.Instance.data;
        enableDancingToggle?.SetIsOnWithoutNotify(data.enableDancing);
        enableMouseTrackingToggle?.SetIsOnWithoutNotify(data.enableMouseTracking);
        isTopmostToggle?.SetIsOnWithoutNotify(data.isTopmost);
        enableParticlesToggle?.SetIsOnWithoutNotify(data.enableParticles);
        bloomToggle?.SetIsOnWithoutNotify(data.bloom);
        dayNightToggle?.SetIsOnWithoutNotify(data.dayNight);
        enableWindowSittingToggle?.SetIsOnWithoutNotify(data.enableWindowSitting);
        enableDiscordRPCToggle?.SetIsOnWithoutNotify(data.enableDiscordRPC);
        enableHandHoldingToggle?.SetIsOnWithoutNotify(data.enableHandHolding);
        ambientOcclusionToggle?.SetIsOnWithoutNotify(data.ambientOcclusion);
        enableIKToggle?.SetIsOnWithoutNotify(data.enableIK);
        enableDanceSwitchToggle?.SetIsOnWithoutNotify(data.enableDanceSwitch);
        followMusicToggle?.SetIsOnWithoutNotify(data.followMusic);
        enableRandomMessagesToggle?.SetIsOnWithoutNotify(data.enableRandomMessages);
        enableHusbandoModeToggle?.SetIsOnWithoutNotify(data.enableHusbandoMode);
        enableAutoMemoryTrimToggle?.SetIsOnWithoutNotify(data.enableAutoMemoryTrim);
        enableMinecraftMessagesToggle?.SetIsOnWithoutNotify(data.enableMinecraftMessages);
        enableFeedSystemToggle?.SetIsOnWithoutNotify(SaveLoadHandler.Instance.data.enableFeedSystem);
        enableRandomAvatarToggle?.SetIsOnWithoutNotify(SaveLoadHandler.Instance.data.enableRandomAvatar);
        enableLocomotionToggle?.SetIsOnWithoutNotify(data.enableLocomotion);
        ApplySettings();
    }

    public void ApplySettings()
    {
        var data = SaveLoadHandler.Instance.data;

        // Random Messages
        foreach (var arm in Resources.FindObjectsOfTypeAll<AvatarRandomMessages>())
        {
            arm.enableRandomMessages = data.enableRandomMessages;
            if (data.enableRandomMessages && arm.isActiveAndEnabled)
            {
                arm.StopAllCoroutines();
                arm.StartCoroutine("RandomMessageLoop");
            }
            else
            {
                arm.StopAllCoroutines();
            }
        }

        foreach (var mt in Resources.FindObjectsOfTypeAll<MemoryTrim>())
            mt.SetAutoTrimEnabled(data.enableAutoMemoryTrim);


        // Visuals
        if (bloomObject != null) bloomObject.SetActive(data.bloom);
        if (dayNightObject != null) dayNightObject.SetActive(data.dayNight);
        if (ambientOcclusionObject != null) ambientOcclusionObject.SetActive(data.ambientOcclusion);

        // Window
        if (uniWindowController == null)
            uniWindowController = FindAnyObjectByType<UniWindowController>();
        if (uniWindowController != null)
            uniWindowController.isTopmost = data.isTopmost;

        // Food
        foreach (var c in Resources.FindObjectsOfTypeAll<AvatarFoodController>())
            c.SetFeatureEnabled(SaveLoadHandler.Instance.data.enableFeedSystem);


        // Particles
        if (currentParticleHandler == null)
        {
            var handlers = FindObjectsByType<AvatarParticleHandler>(FindObjectsInactive.Include);
            currentParticleHandler = handlers.Length > 0 ? handlers[0] : null;
        }
        if (currentParticleHandler != null)
        {
            currentParticleHandler.featureEnabled = data.enableParticles;
            currentParticleHandler.enabled = data.enableParticles;
        }
        PetVoiceReactionHandler.GlobalHoverObjectsEnabled = data.enableParticles;

        foreach (var amm in Resources.FindObjectsOfTypeAll<AvatarMinecraftMessages>())
            amm.enableMinecraftMessages = data.enableMinecraftMessages;

        foreach (var loco in Resources.FindObjectsOfTypeAll<AvatarLocomotionController>())
            loco.EnableLocomotion = data.enableLocomotion;

    }

    public void ResetToDefaults()
    {
        enableDancingToggle?.SetIsOnWithoutNotify(true);
        enableMouseTrackingToggle?.SetIsOnWithoutNotify(true);
        isTopmostToggle?.SetIsOnWithoutNotify(true);
        enableParticlesToggle?.SetIsOnWithoutNotify(true);
        bloomToggle?.SetIsOnWithoutNotify(false);
        dayNightToggle?.SetIsOnWithoutNotify(true);
        enableWindowSittingToggle?.SetIsOnWithoutNotify(false);
        enableDiscordRPCToggle?.SetIsOnWithoutNotify(true);
        enableHandHoldingToggle?.SetIsOnWithoutNotify(true);
        ambientOcclusionToggle?.SetIsOnWithoutNotify(false);
        enableIKToggle?.SetIsOnWithoutNotify(true);
        enableDanceSwitchToggle?.SetIsOnWithoutNotify(false);
        followMusicToggle?.SetIsOnWithoutNotify(true);
        enableRandomMessagesToggle?.SetIsOnWithoutNotify(false);
        enableHusbandoModeToggle?.SetIsOnWithoutNotify(false);
        enableAutoMemoryTrimToggle?.SetIsOnWithoutNotify(false);
        enableMinecraftMessagesToggle?.SetIsOnWithoutNotify(false);
        enableFeedSystemToggle?.SetIsOnWithoutNotify(false);
        enableRandomAvatarToggle?.SetIsOnWithoutNotify(false);
        enableLocomotionToggle?.SetIsOnWithoutNotify(false);
        SaveLoadHandler.Instance.data.enableLocomotion = false;


        var data = SaveLoadHandler.Instance.data;
        data.enableDancing = true;
        data.enableMouseTracking = true;
        data.isTopmost = true;
        data.enableParticles = true;
        data.bloom = false;
        data.dayNight = true;
        data.enableWindowSitting = false;
        data.enableDiscordRPC = true;
        data.enableHandHolding = true;
        data.ambientOcclusion = false;
        data.enableIK = true;
        data.enableDanceSwitch = false;
        data.followMusic = true;
        data.enableRandomMessages = false;
        data.enableHusbandoMode = false;
        data.enableAutoMemoryTrim = false;
        data.enableFeedSystem = false;
        data.enableMinecraftMessages = false;
        SaveLoadHandler.Instance.SaveToDisk();
        ApplySettings();
    }

    private void Save()
    {
        SaveLoadHandler.Instance.SaveToDisk();
        SaveLoadHandler.ApplyAllSettingsToAllAvatars();
    }
}