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

    void Awake()
    {
        if (button == null) button = GetComponent<Button>();
        if (progressBar == null)
        {
            var trs = GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < trs.Length; i++)
                if (trs[i].name == "Progress") { progressBar = trs[i].GetComponent<Slider>(); break; }
        }
        if (button != null) button.onClick.AddListener(OnClick);
    }

    void OnClick()
    {
        var h = SteamWorkshopHandler.Instance;
        if (h == null || string.IsNullOrEmpty(filePath)) return;
        h.UploadMod(filePath, displayName, author, isNSFW, thumbnailPath, 0UL, progressBar);
    }
}
