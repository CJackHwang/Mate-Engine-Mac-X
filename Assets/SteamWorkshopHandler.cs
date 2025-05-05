using System;
using System.IO;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Steamworks;
using Newtonsoft.Json;

public class SteamWorkshopHandler : MonoBehaviour
{
    public static SteamWorkshopHandler Instance { get; private set; }
    private static readonly AppId_t appId = new AppId_t(3625270);
    private Coroutine activeProgressRoutine = null;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(this.gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(this.gameObject);
    }

    public void UploadToWorkshop(AvatarLibraryMenu.AvatarEntry entry, Slider progressBar = null)
    {
        if (!SteamManager.Initialized) return;
        if (!File.Exists(entry.filePath))
        {
            Debug.LogError("[SteamWorkshopHandler] File not found: " + entry.filePath);
            return;
        }

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
                entry.polygonCount
            }, Formatting.Indented);

            File.WriteAllText(Path.Combine(contentDir, "metadata.json"), metaJson);
        }
        catch (Exception ex)
        {
            Debug.LogWarning("[SteamWorkshopHandler] Failed to write metadata.json: " + ex.Message);
        }

        if (entry.steamFileId != 0)
        {
            Debug.Log("[SteamWorkshopHandler] Updating existing item: " + entry.steamFileId);
            var updateHandle = SteamUGC.StartItemUpdate(appId, new PublishedFileId_t(entry.steamFileId));
            ApplyUpdateSettings(entry, contentDir, copiedThumbnailPath, updateHandle);

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
                    Debug.LogError("[SteamWorkshopHandler] Failed to create item: " + result.m_eResult);
                    if (progressBar != null) progressBar.gameObject.SetActive(false);
                    return;
                }

                var newFileId = result.m_nPublishedFileId.m_PublishedFileId;
                entry.steamFileId = newFileId;

                var updateHandle = SteamUGC.StartItemUpdate(appId, result.m_nPublishedFileId);
                ApplyUpdateSettings(entry, contentDir, copiedThumbnailPath, updateHandle);

                SteamAPICall_t submitCall = SteamUGC.SubmitItemUpdate(updateHandle, "Initial upload from Avatar Library");
                CallResult<SubmitItemUpdateResult_t> submitCallResult = CallResult<SubmitItemUpdateResult_t>.Create();

                if (progressBar != null && Instance != null)
                    activeProgressRoutine = Instance.StartCoroutine(Instance.ProgressRoutine(progressBar));

                submitCallResult.Set(submitCall, (submitResult, submitFailure) =>
                {
                    FinalizeUpload(submitResult, progressBar);
                    if (submitResult.m_eResult == EResult.k_EResultOK)
                    {
                        SaveSteamFileId(entry);
                        OpenWorkshopPage(newFileId);
                    }
                });
            });
        }
    }

    private void ApplyUpdateSettings(AvatarLibraryMenu.AvatarEntry entry, string contentDir, string thumbnailPath, UGCUpdateHandle_t handle)
    {
        SteamUGC.SetItemTitle(handle, entry.displayName ?? "Untitled Avatar");
        SteamUGC.SetItemDescription(handle, $"Uploaded via MateEngine\nAuthor: {entry.author}\nFormat: {entry.fileType}\nPolygons: {entry.polygonCount}");
        SteamUGC.SetItemVisibility(handle, ERemoteStoragePublishedFileVisibility.k_ERemoteStoragePublishedFileVisibilityPublic);

        var tags = new List<string> { "Avatar" };
        if (entry.fileType.Contains("1.X")) tags.Add("VRM1"); else tags.Add("VRM0");
        SteamUGC.SetItemTags(handle, tags);

        SteamUGC.SetItemContent(handle, contentDir);
        if (!string.IsNullOrEmpty(thumbnailPath)) SteamUGC.SetItemPreview(handle, thumbnailPath);
    }

    private void FinalizeUpload(SubmitItemUpdateResult_t result, Slider slider)
    {
        if (result.m_eResult != EResult.k_EResultOK)
            Debug.LogError("[SteamWorkshopHandler] Upload failed: " + result.m_eResult);
        else
            Debug.Log("[SteamWorkshopHandler] Upload successful!");

        if (activeProgressRoutine != null && Instance != null)
            Instance.StopCoroutine(activeProgressRoutine);

        if (slider != null)
        {
            slider.value = 100f;
            slider.gameObject.SetActive(false);
        }
    }

    private void SaveSteamFileId(AvatarLibraryMenu.AvatarEntry updatedEntry)
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
                    item.isSteamWorkshop = true;
                    break;
                }
            }
            File.WriteAllText(path, JsonConvert.SerializeObject(list, Formatting.Indented));
            Debug.Log("[SteamWorkshopHandler] Saved steamFileId to avatars.json");
        }
        catch (Exception e)
        {
            Debug.LogError("[SteamWorkshopHandler] Failed to save steamFileId: " + e.Message);
        }
    }

    private void OpenWorkshopPage(ulong fileId)
    {
        Application.OpenURL($"https://steamcommunity.com/sharedfiles/filedetails/?id={fileId}");
    }

    private IEnumerator ProgressRoutine(Slider slider)
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
        Debug.Log("[SteamWorkshopHandler] Unsubscribed: " + fileId);
    }
}
