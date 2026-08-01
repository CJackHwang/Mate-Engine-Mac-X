using UnityEngine;
using TMPro;
using UnityEngine.UI;
using System.Collections.Generic;

public class SettingsHandlerDropdowns : MonoBehaviour
{
    public TMP_Dropdown graphicsDropdown;

    [System.Serializable]
    public class ParticleThemeEntry
    {
        public string id = "Standard";
        public string display = "Standard";
    }

    [Header("Particle Theme")]
    public TMP_Dropdown particleDropdown;
    public List<ParticleThemeEntry> particleThemes = new List<ParticleThemeEntry>();

    [Header("LLM Settings")]
    public InputField llmBaseUrlInput;
    public InputField llmAuthTokenInput;
    public InputField llmModelInput;
    public InputField llmMaxMessagesInput;
    public InputField llmMaxTokensInput;

    [Header("TTS Settings")]
    public InputField ttsApiUrlInput;
    public InputField ttsRefAudioPathInput;
    public InputField ttsPromptTextInput;
    public InputField ttsPromptLangInput;
    public InputField ttsTextLangInput;
    public InputField ttsTopKInput;
    public InputField ttsTopPInput;
    public InputField ttsTemperatureInput;
    public InputField ttsTextSplitMethodInput;
    public Toggle ttsEnabledToggle;

    void Start()
    {
        if (graphicsDropdown != null)
        {
            graphicsDropdown.ClearOptions();
            graphicsDropdown.AddOptions(new List<string>
            {
                LocText.T("ULTRA", "ULTRA"),
                LocText.T("VERY_HIGH", "VERY HIGH"),
                LocText.T("HIGH", "HIGH"),
                LocText.T("NORMAL", "NORMAL"),
                LocText.T("LOW", "LOW"),
            });
            graphicsDropdown.onValueChanged.AddListener(OnGraphicsChanged);
        }

        if (particleDropdown != null)
        {
            BuildParticleDropdown();
            particleDropdown.onValueChanged.AddListener(OnParticleChanged);
        }

        BindLLMInputs();
        BindTTSInputs();

        LoadSettings();
        ApplySettings();
    }

    void BindLLMInputs()
    {
        if (llmBaseUrlInput) llmBaseUrlInput.onEndEdit.AddListener(v => { SaveLoadHandler.Instance.data.llmBaseUrl = v; SaveLoadHandler.Instance.SaveToDisk(); });
        if (llmAuthTokenInput) llmAuthTokenInput.onEndEdit.AddListener(v => { SaveLoadHandler.Instance.data.llmAuthToken = v; SaveLoadHandler.Instance.SaveToDisk(); });
        if (llmModelInput) llmModelInput.onEndEdit.AddListener(v => { SaveLoadHandler.Instance.data.llmModel = v; SaveLoadHandler.Instance.SaveToDisk(); });
        if (llmMaxMessagesInput) llmMaxMessagesInput.onEndEdit.AddListener(v => { if (int.TryParse(v, out int n)) { SaveLoadHandler.Instance.data.llmMaxMessages = n; SaveLoadHandler.Instance.SaveToDisk(); } });
        if (llmMaxTokensInput) llmMaxTokensInput.onEndEdit.AddListener(v => { if (int.TryParse(v, out int n)) { SaveLoadHandler.Instance.data.llmMaxTokens = n; SaveLoadHandler.Instance.SaveToDisk(); } });
    }

    void BindTTSInputs()
    {
        if (ttsApiUrlInput) ttsApiUrlInput.onEndEdit.AddListener(v => { SaveLoadHandler.Instance.data.ttsApiUrl = v; SaveLoadHandler.Instance.SaveToDisk(); });
        if (ttsRefAudioPathInput) ttsRefAudioPathInput.onEndEdit.AddListener(v => { SaveLoadHandler.Instance.data.ttsRefAudioPath = v; SaveLoadHandler.Instance.SaveToDisk(); });
        if (ttsPromptTextInput) ttsPromptTextInput.onEndEdit.AddListener(v => { SaveLoadHandler.Instance.data.ttsPromptText = v; SaveLoadHandler.Instance.SaveToDisk(); });
        if (ttsPromptLangInput) ttsPromptLangInput.onEndEdit.AddListener(v => { SaveLoadHandler.Instance.data.ttsPromptLang = v; SaveLoadHandler.Instance.SaveToDisk(); });
        if (ttsTextLangInput) ttsTextLangInput.onEndEdit.AddListener(v => { SaveLoadHandler.Instance.data.ttsTextLang = v; SaveLoadHandler.Instance.SaveToDisk(); });
        if (ttsTopKInput) ttsTopKInput.onEndEdit.AddListener(v => { if (int.TryParse(v, out int n)) { SaveLoadHandler.Instance.data.ttsTopK = n; SaveLoadHandler.Instance.SaveToDisk(); } });
        if (ttsTopPInput) ttsTopPInput.onEndEdit.AddListener(v => { if (float.TryParse(v, out float f)) { SaveLoadHandler.Instance.data.ttsTopP = f; SaveLoadHandler.Instance.SaveToDisk(); } });
        if (ttsTemperatureInput) ttsTemperatureInput.onEndEdit.AddListener(v => { if (float.TryParse(v, out float f)) { SaveLoadHandler.Instance.data.ttsTemperature = f; SaveLoadHandler.Instance.SaveToDisk(); } });
        if (ttsTextSplitMethodInput) ttsTextSplitMethodInput.onEndEdit.AddListener(v => { SaveLoadHandler.Instance.data.ttsTextSplitMethod = v; SaveLoadHandler.Instance.SaveToDisk(); });
        if (ttsEnabledToggle) ttsEnabledToggle.onValueChanged.AddListener(v => { SaveLoadHandler.Instance.data.ttsEnabled = v; SaveLoadHandler.Instance.SaveToDisk(); });
    }

    void BuildParticleDropdown()
    {
        if (particleDropdown == null) return;

        if (particleThemes == null) particleThemes = new List<ParticleThemeEntry>();
        if (particleThemes.Count == 0)
            particleThemes.Add(new ParticleThemeEntry { id = "Standard", display = "Standard" });

        if (particleThemes.FindIndex(e => e.id == "None") < 0)
            particleThemes.Add(new ParticleThemeEntry { id = "None", display = LocText.T("NO_EFFECT", "No Effect") });

        var options = new List<string>();
        for (int i = 0; i < particleThemes.Count; i++)
        {
            var d = string.IsNullOrWhiteSpace(particleThemes[i].display) ? particleThemes[i].id : particleThemes[i].display;
            options.Add(d);
        }

        particleDropdown.ClearOptions();
        particleDropdown.AddOptions(options);

        string sel = SaveLoadHandler.Instance.data.selectedParticleTheme;
        int idx = Mathf.Max(0, particleThemes.FindIndex(e => e.id == sel));
        particleDropdown.SetValueWithoutNotify(idx);
    }

    void OnParticleChanged(int index)
    {
        if (particleThemes == null || index < 0 || index >= particleThemes.Count) return;
        SaveLoadHandler.Instance.data.selectedParticleTheme = particleThemes[index].id;
        SaveLoadHandler.ApplyAllSettingsToAllAvatars();
        SaveLoadHandler.Instance.SaveToDisk();
    }

    void OnGraphicsChanged(int index)
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

        if (particleDropdown != null) BuildParticleDropdown();

        // LLM
        if (llmBaseUrlInput) llmBaseUrlInput.SetTextWithoutNotify(data.llmBaseUrl);
        if (llmAuthTokenInput) llmAuthTokenInput.SetTextWithoutNotify(data.llmAuthToken);
        if (llmModelInput) llmModelInput.SetTextWithoutNotify(data.llmModel);
        if (llmMaxMessagesInput) llmMaxMessagesInput.SetTextWithoutNotify(data.llmMaxMessages.ToString());
        if (llmMaxTokensInput) llmMaxTokensInput.SetTextWithoutNotify(data.llmMaxTokens.ToString());

        // TTS
        if (ttsApiUrlInput) ttsApiUrlInput.SetTextWithoutNotify(data.ttsApiUrl);
        if (ttsRefAudioPathInput) ttsRefAudioPathInput.SetTextWithoutNotify(data.ttsRefAudioPath);
        if (ttsPromptTextInput) ttsPromptTextInput.SetTextWithoutNotify(data.ttsPromptText);
        if (ttsPromptLangInput) ttsPromptLangInput.SetTextWithoutNotify(data.ttsPromptLang);
        if (ttsTextLangInput) ttsTextLangInput.SetTextWithoutNotify(data.ttsTextLang);
        if (ttsTopKInput) ttsTopKInput.SetTextWithoutNotify(data.ttsTopK.ToString());
        if (ttsTopPInput) ttsTopPInput.SetTextWithoutNotify(data.ttsTopP.ToString());
        if (ttsTemperatureInput) ttsTemperatureInput.SetTextWithoutNotify(data.ttsTemperature.ToString());
        if (ttsTextSplitMethodInput) ttsTextSplitMethodInput.SetTextWithoutNotify(data.ttsTextSplitMethod);
        if (ttsEnabledToggle) ttsEnabledToggle.SetIsOnWithoutNotify(data.ttsEnabled);
    }

    public void ApplySettings()
    {
        var data = SaveLoadHandler.Instance.data;

        data.graphicsQualityLevel = graphicsDropdown?.value ?? data.graphicsQualityLevel;
        QualitySettings.SetQualityLevel(data.graphicsQualityLevel, true);

        if (particleDropdown != null)
        {
            int idx = Mathf.Clamp(particleDropdown.value, 0, particleThemes.Count - 1);
            if (particleThemes.Count > 0) data.selectedParticleTheme = particleThemes[idx].id;
        }

        SaveLoadHandler.ApplyAllSettingsToAllAvatars();
        SaveLoadHandler.Instance.SaveToDisk();
    }

    public void ResetToDefaults()
    {
        graphicsDropdown?.SetValueWithoutNotify(1);
        QualitySettings.SetQualityLevel(1, true);
        SaveLoadHandler.Instance.data.graphicsQualityLevel = 1;

        SaveLoadHandler.Instance.data.selectedParticleTheme = "Standard";
        if (particleDropdown != null) BuildParticleDropdown();

        SaveLoadHandler.ApplyAllSettingsToAllAvatars();
        SaveLoadHandler.Instance.SaveToDisk();
    }
}
