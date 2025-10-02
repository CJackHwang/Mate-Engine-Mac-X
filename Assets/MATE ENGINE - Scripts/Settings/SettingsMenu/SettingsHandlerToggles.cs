using Kirurobo;
using UnityEngine;
using UnityEngine.UI;

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
    public Toggle enableRandomMessagesToggle; 


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
            uniWindowController = FindFirstObjectByType<UniWindowController>();
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
        enableRandomMessagesToggle?.onValueChanged.AddListener(OnEnableRandomMessagesChanged);

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
    private void OnEnableWindowSittingChanged(bool v) { SaveLoadHandler.Instance.data.enableWindowSitting = v; ApplySettings(); if (!v){ var handlers = FindObjectsByType<AvatarWindowHandler>(FindObjectsInactive.Include, FindObjectsSortMode.None); foreach (var handler in handlers) handler.ForceExitWindowSitting(); } Save(); }
    private void OnEnableDiscordRPCChanged(bool v) { SaveLoadHandler.Instance.data.enableDiscordRPC = v; ApplySettings(); Save(); }
    private void OnEnableHandHoldingChanged(bool v) { SaveLoadHandler.Instance.data.enableHandHolding = v; ApplySettings(); Save(); }
    private void OnAmbientOcclusionChanged(bool v) { SaveLoadHandler.Instance.data.ambientOcclusion = v; ApplySettings(); Save(); }
    private void OnEnableIKChanged(bool v) { SaveLoadHandler.Instance.data.enableIK = v; ApplySettings(); Save(); }
    private void OnEnableDanceSwitchChanged(bool v) { SaveLoadHandler.Instance.data.enableDanceSwitch = v; Save(); }
    private void OnEnableRandomMessagesChanged(bool v)
    {
        SaveLoadHandler.Instance.data.enableRandomMessages = v;
        ApplySettings();
        Save();
    }


    #endregion

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
        enableRandomMessagesToggle?.SetIsOnWithoutNotify(data.enableRandomMessages);
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


        // Visuals
        if (bloomObject != null) bloomObject.SetActive(data.bloom);
        if (dayNightObject != null) dayNightObject.SetActive(data.dayNight);
        if (ambientOcclusionObject != null) ambientOcclusionObject.SetActive(data.ambientOcclusion);

        // Window
        if (uniWindowController == null)
            uniWindowController = FindFirstObjectByType<UniWindowController>();
        if (uniWindowController != null)
            uniWindowController.isTopmost = data.isTopmost;

        // Particles
        if (currentParticleHandler == null)
        {
            var handlers = FindObjectsByType<AvatarParticleHandler>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            currentParticleHandler = handlers.Length > 0 ? handlers[0] : null;
        }
        if (currentParticleHandler != null)
        {
            currentParticleHandler.featureEnabled = data.enableParticles;
            currentParticleHandler.enabled = data.enableParticles;
        }
        PetVoiceReactionHandler.GlobalHoverObjectsEnabled = data.enableParticles;
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
        enableRandomMessagesToggle?.SetIsOnWithoutNotify(false);

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
        data.enableRandomMessages = false;

        SaveLoadHandler.Instance.SaveToDisk();
        ApplySettings();
    }

    private void Save()
    {
        SaveLoadHandler.Instance.SaveToDisk();
        SaveLoadHandler.ApplyAllSettingsToAllAvatars();
    }
}