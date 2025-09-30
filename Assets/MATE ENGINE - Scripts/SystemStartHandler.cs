using System;
using System.IO;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Debug = UnityEngine.Debug;

public class SystemStartHandler : MonoBehaviour
{
    [Header("UI (Optional)")]
    public Toggle autoStartToggle;
    public TMP_Text checkmarkText;

    [Header("Settings")]
    public string runKeyName = "MateEngine";
    public string commandLineArgs = "";

    private bool _isApplyingUI;

    private void Awake()
    {
        if (SaveLoadHandler.Instance == null)
        {
            Debug.LogError("[SystemStartHandler] SaveLoadHandler.Instance is null. Place SaveLoadHandler in the scene first.");
            enabled = false;
            return;
        }
    }

    private void Start()
    {
        if (autoStartToggle != null)
            autoStartToggle.onValueChanged.AddListener(OnUIToggleChanged);

        LoadFromSaveWithoutNotify();
        TryApplyRegistry(SaveLoadHandler.Instance.data.startWithWindows);
    }

    private void OnDestroy()
    {
        if (autoStartToggle != null)
            autoStartToggle.onValueChanged.RemoveListener(OnUIToggleChanged);
    }

    private void OnUIToggleChanged(bool isOn)
    {
        if (_isApplyingUI) return;

        SaveLoadHandler.Instance.data.startWithWindows = isOn;
        SaveLoadHandler.Instance.SaveToDisk();

        TryApplyRegistry(isOn);
        UpdateCheckmarkText(isOn);
    }

    public void OnCheckmarkClicked()
    {
        bool newState = !GetSavedState();
        SetStateFromCode(newState);
    }

    public void SetStateFromCode(bool isOn)
    {
        SaveLoadHandler.Instance.data.startWithWindows = isOn;
        SaveLoadHandler.Instance.SaveToDisk();
        TryApplyRegistry(isOn);
        ApplyToUIWithoutNotify(isOn);
    }

    private void LoadFromSaveWithoutNotify()
    {
        ApplyToUIWithoutNotify(GetSavedState());
    }

    private bool GetSavedState()
    {
        return SaveLoadHandler.Instance.data != null && SaveLoadHandler.Instance.data.startWithWindows;
    }

    private void ApplyToUIWithoutNotify(bool isOn)
    {
        _isApplyingUI = true;
        try
        {
            if (autoStartToggle != null)
                autoStartToggle.SetIsOnWithoutNotify(isOn);
            UpdateCheckmarkText(isOn);
        }
        finally
        {
            _isApplyingUI = false;
        }
    }

    private void UpdateCheckmarkText(bool isOn)
    {
        if (checkmarkText != null)
            checkmarkText.text = isOn ? "☑ Start with Windows" : "☐ Start with Windows";
    }

    // ---------------- Registry Handling ----------------

    private void TryApplyRegistry(bool enable)
    {
#if UNITY_STANDALONE_WIN
        if (Application.platform != RuntimePlatform.WindowsPlayer &&
            Application.platform != RuntimePlatform.WindowsEditor)
        {
            Debug.Log("[SystemStartHandler] Skipping registry (not on Windows).");
            return;
        }

        try
        {
            string exePath = GetCurrentExecutablePathQuoted();
            if (string.IsNullOrEmpty(exePath))
            {
                Debug.LogWarning("[SystemStartHandler] Executable path empty. Skipping registry write.");
                return;
            }

            string value = string.IsNullOrWhiteSpace(commandLineArgs)
                ? exePath
                : exePath + " " + commandLineArgs;

            using (var key = global::Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                       @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", writable: true))
            {
                if (key == null)
                {
                    Debug.LogError("[SystemStartHandler] HKCU Run key not found.");
                    return;
                }

                if (enable)
                {
                    key.SetValue(runKeyName, value);
                    Debug.Log($"[SystemStartHandler] Enabled autostart (HKCU) as '{runKeyName}' → {value}");
                }
                else
                {
                    key.DeleteValue(runKeyName, false);
                    Debug.Log($"[SystemStartHandler] Disabled autostart (HKCU) for '{runKeyName}'.");
                }
            }
        }
        catch (Exception ex)
        {
            Debug.LogError("[SystemStartHandler] Registry write failed: " + ex.Message);
        }
#else
        Debug.Log("[SystemStartHandler] Registry disabled on this platform.");
#endif
    }

    private string GetCurrentExecutablePathQuoted()
    {
#if UNITY_EDITOR
        return string.Empty;
#else
        try
        {
            // Safer way in builds: Application.dataPath → go up one folder
            string exe = Path.Combine(Directory.GetParent(Application.dataPath).FullName,
                                      Application.productName + ".exe");
            if (File.Exists(exe))
                return $"\"{exe}\"";

            // Fallback: try Process API
            string proc = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;
            return string.IsNullOrEmpty(proc) ? string.Empty : $"\"{proc}\"";
        }
        catch (Exception ex)
        {
            Debug.LogWarning("[SystemStartHandler] Failed to get exe path: " + ex.Message);
            return string.Empty;
        }
#endif
    }
}
