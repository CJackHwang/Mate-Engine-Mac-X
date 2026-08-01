using UnityEngine.Localization.Settings;

/// <summary>
/// Runtime localization helper for code that sets text directly (TMP labels,
/// dropdown options, tooltips) instead of going through a LocalizeStringEvent.
/// Resolves a key from the "Languages (UI)" string table and falls back to the
/// given default when the key or table is unavailable or not yet initialized.
/// </summary>
public static class LocText
{
    public const string TableName = "Languages (UI)";

    public static string T(string key, string fallback)
    {
        if (string.IsNullOrEmpty(key)) return fallback;
        try
        {
            // The synchronous GetLocalizedString blocks on an Addressables load.
            // Calling it before localization is initialized (constructors, early
            // Start, Awake) re-enters WaitForCompletion and overflows the stack,
            // which crashed the app at startup. If we're not ready, return the
            // fallback now — LocTextBinder / the SelectedLocaleChanged event will
            // re-apply the real translation once initialization completes.
            if (!LocalizationSettings.InitializationOperation.IsDone)
                return fallback;
            var s = LocalizationSettings.StringDatabase.GetLocalizedString(TableName, key);
            if (!string.IsNullOrEmpty(s)) return s;
        }
        catch { }
        return fallback;
    }
}
