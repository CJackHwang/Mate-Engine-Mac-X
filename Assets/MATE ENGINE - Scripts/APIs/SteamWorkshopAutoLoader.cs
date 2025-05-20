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

        StartCoroutine(LoadSubscribedWorkshopAvatars());
        CleanupUnsubscribedWorkshopFiles();
    }

    private IEnumerator LoadSubscribedWorkshopAvatars()
    {
        uint count = SteamUGC.GetNumSubscribedItems();
        if (count == 0) yield break;

        PublishedFileId_t[] subscribed = new PublishedFileId_t[count];
        SteamUGC.GetSubscribedItems(subscribed, count);

        foreach (var fileId in subscribed)
        {
            SteamUGC.DownloadItem(fileId, true);

            bool installed = false;
            string installPath = null;

            float timeout = 10f;
            while (timeout > 0f)
            {
                yield return new WaitForSeconds(0.5f);
                installed = SteamUGC.GetItemInstallInfo(fileId, out ulong _, out installPath, 260, out _);
                if (installed && Directory.Exists(installPath)) break;
                timeout -= 0.5f;
            }

            if (!installed || string.IsNullOrEmpty(installPath))
            {
                Debug.LogWarning("[WorkshopAutoLoader] Failed to install item: " + fileId);
                continue;
            }

            // Look for VRM or ME file
            string file = Directory.GetFiles(installPath)
                                   .FirstOrDefault(f => allowedExtensions.Contains(Path.GetExtension(f).ToLower()));
            if (string.IsNullOrEmpty(file))
            {
                Debug.LogWarning("[WorkshopAutoLoader] No VRM/ME file found in: " + installPath);
                continue;
            }

            string targetPath = Path.Combine(workshopFolderPath, Path.GetFileName(file));
            if (!File.Exists(targetPath))
                File.Copy(file, targetPath);

            var allAvatars = GetAvatarEntries();
            if (allAvatars.Any(e => e.filePath == targetPath)) continue;

            // --- Load metadata.json ---
            string displayName = Path.GetFileNameWithoutExtension(file);
            string author = "Workshop";
            string version = "1.0";
            string format = file.ToLower().EndsWith(".me") ? ".ME" : "VRM";
            int polygonCount = 0;

            bool isNSFW = false;
            string metaPath = Path.Combine(installPath, "metadata.json");
            if (File.Exists(metaPath))
            {
                try
                {
                    var meta = JsonConvert.DeserializeObject<Dictionary<string, object>>(File.ReadAllText(metaPath));
                    if (meta.TryGetValue("displayName", out var d)) displayName = d.ToString();
                    if (meta.TryGetValue("author", out var a)) author = a.ToString();
                    if (meta.TryGetValue("version", out var v)) version = v.ToString();
                    if (meta.TryGetValue("fileType", out var f)) format = f.ToString();
                    if (meta.TryGetValue("polygonCount", out var p)) polygonCount = Convert.ToInt32(p);
                    if (meta.TryGetValue("isNSFW", out var nswfVal)) isNSFW = Convert.ToBoolean(nswfVal); // <<<<<<<< HINZUGEFÜGT
                }
                catch (Exception e)
                {
                    Debug.LogWarning("[WorkshopAutoLoader] Failed to parse metadata.json: " + e.Message);
                }
            }


            // --- Copy thumbnail based on filename ---
            string thumbnailsFolder = Path.Combine(Application.persistentDataPath, "Thumbnails");
            Directory.CreateDirectory(thumbnailsFolder);
            string thumbFileName = Path.GetFileNameWithoutExtension(file) + "_thumb.png";
            string thumbSource = Path.Combine(installPath, thumbFileName);
            string thumbnailPath = "";

            if (File.Exists(thumbSource))
            {
                thumbnailPath = Path.Combine(thumbnailsFolder, Path.GetFileName(thumbSource));
                File.Copy(thumbSource, thumbnailPath, true);
            }

            // --- Register avatar entry ---
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
                isNSFW = isNSFW
            };


            allAvatars.Add(newEntry);
            SaveAvatarEntries(allAvatars);
        }

        library.ReloadAvatars();
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
        for (int i = avatars.Count - 1; i >= 0; i--)
        {
            var a = avatars[i];
            if (a.isSteamWorkshop && a.steamFileId != 0 && !subscribedIds.Contains(a.steamFileId))
            {
                if (File.Exists(a.filePath)) File.Delete(a.filePath);
                if (File.Exists(a.thumbnailPath)) File.Delete(a.thumbnailPath);
                avatars.RemoveAt(i);
                changed = true;
                Debug.Log("[SteamWorkshopAutoLoader] Removed unsubscribed avatar: " + a.displayName);
            }
        }

        if (changed)
            SaveAvatarEntries(avatars);
    }

}
