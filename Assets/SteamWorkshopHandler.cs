using System;
using System.IO;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Steamworks;

public class SteamWorkshopHandler : MonoBehaviour
{
    public static SteamWorkshopHandler Instance { get; private set; }
    private static readonly AppId_t appId = new AppId_t(3625270);

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

        // Prepare clean upload folder
        string uploadDir = Path.Combine(Application.temporaryCachePath, "WorkshopUpload");
        if (Directory.Exists(uploadDir)) Directory.Delete(uploadDir, true);
        Directory.CreateDirectory(uploadDir);

        string contentDir = Path.Combine(uploadDir, "Content");
        Directory.CreateDirectory(contentDir);

        string copiedModelPath = Path.Combine(contentDir, Path.GetFileName(entry.filePath));
        File.Copy(entry.filePath, copiedModelPath, true);
        Debug.Log($"[SteamWorkshopHandler] Copied model to: {copiedModelPath}");

        // Fix path slashes
        contentDir = contentDir.Replace("\\", "/");

        string copiedThumbnailPath = null;
        if (File.Exists(entry.thumbnailPath))
        {
            copiedThumbnailPath = Path.Combine(uploadDir, Path.GetFileName(entry.thumbnailPath));
            File.Copy(entry.thumbnailPath, copiedThumbnailPath, true);
            copiedThumbnailPath = copiedThumbnailPath.Replace("\\", "/");
            Debug.Log($"[SteamWorkshopHandler] Copied thumbnail to: {copiedThumbnailPath}");
        }

        // Create workshop item
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

            var updateHandle = SteamUGC.StartItemUpdate(appId, result.m_nPublishedFileId);
            SteamUGC.SetItemTitle(updateHandle, entry.displayName ?? "Untitled Avatar");
            SteamUGC.SetItemDescription(updateHandle, $"Uploaded via MateEngine\nAuthor: {entry.author}\nFormat: {entry.fileType}\nPolygons: {entry.polygonCount}");
            SteamUGC.SetItemVisibility(updateHandle, ERemoteStoragePublishedFileVisibility.k_ERemoteStoragePublishedFileVisibilityPublic);

            var tags = new List<string> { "Avatar" };
            if (entry.fileType.Contains("1.X")) tags.Add("VRM1");
            else tags.Add("VRM0");
            SteamUGC.SetItemTags(updateHandle, tags);

            SteamUGC.SetItemContent(updateHandle, contentDir);
            if (!string.IsNullOrEmpty(copiedThumbnailPath)) SteamUGC.SetItemPreview(updateHandle, copiedThumbnailPath);

            SteamAPICall_t submitCall = SteamUGC.SubmitItemUpdate(updateHandle, "Initial upload from Avatar Library");
            CallResult<SubmitItemUpdateResult_t> submitCallResult = CallResult<SubmitItemUpdateResult_t>.Create();

            submitCallResult.Set(submitCall, (submitResult, submitFailure) =>
            {
                if (submitFailure || submitResult.m_eResult != EResult.k_EResultOK)
                    Debug.LogError("[SteamWorkshopHandler] Upload failed: " + submitResult.m_eResult);
                else
                    Debug.Log("[SteamWorkshopHandler] Upload successful! File ID: " + result.m_nPublishedFileId);

                if (progressBar != null)
                    progressBar.gameObject.SetActive(false);
            });

            if (progressBar != null && Instance != null)
                Instance.StartCoroutine(Instance.ProgressRoutine(progressBar));
        });
    }

    private IEnumerator ProgressRoutine(Slider slider)
    {
        float value = 0f;
        while (value < 1f)
        {
            value += Time.deltaTime * 0.25f;
            slider.value = value;
            yield return null;
        }
        slider.value = 1f;
    }

    public void UnsubscribeAndDelete(PublishedFileId_t fileId)
    {
        if (!SteamManager.Initialized) return;
        SteamUGC.UnsubscribeItem(fileId);
        Debug.Log("[SteamWorkshopHandler] Unsubscribed: " + fileId);
    }
}
