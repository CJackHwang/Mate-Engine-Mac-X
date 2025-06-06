using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;

public class SettingsHandlerAccessory : MonoBehaviour
{
    [System.Serializable]
    public class AccessoryToggleEntry
    {
        public string ruleName;
        public Toggle toggle;
    }

    public List<AccessoryToggleEntry> accessoryToggleBindings = new List<AccessoryToggleEntry>();

    private bool isResetting = false;

    private void Start()
    {
        SetupListeners();
        LoadSettings();
    }

    public void SetupListeners()
    {
        foreach (var entry in accessoryToggleBindings)
        {
            if (!string.IsNullOrEmpty(entry.ruleName) && entry.toggle != null)
            {
                string key = entry.ruleName;
                entry.toggle.onValueChanged.AddListener((v) =>
                {
                    if (isResetting) return; // nicht auf Event reagieren beim Reset!
                    SaveLoadHandler.Instance.data.accessoryStates[key] = v;
                    UpdateAccessoryObjects();
                    SaveLoadHandler.Instance.SaveToDisk();
                });
            }
        }
    }

    public void LoadSettings()
    {
        foreach (var entry in accessoryToggleBindings)
        {
            if (!string.IsNullOrEmpty(entry.ruleName) && entry.toggle != null)
            {
                bool state = false;
                SaveLoadHandler.Instance.data.accessoryStates.TryGetValue(entry.ruleName, out state);
                entry.toggle.SetIsOnWithoutNotify(state);
            }
        }
        UpdateAccessoryObjects();
    }

    public void ApplySettings()
    {
        UpdateAccessoryObjects();
    }

    public void ResetToDefaults()
    {
        isResetting = true;

        // States und UI updaten ohne Events zu triggern
        foreach (var entry in accessoryToggleBindings)
        {
            if (!string.IsNullOrEmpty(entry.ruleName))
            {
                SaveLoadHandler.Instance.data.accessoryStates[entry.ruleName] = false;
                if (entry.toggle != null)
                    entry.toggle.SetIsOnWithoutNotify(false);
            }
        }

        // Accessoire-Objekte im Scene aus
        foreach (var handler in AccessoiresHandler.ActiveHandlers)
        {
            foreach (var rule in handler.rules)
            {
                rule.isEnabled = false;
                if (rule.linkedObject != null)
                    rule.linkedObject.SetActive(false);
            }
        }

        SaveLoadHandler.Instance.SaveToDisk();
        isResetting = false;

        // Jetzt alles neu laden, damit es KEINEN Sprung gibt
        LoadSettings();
    }

    private void UpdateAccessoryObjects()
    {
        foreach (var entry in accessoryToggleBindings)
        {
            bool toggleOn = entry.toggle != null && entry.toggle.isOn;
            foreach (var handler in AccessoiresHandler.ActiveHandlers)
            {
                foreach (var rule in handler.rules)
                {
                    if (rule.ruleName == entry.ruleName)
                        rule.isEnabled = toggleOn;
                }
            }
        }
    }
}
