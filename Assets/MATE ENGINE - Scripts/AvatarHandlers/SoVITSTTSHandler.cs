using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;
using Newtonsoft.Json;

/// <summary>
/// GPT-SoVITS TTS handler. Mirrors tts.py synthesize() exactly.
/// AudioClips are cached in memory until application quit.
/// </summary>
public class SoVITSTTSHandler : MonoBehaviour
{
    [Header("Audio")]
    public AudioSource audioSource;

    [Serializable]
    private class TTSPayload
    {
        public string text;
        public string text_lang;
        public string ref_audio_path;
        public string prompt_text;
        public string prompt_lang;
        public int top_k;
        public float top_p;
        public float temperature;
        public string text_split_method;
        public string media_type = "wav";
    }

    private Coroutine _currentTTS;
    private readonly Dictionary<string, AudioClip> _clipCache = new();

    public bool IsPlaying => _currentTTS != null;

    void Awake()
    {
        if (audioSource == null)
            audioSource = gameObject.AddComponent<AudioSource>();
    }

    void OnApplicationQuit()
    {
        foreach (var clip in _clipCache.Values)
            if (clip != null) Destroy(clip);
        _clipCache.Clear();
    }

    /// <summary>Synthesize and play. Calls onClipReady with the AudioClip for caching by caller.</summary>
    public void Speak(string text, Action<AudioClip> onClipReady = null, Action onComplete = null)
    {
        if (_currentTTS != null)
        {
            StopCoroutine(_currentTTS);
            if (audioSource != null) audioSource.Stop();
        }
        _currentTTS = StartCoroutine(SpeakCoroutine(text, onClipReady, onComplete));
    }

    /// <summary>Play a cached AudioClip directly without re-synthesizing.</summary>
    public void PlayClip(AudioClip clip, Action onComplete = null)
    {
        if (clip == null) return;
        if (_currentTTS != null)
        {
            StopCoroutine(_currentTTS);
            if (audioSource != null) audioSource.Stop();
        }
        _currentTTS = StartCoroutine(PlayClipCoroutine(clip, onComplete));
    }

    public void Stop()
    {
        if (_currentTTS != null)
        {
            StopCoroutine(_currentTTS);
            _currentTTS = null;
        }
        if (audioSource != null) audioSource.Stop();
    }

    private IEnumerator PlayClipCoroutine(AudioClip clip, Action onComplete)
    {
        if (audioSource != null)
        {
            audioSource.clip = clip;
            audioSource.Play();
            yield return new WaitWhile(() => audioSource.isPlaying);
        }
        _currentTTS = null;
        onComplete?.Invoke();
    }

    private IEnumerator SpeakCoroutine(string text, Action<AudioClip> onClipReady, Action onComplete)
    {
        var data = SaveLoadHandler.Instance?.data;
        if (data == null || !data.ttsEnabled || string.IsNullOrEmpty(data.ttsApiUrl))
        {
            onComplete?.Invoke();
            yield break;
        }

        var payload = new TTSPayload
        {
            text = text,
            text_lang = data.ttsTextLang,
            ref_audio_path = data.ttsRefAudioPath,
            prompt_text = data.ttsPromptText,
            prompt_lang = data.ttsPromptLang,
            top_k = data.ttsTopK,
            top_p = data.ttsTopP,
            temperature = data.ttsTemperature,
            text_split_method = data.ttsTextSplitMethod,
            media_type = "wav"
        };

        string jsonBody = JsonConvert.SerializeObject(payload);
        byte[] bodyRaw = System.Text.Encoding.UTF8.GetBytes(jsonBody);

        using var req = new UnityWebRequest(data.ttsApiUrl, "POST");
        req.uploadHandler = new UploadHandlerRaw(bodyRaw);
        req.downloadHandler = new DownloadHandlerBuffer();
        req.SetRequestHeader("content-type", "application/json");
        req.timeout = 120;

        yield return req.SendWebRequest();

        if (req.result != UnityWebRequest.Result.Success)
        {
            Debug.LogError($"[SoVITSTTSHandler] TTS error {req.responseCode}: {req.downloadHandler.text}");
            onComplete?.Invoke();
            yield break;
        }

        // Save to temp file to load as AudioClip (temp file kept until app quit)
        string tmpPath = Path.Combine(Application.temporaryCachePath, $"tts_{DateTime.Now:yyyyMMdd_HHmmss}.wav");
        File.WriteAllBytes(tmpPath, req.downloadHandler.data);

        string fileUrl = "file://" + tmpPath;
        using var audioReq = UnityWebRequestMultimedia.GetAudioClip(fileUrl, AudioType.WAV);
        ((DownloadHandlerAudioClip)audioReq.downloadHandler).streamAudio = false;
        yield return audioReq.SendWebRequest();

        if (audioReq.result != UnityWebRequest.Result.Success)
        {
            Debug.LogError("[SoVITSTTSHandler] Failed to load audio: " + audioReq.error);
            onComplete?.Invoke();
            yield break;
        }

        AudioClip clip = DownloadHandlerAudioClip.GetContent(audioReq);
        if (clip == null || clip.length <= 0)
        {
            Debug.LogError("[SoVITSTTSHandler] AudioClip is null or empty");
            onComplete?.Invoke();
            yield break;
        }

        // Notify caller so it can cache the clip
        onClipReady?.Invoke(clip);

        if (audioSource != null)
        {
            audioSource.volume = SaveLoadHandler.Instance?.data.ttsVolume ?? 1f;
            audioSource.clip = clip;
            audioSource.Play();
            yield return new WaitWhile(() => audioSource.isPlaying);
        }

        _currentTTS = null;
        onComplete?.Invoke();
    }
}
