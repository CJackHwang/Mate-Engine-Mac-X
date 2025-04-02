using UnityEngine;
using System.Collections.Generic;
using System.IO;

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
    }

    public void SaveToDisk()
    {
        try
        {
            string dir = Path.GetDirectoryName(FilePath);
            if (!Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            string json = JsonUtility.ToJson(data, true);
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
                data = JsonUtility.FromJson<SettingsData>(json);
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
        public float soundThreshold = 0.2f;
        public float idleSwitchTime = 10f;
        public float idleTransitionTime = 1f;
        public float avatarSize = 1.0f;
        public bool enableAudioDetection = true;
        public bool enableDancing = true;
        public bool enableMouseTracking = true;
        public int fpsLimit = 90;
        public bool isTopmost = true;

        public List<string> allowedApps = new List<string>();

        // These are OFF by default
        public bool fakeHDR = false;
        public bool bloom = false;
        public bool dayNight = false;
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
            avatar.enableAudioDetection = data.enableAudioDetection;
            avatar.enableDancing = data.enableDancing;
            avatar.allowedApps = new List<string>(data.allowedApps);
            avatar.transform.localScale = Vector3.one * data.avatarSize;

            foreach (var tracker in avatar.GetComponentsInChildren<AvatarMouseTracking>(true))
            {
                tracker.enableMouseTracking = data.enableMouseTracking;
            }

            // Force animator to sync with updated state
            if (avatar.animator != null)
            {
                avatar.animator.SetBool("isDancing", false);
                avatar.animator.SetBool("isDragging", false);

                // Trigger re-check on next Update()
                avatar.isDancing = false;
                avatar.isDragging = false;
            }
        }
    }



}
