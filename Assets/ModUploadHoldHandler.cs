using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.EventSystems;
using System.Collections;
using System.IO;
using Steamworks;

public class ModUploadHoldHandler : MonoBehaviour, IPointerDownHandler, IPointerUpHandler
{
    public Slider progressSlider;
    public TMP_Text labelText;
    public TMP_Text errorText;
    public AudioSource audioSource;
    public AudioClip completeSound;
    public AudioClip tickSound;
    public string fallbackUpload = "Upload";
    public string fallbackUpdate = "Update";
    public float holdSeconds = 3f;

    Button button;
    ModUploadButton modButton;
    Coroutine holdRoutine;
    bool holding;

    void Awake()
    {
        button = GetComponent<Button>();
        modButton = GetComponent<ModUploadButton>();
        if (modButton != null && modButton.progressBar == null && progressSlider != null) modButton.progressBar = progressSlider;
    }

    void OnEnable()
    {
        holding = false;
        CancelHold();
        SetInteractable(true);
        UpdateLabel();
        ClearError();
    }

    void OnDisable()
    {
        holding = false;
        CancelHold();
        SetInteractable(true);
        UpdateLabel();
    }

    public void OnPointerDown(PointerEventData e)
    {
        if (!CanUpload())
        {
            SetError("Not ready");
            return;
        }
        if (holdRoutine == null)
        {
            holding = true;
            holdRoutine = StartCoroutine(HoldAndUpload());
        }
    }

    public void OnPointerUp(PointerEventData e)
    {
        holding = false;
    }

    IEnumerator HoldAndUpload()
    {
        float t = 0f;
        int lastShown = -1;
        SetInteractable(false);
        while (holding && t < holdSeconds)
        {
            t += Time.deltaTime;
            int left = Mathf.CeilToInt(holdSeconds - t);
            if (left != lastShown)
            {
                lastShown = left;
                SetLabel(left > 0 ? left.ToString() : "0");
                if (audioSource != null && tickSound != null && left > 0) audioSource.PlayOneShot(tickSound);
            }
            yield return null;
        }

        if (holding)
        {
            if (audioSource != null && completeSound != null) audioSource.PlayOneShot(completeSound);
            yield return null;
            StartUpload();
        }

        UpdateLabel();
        SetInteractable(true);
        holdRoutine = null;
    }

    void StartUpload()
    {
        ClearError();
        if (SteamWorkshopHandler.Instance == null)
        {
            SetError("Steam not initialized");
            return;
        }
        if (modButton == null || string.IsNullOrEmpty(modButton.filePath))
        {
            SetError("Missing file");
            return;
        }
        SteamWorkshopHandler.Instance.UploadMod(
            modButton.filePath,
            modButton.displayName,
            modButton.author,
            modButton.isNSFW,
            modButton.thumbnailPath,
            ResolveWorkshopIdForPath(modButton.filePath),
            modButton.progressBar != null ? modButton.progressBar : progressSlider
        );
        SetLabel("Uploading");
    }

    void UpdateLabel()
    {
        if (labelText == null) return;
        bool isUpdate = ResolveWorkshopIdForPath(modButton != null ? modButton.filePath : null) != 0UL;
        labelText.text = isUpdate ? fallbackUpdate : fallbackUpload;
    }

    void SetLabel(string s)
    {
        if (labelText != null) labelText.text = s;
    }

    void SetError(string s)
    {
        if (errorText != null) errorText.text = s;
    }

    void ClearError()
    {
        if (errorText != null) errorText.text = "";
    }

    bool CanUpload()
    {
        if (button == null || modButton == null) return false;
        if (string.IsNullOrEmpty(modButton.filePath)) return false;
        if (!File.Exists(modButton.filePath)) return false;
        return true;
    }

    void SetInteractable(bool v)
    {
        if (button != null) button.interactable = v;
    }

    void CancelHold()
    {
        if (holdRoutine != null)
        {
            StopCoroutine(holdRoutine);
            holdRoutine = null;
        }
    }

    ulong ResolveWorkshopIdForPath(string localPath)
    {
        try
        {
            if (!SteamManager.Initialized) return 0UL;
            if (string.IsNullOrEmpty(localPath)) return 0UL;
            uint count = SteamUGC.GetNumSubscribedItems();
            if (count == 0) return 0UL;
            var ids = new PublishedFileId_t[count];
            SteamUGC.GetSubscribedItems(ids, count);
            string targetName = Path.GetFileName(localPath);
            for (int i = 0; i < ids.Length; i++)
            {
                if (!SteamUGC.GetItemInstallInfo(ids[i], out ulong _, out string installPath, 1024, out uint _)) continue;
                if (string.IsNullOrEmpty(installPath) || !Directory.Exists(installPath)) continue;
                var top = Directory.GetFiles(installPath, "*", SearchOption.TopDirectoryOnly);
                for (int f = 0; f < top.Length; f++)
                    if (string.Equals(Path.GetFileName(top[f]), targetName, System.StringComparison.OrdinalIgnoreCase))
                        return ids[i].m_PublishedFileId;
            }
        }
        catch { }
        return 0UL;
    }
}
