using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;
using Newtonsoft.Json;

/// <summary>
/// Anthropic API chat handler. Drop-in replacement for LLMCharacter in ChatBot.cs.
/// Mirrors the request format from cli_agent.py exactly.
/// </summary>
public class AnthropicChatHandler : MonoBehaviour
{
    [Serializable]
    private class Message
    {
        public string role;
        public string content;
    }

    [Serializable]
    private class RequestPayload
    {
        public string model;
        public int max_tokens;
        public string system;
        public List<Message> messages;
    }

#pragma warning disable CS0649 // fields populated via Newtonsoft.Json
    [Serializable]
    private class ContentBlock
    {
        public string type;
        public string text;
    }

    [Serializable]
    private class ResponsePayload
    {
        public List<ContentBlock> content;
    }
#pragma warning restore CS0649

    private List<Message> history = new List<Message>();
    private bool isCancelled = false;

    // Expose chat list for ChatBot.ShowLoadedMessages compatibility
    public List<(string role, string content)> chat
    {
        get
        {
            var result = new List<(string, string)>();
            foreach (var m in history)
                result.Add((m.role, m.content));
            return result;
        }
    }

    public void SetPrompt(string prompt)
    {
        if (SaveLoadHandler.Instance != null)
        {
            SaveLoadHandler.Instance.data.llmSystemPrompt = prompt;
            SaveLoadHandler.Instance.SaveToDisk();
        }
    }

    public void CancelRequests()
    {
        isCancelled = true;
    }

    public void ClearHistory()
    {
        history.Clear();
    }

    /// <summary>
    /// Send a message. Calls onReply with the full response, then onComplete.
    /// Compatible with ChatBot.cs usage pattern.
    /// </summary>
    public void Chat(string userMessage, Action<string> onReply, Action onComplete)
    {
        isCancelled = false;
        StartCoroutine(ChatCoroutine(userMessage, onReply, onComplete));
    }

    private IEnumerator ChatCoroutine(string userMessage, Action<string> onReply, Action onComplete)
    {
        var data = SaveLoadHandler.Instance?.data;
        if (data == null || string.IsNullOrEmpty(data.llmBaseUrl) || string.IsNullOrEmpty(data.llmAuthToken))
        {
            onReply?.Invoke("[LLM not configured. Set Base URL and Auth Token in settings.]");
            onComplete?.Invoke();
            yield break;
        }

        // Trim history to max messages (same as cli_agent.py trim_history)
        TrimHistory(data.llmMaxMessages);

        var messages = new List<Message>(history)
        {
            new Message { role = "user", content = userMessage }
        };

        var payload = new RequestPayload
        {
            model = data.llmModel,
            max_tokens = data.llmMaxTokens,
            system = data.llmSystemPrompt,
            messages = messages
        };

        string url = data.llmBaseUrl.TrimEnd('/') + "/v1/messages";
        string jsonBody = JsonConvert.SerializeObject(payload);
        byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonBody);

        using var req = new UnityWebRequest(url, "POST");
        req.uploadHandler = new UploadHandlerRaw(bodyRaw);
        req.downloadHandler = new DownloadHandlerBuffer();
        req.SetRequestHeader("x-api-key", data.llmAuthToken);
        req.SetRequestHeader("anthropic-version", "2023-06-01");
        req.SetRequestHeader("content-type", "application/json");
        req.timeout = 120;

        yield return req.SendWebRequest();

        if (isCancelled)
        {
            onComplete?.Invoke();
            yield break;
        }

        if (req.result != UnityWebRequest.Result.Success)
        {
            string err = $"[API Error {req.responseCode}]: {req.downloadHandler.text}";
            Debug.LogError("[AnthropicChatHandler] " + err);
            onReply?.Invoke(err);
            onComplete?.Invoke();
            yield break;
        }

        string responseText = ExtractText(req.downloadHandler.text);

        // Update history (same as cli_agent.py)
        history.Add(new Message { role = "user", content = userMessage });
        history.Add(new Message { role = "assistant", content = responseText });
        TrimHistory(data.llmMaxMessages);

        onReply?.Invoke(responseText);
        onComplete?.Invoke();
    }

    private string ExtractText(string json)
    {
        try
        {
            var resp = JsonConvert.DeserializeObject<ResponsePayload>(json);
            if (resp?.content == null) return json;
            var sb = new StringBuilder();
            foreach (var block in resp.content)
                if (block.type == "text" && !string.IsNullOrEmpty(block.text))
                    sb.Append(block.text);
            string result = sb.ToString().Trim();
            return string.IsNullOrEmpty(result) ? json : result;
        }
        catch
        {
            return json;
        }
    }

    private void TrimHistory(int maxMessages)
    {
        if (maxMessages <= 0) return;
        while (history.Count > maxMessages)
            history.RemoveAt(0);
    }

    // Warmup stub for ChatBot.cs compatibility
    public void Warmup(Action callback)
    {
        callback?.Invoke();
    }
}
