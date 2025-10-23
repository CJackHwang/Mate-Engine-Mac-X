using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;
using System;

public class SettingsHandlerBigScreen : MonoBehaviour
{
    public Toggle bigScreenSaverEnableToggle;
    public Slider bigScreenSaverTimeoutSlider;
    public TMP_Text bigScreenSaverTimeoutLabel;
    public RectTransform alarmsContent;
    public GameObject alarmItemTemplate;
    public Button addAlarmButton;

    [Header("AlarmItem Template Refs")]
    public Toggle templateAlarmToggle;
    public TMP_Dropdown templateHours;
    public TMP_Dropdown templateMinutes;
    public Toggle templateMonday;
    public Toggle templateTuesday;
    public Toggle templateWednesday;
    public Toggle templateThursday;
    public Toggle templateFriday;
    public Toggle templateSaturday;
    public Toggle templateSunday;
    public InputField templateAlarmText;
    public Button templateRemove;



    private static readonly string[] TimeoutLabels = {
        "30s", "1 min", "5 min", "15 min", "30 min", "45 min", "1 h", "1.5 h", "2 h", "2.5 h", "3 h"
    };

    private void Start()
    {
        SetupListeners();
        LoadSettings();
        if (addAlarmButton != null) addAlarmButton.onClick.AddListener(OnAddAlarm);
        BuildAlarmsUI();
    }


    void BuildAlarmsUI()
    {
        if (alarmsContent == null || alarmItemTemplate == null) return;
        EnsureTemplateDropdowns();

        for (int i = 0; i < alarmsContent.childCount; i++)
        {
            var c = alarmsContent.GetChild(i).gameObject;
            if (c.activeSelf) Destroy(c);
        }

        var d = SaveLoadHandler.Instance.data;
        if (d.alarms == null) d.alarms = new List<SaveLoadHandler.SettingsData.AlarmEntry>();
        foreach (var e in d.alarms) AddRow(e);
    }

    string GetRelativePath(Transform root, Transform target)
    {
        var stack = new System.Collections.Generic.List<string>();
        var t = target;
        while (t != null && t != root)
        {
            stack.Add(t.name);
            t = t.parent;
        }
        stack.Reverse();
        return string.Join("/", stack);
    }

    T CloneGet<T>(GameObject clone, Component templateComp) where T : Component
    {
        if (templateComp == null) return null;
        var rel = GetRelativePath(alarmItemTemplate.transform, templateComp.transform);
        var tr = clone.transform.Find(rel);
        return tr ? tr.GetComponent<T>() : null;
    }

    void EnsureTemplateDropdowns()
    {
        if (templateHours != null && templateHours.options.Count != 24)
        {
            templateHours.ClearOptions();
            var hours = new List<string>();
            for (int i = 0; i < 24; i++) hours.Add(i.ToString("D2"));
            templateHours.AddOptions(hours);
        }
        if (templateMinutes != null && templateMinutes.options.Count != 60)
        {
            templateMinutes.ClearOptions();
            var mins = new List<string>();
            for (int i = 0; i < 60; i++) mins.Add(i.ToString("D2"));
            templateMinutes.AddOptions(mins);
        }
    }


    void AddRow(SaveLoadHandler.SettingsData.AlarmEntry e)
    {
        var go = Instantiate(alarmItemTemplate, alarmsContent);
        go.SetActive(true);

        var tgl = CloneGet<Toggle>(go, templateAlarmToggle);
        var hours = CloneGet<TMP_Dropdown>(go, templateHours);
        var minutes = CloneGet<TMP_Dropdown>(go, templateMinutes);
        var mon = CloneGet<Toggle>(go, templateMonday);
        var tue = CloneGet<Toggle>(go, templateTuesday);
        var wed = CloneGet<Toggle>(go, templateWednesday);
        var thu = CloneGet<Toggle>(go, templateThursday);
        var fri = CloneGet<Toggle>(go, templateFriday);
        var sat = CloneGet<Toggle>(go, templateSaturday);
        var sun = CloneGet<Toggle>(go, templateSunday);
        var txt = CloneGet<InputField>(go, templateAlarmText);
        var remove = CloneGet<Button>(go, templateRemove);

        if (tgl != null) tgl.SetIsOnWithoutNotify(e.enabled);
        if (hours != null) hours.SetValueWithoutNotify(Mathf.Clamp(e.hour, 0, 23));
        if (minutes != null) minutes.SetValueWithoutNotify(Mathf.Clamp(e.minute, 0, 59));
        if (mon != null) mon.SetIsOnWithoutNotify((e.daysMask & (1 << 0)) != 0);
        if (tue != null) tue.SetIsOnWithoutNotify((e.daysMask & (1 << 1)) != 0);
        if (wed != null) wed.SetIsOnWithoutNotify((e.daysMask & (1 << 2)) != 0);
        if (thu != null) thu.SetIsOnWithoutNotify((e.daysMask & (1 << 3)) != 0);
        if (fri != null) fri.SetIsOnWithoutNotify((e.daysMask & (1 << 4)) != 0);
        if (sat != null) sat.SetIsOnWithoutNotify((e.daysMask & (1 << 5)) != 0);
        if (sun != null) sun.SetIsOnWithoutNotify((e.daysMask & (1 << 6)) != 0);
        if (txt != null) txt.SetTextWithoutNotify(e.text ?? "");

        if (tgl != null) tgl.onValueChanged.AddListener(v => { e.enabled = v; SaveLoadHandler.Instance.SaveToDisk(); });
        if (hours != null) hours.onValueChanged.AddListener(v => { e.hour = v; SaveLoadHandler.Instance.SaveToDisk(); });
        if (minutes != null) minutes.onValueChanged.AddListener(v => { e.minute = v; SaveLoadHandler.Instance.SaveToDisk(); });
        if (mon != null) mon.onValueChanged.AddListener(v => { e.daysMask = SetBit(e.daysMask, 0, v); SaveLoadHandler.Instance.SaveToDisk(); });
        if (tue != null) tue.onValueChanged.AddListener(v => { e.daysMask = SetBit(e.daysMask, 1, v); SaveLoadHandler.Instance.SaveToDisk(); });
        if (wed != null) wed.onValueChanged.AddListener(v => { e.daysMask = SetBit(e.daysMask, 2, v); SaveLoadHandler.Instance.SaveToDisk(); });
        if (thu != null) thu.onValueChanged.AddListener(v => { e.daysMask = SetBit(e.daysMask, 3, v); SaveLoadHandler.Instance.SaveToDisk(); });
        if (fri != null) fri.onValueChanged.AddListener(v => { e.daysMask = SetBit(e.daysMask, 4, v); SaveLoadHandler.Instance.SaveToDisk(); });
        if (sat != null) sat.onValueChanged.AddListener(v => { e.daysMask = SetBit(e.daysMask, 5, v); SaveLoadHandler.Instance.SaveToDisk(); });
        if (sun != null) sun.onValueChanged.AddListener(v => { e.daysMask = SetBit(e.daysMask, 6, v); SaveLoadHandler.Instance.SaveToDisk(); });
        if (txt != null) txt.onEndEdit.AddListener(v => { e.text = v ?? ""; SaveLoadHandler.Instance.SaveToDisk(); });

        if (remove != null) remove.onClick.AddListener(() =>
        {
            SaveLoadHandler.Instance.data.alarms.Remove(e);
            Destroy(go);
            SaveLoadHandler.Instance.SaveToDisk();
        });
    }
    void OnAddAlarm()
    {
        var e = new SaveLoadHandler.SettingsData.AlarmEntry
        {
            id = System.Guid.NewGuid().ToString("N"),
            enabled = true,
            hour = 7,
            minute = 0,
            daysMask = 0,
            text = "Alarm",
            lastTriggeredUnixMinute = 0
        };
        var d = SaveLoadHandler.Instance.data;
        if (d.alarms == null) d.alarms = new System.Collections.Generic.List<SaveLoadHandler.SettingsData.AlarmEntry>();
        d.alarms.Add(e);
        d.alarmsEnabled = true;
        SaveLoadHandler.Instance.SaveToDisk();
        AddRow(e);
    }


    byte SetBit(byte mask, int bit, bool on)
    {
        return on ? (byte)(mask | (1 << bit)) : (byte)(mask & ~(1 << bit));
    }

    private void SetupListeners()
    {
        bigScreenSaverEnableToggle?.onValueChanged.AddListener(OnScreenSaverEnableChanged);
        bigScreenSaverTimeoutSlider?.onValueChanged.AddListener(OnTimeoutSliderChanged);
    }

    public void LoadSettings()
    {
        var data = SaveLoadHandler.Instance.data;
        bigScreenSaverEnableToggle?.SetIsOnWithoutNotify(data.bigScreenScreenSaverEnabled);
        bigScreenSaverTimeoutSlider?.SetValueWithoutNotify(data.bigScreenScreenSaverTimeoutIndex);
        if (bigScreenSaverTimeoutLabel != null && data.bigScreenScreenSaverTimeoutIndex >= 0 && data.bigScreenScreenSaverTimeoutIndex < TimeoutLabels.Length)
            bigScreenSaverTimeoutLabel.text = TimeoutLabels[data.bigScreenScreenSaverTimeoutIndex];
    }


    void OnScreenSaverEnableChanged(bool v)
    {
        SaveLoadHandler.Instance.data.bigScreenScreenSaverEnabled = v;
        Save();
    }

    void OnTimeoutSliderChanged(float v)
    {
        int idx = Mathf.Clamp(Mathf.RoundToInt(v), 0, TimeoutLabels.Length - 1);
        SaveLoadHandler.Instance.data.bigScreenScreenSaverTimeoutIndex = idx;
        if (bigScreenSaverTimeoutLabel != null)
            bigScreenSaverTimeoutLabel.text = TimeoutLabels[idx];
        Save();
    }

    public void ApplySettings()
    {
        var data = SaveLoadHandler.Instance.data;
        data.bigScreenScreenSaverEnabled = bigScreenSaverEnableToggle?.isOn ?? data.bigScreenScreenSaverEnabled;
        data.bigScreenScreenSaverTimeoutIndex = Mathf.RoundToInt(bigScreenSaverTimeoutSlider?.value ?? data.bigScreenScreenSaverTimeoutIndex);
    }

    public void ResetToDefaults()
    {
        bigScreenSaverEnableToggle?.SetIsOnWithoutNotify(false);
        bigScreenSaverTimeoutSlider?.SetValueWithoutNotify(0);
        var data = SaveLoadHandler.Instance.data;
        data.bigScreenScreenSaverEnabled = false;
        data.bigScreenScreenSaverTimeoutIndex = 0;
        SaveLoadHandler.Instance.SaveToDisk();
    }



    private void Save()
    {
        SaveLoadHandler.Instance.SaveToDisk();
    }
}
