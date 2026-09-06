using UnityEngine;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using System;

public class SaveLoadHandler : MonoBehaviour
{
    public static SaveLoadHandler Instance { get; private set; }

    public SettingsData data;

    // Multi-Instance Variablen
    private static string fileName = "settings.json";
    private static string customDataDir = null;

    private string BaseDir => string.IsNullOrEmpty(customDataDir)
        ? Application.persistentDataPath
        : Path.Combine(Application.persistentDataPath, customDataDir);

    private string FilePath => Path.Combine(BaseDir, fileName);

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);

        // Kommandozeilen-Argumente lesen
        var args = Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i].Equals("--savefile", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                fileName = args[i + 1].Trim('"');

            if (args[i].Equals("--datadir", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                customDataDir = args[i + 1].Trim('"');
        }

        LoadFromDisk();
        ApplyAllSettingsToAllAvatars();

        var theme = FindAnyObjectByType<ThemeManager>();
        if (theme != null)
        {
            theme.SetHue(data.uiHueShift);
            theme.SetSaturation(data.uiSaturation);
        }


        var limiters = FindObjectsByType<FPSLimiter>();
        foreach (var limiter in limiters)
        {
            limiter.targetFPS = data.fpsLimit;
            limiter.ApplyFPSLimit();
        }

#if UNITY_STANDALONE_OSX
        // 启动时不再把窗口强制设为显示器原生像素分辨率（Retina 下 Display.main.systemWidth/
        // systemHeight 是像素、窗口却按点缩放，会导致开屏窗口高度超出屏幕）。改为延迟到首帧
        // 把窗口调整到主显示器可见工作区大小：宽度保持全屏、高度自适应可见区域。
        StartCoroutine(FitWindowToVisibleScreen());
#endif
    }

#if UNITY_STANDALONE_OSX
    // Sizes the window to the primary display's visible work area (in points) so
    // the startup window/popup never overflows past the macOS menu bar or dock.
    // Waits up to two seconds for UniWindowController to report a real window size.
    private System.Collections.IEnumerator FitWindowToVisibleScreen()
    {
        Kirurobo.UniWindowController uwc = null;
        Vector2 size = Vector2.zero;
        for (int i = 0; i < 120; i++)
        {
            uwc = Kirurobo.UniWindowController.current;
            if (uwc != null)
            {
                size = uwc.windowSize;
                if (size.x > 0f && size.y > 0f) break;
            }
            yield return null;
        }
        if (uwc == null || size.x <= 0f || size.y <= 0f) yield break;

        RectInt primary = MacWindowHelper.GetPrimaryMonitorRect();
        var monitors = MacWindowHelper.GetMonitors();
        int idx = monitors != null ? monitors.IndexOf(primary) : -1;
        if (idx < 0) idx = 0;
        int vx = primary.x, vy = primary.y, vw = primary.width, vh = primary.height;
        try { MacSystemBridge.MacSys_GetScreenVisibleRect(idx, out vx, out vy, out vw, out vh); }
        catch (System.Exception) { }
        if (vw <= 0 || vh <= 0) { vw = primary.width; vh = primary.height; }
        vw = Mathf.Min(vw, primary.width);
        vh = Mathf.Min(vh, primary.height);

        uwc.windowSize = new Vector2(vw, vh);
        float screenH = MacWindowHelper.GetGlobalScreenHeight();
        // AppKit origin is bottom-left, Y up: place the window's top-left at the
        // visible area's top-left.
        uwc.windowPosition = new Vector2(vx, screenH - (vy + vh));
    }
#endif

    // Speichern
    public void SaveToDisk()
    {
        if (data == null)
            return;

        try
        {
            string dir = Path.GetDirectoryName(FilePath);
            if (!Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            string json = JsonConvert.SerializeObject(data, Formatting.Indented);
            string tmpPath = FilePath + ".tmp";
            File.WriteAllText(tmpPath, json);
            if (File.Exists(FilePath))
                File.Replace(tmpPath, FilePath, null);
            else
                File.Move(tmpPath, FilePath);

            Debug.Log("[SaveLoadHandler] Saved settings to: " + FilePath);
        }
        catch (Exception e)
        {
            Debug.LogError("[SaveLoadHandler] Failed to save: " + e);
        }
    }

    // Laden
    public void LoadFromDisk()
    {
        if (File.Exists(FilePath))
        {
            try
            {
                string json = File.ReadAllText(FilePath);
                data = JsonConvert.DeserializeObject<SettingsData>(json);
            }
            catch
            {
                data = new SettingsData();
            }
        }
        else
        {
            data = new SettingsData();
        }

        if (data == null)
            data = new SettingsData();

        MigrateAfterLoad();
    }


    [Serializable]
    public class SettingsData
    {
        public enum WindowSizeState { Normal, Big, Small }
        public WindowSizeState windowSizeState = WindowSizeState.Normal;

        public float soundThreshold = 0.1f;
        public float idleSwitchTime = 10f;
        public float idleTransitionTime = 1f;
        public bool enableDanceSwitch = true;
        public float danceSwitchTime = 15f;
        public float danceTransitionTime = 2f;

        // ── 舞蹈选择 ──────────────────────────────────────────────────────────
        // Female Animator blend tree 共有 20 个舞蹈动作（threshold 0-19）
        // danceClipCount : 自动循环时使用的舞蹈数量上限，范围 1-20
        // pinnedDanceIndex : -1 = 自动循环，0-19 = 固定到指定编号的舞蹈
        // ─────────────────────────────────────────────────────────────────────
        public int danceClipCount = 20;
        // -1 = auto cycle, 0~(danceClipCount-1) = pin to specific dance
        public int pinnedDanceIndex = -1;
        public float avatarSize = 1.0f;
        public bool enableDancing = true;
        // true = dance while a system player outputs audio (macOS SCK capture);
        // false = manual, enableDancing on = dance immediately.
        public bool followMusic = true;
        public bool enableMouseTracking = true;
        public int fpsLimit = 60;
        public bool isTopmost = false;

        public List<string> allowedApps = new();
        public bool bloom = true;
        public bool dayNight = true;

        public bool enableParticles = true;
        public float petVolume = 1f;
        public float effectsVolume = 1f;
        public float menuVolume = 1f;
        public float ttsVolume = 1f;

        public float headBlend = 0.7f;
        public float eyeBlend = 1f;
        public float spineBlend = 0.5f;

        public bool enableHandHolding = true;
        public bool enableWindowSitting = true;
        // "auto" = snap to both edges, "up" = top edge only, "down" = bottom edge only
        public string windowSitEdge = "auto";
        public bool ambientOcclusion = true;

        public float uiHueShift = 0f;
        public float uiSaturation = 1.0f;

        public bool enableDiscordRPC = true;

        public bool tutorialDone = false;

        public string selectedLocaleCode = "en";
        public bool enableIK = true;

        public int bigScreenScreenSaverTimeoutIndex = 0;
        public bool bigScreenScreenSaverEnabled = false;
        public float windowSitYOffset = -0.02f;
        // Runtime-tunable cliff occluder depth (⌘+[ / ⌘+]). offsetSet distinguishes
        // "never tuned" (use the Inspector value) from an explicit saved value.
        public bool windowSitCliffOffsetSet = false;
        public float windowSitCliffOffset = 0f;

        public Dictionary<string, float> lightIntensities = new();
        public Dictionary<string, float> lightSaturations = new();
        public Dictionary<string, float> lightHues = new();
        public Dictionary<string, bool> groupToggles = new();

        public Dictionary<string, bool> modStates = new();
        public int graphicsQualityLevel = 2;
        public Dictionary<string, bool> accessoryStates = new();

        public bool startWithWindows = false;
        public bool enableRandomMessages = false;

        public string selectedModelPath = "";
        public int contextLength = 4096;
        public bool enableHusbandoMode = false;
        public bool enableAutoMemoryTrim = false;

        // Anthropic LLM settings
        public string llmBaseUrl = "";
        public string llmAuthToken = "";
        public string llmModel = "claude-sonnet-4-6";
        public string llmSystemPrompt = "你是一个简洁、自然的对话助手。回答尽量直接、清楚，适合朗读。";
        public int llmMaxMessages = 20;
        public int llmMaxTokens = 1024;

        // GPT-SoVITS TTS settings
        public string ttsApiUrl = "http://100.75.53.37:9880/tts";
        public string ttsRefAudioPath = "/media/zichen/E/workspace/GPT-SoVITS/参考音频/yanami1.mp3";
        public string ttsPromptText = "物申す必要が生じただけなの。ほら、うちのクラスのツワブキ祭の企画、準備が始まったでしょ?";
        public string ttsPromptLang = "ja";
        public string ttsTextLang = "ja";
        public int ttsTopK = 15;
        public float ttsTopP = 1f;
        public float ttsTemperature = 1f;
        public string ttsTextSplitMethod = "cut0";
        public bool ttsEnabled = true;

        public int settingsVersion = 0;
        public bool alarmsEnabled = true;
        public bool enableMinecraftMessages = false;

        public string selectedParticleTheme = "Standard";
        public bool enableFeedSystem = false;
        public bool enableRandomAvatar = false;

        public bool enableLocomotion = false;


        //ALARM
        [Serializable]
        public class AlarmEntry
        {
            public string id;
            public bool enabled;
            public int hour;
            public int minute;
            public byte daysMask;
            public string text;
            public long lastTriggeredUnixMinute;
        }

        public List<AlarmEntry> alarms = new List<AlarmEntry>();

        //Timer
        [Serializable]
        public class TimerEntry
        {
            public string id;
            public bool enabled;
            public int hours;
            public int minutes;
            public int presetSeconds;
            public bool running;
            public long targetUnix;
            public string text;
        }

        public List<TimerEntry> timers = new List<TimerEntry>();


    }
    //ALARM
    void MigrateAfterLoad()
    {
        if (data == null) data = new SettingsData();
        if (data.timers == null) data.timers = new List<SettingsData.TimerEntry>();
        if (string.IsNullOrEmpty(data.selectedParticleTheme)) data.selectedParticleTheme = "Standard";
        if (data.alarms == null) data.alarms = new List<SettingsData.AlarmEntry>();
        if (data.settingsVersion < 1)
        {
            data.settingsVersion = 1;
            SaveToDisk();
        }
    }

    public static void SyncAllowedAppsToAllAvatars()
    {
        var allAvatars = Resources.FindObjectsOfTypeAll<AvatarAnimatorController>();
        var list = new List<string>(Instance.data.allowedApps);

        foreach (var avatar in allAvatars)
            avatar.allowedApps = list;
    }

    public static void ApplyAllSettingsToAllAvatars()
    {
        var data = Instance.data;
        var avatars = Resources.FindObjectsOfTypeAll<AvatarAnimatorController>();

        foreach (var avatar in avatars)
        {
            avatar.SOUND_THRESHOLD = data.soundThreshold;
            avatar.IDLE_SWITCH_TIME = data.idleSwitchTime;
            avatar.IDLE_TRANSITION_TIME = data.idleTransitionTime;
            avatar.enableDancing = data.enableDancing;
            avatar.followMusic = data.followMusic;
            avatar.allowedApps = new List<string>(data.allowedApps);
            avatar.transform.localScale = Vector3.one * data.avatarSize;
            avatar.DANCE_SWITCH_TIME = data.danceSwitchTime;
            avatar.DANCE_TRANSITION_TIME = data.danceTransitionTime;
            avatar.enableDanceSwitch = data.enableDanceSwitch;
            avatar.DANCE_CLIP_COUNT = Mathf.Clamp(data.danceClipCount, 1, 20);
            avatar.pinnedDanceIndex = data.pinnedDanceIndex;
            avatar.enableHusbandoMode = data.enableHusbandoMode;

            foreach (var tracker in avatar.GetComponentsInChildren<AvatarMouseTracking>(true))
            {
                tracker.enableMouseTracking = data.enableMouseTracking;
                tracker.headBlend = data.headBlend;
                tracker.spineBlend = data.spineBlend;
                tracker.eyeBlend = data.eyeBlend;
            }

            foreach (var ik in avatar.GetComponentsInChildren<IKFix>(true))
                ik.enableIK = data.enableIK;

            foreach (var handler in avatar.GetComponentsInChildren<AvatarParticleHandler>(true))
            {
                handler.featureEnabled = data.enableParticles;
                handler.enabled = data.enableParticles;
                handler.selectedTheme = data.selectedParticleTheme;
                try { handler.SetTheme(data.selectedParticleTheme); } catch { }
            }

            foreach (var holder in avatar.GetComponentsInChildren<HandHolder>(true))
                holder.enableHandHolding = data.enableHandHolding;

            if (avatar.animator != null &&
                avatar.animator.isActiveAndEnabled &&
                avatar.animator.runtimeAnimatorController != null)
            {
                avatar.animator.SetBool("isDancing", false);
                avatar.animator.SetBool("isDragging", false);
                avatar.isDancing = false;
                avatar.isDragging = false;
            }

            foreach (var food in Resources.FindObjectsOfTypeAll<AvatarFoodController>())
                food.SetFeatureEnabled(Instance.data.enableFeedSystem);

            foreach (var handler in Resources.FindObjectsOfTypeAll<AvatarWindowHandler>())
            {
                handler.windowSitYOffset = data.windowSitYOffset;
                handler.windowSitEdge = data.windowSitEdge;
            }

            foreach (var loco in Resources.FindObjectsOfTypeAll<AvatarLocomotionController>())
                loco.EnableLocomotion = data.enableLocomotion;

        }
    }
}
