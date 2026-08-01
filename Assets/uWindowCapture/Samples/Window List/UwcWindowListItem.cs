using UnityEngine;
using UnityEngine.UI;

namespace uWindowCapture
{

[RequireComponent(typeof(Image))]
public class UwcWindowListItem : MonoBehaviour 
{
    Image image_;
    [SerializeField] Color selected = default;
    [SerializeField] Color notSelected = default;

#pragma warning disable IDE1006 // vendored sample: public props keep upstream lowercase naming
    public UwcWindow window { get; set; }
    public UwcWindowList list { get; set; }
    public UwcWindowTexture windowTexture { get; set; }
#pragma warning restore IDE1006

#pragma warning disable IDE0044 // [SerializeField] fields must stay mutable for Unity serialization
    [SerializeField] RawImage icon = null;
    [SerializeField] Text title = null;
    [SerializeField] Text x = null;
    [SerializeField] Text y = null;
    [SerializeField] Text z = null;
    [SerializeField] Text width = null;
    [SerializeField] Text height = null;
    [SerializeField] Text status = null;
#pragma warning restore IDE0044

    void Awake()
    {
        image_ = GetComponent<Image>();
        image_.color = notSelected;
    }

    void Update()
    {
        if (window == null) return;

        if (!window.hasIconTexture && !window.isIconic) {
            icon.texture = window.texture;
        } else {
            icon.texture = window.iconTexture;
        }

        var windowTitle = window.title;
        title.text = string.IsNullOrEmpty(windowTitle) ? "-No Name-" : windowTitle;

        x.text = window.isMinimized ? "-" : window.x.ToString();
        y.text = window.isMinimized ? "-" : window.y.ToString();
        z.text = window.zOrder.ToString();

        width.text = window.width.ToString();
        height.text = window.height.ToString();

        status.text = 
            window.isIconic ? "Iconic" :
            window.isZoomed ? "Zoomed" :
            "-";
    }

    public void OnClick()
    {
        if (windowTexture == null) {
            AddWindow();
        } else {
            RemoveWindow();
        }
    }

    void AddWindow()
    {
        var manager = list.windowTextureManager;
        windowTexture = manager.AddWindowTexture(window);
        image_.color = selected;
    }

    public void RemoveWindow()
    {
        var manager = list.windowTextureManager;
        manager.RemoveWindowTexture(window);
        windowTexture = null;
        image_.color = notSelected;
    }
}

}