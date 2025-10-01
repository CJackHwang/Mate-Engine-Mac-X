using System;
using System.IO;
using System.Linq;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Steamworks;
using Newtonsoft.Json;

public class SteamWorkshopAutoLoader : MonoBehaviour
{
    private const string WorkshopFolderName = "Steam Workshop";
    private string workshopFolderPath => Path.Combine(Application.persistentDataPath, WorkshopFolderName);
    private readonly List<string> allowedExtensions = new List<string> { ".vrm", ".me" };
    private AvatarLibraryMenu library;

    private Callback<DownloadItemResult_t> downloadCallback;
    private Callback<RemoteStoragePublishedFileSubscribed_t> subscribedCallback;

    private bool isRefreshing = false;
    public bool hadChangesLastRun { get; private set; }

    private void Awake()
    {
        if (SteamManager.Initialized)
        {
            downloadCallback = Callback<DownloadItemResult_t>.Create(OnWorkshopItemDownloaded);
            subscribedCallback = Callback<RemoteStoragePublishedFileSubscribed_t>.Create(OnWorkshopItemSubscribed);
        }
    }

    private void Start()
    {
        if (!SteamManager.Initialized)
        {
            Debug.LogWarning("[WorkshopAutoLoader] Steam not initialized.");
            return;
        }

        library = FindFirstObjectByType<AvatarLibraryMenu>();
        if (library == null)
        {
            Debug.LogError("[WorkshopAutoLoader] AvatarLibraryMenu not found.");
            return;
        }

        if (!Directory.Exists(workshopFolderPath))
            Directory.CreateDirectory(workshopFolderPath);

        RefreshWorkshopAvatars();
    }

    private void OnWorkshopItemSubscribed(RemoteStoragePublishedFileSubscribed_t data)
    {
        SteamUGC.DownloadItem(data.m_nPublishedFileId, true);
        RefreshWorkshopAvatars();
    }

    private void OnWorkshopItemDownloaded(DownloadItemResult_t result)
    {
        if (result.m_eResult == EResult.k_EResultOK)
            RefreshWorkshopAvatars();
    }

    public void RefreshWorkshopAvatars()
    {
        if (isRefreshing) return;
        StartCoroutine(LoadSubscribedWorkshopAvatars());
        CleanupUnsubscribedWorkshopFiles();
    }

    private IEnumerator LoadSubscribedWorkshopAvatars()
    {
        if (isRefreshing) yield break;
        isRefreshing = true;
        hadChangesLastRun = false;

        try
        {
            uint count = SteamUGC.GetNumSubscribedItems();
            if (count == 0) yield break;

            PublishedFileId_t[] subscribed = new PublishedFileId_t[count];
            SteamUGC.GetSubscribedItems(subscribed, count);

            bool anyChange = false;

            foreach (var fileId in subscribed)
            {
                uint state = SteamUGC.GetItemState(fileId);
                bool isInstalled = (state & (uint)EItemState.k_EItemStateInstalled) != 0;
                bool needsUpdate = (state & (uint)EItemState.k_EItemStateNeedsUpdate) != 0;

                if (!isInstalled || needsUpdate)
                {
                    SteamUGC.DownloadItem(fileId, true);
                }

                bool installed = false;
                string installPath = null;

                float timeout = 10f;
                while (timeout > 0f)
                {
                    installed = SteamUGC.GetItemInstallInfo(fileId, out ulong _, out installPath, 1024, out _);
                    if (installed && !string.IsNullOrEmpty(installPath) && Directory.Exists(installPath))
                        break;

                    yield return new WaitForSeconds(0.5f);
                    timeout -= 0.5f;
                }

                if (!installed || string.IsNullOrEmpty(installPath) || !Directory.Exists(installPath))
                {
                    Debug.LogWarning("[WorkshopAutoLoader] Failed to install item: " + fileId);
                    continue;
                }
                string file = Directory.GetFiles(installPath)
                                       .FirstOrDefault(f => allowedExtensions.Contains(Path.GetExtension(f).ToLower()));
                if (string.IsNullOrEmpty(file))
                {
                    Debug.LogWarning("[WorkshopAutoLoader] No VRM/ME file found in: " + installPath);
                    continue;
                }
                string baseName = Path.GetFileName(file);
                string targetPath = Path.Combine(workshopFolderPath, baseName);

                var allAvatarsPeek = GetAvatarEntries();
                var existingSamePath = allAvatarsPeek.FirstOrDefault(e => e.filePath == targetPath);
                if (existingSamePath != null && existingSamePath.steamFileId != fileId.m_PublishedFileId)
                {
                    targetPath = Path.Combine(workshopFolderPath, $"{fileId.m_PublishedFileId}_{baseName}");
                }

                bool copiedModel = false;
                try
                {
                    if (!File.Exists(targetPath))
                    {
                        File.Copy(file, targetPath, true);
                        copiedModel = true;
                    }
                    else if (needsUpdate)
                    {
                        File.Copy(file, targetPath, true);
                        copiedModel = true;
                    }
                }
                catch (Exception e)
                {
                    Debug.LogWarning("[WorkshopAutoLoader] Could not copy model file: " + e.Message);
                }
                var allAvatars = GetAvatarEntries();
                bool alreadyRegistered = allAvatars.Any(e => e.filePath == targetPath);
                string displayName = Path.GetFileNameWithoutExtension(file);
                string author = "Workshop";
                string version = "1.0";
                string format = file.EndsWith(".me", StringComparison.OrdinalIgnoreCase) ? ".ME" : "VRM";
                int polygonCount = 0;
                bool isNSFW = false;

                string metaPath = Path.Combine(installPath, "metadata.json");
                if (File.Exists(metaPath))
                {
                    try
                    {
                        var meta = JsonConvert.DeserializeObject<Dictionary<string, object>>(File.ReadAllText(metaPath));
                        if (meta != null)
                        {
                            if (meta.TryGetValue("displayName", out var d)) displayName = d?.ToString() ?? displayName;
                            if (meta.TryGetValue("author", out var a)) author = a?.ToString() ?? author;
                            if (meta.TryGetValue("version", out var v)) version = v?.ToString() ?? version;
                            if (meta.TryGetValue("fileType", out var f)) format = f?.ToString() ?? format;
                            if (meta.TryGetValue("polygonCount", out var p)) polygonCount = Convert.ToInt32(p);
                            if (meta.TryGetValue("isNSFW", out var n)) isNSFW = Convert.ToBoolean(n);
                        }
                    }
                    catch (Exception e)
                    {
                        Debug.LogWarning("[WorkshopAutoLoader] Failed to parse metadata.json: " + e.Message);
                    }
                }
                string thumbnailsFolder = Path.Combine(Application.persistentDataPath, "Thumbnails");
                Directory.CreateDirectory(thumbnailsFolder);
                string thumbFileName = Path.GetFileNameWithoutExtension(targetPath) + "_thumb.png";
                string thumbSourceA = Path.Combine(installPath, Path.GetFileNameWithoutExtension(file) + "_thumb.png");
                string thumbSourceB = Path.Combine(installPath, Path.GetFileNameWithoutExtension(targetPath) + "_thumb.png");
                string thumbSource = File.Exists(thumbSourceA) ? thumbSourceA : thumbSourceB;

                string thumbnailPath = "";
                if (File.Exists(thumbSource))
                {
                    try
                    {
                        thumbnailPath = Path.Combine(thumbnailsFolder, Path.GetFileName(thumbFileName));
                        File.Copy(thumbSource, thumbnailPath, true);
                    }
                    catch (Exception e)
                    {
                        Debug.LogWarning("[WorkshopAutoLoader] Could not copy thumbnail: " + e.Message);
                    }
                }
                if (!alreadyRegistered)
                {
                    var newEntry = new AvatarLibraryMenu.AvatarEntry
                    {
                        displayName = displayName,
                        author = author,
                        version = version,
                        fileType = format,
                        filePath = targetPath,
                        thumbnailPath = thumbnailPath,
                        polygonCount = polygonCount,
                        isSteamWorkshop = true,
                        steamFileId = fileId.m_PublishedFileId,
                        isNSFW = isNSFW,

                        isOwner = false
                    };


                    allAvatars.Add(newEntry);
                    SaveAvatarEntries(allAvatars);
                    anyChange = true;
                }
                else
                {
                    var entry = allAvatars.First(e => e.filePath == targetPath);
                    bool metaChanged = false;

                    if (entry.displayName != displayName) { entry.displayName = displayName; metaChanged = true; }
                    if (entry.author != author) { entry.author = author; metaChanged = true; }
                    if (entry.version != version) { entry.version = version; metaChanged = true; }
                    if (entry.fileType != format) { entry.fileType = format; metaChanged = true; }
                    if (entry.polygonCount != polygonCount) { entry.polygonCount = polygonCount; metaChanged = true; }
                    if (entry.isNSFW != isNSFW) { entry.isNSFW = isNSFW; metaChanged = true; }
                    if (!string.IsNullOrEmpty(thumbnailPath) && entry.thumbnailPath != thumbnailPath)
                    {
                        entry.thumbnailPath = thumbnailPath; metaChanged = true;
                    }
                    if (entry.steamFileId == 0)
                    {
                        entry.isSteamWorkshop = true;
                        entry.steamFileId = fileId.m_PublishedFileId;
                        metaChanged = true;
                    }

                    if (metaChanged || copiedModel)
                    {
                        SaveAvatarEntries(allAvatars);
                        anyChange = true;
                    }
                }
            }

            if (anyChange && library != null)
            {
                library.ReloadAvatars();
                hadChangesLastRun = true;
            }
        }
        finally
        {
            isRefreshing = false;
        }
    }

    private List<AvatarLibraryMenu.AvatarEntry> GetAvatarEntries()
    {
        string path = Path.Combine(Application.persistentDataPath, "avatars.json");
        if (!File.Exists(path)) return new List<AvatarLibraryMenu.AvatarEntry>();

        try
        {
            string json = File.ReadAllText(path);
            return JsonConvert.DeserializeObject<List<AvatarLibraryMenu.AvatarEntry>>(json) ?? new List<AvatarLibraryMenu.AvatarEntry>();
        }
        catch
        {
            return new List<AvatarLibraryMenu.AvatarEntry>();
        }
    }

    private void SaveAvatarEntries(List<AvatarLibraryMenu.AvatarEntry> entries)
    {
        string path = Path.Combine(Application.persistentDataPath, "avatars.json");
        string json = JsonConvert.SerializeObject(entries, Formatting.Indented);
        File.WriteAllText(path, json);
    }

    private void CleanupUnsubscribedWorkshopFiles()
    {
        var avatars = GetAvatarEntries();
        var subscribedIds = new HashSet<ulong>();

        uint count = SteamUGC.GetNumSubscribedItems();
        if (count > 0)
        {
            PublishedFileId_t[] subscribed = new PublishedFileId_t[count];
            SteamUGC.GetSubscribedItems(subscribed, count);
            foreach (var id in subscribed)
                subscribedIds.Add(id.m_PublishedFileId);
        }

        bool changed = false;
        string workshopDir = Path.GetFullPath(workshopFolderPath);
        string thumbsDir = Path.GetFullPath(Path.Combine(Application.persistentDataPath, "Thumbnails"));

        for (int i = avatars.Count - 1; i >= 0; i--)
        {
            var a = avatars[i];
            if (!(a.isSteamWorkshop && a.steamFileId != 0 && !subscribedIds.Contains(a.steamFileId)))
                continue;

            string fileFull = string.IsNullOrEmpty(a.filePath) ? "" : Path.GetFullPath(a.filePath);
            string thumbFull = string.IsNullOrEmpty(a.thumbnailPath) ? "" : Path.GetFullPath(a.thumbnailPath);

            bool isInsideWorkshop = !string.IsNullOrEmpty(fileFull) &&
                                    fileFull.StartsWith(workshopDir, StringComparison.OrdinalIgnoreCase);

            if (isInsideWorkshop)
            {
                try { if (File.Exists(fileFull)) File.Delete(fileFull); } catch (Exception e) { Debug.LogWarning("[SteamWorkshopAutoLoader] Could not delete model file: " + e.Message); }
                try
                {
                    if (!string.IsNullOrEmpty(thumbFull) &&
                        thumbFull.StartsWith(thumbsDir, StringComparison.OrdinalIgnoreCase) &&
                        File.Exists(thumbFull))
                    {
                        File.Delete(thumbFull);
                    }
                }
                catch { }

                avatars.RemoveAt(i);
                changed = true;
                Debug.Log("[SteamWorkshopAutoLoader] Removed unsubscribed avatar (workshop copy): " + a.displayName);
            }
            else
            {
                a.isSteamWorkshop = false;
                changed = true;
                Debug.Log("[SteamWorkshopAutoLoader] Kept local file (not in workshop dir); cleared isSteamWorkshop but PRESERVED steamFileId for: " + a.displayName);
            }
        }

        if (changed)
            SaveAvatarEntries(avatars);
    }
}