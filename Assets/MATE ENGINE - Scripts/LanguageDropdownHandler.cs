using UnityEngine;
using TMPro;
using UnityEngine.Localization.Settings;

public class LanguageDropdownHandler : MonoBehaviour
{
    [SerializeField] private TMP_Dropdown languageDropdown;

    private bool isInitializing = true;

    private void Start()
    {
        var locales = LocalizationSettings.AvailableLocales.Locales;
        string savedCode = SaveLoadHandler.Instance.data.selectedLocaleCode;

        // Find saved locale index
        int index = locales.FindIndex(locale => locale.Identifier.Code == savedCode);
        if (index < 0) index = 0; // fallback

        languageDropdown.SetValueWithoutNotify(index);
        LocalizationSettings.SelectedLocale = locales[index];

        languageDropdown.onValueChanged.AddListener(OnLanguageChanged);
        isInitializing = false;
    }

    private void OnLanguageChanged(int index)
    {
        if (isInitializing) return;

        var selected = LocalizationSettings.AvailableLocales.Locales[index];
        LocalizationSettings.SelectedLocale = selected;

        // Save to settings
        SaveLoadHandler.Instance.data.selectedLocaleCode = selected.Identifier.Code;
        SaveLoadHandler.Instance.SaveToDisk();
    }
}
