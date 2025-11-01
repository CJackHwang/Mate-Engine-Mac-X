using UnityEngine;
using UnityEngine.Localization.Settings;
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine.UI;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

public class AvatarMinecraftMessages : MonoBehaviour
{
    [Serializable]
    public enum McEventType { Entity, DayStart, NightStart, LowHealth, LowHunger, Death, RainStart, Drowning, Sleep, Crafting, Eat, KillConfirm, BiomeDiscovery }

    [Serializable]
    public class McMessageEntry
    {
        public string locKey;
        [TextArea] public string text;
        public McEventType type = McEventType.Entity;
    }

    [Serializable]
    class ProxEvent
    {
        public string type;
        public string phase;
        public string uuid;
        public string id;
        public string name;
        public float distance;
        public long ts;
        public string player;
        public string biome;
    }

    [Header("Toggle")]
    public bool enableMinecraftMessages = true;

    [Header("Networking")]
    public int port = 32145;
    public bool debugLog = true;

    [Header("Bubble")]
    public Material bubbleMaterial;
    public string localizationTable = "Languages (UI)";
    [Range(1, 60)] public int despawnTime = 10;
    [HideInInspector] public List<AvatarMessage> messages = new List<AvatarMessage>();
    [Header("MC Messages")]
    public List<McMessageEntry> mcMessages = new List<McMessageEntry>();
    public Transform chatContainer;
    public Sprite bubbleSprite;
    public Color bubbleColor = new Color32(120, 120, 255, 255);
    public Color fontColor = Color.white;
    public Font font;
    public int fontSize = 16;
    public int bubbleWidth = 600;
    public float textPadding = 10f;
    public float bubbleSpacing = 10f;
    [Range(5, 100)] public int streamSpeed = 35;

    [Header("Gating")]
    public string[] allowedStates = { "Idle" };
    public bool requireAllowedStates = true;
    public List<GameObject> blockObjects = new List<GameObject>();
    public bool useBlockObjects = true;

    public AudioSource streamAudioSource;

    LLMUnitySamples.Bubble activeBubble;
    Coroutine streamCoroutine;
    Coroutine despawnCoroutine;
    Animator avatarAnimator;

    UdpClient client;
    Thread thread;
    volatile bool run;
    readonly ConcurrentQueue<string> queue = new ConcurrentQueue<string>();
    System.Random rng = new System.Random();

    void Start()
    {
        avatarAnimator = GetComponent<Animator>();
    }

    string InjectEntity(string text, string entity)
    {
        if (string.IsNullOrEmpty(text)) return "";
        return text.Replace("<entity>", entity)
                   .Replace("<Entity>", entity)
                   .Replace("{entity}", entity)
                   .Replace("{Entity}", entity);
    }

    string InjectBiome(string text, string biome)
    {
        if (string.IsNullOrEmpty(text)) return "";
        return text.Replace("<biome>", biome)
                   .Replace("<Biome>", biome)
                   .Replace("{biome}", biome)
                   .Replace("{Biome}", biome);
    }

    void OnEnable()
    {
        try
        {
            client = new UdpClient(port);
            client.Client.ReceiveTimeout = 1000;
            run = true;
            thread = new Thread(Listen) { IsBackground = true };
            thread.Start();
            if (debugLog) Debug.Log("[AvatarMinecraftMessages] UDP ready on " + port);
        }
        catch (Exception ex)
        {
            if (debugLog) Debug.LogError("[AvatarMinecraftMessages] UDP bind failed: " + ex.Message);
            SafeClose();
        }
    }

    void OnDisable()
    {
        SafeClose();
    }

    void SafeClose()
    {
        run = false;
        try { client?.Close(); } catch { }
        client = null;
        try { thread?.Join(100); } catch { }
        thread = null;
    }

    void Listen()
    {
        var ep = new IPEndPoint(IPAddress.Any, port);
        while (run)
        {
            try
            {
                if (client == null) { Thread.Sleep(50); continue; }
                var data = client.Receive(ref ep);
                var s = Encoding.UTF8.GetString(data);
                queue.Enqueue(s);
            }
            catch (SocketException) { }
            catch { }
        }
    }

    void Update()
    {
        while (queue.TryDequeue(out var json))
        {
            if (!enableMinecraftMessages) continue;
            var e = JsonUtility.FromJson<ProxEvent>(json);
            if (e == null) continue;

            if (e.type == "mob_proximity")
            {
                if (!string.IsNullOrEmpty(e.phase) && e.phase != "enter") continue;
                var entityName = string.IsNullOrEmpty(e.name) ? (string.IsNullOrEmpty(e.id) ? "entity" : e.id) : e.name;
                if (debugLog) Debug.Log("[AvatarMinecraftMessages] Event: " + entityName);
                ShowEvent(McEventType.Entity, entityName);
                continue;
            }

            if (e.type == "day_start" || e.type == "time_day")
            {
                if (debugLog) Debug.Log("[AvatarMinecraftMessages] Event: day_start");
                ShowEvent(McEventType.DayStart, "");
                continue;
            }

            if (e.type == "night_start" || e.type == "time_night")
            {
                if (debugLog) Debug.Log("[AvatarMinecraftMessages] Event: night_start");
                ShowEvent(McEventType.NightStart, "");
                continue;
            }

            if (e.type == "low_health")
            {
                if (debugLog) Debug.Log("[AvatarMinecraftMessages] Event: low_health");
                ShowEvent(McEventType.LowHealth, "");
                continue;
            }

            if (e.type == "low_hunger")
            {
                if (debugLog) Debug.Log("[AvatarMinecraftMessages] Event: low_hunger");
                ShowEvent(McEventType.LowHunger, "");
                continue;
            }

            if (e.type == "death" || e.type == "player_death" || e.type == "you_died")
            {
                if (debugLog) Debug.Log("[AvatarMinecraftMessages] Event: death");
                ShowEvent(McEventType.Death, "");
                continue;
            }

            if (e.type == "rain_start" || e.type == "weather_rain_start" || e.type == "rain")
            {
                if (debugLog) Debug.Log("[AvatarMinecraftMessages] Event: rain_start");
                ShowEvent(McEventType.RainStart, "");
                continue;
            }

            if (e.type == "drowning" || e.type == "low_air" || e.type == "air_low")
            {
                if (debugLog) Debug.Log("[AvatarMinecraftMessages] Event: drowning");
                ShowEvent(McEventType.Drowning, "");
                continue;
            }

            if (e.type == "sleep" || e.type == "sleep_start" || e.type == "player_sleep")
            {
                if (debugLog) Debug.Log("[AvatarMinecraftMessages] Event: sleep");
                ShowEvent(McEventType.Sleep, "");
                continue;
            }

            if (e.type == "crafting" || e.type == "crafted" || e.type == "crafted_item")
            {
                if (rng.NextDouble() < 0.3)
                {
                    if (debugLog) Debug.Log("[AvatarMinecraftMessages] Event: crafting");
                    ShowEvent(McEventType.Crafting, "");
                }
                continue;
            }

            if (e.type == "eat")
            {
                if (debugLog) Debug.Log("[AvatarMinecraftMessages] Event: eat");
                ShowEvent(McEventType.Eat, "");
                continue;
            }

            if (e.type == "kill_confirm")
            {
                var nm = string.IsNullOrEmpty(e.name) ? (string.IsNullOrEmpty(e.id) ? "entity" : e.id) : e.name;
                if (debugLog) Debug.Log("[AvatarMinecraftMessages] Event: kill_confirm " + nm);
                ShowEvent(McEventType.KillConfirm, nm);
                continue;
            }

            if (e.type == "biome_discovery")
            {
                var b = string.IsNullOrEmpty(e.biome) ? "" : e.biome;
                if (debugLog) Debug.Log("[AvatarMinecraftMessages] Event: biome_discovery " + b);
                ShowEvent(McEventType.BiomeDiscovery, "", b);
                continue;
            }
        }
    }

    public void ShowEntityMessage(string entityDisplayName)
    {
        ShowEvent(McEventType.Entity, entityDisplayName);
    }

    void ShowEvent(McEventType type, string entityDisplayName, string biomeName = "")
    {
        if (!enableMinecraftMessages) return;
        if (chatContainer == null) { if (debugLog) Debug.LogWarning("[AvatarMinecraftMessages] No chatContainer"); return; }
        if (useBlockObjects && IsBlockedByObjects()) { if (debugLog) Debug.Log("[AvatarMinecraftMessages] Blocked by object"); return; }
        if (requireAllowedStates && !IsInAllowedState()) { if (debugLog) Debug.Log("[AvatarMinecraftMessages] State not allowed"); return; }

        string baseText = PickTextFor(type);
        if (string.IsNullOrEmpty(baseText))
        {
            AvatarMessage msg = null;
            if (messages != null && messages.Count > 0) msg = messages[0];
            baseText = ResolveText(msg);
            if (string.IsNullOrEmpty(baseText)) baseText = "there's a <entity> nearby... take care!";
        }

        string finalText = baseText;
        finalText = InjectEntity(finalText, entityDisplayName);
        finalText = InjectBiome(finalText, biomeName);

        RemoveBubble();

        var ui = new LLMUnitySamples.BubbleUI
        {
            sprite = bubbleSprite,
            font = font,
            fontSize = fontSize,
            fontColor = fontColor,
            bubbleColor = bubbleColor,
            bottomPosition = 0,
            leftPosition = 1,
            textPadding = textPadding,
            bubbleOffset = bubbleSpacing,
            bubbleWidth = bubbleWidth,
            bubbleHeight = -1
        };

        activeBubble = new LLMUnitySamples.Bubble(chatContainer, ui, "MinecraftBubble", "");
        var rt = activeBubble.GetRectTransform();
        var imgs = rt.GetComponentsInChildren<Image>(true);
        for (int i = 0; i < imgs.Length; i++)
        {
            if (bubbleMaterial != null) imgs[i].material = bubbleMaterial;
            imgs[i].pixelsPerUnitMultiplier = 0.25f;
        }

        if (streamAudioSource != null) { streamAudioSource.Stop(); streamAudioSource.Play(); }
        if (streamCoroutine != null) StopCoroutine(streamCoroutine);
        if (avatarAnimator != null) avatarAnimator.SetBool("isTalking", true);

        streamCoroutine = StartCoroutine(FakeStreamText(finalText));
        if (despawnCoroutine != null) StopCoroutine(despawnCoroutine);
        despawnCoroutine = StartCoroutine(DespawnAfterDelay());
    }

    string PickTextFor(McEventType type)
    {
        if (mcMessages == null || mcMessages.Count == 0) return "";
        var pool = new List<McMessageEntry>();
        for (int i = 0; i < mcMessages.Count; i++)
        {
            var m = mcMessages[i];
            if (m != null && m.type == type) pool.Add(m);
        }
        if (pool.Count == 0) return "";
        var pick = pool[rng.Next(pool.Count)];
        return ResolveText(pick.locKey, pick.text);
    }

    string ResolveText(AvatarMessage msg)
    {
        if (msg != null && !string.IsNullOrEmpty(msg.locKey))
        {
            try
            {
                string localized = LocalizationSettings.StringDatabase.GetLocalizedString(localizationTable, msg.locKey);
                if (!string.IsNullOrEmpty(localized)) return localized;
            }
            catch { }
        }
        if (msg != null && !string.IsNullOrEmpty(msg.text)) return msg.text;
        return "";
    }

    string ResolveText(string locKey, string fallback)
    {
        if (!string.IsNullOrEmpty(locKey))
        {
            try
            {
                string localized = LocalizationSettings.StringDatabase.GetLocalizedString(localizationTable, locKey);
                if (!string.IsNullOrEmpty(localized)) return localized;
            }
            catch { }
        }
        if (!string.IsNullOrEmpty(fallback)) return fallback;
        return "";
    }

    IEnumerator FakeStreamText(string fullText)
    {
        if (activeBubble == null) yield break;
        activeBubble.SetText("");
        int length = 0;
        float delay = 1f / Mathf.Max(streamSpeed, 1);
        while (length < fullText.Length)
        {
            length++;
            if (activeBubble == null) yield break;
            activeBubble.SetText(fullText.Substring(0, length));
            yield return new WaitForSeconds(delay);
        }
        if (activeBubble != null) activeBubble.SetText(fullText);
        if (streamAudioSource != null && streamAudioSource.isPlaying) streamAudioSource.Stop();
        if (avatarAnimator != null) avatarAnimator.SetBool("isTalking", false);
        streamCoroutine = null;
    }

    IEnumerator DespawnAfterDelay()
    {
        yield return new WaitForSeconds(Mathf.Max(1, despawnTime));
        RemoveBubble();
    }

    void RemoveBubble()
    {
        if (streamCoroutine != null) { StopCoroutine(streamCoroutine); streamCoroutine = null; }
        if (despawnCoroutine != null) { StopCoroutine(despawnCoroutine); despawnCoroutine = null; }
        if (activeBubble != null) { activeBubble.Destroy(); activeBubble = null; }
        if (streamAudioSource != null && streamAudioSource.isPlaying) streamAudioSource.Stop();
        if (avatarAnimator != null) avatarAnimator.SetBool("isTalking", false);
    }

    bool IsInAllowedState()
    {
        if (avatarAnimator == null || allowedStates == null || allowedStates.Length == 0) return true;
        var s = avatarAnimator.GetCurrentAnimatorStateInfo(0);
        for (int i = 0; i < allowedStates.Length; i++)
        {
            var n = allowedStates[i];
            if (!string.IsNullOrEmpty(n) && s.IsName(n)) return true;
        }
        return false;
    }

    bool IsBlockedByObjects()
    {
        if (blockObjects == null || blockObjects.Count == 0) return false;
        for (int i = 0; i < blockObjects.Count; i++)
        {
            var go = blockObjects[i];
            if (go != null && go.activeInHierarchy) return true;
        }
        return false;
    }
}
