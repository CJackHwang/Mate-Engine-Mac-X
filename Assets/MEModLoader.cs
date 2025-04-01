using UnityEngine;
using UnityEngine.Networking;
using System.Collections;
using System.Collections.Generic;
using System.IO;

public class MEModLoader : MonoBehaviour
{
    [Header("Required")]
    public ChibiToggle chibiToggle;

    [Header("Optional")]
    public AvatarDragSoundHandler dragSoundHandler;

    // Chibi Mode paths
    private string enterFolder;
    private string exitFolder;
    private string chibiSettingsPath;

    // Drag Mode paths
    private string dragFolder;
    private string placeFolder;

    void Start()
    {
        // Chibi Mode folders
        string chibiBase = Path.Combine(Application.streamingAssetsPath, "Mods/ModLoader/Chibi Mode/Sounds");
        enterFolder = Path.Combine(chibiBase, "Enter Sounds");
        exitFolder = Path.Combine(chibiBase, "Exit Sounds");
        chibiSettingsPath = Path.Combine(Application.streamingAssetsPath, "Mods/ModLoader/Chibi Mode/settings.json");

        // Drag Mode folders
        string dragBase = Path.Combine(Application.streamingAssetsPath, "Mods/ModLoader/Drag Mode/Sounds");
        dragFolder = Path.Combine(dragBase, "Drag Sounds");
        placeFolder = Path.Combine(dragBase, "Place Sounds");

        EnsureFolderStructure();

        StartCoroutine(LoadChibiSounds());
        StartCoroutine(LoadDragSounds());
        StartCoroutine(ApplyChibiSettings());
    }

    private void EnsureFolderStructure()
    {
        TryCreateDirectory(enterFolder);
        TryCreateDirectory(exitFolder);
        TryCreateDirectory(dragFolder);
        TryCreateDirectory(placeFolder);
    }

    private void TryCreateDirectory(string path)
    {
        if (!Directory.Exists(path))
        {
            Directory.CreateDirectory(path);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            Debug.Log("[MEModLoader] Created folder: " + path);
#endif
        }
    }

    IEnumerator LoadChibiSounds()
    {
        List<AudioClip> enterSounds = new List<AudioClip>();
        List<AudioClip> exitSounds = new List<AudioClip>();

        string[] enterFiles = Directory.GetFiles(enterFolder);
        foreach (string file in enterFiles)
            yield return LoadClip(file, clip => enterSounds.Add(clip));

        string[] exitFiles = Directory.GetFiles(exitFolder);
        foreach (string file in exitFiles)
            yield return LoadClip(file, clip => exitSounds.Add(clip));

        if (enterSounds.Count > 0)
            chibiToggle.chibiEnterSounds = enterSounds;

        if (exitSounds.Count > 0)
            chibiToggle.chibiExitSounds = exitSounds;
    }

    IEnumerator LoadDragSounds()
    {
        if (dragSoundHandler == null) yield break;

        List<AudioClip> dragClips = new List<AudioClip>();
        List<AudioClip> placeClips = new List<AudioClip>();

        string[] dragFiles = Directory.GetFiles(dragFolder);
        foreach (string file in dragFiles)
            yield return LoadClip(file, clip => dragClips.Add(clip));

        string[] placeFiles = Directory.GetFiles(placeFolder);
        foreach (string file in placeFiles)
            yield return LoadClip(file, clip => placeClips.Add(clip));

        if (dragClips.Count > 0)
            dragSoundHandler.dragStartSound = CreateRandomAudioSource(dragClips, "DragStart");

        if (placeClips.Count > 0)
            dragSoundHandler.dragStopSound = CreateRandomAudioSource(placeClips, "DragStop");
    }

    private AudioSource CreateRandomAudioSource(List<AudioClip> clips, string label)
    {
        GameObject soundObj = new GameObject($"DynamicSoundPlayer_{label}");
        soundObj.transform.SetParent(this.transform);
        AudioSource source = soundObj.AddComponent<AudioSource>();
        source.playOnAwake = false;
        StartCoroutine(RandomizeClipEveryFrame(source, clips));
        return source;
    }

    private IEnumerator RandomizeClipEveryFrame(AudioSource source, List<AudioClip> clips)
    {
        while (true)
        {
            if (!source.isPlaying)
            {
                source.clip = clips[Random.Range(0, clips.Count)];
            }
            yield return null;
        }
    }

    IEnumerator LoadClip(string filePath, System.Action<AudioClip> onSuccess)
    {
        string ext = Path.GetExtension(filePath).ToLower();
        if (ext != ".wav" && ext != ".mp3" && ext != ".ogg") yield break;

        UnityWebRequest www = UnityWebRequestMultimedia.GetAudioClip("file://" + filePath, GetAudioType(ext));
        yield return www.SendWebRequest();

        if (www.result == UnityWebRequest.Result.Success)
        {
            AudioClip clip = DownloadHandlerAudioClip.GetContent(www);
            onSuccess?.Invoke(clip);
        }
        else
        {
            Debug.LogWarning($"[MEModLoader] Failed to load sound: {filePath} | {www.error}");
        }
    }

    private AudioType GetAudioType(string extension)
    {
        switch (extension)
        {
            case ".mp3": return AudioType.MPEG;
            case ".ogg": return AudioType.OGGVORBIS;
            case ".wav": return AudioType.WAV;
            default: return AudioType.UNKNOWN;
        }
    }

    IEnumerator ApplyChibiSettings()
    {
        if (!File.Exists(chibiSettingsPath))
        {
            Debug.Log("[MEModLoader] No Chibi settings.json found, skipping.");
            yield break;
        }

        string json = File.ReadAllText(chibiSettingsPath);
        if (string.IsNullOrEmpty(json)) yield break;

        ChibiSettingsData settings;
        try
        {
            settings = JsonUtility.FromJson<ChibiSettingsData>(json);
        }
        catch
        {
            Debug.LogWarning("[MEModLoader] Failed to parse Chibi settings.json.");
            yield break;
        }

        if (chibiToggle != null)
        {
            chibiToggle.chibiArmatureScale = settings.chibiArmatureScale;
            chibiToggle.chibiHeadScale = settings.chibiHeadScale;
            chibiToggle.chibiUpperLegScale = settings.chibiUpperLegScale;
            chibiToggle.chibiYOffset = settings.chibiYOffset;
            chibiToggle.screenInteractionRadius = settings.screenInteractionRadius;
            chibiToggle.holdDuration = settings.holdDuration;

            Debug.Log("[MEModLoader] Applied Chibi settings from JSON.");
        }
    }
}

[System.Serializable]
public class ChibiSettingsData
{
    public Vector3 chibiArmatureScale = new Vector3(0.3f, 0.3f, 0.3f);
    public Vector3 chibiHeadScale = new Vector3(2.7f, 2.7f, 2.7f);
    public Vector3 chibiUpperLegScale = new Vector3(0.6f, 0.6f, 0.6f);
    public float chibiYOffset = 0.5f;
    public float screenInteractionRadius = 30f;
    public float holdDuration = 2f;
}
