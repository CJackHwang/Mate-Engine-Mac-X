using UnityEngine;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;

public class SaveLoadHandler : MonoBehaviour
{
    public static SaveLoadHandler Instance { get; private set; }

    public SettingsData data;

    private string fileName = "settings.json";
    private string FilePath => Path.Combine(Application.persistentDataPath, fileName);

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);

        LoadFromDisk();
        ApplyAllSettingsToAllAvatars();
        LoadAccessoryStatesToAll();

        var limiters = FindObjectsByType<FPSLimiter>(FindObjectsSortMode.None);
        foreach (var limiter in limiters)
        {
            limiter.targetFPS = data.fpsLimit;
            limiter.ApplyFPSLimit();
        }
    }

    public void SaveToDisk()
    {
        try
        {
            SaveAccessoryStatesFromAll();

            string dir = Path.GetDirectoryName(FilePath);
            if (!Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            string json = JsonConvert.SerializeObject(data, Formatting.Indented);
            File.WriteAllText(FilePath, json);
            Debug.Log("[SaveLoadHandler] Saved settings to: " + FilePath);
        }
        catch (System.Exception e)
        {
            Debug.LogError("[SaveLoadHandler] Failed to save: " + e);
        }
    }

    public void LoadFromDisk()
    {
        if (File.Exists(FilePath))
        {
            try
            {
                string json = File.ReadAllText(FilePath);
                data = JsonConvert.DeserializeObject<SettingsData>(json);
                Debug.Log("[SaveLoadHandler] Loaded settings from: " + FilePath);
            }
            catch (System.Exception e)
            {
                Debug.LogError("[SaveLoadHandler] Failed to load: " + e);
                data = new SettingsData(); // fallback
            }
        }
        else
        {
            data = new SettingsData(); // defaults
        }
    }

    [System.Serializable]
    public class SettingsData
    {
        public enum WindowSizeState { Normal, Big, Small }
        public WindowSizeState windowSizeState = WindowSizeState.Normal;

        public float soundThreshold = 0.2f;
        public float idleSwitchTime = 10f;
        public float idleTransitionTime = 1f;
        public float avatarSize = 1.0f;
        public bool enableDancing = true;
        public bool enableMouseTracking = true;
        public int fpsLimit = 90;
        public bool isTopmost = true;

        public List<string> allowedApps = new List<string>();
        public bool bloom = false;
        public bool dayNight = true;

        public bool enableParticles = true;
        public float petVolume = 1f;
        public float effectsVolume = 1f;
        public float menuVolume = 1f;
        public float headBlend = 0.7f;
        public float spineBlend = 0.5f;
        public bool enableHandHolding = true;
        public bool enableWindowSitting = false;
        public bool ambientOcclusion = false;

        public float uiHueShift = 0f;     // Default hue = 0
        public float uiSaturation = 0.5f; // Default saturation = neutral


        public bool enableDiscordRPC = true;

        public bool tutorialDone = false; // NEW: Save flag for tutorial completion

        public string selectedLocaleCode = "en"; // Default to English


        public Dictionary<string, bool> modStates = new Dictionary<string, bool>(); // Mod States



        public int graphicsQualityLevel = 1;

        // Steam DLCS
        public Dictionary<string, bool> accessoryStates = new Dictionary<string, bool>();
    }

    public static void SyncAllowedAppsToAllAvatars()
    {
        var allAvatars = Resources.FindObjectsOfTypeAll<AvatarAnimatorController>();
        var list = new List<string>(Instance.data.allowedApps);

        foreach (var avatar in allAvatars)
        {
            avatar.allowedApps = list;
        }
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
            avatar.allowedApps = new List<string>(data.allowedApps);
            avatar.transform.localScale = Vector3.one * data.avatarSize;

            foreach (var tracker in avatar.GetComponentsInChildren<AvatarMouseTracking>(true))
            {
                tracker.enableMouseTracking = data.enableMouseTracking;
                tracker.headBlend = data.headBlend;
                tracker.spineBlend = data.spineBlend;
            }


            foreach (var handler in avatar.GetComponentsInChildren<AvatarParticleHandler>(true))
            {
                handler.featureEnabled = data.enableParticles;
                handler.enabled = data.enableParticles;
            }

            foreach (var holder in avatar.GetComponentsInChildren<HandHolder>(true))
            {
                holder.enableHandHolding = data.enableHandHolding;
            }


            if (avatar.animator != null &&
                avatar.animator.isActiveAndEnabled &&
                avatar.animator.runtimeAnimatorController != null)
            {
                avatar.animator.SetBool("isDancing", false);
                avatar.animator.SetBool("isDragging", false);
                avatar.isDancing = false;
                avatar.isDragging = false;
            }


        }
    }

    // DLCs and Steam

    public static void LoadAccessoryStatesToAll()
    {
        foreach (var handler in AccessoiresHandler.ActiveHandlers)
        {
            handler.LoadAccessoryStatesFromSave();
        }
    }

    public static void SaveAccessoryStatesFromAll()
    {
        foreach (var handler in AccessoiresHandler.ActiveHandlers)
        {
            handler.SaveAccessoryStatesToSave();
        }
    }
}
