using Kirurobo;
using UnityEngine;
using UnityEngine.UI;

public class SettingsHandlerButtons : MonoBehaviour
{
    public Button applyButton;
    public Button resetButton;
    public Button windowSizeButton;
    public Button refreshAppsListButton;

    public SettingsHandlerToggles togglesHandler;
    public SettingsHandlerSliders slidersHandler;
    public SettingsHandlerDropdowns dropdownsHandler;
    public SettingsHandlerAudio audioHandler;
    public SettingsHandlerLights lightsHandler;
    public SettingsHandlerAccessory accessoryHandler;
    public SettingsHandlerBigScreen bigScreenHandler;

    public VRMLoader vrmLoader;
    public GameObject uniWindowControllerObject;
    private UniWindowController uniWindowController;

    private void Start()
    {
        if (applyButton != null) applyButton.onClick.AddListener(OnApplyClicked);
        if (resetButton != null) resetButton.onClick.AddListener(OnResetClicked);
        if (windowSizeButton != null) windowSizeButton.onClick.AddListener(CycleWindowSize);
        if (refreshAppsListButton != null) refreshAppsListButton.onClick.AddListener(OnRefreshAppsClicked);

        if (uniWindowControllerObject != null)
            uniWindowController = uniWindowControllerObject.GetComponent<UniWindowController>();
        else
            uniWindowController = FindFirstObjectByType<UniWindowController>();
    }

    private void OnApplyClicked()
    {
        togglesHandler?.ApplySettings();
        slidersHandler?.ApplySettings();
        dropdownsHandler?.ApplySettings();
        audioHandler?.ApplySettings();
        lightsHandler?.ApplySettings();
        accessoryHandler?.ApplySettings();
        bigScreenHandler?.ApplySettings();
        SaveLoadHandler.Instance.SaveToDisk();
        SaveLoadHandler.ApplyAllSettingsToAllAvatars();
    }

    private void OnResetClicked()
    {
        togglesHandler?.ResetToDefaults();
        slidersHandler?.ResetToDefaults();
        dropdownsHandler?.ResetToDefaults();
        audioHandler?.ResetToDefaults();
        lightsHandler?.ResetAllLightsToDefault();
        lightsHandler?.ResetAllLightTogglesToDefault();
        accessoryHandler?.ResetToDefaults();
        bigScreenHandler?.ResetToDefaults();
        if (vrmLoader != null) vrmLoader.ResetModel();
        SaveLoadHandler.Instance.SaveToDisk();
    }

    private void CycleWindowSize()
    {
        var data = SaveLoadHandler.Instance.data;
        var controller = uniWindowController ?? UniWindowController.current;
        switch (data.windowSizeState)
        {
            case SaveLoadHandler.SettingsData.WindowSizeState.Normal:
                data.windowSizeState = SaveLoadHandler.SettingsData.WindowSizeState.Big;
                controller.windowSize = new Vector2(2048, 1536); break;
            case SaveLoadHandler.SettingsData.WindowSizeState.Big:
                data.windowSizeState = SaveLoadHandler.SettingsData.WindowSizeState.Small;
                controller.windowSize = new Vector2(768, 512); break;
            case SaveLoadHandler.SettingsData.WindowSizeState.Small:
                data.windowSizeState = SaveLoadHandler.SettingsData.WindowSizeState.Normal;
                controller.windowSize = new Vector2(1536, 1024); break;
        }
        SaveLoadHandler.Instance.SaveToDisk();
    }

    private void OnRefreshAppsClicked()
    {
        var appManager = FindFirstObjectByType<AllowedAppsManager>();
        if (appManager != null) appManager.RefreshUI();
    }
}
