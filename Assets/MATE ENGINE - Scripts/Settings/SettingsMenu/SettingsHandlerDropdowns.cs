using UnityEngine;
using TMPro;

public class SettingsHandlerDropdowns : MonoBehaviour
{
    public TMP_Dropdown graphicsDropdown;

    private void Start()
    {
        if (graphicsDropdown != null)
        {
            graphicsDropdown.ClearOptions();
            graphicsDropdown.AddOptions(new System.Collections.Generic.List<string> {
                "ULTRA", "VERY HIGH", "HIGH", "NORMAL", "LOW"
            });
            graphicsDropdown.onValueChanged.AddListener(OnGraphicsChanged);
        }
        LoadSettings();
        ApplySettings();
    }

    private void OnGraphicsChanged(int index)
    {
        SaveLoadHandler.Instance.data.graphicsQualityLevel = index;
        QualitySettings.SetQualityLevel(index, true);
        SaveLoadHandler.Instance.SaveToDisk();
    }

    public void LoadSettings()
    {
        var data = SaveLoadHandler.Instance.data;
        graphicsDropdown?.SetValueWithoutNotify(data.graphicsQualityLevel);
        QualitySettings.SetQualityLevel(data.graphicsQualityLevel, true);
    }

    public void ApplySettings()
    {
        var data = SaveLoadHandler.Instance.data;
        data.graphicsQualityLevel = graphicsDropdown?.value ?? data.graphicsQualityLevel;
        QualitySettings.SetQualityLevel(data.graphicsQualityLevel, true);
    }

    public void ResetToDefaults()
    {
        graphicsDropdown?.SetValueWithoutNotify(1);
        QualitySettings.SetQualityLevel(1, true);

        var data = SaveLoadHandler.Instance.data;
        data.graphicsQualityLevel = 1;

        SaveLoadHandler.Instance.SaveToDisk();
    }

}
