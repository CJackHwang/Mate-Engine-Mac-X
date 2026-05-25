using System;
using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;
using Newtonsoft.Json;

/// <summary>
/// GPT-SoVITS TTS handler. Mirrors tts.py synthesize() exactly.
/// Plays audio via AudioSource after synthesis.
/// </summary>
public class SoVITSTTSHandler : MonoBehaviour
{
    [Header("Audio")]
    public AudioSource audioSource;

    void Awake()
    {
        if (audioSource == null)
            audioSource = gameObject.AddComponent<AudioSource>();
    }

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

    public void Speak(string text, Action onComplete = null)
    {
        if (_currentTTS != null)
        {
            StopCoroutine(_currentTTS);
            if (audioSource != null) audioSource.Stop();
        }
        _currentTTS = StartCoroutine(SpeakCoroutine(text, onComplete));
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

    private IEnumerator SpeakCoroutine(string text, Action onComplete)
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

        // Save wav to temp file and load as AudioClip
        string tmpPath = Path.Combine(Application.temporaryCachePath, $"tts_{DateTime.Now:yyyyMMdd_HHmmss}.wav");
        File.WriteAllBytes(tmpPath, req.downloadHandler.data);
        Debug.Log($"[TTS] WAV saved: {tmpPath} ({req.downloadHandler.data.Length} bytes)");

        // On Mac, absolute path needs file:/// (three slashes)
        string fileUrl = "file://" + tmpPath;
        using var audioReq = UnityWebRequestMultimedia.GetAudioClip(fileUrl, AudioType.WAV);
        ((DownloadHandlerAudioClip)audioReq.downloadHandler).streamAudio = false;
        yield return audioReq.SendWebRequest();

        if (audioReq.result != UnityWebRequest.Result.Success)
        {
            Debug.LogError("[SoVITSTTSHandler] Failed to load audio: " + audioReq.error + " url=" + fileUrl);
            onComplete?.Invoke();
            yield break;
        }

        AudioClip clip = DownloadHandlerAudioClip.GetContent(audioReq);
        Debug.Log($"[TTS] AudioClip: {(clip == null ? "null" : $"length={clip.length}s samples={clip.samples}")}");
        if (clip == null || clip.length <= 0)
        {
            Debug.LogError("[SoVITSTTSHandler] AudioClip is null or empty");
            onComplete?.Invoke();
            yield break;
        }

        Debug.Log($"[TTS] AudioSource: {(audioSource == null ? "null" : audioSource.gameObject.name)} volume={audioSource?.volume}");
        if (audioSource != null)
        {
            audioSource.clip = clip;
            audioSource.Play();
            Debug.Log("[TTS] Playing...");
            yield return new WaitWhile(() => audioSource.isPlaying);
            Debug.Log("[TTS] Playback done.");
        }
        else
        {
            Debug.LogError("[TTS] AudioSource is null, cannot play.");
        }

        try { File.Delete(tmpPath); } catch { }

        _currentTTS = null;
        onComplete?.Invoke();
    }
}
