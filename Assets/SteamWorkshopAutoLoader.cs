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

            // Wait until file is downloaded
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

            // Look for VRM or ME files inside the install directory
            string file = Directory.GetFiles(installPath)
                                   .FirstOrDefault(f => allowedExtensions.Contains(Path.GetExtension(f).ToLower()));
            if (string.IsNullOrEmpty(file))
            {
                Debug.LogWarning("[WorkshopAutoLoader] No VRM/ME file found in: " + installPath);
                continue;
            }

            // Copy to Workshop folder
            string targetPath = Path.Combine(workshopFolderPath, Path.GetFileName(file));
            if (!File.Exists(targetPath))
                File.Copy(file, targetPath);

            // Load model meta from filename
            string displayName = Path.GetFileNameWithoutExtension(file);
            string author = "Workshop";
            string version = "1.0";
            string format = file.ToLower().EndsWith(".me") ? ".ME" : "VRM";
            int polygonCount = 0;

            Texture2D thumbnail = Texture2D.blackTexture;

            // Avoid duplicates
            var allAvatars = GetAvatarEntries();
            if (allAvatars.Any(e => e.filePath == targetPath)) continue;

            var newEntry = new AvatarLibraryMenu.AvatarEntry
            {
                displayName = displayName,
                author = author,
                version = version,
                fileType = format,
                filePath = targetPath,
                thumbnailPath = "", // Optional for now
                polygonCount = polygonCount,
                isSteamWorkshop = true,
                steamFileId = fileId.m_PublishedFileId
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
}
