using TMPro;
using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.Localization.Settings;

/// <summary>
/// Sets a TMP (or legacy Text) label from the "Languages (UI)" table and
/// re-applies it whenever the selected locale changes or localization finishes
/// initializing. Used for text that is assigned from code (e.g. runtime-cloned
/// settings rows) where no LocalizeStringEvent is present.
/// </summary>
[DisallowMultipleComponent]
public class LocTextBinder : MonoBehaviour
{
    public string key = "";
    [TextArea(1, 4)] public string fallback = "";

    void OnEnable()
    {
        LocalizationSettings.SelectedLocaleChanged += OnLocaleChanged;
        if (LocalizationSettings.InitializationOperation.IsDone)
        {
            Apply();
        }
        else
        {
            LocalizationSettings.InitializationOperation.Completed += _ => Apply();
        }
    }

    void OnDisable()
    {
        LocalizationSettings.SelectedLocaleChanged -= OnLocaleChanged;
    }

    void OnLocaleChanged(Locale locale) => Apply();

    public void Apply()
    {
        if (string.IsNullOrEmpty(key)) return;
        string s = LocText.T(key, fallback);
        var tmp = GetComponent<TextMeshProUGUI>();
        if (tmp != null)
        {
            if (tmp.text != s) tmp.text = s;
            return;
        }
        var t = GetComponent<UnityEngine.UI.Text>();
        if (t != null && t.text != s) t.text = s;
    }
}
