using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Newtonsoft.Json;
using UnityEngine.Events;
using System.Linq;
using UnityEngine.Audio;

public class AvatarLibraryMenu : MonoBehaviour
{
    [Header("UI References")]
    public GameObject avatarItemPrefab;
    public GameObject avatarItemPrefabDLC;
    public Transform contentParent;
    public GameObject libraryPanel;

    [Header("DLC Avatars")]
    public List<DLCEntry> dlcAvatars = new List<DLCEntry>();

    private string avatarsJsonPath => Path.Combine(Application.persistentDataPath, "avatars.json");
    private string thumbnailsFolder => Path.Combine(Application.persistentDataPath, "Thumbnails");

    private List<AvatarEntry> avatarEntries = new List<AvatarEntry>();

    [System.Serializable]
    public class DLCEntry
    {
        public GameObject prefab;
        public string displayName;
        public string author;
        public string version;
        public string fileType;
        public Texture2D thumbnail;
    }

    [System.Serializable]
    public class AvatarEntry
    {
        public string displayName;
        public string author;
        public string version;
        public string fileType; // VRM0.X or VRM1.X
        public string filePath;
        public string thumbnailPath;
        public int polygonCount;
        public bool isSteamWorkshop = false;
        public ulong steamFileId = 0;
    }

    private void Start()
    {
        if (!Directory.Exists(thumbnailsFolder))
            Directory.CreateDirectory(thumbnailsFolder);

        LoadAvatarList();
        RefreshUI();
    }

    public void OpenLibrary()
    {
        libraryPanel.SetActive(true);
    }

    public void CloseLibrary()
    {
        libraryPanel.SetActive(false);
    }

    private void LoadAvatarList()
    {
        avatarEntries.Clear();

        if (File.Exists(avatarsJsonPath))
        {
            try
            {
                string json = File.ReadAllText(avatarsJsonPath);
                avatarEntries = JsonConvert.DeserializeObject<List<AvatarEntry>>(json);
            }
            catch (System.Exception e)
            {
                Debug.LogError("[AvatarLibraryMenu] Failed to load avatars.json: " + e.Message);
            }
        }
    }

    private void RefreshUI()
    {
        foreach (Transform child in contentParent)
        {
            Destroy(child.gameObject);
        }

        foreach (var dlc in dlcAvatars)
        {
            if (dlc.prefab == null) continue;
            GameObject item = Instantiate(avatarItemPrefabDLC != null ? avatarItemPrefabDLC : avatarItemPrefab, contentParent);
            SetupDLCItem(item, dlc);
        }

        foreach (var entry in avatarEntries)
        {
            GameObject item = Instantiate(avatarItemPrefab, contentParent);
            SetupAvatarItem(item, entry);
        }
    }

    private void SetupDLCItem(GameObject item, DLCEntry dlc)
    {
        RawImage thumbnail = item.transform.Find("RawImage").GetComponent<RawImage>();
        TMP_Text titleText = item.transform.Find("Title").GetComponent<TMP_Text>();
        TMP_Text authorText = item.transform.Find("Author").GetComponent<TMP_Text>();
        TMP_Text versionText = item.transform.Find("Version").GetComponent<TMP_Text>();
        TMP_Text fileTypeText = item.transform.Find("File Type").GetComponent<TMP_Text>();
        TMP_Text polygonText = item.transform.Find("Polygons")?.GetComponent<TMP_Text>();
        Button loadButton = item.transform.Find("Button").GetComponent<Button>();
        Button removeButton = item.transform.Find("Remove")?.GetComponent<Button>();
        Button uploadButton = item.transform.Find("Upload")?.GetComponent<Button>();
        Slider uploadSlider = item.transform.Find("UploadBar")?.GetComponent<Slider>();

        if (titleText != null) titleText.text = "Name: " + (!string.IsNullOrEmpty(dlc.displayName) ? dlc.displayName : dlc.prefab.name);
        if (authorText != null) authorText.text = "Author: " + dlc.author;
        if (versionText != null) versionText.text = "Version: " + dlc.version;
        if (fileTypeText != null) fileTypeText.text = "Format: " + dlc.fileType;

        int polyCount = 0;
        foreach (var mesh in dlc.prefab.GetComponentsInChildren<MeshFilter>(true))
            if (mesh.sharedMesh != null) polyCount += mesh.sharedMesh.triangles.Length / 3;
        foreach (var smr in dlc.prefab.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            if (smr.sharedMesh != null) polyCount += smr.sharedMesh.triangles.Length / 3;
        if (polygonText != null) polygonText.text = "Polygons: " + polyCount;

        if (thumbnail != null && dlc.thumbnail != null)
        {
            thumbnail.texture = dlc.thumbnail;
        }

        loadButton.onClick.RemoveAllListeners();
        loadButton.onClick.AddListener(() => LoadAvatar(dlc.prefab.name));

        if (uploadSlider != null) uploadSlider.gameObject.SetActive(false);
    }

    private void SetupAvatarItem(GameObject item, AvatarEntry entry)
    {
        RawImage thumbnail = item.transform.Find("RawImage").GetComponent<RawImage>();
        TMP_Text titleText = item.transform.Find("Title").GetComponent<TMP_Text>();
        TMP_Text authorText = item.transform.Find("Author").GetComponent<TMP_Text>();
        TMP_Text versionText = item.transform.Find("Version").GetComponent<TMP_Text>();
        TMP_Text fileTypeText = item.transform.Find("File Type").GetComponent<TMP_Text>();
        TMP_Text polygonText = item.transform.Find("Polygons")?.GetComponent<TMP_Text>();
        Button loadButton = item.transform.Find("Button").GetComponent<Button>();
        Button removeButton = item.transform.Find("Remove").GetComponent<Button>();
        Button uploadButton = item.transform.Find("Upload")?.GetComponent<Button>();
        Slider uploadSlider = item.transform.Find("UploadBar")?.GetComponent<Slider>();

        if (titleText != null) titleText.text = "Name: " + entry.displayName;
        if (authorText != null) authorText.text = "Author: " + entry.author;
        if (versionText != null) versionText.text = "Version: " + entry.version;
        if (fileTypeText != null) fileTypeText.text = "Format: " + entry.fileType;
        if (polygonText != null) polygonText.text = "Polygons: " + entry.polygonCount;

        if (thumbnail != null && File.Exists(entry.thumbnailPath))
        {
            byte[] imageBytes = File.ReadAllBytes(entry.thumbnailPath);
            Texture2D tex = new Texture2D(2, 2);
            tex.LoadImage(imageBytes);
            thumbnail.texture = tex;
        }

        loadButton.onClick.RemoveAllListeners();
        loadButton.onClick.AddListener(() => LoadAvatar(entry.filePath));

        removeButton.onClick.RemoveAllListeners();
        removeButton.onClick.AddListener(() =>
        {
            if (entry.isSteamWorkshop && entry.steamFileId != 0)
            {
                if (SteamWorkshopHandler.Instance != null)
                    SteamWorkshopHandler.Instance.UnsubscribeAndDelete(new Steamworks.PublishedFileId_t(entry.steamFileId));
            }
            RemoveAvatar(entry);
        });

        if (uploadButton != null)
        {
            uploadButton.onClick.RemoveAllListeners();

            var handler = uploadButton.GetComponent<UploadButtonHoldHandler>();
            if (handler != null)
            {
                handler.entry = new AvatarEntry
                {
                    displayName = entry.displayName,
                    author = entry.author,
                    version = entry.version,
                    fileType = entry.fileType,
                    filePath = entry.filePath,
                    thumbnailPath = entry.thumbnailPath,
                    polygonCount = entry.polygonCount,
                    isSteamWorkshop = entry.isSteamWorkshop,
                    steamFileId = entry.steamFileId
                };

                handler.progressSlider = uploadSlider;
                handler.labelText = uploadButton.GetComponentInChildren<TMP_Text>();
            }
        }

        if (uploadSlider != null)
        {
            uploadSlider.gameObject.SetActive(false);
        }
    }

    private void LoadAvatar(string path)
    {
        var loader = FindFirstObjectByType<VRMLoader>();
        if (loader != null)
        {
            loader.LoadVRM(path);
        }
        else
        {
            Debug.LogError("[AvatarLibraryMenu] VRMLoader not found in scene!");
        }
    }

    public static void AddAvatarToLibrary(string displayName, string author, string version, string fileType, string filePath, Texture2D thumbnail, int polygonCount)
    {
        string avatarsJsonPath = Path.Combine(Application.persistentDataPath, "avatars.json");
        string thumbnailsFolder = Path.Combine(Application.persistentDataPath, "Thumbnails");

        if (!Directory.Exists(thumbnailsFolder))
            Directory.CreateDirectory(thumbnailsFolder);

        List<AvatarEntry> entries = new List<AvatarEntry>();

        if (File.Exists(avatarsJsonPath))
        {
            try
            {
                string json = File.ReadAllText(avatarsJsonPath);
                entries = JsonConvert.DeserializeObject<List<AvatarEntry>>(json);
            }
            catch { }
        }
        if (entries.Exists(e => e.filePath == filePath))
        {
            Debug.Log($"[AvatarLibraryMenu] VRM already exists in library, skipping: {displayName}");
            return;
        }

        string thumbnailFileName = Path.GetFileNameWithoutExtension(filePath) + "_thumb.png";
        string thumbnailPath = Path.Combine(thumbnailsFolder, thumbnailFileName);

        if (thumbnail != null)
        {
            File.WriteAllBytes(thumbnailPath, thumbnail.EncodeToPNG());
        }

        AvatarEntry newEntry = new AvatarEntry
        {
            displayName = displayName,
            author = author,
            version = version,
            fileType = fileType,
            filePath = filePath,
            thumbnailPath = thumbnailPath,
            polygonCount = polygonCount
        };

        entries.Add(newEntry);

        string newJson = JsonConvert.SerializeObject(entries, Formatting.Indented);
        File.WriteAllText(avatarsJsonPath, newJson);
    }

    public void ReloadAvatars()
    {
        LoadAvatarList();
        RefreshUI();
    }

    private void RemoveAvatar(AvatarEntry entryToRemove)
    {
        string avatarsJsonPath = Path.Combine(Application.persistentDataPath, "avatars.json");

        if (!File.Exists(avatarsJsonPath))
            return;

        List<AvatarEntry> entries = new List<AvatarEntry>();

        try
        {
            string json = File.ReadAllText(avatarsJsonPath);
            entries = JsonConvert.DeserializeObject<List<AvatarEntry>>(json);
        }
        catch { }

        entries = entries.Where(e => e.filePath != entryToRemove.filePath).ToList();

        if (entryToRemove.isSteamWorkshop && File.Exists(entryToRemove.filePath))
        {
            try
            {
                File.Delete(entryToRemove.filePath);
                Debug.Log("[AvatarLibraryMenu] Deleted Workshop model file: " + entryToRemove.filePath);
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("[AvatarLibraryMenu] Could not delete Workshop model file: " + e.Message);
            }
        }

        if (File.Exists(entryToRemove.thumbnailPath))
        {
            try
            {
                File.Delete(entryToRemove.thumbnailPath);
            }
            catch { }
        }

        string newJson = JsonConvert.SerializeObject(entries, Formatting.Indented);
        File.WriteAllText(avatarsJsonPath, newJson);

        ReloadAvatars();
    }

}
