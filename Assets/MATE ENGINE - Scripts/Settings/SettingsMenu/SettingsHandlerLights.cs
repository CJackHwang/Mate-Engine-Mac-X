using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;
using UnityEngine.Localization.Components;

public class SettingsHandlerLights : MonoBehaviour
{
    [System.Serializable]
    public class LightControlEntry
    {
        public string lightID;
        public Slider intensitySlider;
        public Slider saturationSlider;
        public Slider hueSlider;
        public float defaultIntensity;
        public float defaultSaturation;
        public float defaultHue;
    }

    [System.Serializable]
    public class LightToggleEntry
    {
        public string activeID;
        public string nonActiveID;
        public Toggle checkmark;
    }

    public List<LightControlEntry> lights = new List<LightControlEntry>();
    public List<LightToggleEntry> lightToggles = new List<LightToggleEntry>();
    public ColorController colorController;
    public Toggle autoAmbientToggle; // unbound in scene; cloned at runtime next to the ambient-light master toggle

    private void Start()
    {
        for (int i = 0; i < lights.Count; i++)
        {
            int idx = i;
            var entry = lights[i];
            entry.defaultIntensity = entry.intensitySlider.value;
            entry.defaultSaturation = entry.saturationSlider.value;
            entry.defaultHue = entry.hueSlider.value;

            entry.intensitySlider.onValueChanged.AddListener((v) => {
                SaveLoadHandler.Instance.data.lightIntensities[entry.lightID] = v;
                OnLightSliderChanged(idx);
                Save();
            });
            entry.saturationSlider.onValueChanged.AddListener((v) => {
                SaveLoadHandler.Instance.data.lightSaturations[entry.lightID] = v;
                OnLightSliderChanged(idx);
                Save();
            });
            entry.hueSlider.onValueChanged.AddListener((v) => {
                SaveLoadHandler.Instance.data.lightHues[entry.lightID] = v;
                OnLightSliderChanged(idx);
                Save();
            });
        }

        for (int i = 0; i < lightToggles.Count; i++)
        {
            int idx = i;
            var entry = lightToggles[i];
            if (entry.checkmark != null)
            {
                entry.checkmark.onValueChanged.AddListener((v) => {
                    if (!string.IsNullOrEmpty(entry.activeID))
                        SaveLoadHandler.Instance.data.groupToggles[entry.activeID] = v;
                    OnLightToggleChanged(idx, v);
                    Save();
                });
            }
        }

        EnsureAutoAmbientToggle();
        autoAmbientToggle?.onValueChanged.AddListener(OnAutoAmbientChanged);

        LoadSettings();
        ApplySettings();
    }

    public void LoadSettings()
    {
        var data = SaveLoadHandler.Instance.data;

        for (int i = 0; i < lights.Count; i++)
        {
            var entry = lights[i];
            if (!string.IsNullOrEmpty(entry.lightID))
            {
                if (data.lightIntensities.TryGetValue(entry.lightID, out float iVal)) entry.intensitySlider.SetValueWithoutNotify(iVal);
                if (data.lightSaturations.TryGetValue(entry.lightID, out float sVal)) entry.saturationSlider.SetValueWithoutNotify(sVal);
                if (data.lightHues.TryGetValue(entry.lightID, out float hVal)) entry.hueSlider.SetValueWithoutNotify(hVal);
            }
            OnLightSliderChanged(i);
        }

        for (int i = 0; i < lightToggles.Count; i++)
        {
            var entry = lightToggles[i];
            if (!string.IsNullOrEmpty(entry.activeID) && entry.checkmark != null)
            {
                bool toggleState = entry.activeID == "ambi_lights"; // 环境光默认开启
                if (data.groupToggles.TryGetValue(entry.activeID, out bool state)) toggleState = state;
                entry.checkmark.SetIsOnWithoutNotify(toggleState);
                OnLightToggleChanged(i, toggleState);
            }
        }

        if (autoAmbientToggle != null)
        {
            bool st = true; // default on
            if (data.groupToggles.TryGetValue("auto_ambient", out bool sv)) st = sv;
            autoAmbientToggle.SetIsOnWithoutNotify(st);
        }
    }

    public void ApplySettings()
    {
        for (int i = 0; i < lights.Count; i++)
            OnLightSliderChanged(i);
        for (int i = 0; i < lightToggles.Count; i++)
        {
            var entry = lightToggles[i];
            OnLightToggleChanged(i, entry.checkmark != null && entry.checkmark.isOn);
        }
    }

    public void ResetLightToDefault(int idx)
    {
        var entry = lights[idx];
        entry.intensitySlider.value = entry.defaultIntensity;
        entry.saturationSlider.value = entry.defaultSaturation;
        entry.hueSlider.value = entry.defaultHue;
        OnLightSliderChanged(idx);
    }

    public void ResetAllLightsToDefault()
    {
        for (int i = 0; i < lights.Count; i++)
        {
            var entry = lights[i];
            entry.intensitySlider.value = entry.defaultIntensity;
            entry.saturationSlider.value = entry.defaultSaturation;
            entry.hueSlider.value = entry.defaultHue;

            if (!string.IsNullOrEmpty(entry.lightID))
            {
                SaveLoadHandler.Instance.data.lightIntensities[entry.lightID] = entry.defaultIntensity;
                SaveLoadHandler.Instance.data.lightSaturations[entry.lightID] = entry.defaultSaturation;
                SaveLoadHandler.Instance.data.lightHues[entry.lightID] = entry.defaultHue;
            }
        }
        SaveLoadHandler.Instance.SaveToDisk();
    }

    public void ResetAllLightTogglesToDefault()
    {
        for (int i = 0; i < lightToggles.Count; i++)
        {
            var entry = lightToggles[i];
            bool def = entry.activeID == "ambi_lights"; // 环境光默认开启
            if (entry.checkmark != null)
            {
                entry.checkmark.SetIsOnWithoutNotify(def);
                OnLightToggleChanged(i, def);
            }
            if (!string.IsNullOrEmpty(entry.activeID))
                SaveLoadHandler.Instance.data.groupToggles[entry.activeID] = def;
        }
        SaveLoadHandler.Instance.SaveToDisk();
    }


    private void OnLightSliderChanged(int idx)
    {
        var entry = lights[idx];
        if (colorController == null) return;
        var target = colorController.targets.Find(t => t.id == entry.lightID);
        if (target != null)
        {
            target.intensity = entry.intensitySlider.value;
            target.saturation = entry.saturationSlider.value;
            target.hue = entry.hueSlider.value;
        }
    }

    private void OnLightToggleChanged(int idx, bool state)
    {
        var entry = lightToggles[idx];
        if (colorController == null) return;
        colorController.SetGroupEnabled(entry.activeID, state);
        colorController.SetGroupEnabled(entry.nonActiveID, !state);
    }

    // Clone the ambient-light master toggle row at runtime and wire a new
    // "auto ambient" toggle right below it (same approach as EnsureCliffOffsetSlider
    // in SettingsHandlerSliders) to avoid hand-editing the fragile scene YAML.
    private void EnsureAutoAmbientToggle()
    {
        if (autoAmbientToggle != null) return;
        if (lightToggles == null || lightToggles.Count == 0 || lightToggles[0].checkmark == null) return;
        Transform row = lightToggles[0].checkmark.transform;
        if (row == null || row.parent == null) return;

        // The ambient rows' labels used a pivot trick (pivot.y=350, anchored
        // y≈8734) that renders a 500px-wide text band ON the row, covering the
        // row's own toggle/checkmark. Repair them before cloning so the clone
        // copies a clean layout.
        RepairAmbientRowLabels(row.parent);

        GameObject clone = Instantiate(row.gameObject, row.parent);
        clone.transform.SetSiblingIndex(row.GetSiblingIndex() + 1);
        clone.name = "AutoAmbient";

        RectTransform rt = clone.GetComponent<RectTransform>();
        RectTransform srcRT = row as RectTransform;
        if (rt != null && srcRT != null)
        {
            // Source sits at y=-220, next row (Enable Lights) at -280, so -250 is
            // the free slot between them — a full -60 gap would land the clone on
            // top of Enable Lights.
            rt.anchoredPosition = new Vector2(srcRT.anchoredPosition.x, srcRT.anchoredPosition.y - 30f);
        }

        Toggle t = clone.GetComponent<Toggle>();
        if (t != null)
        {
            t.onValueChanged.RemoveAllListeners();
            autoAmbientToggle = t;
        }

        const string labelKey = "AUTO_AMBIENT";
        foreach (var tmp in clone.GetComponentsInChildren<TMPro.TextMeshProUGUI>(true))
        {
            // Disable the copied LocalizeStringEvent (still bound to the source row's
            // key) and bind our own key instead so the label re-localizes on language
            // changes via LocTextBinder.
            var lse = tmp.GetComponent<LocalizeStringEvent>();
            if (lse != null) lse.enabled = false;
            var binder = tmp.GetComponent<LocTextBinder>();
            if (binder == null) binder = tmp.gameObject.AddComponent<LocTextBinder>();
            binder.key = labelKey;
            binder.fallback = "自动环境光";
            binder.Apply();
            break;
        }
        foreach (var txt in clone.GetComponentsInChildren<UnityEngine.UI.Text>(true))
        {
            var lse = txt.GetComponent<LocalizeStringEvent>();
            if (lse != null) lse.enabled = false;
            var binder = txt.GetComponent<LocTextBinder>();
            if (binder == null) binder = txt.gameObject.AddComponent<LocTextBinder>();
            binder.key = labelKey;
            binder.fallback = "自动环境光";
            binder.Apply();
            break;
        }
        var tooltip = clone.GetComponent<UiTooltip>();
        if (tooltip != null)
        {
            tooltip.locKey = "TIP_AUTO_AMBIENT";
            tooltip.tooltipText = "开启时，环境光自动跟随桌面配色；关闭时用手动滑杆。";
        }
    }

    private void OnAutoAmbientChanged(bool v)
    {
        SaveLoadHandler.Instance.data.groupToggles["auto_ambient"] = v;
        var probes = Resources.FindObjectsOfTypeAll<DesktopAmbientProbe>();
        for (int i = 0; i < probes.Length; i++) probes[i].SetEnabled(v);
        Save();
    }

    // Called by DesktopAmbientProbe when it auto-switches off (no screen-capture
    // sample, e.g. missing screen-recording permission). Updates the toggle UI
    // without firing its listener so we don't re-enter OnAutoAmbientChanged.
    public void SyncAutoAmbientToggle(bool v)
    {
        if (autoAmbientToggle != null)
            autoAmbientToggle.SetIsOnWithoutNotify(v);
    }

    // Some ambient rows carry a label with a pivot hack (pivot.y = 350, huge
    // anchored y) that renders a 500px-wide text band ON the row, covering the
    // row's own toggle/checkmark. Re-anchor every such on-row label just right
    // of its row (labels that float above their row are left alone).
    private void RepairAmbientRowLabels(Transform sectionRoot)
    {
        if (sectionRoot == null) return;
        foreach (var lrt in sectionRoot.GetComponentsInChildren<RectTransform>(true))
        {
            if (lrt == null || lrt.pivot.y <= 1f) continue;
            float h = lrt.rect.height;
            if (h <= 0f) continue;
            RectTransform parent = lrt.parent as RectTransform;
            if (parent == null) continue;
            float parentHalfH = parent.rect.height * 0.5f;
            float bandTop = lrt.anchoredPosition.y - lrt.pivot.y * h;
            float bandBottom = bandTop + h;
            // Only labels whose band actually sits ON the row cover the control.
            bool overlapsRow = bandTop < parentHalfH && bandBottom > -parentHalfH;
            if (!overlapsRow) continue;
            float parentHalfW = parent.rect.width * 0.5f;
            lrt.pivot = new Vector2(0f, 0.5f);
            lrt.anchoredPosition = new Vector2(parentHalfW + 10f, 0f);
            lrt.sizeDelta = new Vector2(Mathf.Min(lrt.sizeDelta.x, 400f), 25f);
        }
    }

    private void Save()
    {
        SaveLoadHandler.Instance.SaveToDisk();
    }
}
