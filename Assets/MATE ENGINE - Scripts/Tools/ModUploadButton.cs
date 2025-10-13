using UnityEngine;
using UnityEngine.UI;

public class ModUploadButton : MonoBehaviour
{
    public Button button;
    public string filePath;
    public Slider progressBar;
    public string displayName;
    public string author;
    public bool isNSFW;
    public string thumbnailPath;

    bool inFlight;

    void Awake()
    {
        if (button == null) button = GetComponent<Button>();
    }

    public void UploadNow()
    {
        if (inFlight) return;
        if (SteamWorkshopHandler.Instance == null) return;
        if (string.IsNullOrEmpty(filePath)) return;

        inFlight = true;
        SteamWorkshopHandler.Instance.UploadMod(
            filePath, displayName, author, isNSFW, thumbnailPath, 0UL, progressBar
        );
        Invoke(nameof(ResetInFlight), 2f);
    }

    void ResetInFlight() { inFlight = false; }
}
