using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.EventSystems;
using System.Collections;
using System.IO;
using SFB;
using Newtonsoft.Json;
using System.Collections.Generic;
using System.Linq;
using UnityEngine.Localization;
using UnityEngine.Localization.Settings;

public class UploadButtonHoldHandler : MonoBehaviour, IPointerDownHandler, IPointerUpHandler
{
    [HideInInspector] public AvatarLibraryMenu.AvatarEntry entry;

    [Header("UI References")]
    public Slider progressSlider;
    public TMP_Text labelText;
    public TMP_Text errorText;

    [Header("Audio")]
    public AudioSource audioSource;
    public AudioClip tickSound;
    public AudioClip completeSound;

    private Coroutine holdRoutine;
    private bool isHolding = false;

    private void Start()
    {
        UpdateButtonLabel();
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        if (IsThumbnailMissing() || IsThumbnailTooBig())
        {
            string[] paths = StandaloneFileBrowser.OpenFilePanel("Select PNG Thumbnail (Max 700KB)", "", new[] {
                new ExtensionFilter("Image", "png")
            }, false);

            if (paths.Length == 0 || !File.Exists(paths[0]))
                return;

            FileInfo fi = new FileInfo(paths[0]);
            if (fi.Length > 700 * 1024)
            {
                if (errorText != null)
                {
                    var localizedString = new LocalizedString("Languages (UI)", "PNG_TOO_BIG");
                    localizedString.StringChanged += (value) => errorText.text = value;
                }
                return;
            }

            string thumbnailsFolder = Path.Combine(Application.persistentDataPath, "Thumbnails");
            if (!Directory.Exists(thumbnailsFolder))
                Directory.CreateDirectory(thumbnailsFolder);

            string safeName = Path.GetFileNameWithoutExtension(entry.filePath) + "_thumb.png";
            string destinationPath = Path.Combine(thumbnailsFolder, safeName);
            File.Copy(paths[0], destinationPath, true);
            entry.thumbnailPath = destinationPath;

            string avatarsJsonPath = Path.Combine(Application.persistentDataPath, "avatars.json");
            if (File.Exists(avatarsJsonPath))
            {
                var json = File.ReadAllText(avatarsJsonPath);
                var list = JsonConvert.DeserializeObject<List<AvatarLibraryMenu.AvatarEntry>>(json);
                var match = list.FirstOrDefault(e => e.filePath == entry.filePath);
                if (match != null)
                {
                    match.thumbnailPath = destinationPath;
                    File.WriteAllText(avatarsJsonPath, JsonConvert.SerializeObject(list, Formatting.Indented));
                }
            }

            var menu = FindFirstObjectByType<AvatarLibraryMenu>();
            if (menu != null)
                menu.ReloadAvatars();

            if (errorText != null) errorText.text = "";
            UpdateButtonLabel();
            return;
        }

        if (holdRoutine == null)
        {
            isHolding = true;
            holdRoutine = StartCoroutine(HoldToUpload());
        }
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        isHolding = false;
    }

    private bool IsThumbnailMissing()
    {
        return string.IsNullOrEmpty(entry.thumbnailPath) || !File.Exists(entry.thumbnailPath);
    }

    private bool IsThumbnailTooBig()
    {
        if (string.IsNullOrEmpty(entry.thumbnailPath) || !File.Exists(entry.thumbnailPath))
            return false;

        FileInfo fi = new FileInfo(entry.thumbnailPath);
        return fi.Length > 700 * 1024;
    }

    private void UpdateButtonLabel()
    {
        if (labelText == null) return;

        string key = IsThumbnailMissing() ? "PNG_MISSING" : (IsThumbnailTooBig() ? "PNG_MISSING" : "UPLOAD");
        var localized = new LocalizedString("Languages (UI)", key);
        localized.StringChanged += (val) => labelText.text = val;
    }

    private IEnumerator HoldToUpload()
    {
        float duration = 5f;
        float timeHeld = 0f;
        int lastSecond = -1;
        float pitch = 1f;
        bool completed = false;

        if (labelText != null) labelText.text = "5";
        GetComponent<Button>().interactable = false;

        while (isHolding && timeHeld < duration)
        {
            timeHeld += Time.deltaTime;
            int currentSecond = Mathf.CeilToInt(duration - timeHeld);

            if (currentSecond != lastSecond)
            {
                lastSecond = currentSecond;
                if (labelText != null) labelText.text = currentSecond.ToString();

                if (audioSource != null && tickSound != null)
                {
                    audioSource.pitch = pitch;
                    audioSource.PlayOneShot(tickSound);
                    pitch += 0.1f;
                }
            }

            yield return null;
        }

        if (timeHeld >= duration && isHolding)
        {
            completed = true;
            if (labelText != null) labelText.text = "0";

            if (audioSource != null && completeSound != null)
            {
                audioSource.pitch = 1f;
                audioSource.PlayOneShot(completeSound);
            }

            yield return new WaitForSeconds(0.5f);

            if (SteamWorkshopHandler.Instance != null)
                SteamWorkshopHandler.Instance.UploadToWorkshop(entry, progressSlider);

            if (labelText != null) labelText.text = "Uploaded";
            yield return new WaitForSeconds(1.5f);
        }

        if (!completed)
            UpdateButtonLabel();

        GetComponent<Button>().interactable = true;
        holdRoutine = null;
    }
}
