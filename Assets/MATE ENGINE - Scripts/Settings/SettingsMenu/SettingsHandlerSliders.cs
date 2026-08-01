using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Localization.Components;

public class SettingsHandlerSliders : MonoBehaviour
{
    public Slider soundThresholdSlider;
    public Slider idleSwitchTimeSlider;
    public Slider idleTransitionTimeSlider;
    public Slider avatarSizeSlider;
    public Slider fpsLimitSlider;
    public Slider headBlendSlider;
    public Slider spineBlendSlider;
    public Slider eyeBlendSlider;
    public Slider hueShiftSlider;
    public Slider saturationSlider;
    public Slider windowSitYOffsetSlider;
    public Slider windowSitCliffOffsetSlider;
    public Slider danceSwitchTimeSlider;
    public Slider danceTransitionTimeSlider;
    // 舞蹈片段总数（1-20），对应 Animator Female blend tree 的 threshold 数量
    public InputField danceClipCountInput;
    // 固定舞蹈编号：-1=自动循环，0-19=固定到指定片段
    public InputField pinnedDanceIndexInput;

    private void Start()
    {
        EnsureCliffOffsetSlider();
        soundThresholdSlider?.onValueChanged.AddListener(v =>
        {
            SaveLoadHandler.Instance.data.soundThreshold = v;
            SaveAll();
        });

        idleSwitchTimeSlider?.onValueChanged.AddListener(v =>
        {
            SaveLoadHandler.Instance.data.idleSwitchTime = v;
            SaveAll();
        });

        idleTransitionTimeSlider?.onValueChanged.AddListener(v =>
        {
            SaveLoadHandler.Instance.data.idleTransitionTime = v;
            SaveAll();
        });

        avatarSizeSlider?.onValueChanged.AddListener(v => {
            SaveLoadHandler.Instance.data.avatarSize = v;
            SaveAll();
        });

        fpsLimitSlider?.onValueChanged.AddListener(v =>
        {
            SaveLoadHandler.Instance.data.fpsLimit = (int)v;
            foreach (var limiter in FindObjectsByType<FPSLimiter>())
                limiter.SetFPSLimit((int)v);
            SaveAll();
        });

        headBlendSlider?.onValueChanged.AddListener(v =>
        {
            SaveLoadHandler.Instance.data.headBlend = v;
            SaveAll();
        });

        spineBlendSlider?.onValueChanged.AddListener(v =>
        {
            SaveLoadHandler.Instance.data.spineBlend = v;
            SaveAll();
        });

        eyeBlendSlider?.onValueChanged.AddListener(v =>
        {
            SaveLoadHandler.Instance.data.eyeBlend = v;
            SaveAll();
        });

        hueShiftSlider?.onValueChanged.AddListener(v =>
        {
            SaveLoadHandler.Instance.data.uiHueShift = v;
            var theme = FindAnyObjectByType<ThemeManager>();
            if (theme != null) theme.SetHue(v);
            SaveAll();
        });

        saturationSlider?.onValueChanged.AddListener(v =>
        {
            SaveLoadHandler.Instance.data.uiSaturation = v;
            var theme = FindAnyObjectByType<ThemeManager>();
            if (theme != null) theme.SetSaturation(v);
            SaveAll();
        });
        windowSitYOffsetSlider?.onValueChanged.AddListener(v =>
        {
            SaveLoadHandler.Instance.data.windowSitYOffset = v;
            SaveAll();
        });
        windowSitCliffOffsetSlider?.onValueChanged.AddListener(v =>
        {
            SaveLoadHandler.Instance.data.windowSitCliffOffset = v;
            SaveLoadHandler.Instance.data.windowSitCliffOffsetSet = true;
            SaveAll();
        });
        danceSwitchTimeSlider?.onValueChanged.AddListener(v =>
        {
            SaveLoadHandler.Instance.data.danceSwitchTime = v;
            SaveAll();
        });

        danceTransitionTimeSlider?.onValueChanged.AddListener(v =>
        {
            SaveLoadHandler.Instance.data.danceTransitionTime = v;
            SaveAll();
        });

        danceClipCountInput?.onEndEdit.AddListener(v =>
        {
            if (int.TryParse(v, out int n))
            {
                SaveLoadHandler.Instance.data.danceClipCount = Mathf.Clamp(n, 1, 20);
                danceClipCountInput.SetTextWithoutNotify(SaveLoadHandler.Instance.data.danceClipCount.ToString());
                SaveAll();
            }
        });

        pinnedDanceIndexInput?.onEndEdit.AddListener(v =>
        {
            if (int.TryParse(v, out int n))
            {
                SaveLoadHandler.Instance.data.pinnedDanceIndex = Mathf.Clamp(n, -1, 19);
                pinnedDanceIndexInput.SetTextWithoutNotify(SaveLoadHandler.Instance.data.pinnedDanceIndex.ToString());
                SaveAll();
            }
        });


        LoadSettings();
        ApplySettings();
    }

    // The settings rows are hand-positioned in the scene, so instead of editing
    // scene YAML (fragile) the cliff-depth slider is cloned at runtime from the
    // existing seat-height slider row and placed right below it.
    private void EnsureCliffOffsetSlider()
    {
        if (windowSitCliffOffsetSlider != null || windowSitYOffsetSlider == null) return;
        Transform row = windowSitYOffsetSlider.transform;
        if (row == null || row.parent == null) return;
        GameObject clone = Instantiate(row.gameObject, row.parent);
        clone.transform.SetSiblingIndex(row.GetSiblingIndex() + 1);
        clone.name = "WindowSitCliffDepth";
        RectTransform rt = clone.GetComponent<RectTransform>();
        RectTransform srcRT = row as RectTransform;
        if (rt != null && srcRT != null)
        {
            Vector2 pos = srcRT.anchoredPosition;
            rt.anchoredPosition = new Vector2(pos.x, pos.y - 60f);
        }
        foreach (var tmp in clone.GetComponentsInChildren<TMPro.TextMeshProUGUI>(true))
        {
            if (tmp.text.Contains("坐下") || tmp.text.Contains("高度") ||
                tmp.text.IndexOf("SITTING", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                tmp.text.IndexOf("OFFSET", System.StringComparison.OrdinalIgnoreCase) >= 0)
            {
                // Disable the copied LocalizeStringEvent (still bound to the source
                // row's key) and bind our own key so the label re-localizes.
                var lse = tmp.GetComponent<LocalizeStringEvent>();
                if (lse != null) lse.enabled = false;
                var binder = tmp.GetComponent<LocTextBinder>();
                if (binder == null) binder = tmp.gameObject.AddComponent<LocTextBinder>();
                binder.key = "WINDOW_SIT_CLIFF";
                binder.fallback = "坐下时的遮挡深度";
                binder.Apply();
                break;
            }
        }
        foreach (var txt in clone.GetComponentsInChildren<UnityEngine.UI.Text>(true))
        {
            if (txt.text.Contains("坐下") || txt.text.Contains("高度") ||
                txt.text.IndexOf("SITTING", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                txt.text.IndexOf("OFFSET", System.StringComparison.OrdinalIgnoreCase) >= 0)
            {
                var lse = txt.GetComponent<LocalizeStringEvent>();
                if (lse != null) lse.enabled = false;
                var binder = txt.GetComponent<LocTextBinder>();
                if (binder == null) binder = txt.gameObject.AddComponent<LocTextBinder>();
                binder.key = "WINDOW_SIT_CLIFF";
                binder.fallback = "坐下时的遮挡深度";
                binder.Apply();
                break;
            }
        }
        Slider s = clone.GetComponentInChildren<Slider>(true);
        if (s != null)
        {
            s.minValue = -1f;
            s.maxValue = 1f;
        }
        windowSitCliffOffsetSlider = s;
    }

    private void SaveAll()
    {
        SaveLoadHandler.Instance.SaveToDisk();
        SaveLoadHandler.ApplyAllSettingsToAllAvatars();
    }

    public void LoadSettings()
    {
        var data = SaveLoadHandler.Instance.data;
        soundThresholdSlider?.SetValueWithoutNotify(data.soundThreshold);
        idleSwitchTimeSlider?.SetValueWithoutNotify(data.idleSwitchTime);
        idleTransitionTimeSlider?.SetValueWithoutNotify(data.idleTransitionTime);
        avatarSizeSlider?.SetValueWithoutNotify(data.avatarSize);
        fpsLimitSlider?.SetValueWithoutNotify(data.fpsLimit);
        headBlendSlider?.SetValueWithoutNotify(data.headBlend);
        spineBlendSlider?.SetValueWithoutNotify(data.spineBlend);
        eyeBlendSlider?.SetValueWithoutNotify(data.eyeBlend);
        hueShiftSlider?.SetValueWithoutNotify(data.uiHueShift);
        saturationSlider?.SetValueWithoutNotify(data.uiSaturation);
        windowSitYOffsetSlider?.SetValueWithoutNotify(data.windowSitYOffset);
        windowSitCliffOffsetSlider?.SetValueWithoutNotify(data.windowSitCliffOffset);
        danceSwitchTimeSlider?.SetValueWithoutNotify(data.danceSwitchTime);
        danceTransitionTimeSlider?.SetValueWithoutNotify(data.danceTransitionTime);
        danceClipCountInput?.SetTextWithoutNotify(data.danceClipCount.ToString());
        pinnedDanceIndexInput?.SetTextWithoutNotify(data.pinnedDanceIndex.ToString());
    }
    public void ApplySettings()
    {
        var data = SaveLoadHandler.Instance.data;

        foreach (var limiter in FindObjectsByType<FPSLimiter>())
            limiter.SetFPSLimit(data.fpsLimit);

        var scaleController = FindAnyObjectByType<AvatarScaleController>();
        if (scaleController != null)
            scaleController.SyncWithSlider();

        var theme = FindAnyObjectByType<ThemeManager>();
        if (theme != null)
        {
            theme.SetHue(data.uiHueShift);
            theme.SetSaturation(data.uiSaturation);
        }

        foreach (var handler in FindObjectsByType<AvatarWindowHandler>())
        {
            handler.windowSitYOffset = SaveLoadHandler.Instance.data.windowSitYOffset;
            handler.windowSitCliffOffset = SaveLoadHandler.Instance.data.windowSitCliffOffset;
        }
        SaveLoadHandler.ApplyAllSettingsToAllAvatars();
    }

    public void ResetToDefaults()
    {
        soundThresholdSlider?.SetValueWithoutNotify(0.1f);
        idleSwitchTimeSlider?.SetValueWithoutNotify(10f);
        idleTransitionTimeSlider?.SetValueWithoutNotify(1f);
        avatarSizeSlider?.SetValueWithoutNotify(1.0f);
        fpsLimitSlider?.SetValueWithoutNotify(60);
        headBlendSlider?.SetValueWithoutNotify(0.7f);
        spineBlendSlider?.SetValueWithoutNotify(0.5f);
        eyeBlendSlider?.SetValueWithoutNotify(1.0f);
        hueShiftSlider?.SetValueWithoutNotify(0f);
        saturationSlider?.SetValueWithoutNotify(1f);
        windowSitYOffsetSlider?.SetValueWithoutNotify(-0.02f);
        windowSitCliffOffsetSlider?.SetValueWithoutNotify(-0.12f);
        danceSwitchTimeSlider?.SetValueWithoutNotify(15f);
        danceTransitionTimeSlider?.SetValueWithoutNotify(2f);



        var data = SaveLoadHandler.Instance.data;
        data.soundThreshold = 0.1f;
        data.idleSwitchTime = 10f;
        data.idleTransitionTime = 1f;
        data.avatarSize = 1.0f;
        data.fpsLimit = 60;
        data.headBlend = 0.7f;
        data.spineBlend = 0.5f;
        data.eyeBlend = 1.0f;
        data.windowSitYOffset = -0.02f;
        data.windowSitCliffOffset = -0.12f;
        data.windowSitCliffOffsetSet = true;
        data.danceSwitchTime = 15f;
        data.danceTransitionTime = 2f;
        data.danceClipCount = 20;
        data.pinnedDanceIndex = -1;
        danceClipCountInput?.SetTextWithoutNotify("20");
        pinnedDanceIndexInput?.SetTextWithoutNotify("-1");
        data.uiHueShift = 0f;
        data.uiSaturation = 1f;

        SaveLoadHandler.Instance.SaveToDisk();
        SaveLoadHandler.ApplyAllSettingsToAllAvatars();
        ApplySettings();
    }

}
