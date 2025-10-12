using System;
using System.IO;
using System.IO.Compression;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using Steamworks;
using Newtonsoft.Json;

public class SteamWorkshopHandler : MonoBehaviour
{
    public static SteamWorkshopHandler Instance { get; private set; }
    private static readonly AppId_t appId = new AppId_t(3625270);
    private Coroutine activeProgressRoutine = null;

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    public void UploadToWorkshop(AvatarLibraryMenu.AvatarEntry entry, Slider progressBar = null)
    {
        if (!SteamManager.Initialized) return;
        if (!File.Exists(entry.filePath)) return;

        if (progressBar != null)
        {
            progressBar.gameObject.SetActive(true);
            progressBar.value = 0f;
        }

        string uploadDir = Path.Combine(Application.temporaryCachePath, "WorkshopUpload");
        if (Directory.Exists(uploadDir)) Directory.Delete(uploadDir, true);
        Directory.CreateDirectory(uploadDir);

        string contentDir = Path.Combine(uploadDir, "Content");
        Directory.CreateDirectory(contentDir);

        string copiedModelPath = Path.Combine(contentDir, Path.GetFileName(entry.filePath));
        File.Copy(entry.filePath, copiedModelPath, true);
        contentDir = contentDir.Replace("\\", "/");

        string copiedThumbnailPath = null;
        if (File.Exists(entry.thumbnailPath))
        {
            copiedThumbnailPath = Path.Combine(contentDir, Path.GetFileName(entry.thumbnailPath));
            File.Copy(entry.thumbnailPath, copiedThumbnailPath, true);
            copiedThumbnailPath = copiedThumbnailPath.Replace("\\", "/");
        }

        try
        {
            string metaJson = JsonConvert.SerializeObject(new
            {
                entry.displayName,
                entry.author,
                entry.version,
                entry.fileType,
                entry.polygonCount,
                isNSFW = entry.isNSFW
            }, Formatting.Indented);

            File.WriteAllText(Path.Combine(contentDir, "metadata.json"), metaJson);
        }
        catch { }

        if (entry.steamFileId != 0)
        {
            var updateHandle = SteamUGC.StartItemUpdate(appId, new PublishedFileId_t(entry.steamFileId));
            ApplyUpdateSettingsAvatar(entry, contentDir, copiedThumbnailPath, updateHandle);

            SteamAPICall_t submitCall = SteamUGC.SubmitItemUpdate(updateHandle, "Updated avatar via Avatar Library");
            CallResult<SubmitItemUpdateResult_t> submitCallResult = CallResult<SubmitItemUpdateResult_t>.Create();

            if (progressBar != null && Instance != null)
                activeProgressRoutine = Instance.StartCoroutine(Instance.ProgressRoutine(progressBar));

            submitCallResult.Set(submitCall, (submitResult, submitFailure) =>
            {
                FinalizeUpload(submitResult, progressBar);
                if (submitResult.m_eResult == EResult.k_EResultOK)
                    OpenWorkshopPage(entry.steamFileId);
            });
        }
        else
        {
            SteamAPICall_t createCall = SteamUGC.CreateItem(appId, EWorkshopFileType.k_EWorkshopFileTypeCommunity);
            CallResult<CreateItemResult_t> createItemCallResult = CallResult<CreateItemResult_t>.Create();

            createItemCallResult.Set(createCall, (result, bIOFailure) =>
            {
                if (bIOFailure || result.m_eResult != EResult.k_EResultOK)
                {
                    if (progressBar != null) progressBar.gameObject.SetActive(false);
                    return;
                }

                var newFileId = result.m_nPublishedFileId.m_PublishedFileId;
                entry.steamFileId = newFileId;

                var updateHandle = SteamUGC.StartItemUpdate(appId, result.m_nPublishedFileId);
                ApplyUpdateSettingsAvatar(entry, contentDir, copiedThumbnailPath, updateHandle);

                SteamAPICall_t submitCall = SteamUGC.SubmitItemUpdate(updateHandle, "Initial upload from Avatar Library");
                CallResult<SubmitItemUpdateResult_t> submitCallResult = CallResult<SubmitItemUpdateResult_t>.Create();

                if (progressBar != null && Instance != null)
                    activeProgressRoutine = Instance.StartCoroutine(Instance.ProgressRoutine(progressBar));

                submitCallResult.Set(submitCall, (submitResult, submitFailure) =>
                {
                    FinalizeUpload(submitResult, progressBar);
                    if (submitResult.m_eResult == EResult.k_EResultOK)
                    {
                        SaveSteamFileIdAvatar(entry);
                        OpenWorkshopPage(newFileId);
                    }
                });
            });
        }
    }

    public void BeginUploadMod(string filePath, Slider progressBar = null)
    {
        UploadMod(filePath, null, null, false, null, 0UL, progressBar);
    }

    public void UploadMod(string filePath, string displayName, string author, bool isNSFW, string thumbnailPath, ulong existingWorkshopId, Slider progressBar = null)
    {
        if (!SteamManager.Initialized) return;
        if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath)) return;

        if (progressBar != null)
        {
            progressBar.gameObject.SetActive(true);
            progressBar.value = 0f;
        }

        string uploadDir = Path.Combine(Application.temporaryCachePath, "WorkshopUpload_Mod");
        if (Directory.Exists(uploadDir)) Directory.Delete(uploadDir, true);
        Directory.CreateDirectory(uploadDir);

        string contentDir = Path.Combine(uploadDir, "Content");
        Directory.CreateDirectory(contentDir);

        string copiedPath = Path.Combine(contentDir, Path.GetFileName(filePath));
        File.Copy(filePath, copiedPath, true);
        contentDir = contentDir.Replace("\\", "/");

        string copiedThumbnailPath = null;
        if (!string.IsNullOrEmpty(thumbnailPath) && File.Exists(thumbnailPath))
        {
            copiedThumbnailPath = Path.Combine(contentDir, Path.GetFileName(thumbnailPath));
            File.Copy(thumbnailPath, copiedThumbnailPath, true);
            copiedThumbnailPath = copiedThumbnailPath.Replace("\\", "/");
        }

        bool isDance = false;
        string detectedAuthor = author;
        var ext = Path.GetExtension(filePath);
        if (ext.Equals(".me", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                using (var fs = File.OpenRead(filePath))
                using (var zip = new ZipArchive(fs, ZipArchiveMode.Read))
                {
                    isDance = zip.Entries.Any(e => string.Equals(e.FullName, "dance_meta.json", StringComparison.OrdinalIgnoreCase)) ||
                              zip.Entries.Any(e => e.FullName.EndsWith(".bundle", StringComparison.OrdinalIgnoreCase));
                    var metaEntry = zip.Entries.FirstOrDefault(e => string.Equals(e.FullName, "dance_meta.json", StringComparison.OrdinalIgnoreCase));
                    if (metaEntry != null)
                    {
                        using var ms = new MemoryStream();
                        using var zs = metaEntry.Open();
                        zs.CopyTo(ms);
                        var json = System.Text.Encoding.UTF8.GetString(ms.ToArray());
                        try
                        {
                            var meta = JsonConvert.DeserializeObject<DanceMeta>(json);
                            if (string.IsNullOrWhiteSpace(detectedAuthor))
                                detectedAuthor = !string.IsNullOrWhiteSpace(meta.songAuthor) ? meta.songAuthor : meta.mmdAuthor;
                        }
                        catch { }
                    }
                }
            }
            catch { }
        }
        else if (ext.Equals(".unity3d", StringComparison.OrdinalIgnoreCase))
        {
            isDance = true;
        }

        string title = string.IsNullOrWhiteSpace(displayName) ? Path.GetFileNameWithoutExtension(filePath) : displayName;
        string desc = "Uploaded via MateEngine Mod Manager";
        if (!string.IsNullOrWhiteSpace(detectedAuthor)) desc += "\nAuthor: " + detectedAuthor;

        var tags = new List<string> { "Mods" };
        if (isDance) tags.Add("Dances");
        if (isNSFW) tags.Add("NSFW");

        try
        {
            var metaObj = new
            {
                title,
                author = detectedAuthor,
                isDance,
                isNSFW,
                originalFile = Path.GetFileName(filePath)
            };
            File.WriteAllText(Path.Combine(contentDir, "metadata.json"), JsonConvert.SerializeObject(metaObj, Formatting.Indented));
        }
        catch { }

        if (existingWorkshopId != 0UL)
        {
            var updateHandle = SteamUGC.StartItemUpdate(appId, new PublishedFileId_t(existingWorkshopId));
            ApplyUpdateSettingsMod(title, desc, tags, contentDir, copiedThumbnailPath, updateHandle);

            SteamAPICall_t submitCall = SteamUGC.SubmitItemUpdate(updateHandle, "Updated mod via Mod Manager");
            CallResult<SubmitItemUpdateResult_t> submitCallResult = CallResult<SubmitItemUpdateResult_t>.Create();

            if (progressBar != null && Instance != null)
                activeProgressRoutine = Instance.StartCoroutine(Instance.ProgressRoutine(progressBar));

            submitCallResult.Set(submitCall, (submitResult, submitFailure) =>
            {
                FinalizeUpload(submitResult, progressBar);
                if (submitResult.m_eResult == EResult.k_EResultOK)
                    OpenWorkshopPage(existingWorkshopId);
            });
        }
        else
        {
            SteamAPICall_t createCall = SteamUGC.CreateItem(appId, EWorkshopFileType.k_EWorkshopFileTypeCommunity);
            CallResult<CreateItemResult_t> createItemCallResult = CallResult<CreateItemResult_t>.Create();

            createItemCallResult.Set(createCall, (result, bIOFailure) =>
            {
                if (bIOFailure || result.m_eResult != EResult.k_EResultOK)
                {
                    if (progressBar != null) progressBar.gameObject.SetActive(false);
                    return;
                }

                var newFileId = result.m_nPublishedFileId.m_PublishedFileId;

                var updateHandle = SteamUGC.StartItemUpdate(appId, result.m_nPublishedFileId);
                ApplyUpdateSettingsMod(title, desc, tags, contentDir, copiedThumbnailPath, updateHandle);

                SteamAPICall_t submitCall = SteamUGC.SubmitItemUpdate(updateHandle, "Initial mod upload");
                CallResult<SubmitItemUpdateResult_t> submitCallResult = CallResult<SubmitItemUpdateResult_t>.Create();

                if (progressBar != null && Instance != null)
                    activeProgressRoutine = Instance.StartCoroutine(Instance.ProgressRoutine(progressBar));

                submitCallResult.Set(submitCall, (submitResult, submitFailure) =>
                {
                    FinalizeUpload(submitResult, progressBar);
                    if (submitResult.m_eResult == EResult.k_EResultOK)
                        OpenWorkshopPage(newFileId);
                });
            });
        }
    }

    void ApplyUpdateSettingsAvatar(AvatarLibraryMenu.AvatarEntry entry, string contentDir, string thumbnailPath, UGCUpdateHandle_t handle)
    {
        SteamUGC.SetItemTitle(handle, entry.displayName ?? "Untitled Avatar");
        SteamUGC.SetItemDescription(handle, $"Uploaded via MateEngine\nAuthor: {entry.author}\nFormat: {entry.fileType}\nPolygons: {entry.polygonCount}");
        SteamUGC.SetItemVisibility(handle, ERemoteStoragePublishedFileVisibility.k_ERemoteStoragePublishedFileVisibilityPublic);

        var tags = new List<string> { "Avatar" };
        if (entry.fileType.Contains("1.X")) tags.Add("VRM1"); else tags.Add("VRM0");
        if (entry.isNSFW) tags.Add("NSFW");
        SteamUGC.SetItemTags(handle, tags);
        SteamUGC.SetItemContent(handle, contentDir);
        if (!string.IsNullOrEmpty(thumbnailPath)) SteamUGC.SetItemPreview(handle, thumbnailPath);
    }

    void ApplyUpdateSettingsMod(string title, string description, List<string> tags, string contentDir, string thumbnailPath, UGCUpdateHandle_t handle)
    {
        SteamUGC.SetItemTitle(handle, string.IsNullOrWhiteSpace(title) ? "Untitled Mod" : title);
        SteamUGC.SetItemDescription(handle, string.IsNullOrWhiteSpace(description) ? "MateEngine Mod" : description);
        SteamUGC.SetItemVisibility(handle, ERemoteStoragePublishedFileVisibility.k_ERemoteStoragePublishedFileVisibilityPublic);
        SteamUGC.SetItemTags(handle, tags);
        SteamUGC.SetItemContent(handle, contentDir);
        if (!string.IsNullOrEmpty(thumbnailPath)) SteamUGC.SetItemPreview(handle, thumbnailPath);
    }

    void FinalizeUpload(SubmitItemUpdateResult_t result, Slider slider)
    {
        if (activeProgressRoutine != null && Instance != null)
            Instance.StopCoroutine(activeProgressRoutine);

        if (slider != null)
        {
            slider.value = 100f;
            slider.gameObject.SetActive(false);
        }
    }

    void SaveSteamFileIdAvatar(AvatarLibraryMenu.AvatarEntry updatedEntry)
    {
        string path = Path.Combine(Application.persistentDataPath, "avatars.json");
        if (!File.Exists(path)) return;

        try
        {
            var list = JsonConvert.DeserializeObject<List<AvatarLibraryMenu.AvatarEntry>>(File.ReadAllText(path));
            foreach (var item in list)
            {
                if (item.filePath == updatedEntry.filePath)
                {
                    item.steamFileId = updatedEntry.steamFileId;

                    string workshopDir = Path.GetFullPath(Path.Combine(Application.persistentDataPath, "Steam Workshop"));
                    string fileFull = string.IsNullOrEmpty(item.filePath) ? "" : Path.GetFullPath(item.filePath);
                    bool isInsideWorkshop = !string.IsNullOrEmpty(fileFull) &&
                                            fileFull.StartsWith(workshopDir, StringComparison.OrdinalIgnoreCase);
                    item.isSteamWorkshop = isInsideWorkshop;

                    if (!isInsideWorkshop) item.isOwner = true;
                    break;
                }
            }
            File.WriteAllText(path, JsonConvert.SerializeObject(list, Formatting.Indented));
        }
        catch { }
    }

    void OpenWorkshopPage(ulong fileId)
    {
        Application.OpenURL($"https://steamcommunity.com/sharedfiles/filedetails/?id={fileId}");
    }

    IEnumerator ProgressRoutine(Slider slider)
    {
        float duration = 10f;
        float elapsed = 0f;
        slider.minValue = 0f;
        slider.maxValue = 100f;
        slider.value = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            slider.value = Mathf.Clamp01(elapsed / duration) * 100f;
            yield return null;
        }

        slider.value = 100f;
    }

    public void UnsubscribeAndDelete(PublishedFileId_t fileId)
    {
        if (!SteamManager.Initialized) return;
        SteamUGC.UnsubscribeItem(fileId);
    }

    class DanceMeta
    {
        public string songName;
        public string songAuthor;
        public string mmdAuthor;
        public float songLength;
        public string placeholderClipName;
    }
}
