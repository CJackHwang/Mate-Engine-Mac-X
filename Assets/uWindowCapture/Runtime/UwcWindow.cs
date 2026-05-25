using UnityEngine;
using UnityEngine.Events;

namespace uWindowCapture
{

public class UwcWindow
{
    public UwcWindow(int id)
    {
        this.id = id;
        isAlive = true;

        onCaptured.AddListener(OnCaptured);
        onSizeChanged.AddListener(OnSizeChanged);
        onIconCaptured.AddListener(OnIconCaptured);

#if UNITY_STANDALONE_WIN
        CreateIconTexture();

        parentWindow = UwcManager.FindParent(id);
        if (parentWindow != null) {
            parentWindow.onChildAdded.Invoke(this);
        }
#endif
    }

    public int id 
    { 
        get; 
        private set; 
    }

    public UwcWindow parentWindow
    {
        get;
        private set;
    }

    public System.IntPtr handle
    {
        get {
#if UNITY_STANDALONE_WIN
            return Lib.GetWindowHandle(id);
#else
            return System.IntPtr.Zero;
#endif
        }
    }

    public System.IntPtr ownerHandle
    {
        get {
#if UNITY_STANDALONE_WIN
            return Lib.GetWindowOwnerHandle(id);
#else
            return System.IntPtr.Zero;
#endif
        }
    }

    public System.IntPtr parentHandle
    {
        get {
#if UNITY_STANDALONE_WIN
            return Lib.GetWindowParentHandle(id);
#else
            return System.IntPtr.Zero;
#endif
        }
    }

    public System.IntPtr instance
    {
        get {
#if UNITY_STANDALONE_WIN
            return Lib.GetWindowInstance(id);
#else
            return System.IntPtr.Zero;
#endif
        }
    }

    public int processId
    {
        get {
#if UNITY_STANDALONE_WIN
            return Lib.GetWindowProcessId(id);
#else
            return 0;
#endif
        }
    }

    public int threadId
    {
        get {
#if UNITY_STANDALONE_WIN
            return Lib.GetWindowThreadId(id);
#else
            return 0;
#endif
        }
    }

    public bool isValid
    {
        get {
#if UNITY_STANDALONE_WIN
            return Lib.CheckWindowExistence(id);
#else
            return false;
#endif
        }
    }

    public bool isAlive
    {
        get;
        set;
    }

    public bool isRoot
    {
        get { return parentWindow == null; }
    }

    public bool isChild
    {
        get { return !isRoot; }
    }

    public bool isVisible
    {
        get {
#if UNITY_STANDALONE_WIN
            return Lib.IsWindowVisible(id);
#else
            return false;
#endif
        }
    }

    public bool isAltTabWindow
    {
        get {
#if UNITY_STANDALONE_WIN
            return Lib.IsAltTabWindow(id);
#else
            return false;
#endif
        }
    }

    public bool isDesktop
    {
        get {
#if UNITY_STANDALONE_WIN
            return Lib.IsDesktop(id);
#else
            return false;
#endif
        }
    }

    public bool isEnabled
    {
        get {
#if UNITY_STANDALONE_WIN
            return Lib.IsWindowEnabled(id);
#else
            return false;
#endif
        }
    }

    public bool isUnicode
    {
        get {
#if UNITY_STANDALONE_WIN
            return Lib.IsWindowUnicode(id);
#else
            return false;
#endif
        }
    }

    public bool isZoomed
    {
        get {
#if UNITY_STANDALONE_WIN
            return Lib.IsWindowZoomed(id);
#else
            return false;
#endif
        }
    }

    public bool isMaximized
    {
        get { return isZoomed; }
    }

    public bool isIconic
    {
        get {
#if UNITY_STANDALONE_WIN
            return Lib.IsWindowIconic(id);
#else
            return false;
#endif
        }
    }

    public bool isMinimized
    {
        get { return isIconic; }
    }

    public bool isHungup
    {
        get {
#if UNITY_STANDALONE_WIN
            return Lib.IsWindowHungUp(id);
#else
            return false;
#endif
        }
    }

    public bool isTouchable
    {
        get {
#if UNITY_STANDALONE_WIN
            return Lib.IsWindowTouchable(id);
#else
            return false;
#endif
        }
    }

    public bool isApplicationFrameWindow
    {
        get {
#if UNITY_STANDALONE_WIN
            return Lib.IsApplicationFrameWindow(id);
#else
            return false;
#endif
        }
    }

    public bool isUWP
    {
        get {
#if UNITY_STANDALONE_WIN
            return Lib.IsWindowUWP(id);
#else
            return false;
#endif
        }
    }

    public bool isBackground
    {
        get {
#if UNITY_STANDALONE_WIN
            return Lib.IsWindowBackground(id);
#else
            return false;
#endif
        }
    }

    public string title
    {
        get {
#if UNITY_STANDALONE_WIN
            return Lib.GetWindowTitle(id);
#else
            return null;
#endif
        }
    }

    public string className
    {
        get {
#if UNITY_STANDALONE_WIN
            return Lib.GetWindowClassName(id);
#else
            return null;
#endif
        }
    }

    public int rawX
    {
        get {
#if UNITY_STANDALONE_WIN
            return Lib.GetWindowX(id);
#else
            return 0;
#endif
        }
    }

    public int rawY
    {
        get {
#if UNITY_STANDALONE_WIN
            return Lib.GetWindowY(id);
#else
            return 0;
#endif
        }
    }

    public int rawWidth
    {
        get {
#if UNITY_STANDALONE_WIN
            return Lib.GetWindowWidth(id);
#else
            return 0;
#endif
        }
    }

    public int rawHeight
    {
        get {
#if UNITY_STANDALONE_WIN
            return Lib.GetWindowHeight(id);
#else
            return 0;
#endif
        }
    }

    public int x
    {
        get {
#if UNITY_STANDALONE_WIN
            return rawX + Lib.GetWindowTextureOffsetX(id);
#else
            return 0;
#endif
        }
    }

    public int y
    {
        get {
#if UNITY_STANDALONE_WIN
            return rawY + Lib.GetWindowTextureOffsetY(id);
#else
            return 0;
#endif
        }
    }

    public int width
    {
        get {
#if UNITY_STANDALONE_WIN
            return Lib.GetWindowTextureWidth(id);
#else
            return 0;
#endif
        }
    }

    public int height
    {
        get {
#if UNITY_STANDALONE_WIN
            return Lib.GetWindowTextureHeight(id);
#else
            return 0;
#endif
        }
    }

    public int zOrder
    {
        get {
#if UNITY_STANDALONE_WIN
            return Lib.GetWindowZOrder(id);
#else
            return 0;
#endif
        }
    }

    public System.IntPtr buffer
    {
        get {
#if UNITY_STANDALONE_WIN
            return Lib.GetWindowBuffer(id);
#else
            return System.IntPtr.Zero;
#endif
        }
    }

    public int textureOffsetX
    {
        get {
#if UNITY_STANDALONE_WIN
            return Lib.GetWindowTextureOffsetX(id);
#else
            return 0;
#endif
        }
    }

    public int textureOffsetY
    {
        get {
#if UNITY_STANDALONE_WIN
            return Lib.GetWindowTextureOffsetY(id);
#else
            return 0;
#endif
        }
    }

    public int iconWidth
    {
        get {
#if UNITY_STANDALONE_WIN
            return Lib.GetWindowIconWidth(id);
#else
            return 0;
#endif
        }
    }

    public int iconHeight
    {
        get {
#if UNITY_STANDALONE_WIN
            return Lib.GetWindowIconHeight(id);
#else
            return 0;
#endif
        }
    }

    private Texture2D backTexture_;
    private bool willTextureSizeChange_ = false;
    public Texture2D texture
    {
        get;
        private set;
    }

    private Texture2D iconTexture_;
    private Texture2D errorIconTexture_;
    private bool hasIconTextureCaptured_ = false;
    public bool hasIconTexture
    {
        get { return hasIconTextureCaptured_; }
    }

    public Texture2D iconTexture
    {
        get { return hasIconTextureCaptured_ ? iconTexture_ : errorIconTexture_; }
    }

    public CaptureMode captureMode
    {
        get {
#if UNITY_STANDALONE_WIN
            return Lib.GetWindowCaptureMode(id);
#else
            return CaptureMode.Auto;
#endif
        }
        set {
#if UNITY_STANDALONE_WIN
            Lib.SetWindowCaptureMode(id, value);
#endif
        }
    }

    public bool cursorDraw
    {
        get {
#if UNITY_STANDALONE_WIN
            return Lib.GetWindowCursorDraw(id);
#else
            return false;
#endif
        }
        set {
#if UNITY_STANDALONE_WIN
            Lib.SetWindowCursorDraw(id, value);
#endif
        }
    }

    private UnityEvent onCaptured_ = new UnityEvent();
    public UnityEvent onCaptured 
    { 
        get { return onCaptured_; } 
    }

    private bool isFirstSizeChangedEvent_ = true;
    private UnityEvent onSizeChanged_ = new UnityEvent();
    public UnityEvent onSizeChanged
    {
        get { return onSizeChanged_; } 
    }

    private UnityEvent onIconCaptured_ = new UnityEvent();
    public UnityEvent onIconCaptured 
    { 
        get { return onIconCaptured_; } 
    }

    public class ChildAddedEvent : UnityEvent<UwcWindow> {}
    private ChildAddedEvent onChildAdded_ = new ChildAddedEvent();
    public ChildAddedEvent onChildAdded
    { 
        get { return onChildAdded_; } 
    }

    public class ChildRemovedEvent : UnityEvent<UwcWindow> {}
    private ChildRemovedEvent onChildRemoved_ = new ChildRemovedEvent();
    public ChildRemovedEvent onChildRemoved
    { 
        get { return onChildRemoved_; } 
    }

    public void RequestUpdateTitle()
    {
#if UNITY_STANDALONE_WIN
        Lib.RequestUpdateWindowTitle(id);
#endif
    }

    public void RequestCaptureIcon()
    {
#if UNITY_STANDALONE_WIN
        Lib.RequestCaptureIcon(id);
#endif
    }

    public void RequestCapture(CapturePriority priority = CapturePriority.High)
    {
#if UNITY_STANDALONE_WIN
        if (!texture) {
            CreateWindowTexture();
        }
        Lib.RequestCaptureWindow(id, priority);
#endif
    }

    void OnSizeChanged()
    {
        if (isFirstSizeChangedEvent_) {
            isFirstSizeChangedEvent_ = false;
            return;
        }

        CreateWindowTexture();
    }

    void OnCaptured()
    {
        UpdateWindowTexture();
    }

    void OnIconCaptured()
    {
        hasIconTextureCaptured_ = true;
    }

    void CreateWindowTexture(bool force = false)
    {
#if UNITY_STANDALONE_WIN
        var w = width;
        var h = height;
        if (w <= 0 || h <= 0) return;

        if (force || !texture || texture.width != w || texture.height != h) {
            if (backTexture_) {
                Object.DestroyImmediate(backTexture_);
            }
            try {
                backTexture_ = new Texture2D(w, h, TextureFormat.BGRA32, false);
                Lib.SetWindowTexturePtr(id, backTexture_.GetNativeTexturePtr());
                willTextureSizeChange_ = true;
            } catch (System.Exception e) {
                Debug.LogError(e.Message);
                Debug.LogErrorFormat("Width: {0}, Height: {1}", w, h);
            }
        }
#endif
    }

    void UpdateWindowTexture()
    {
        if (willTextureSizeChange_) {
            if (texture) {
                Object.DestroyImmediate(texture);
            }
            texture = backTexture_;
            backTexture_ = null;
            willTextureSizeChange_ = false;
        }
    }

    public void ResetWindowTexture()
    {
        CreateWindowTexture(true);
    }

    void CreateIconTexture()
    {
#if UNITY_STANDALONE_WIN
        var w = iconWidth;
        var h = iconHeight;
        if (w == 0 || h == 0) return;
        iconTexture_ = new Texture2D(w, h, TextureFormat.BGRA32, false);
        iconTexture_.filterMode = FilterMode.Bilinear;
        iconTexture_.wrapMode = TextureWrapMode.Clamp;
        Lib.SetWindowIconTexturePtr(id, iconTexture_.GetNativeTexturePtr());
        errorIconTexture_ = Resources.Load<Texture2D>("uWindowCapture/Textures/uWC_No_Image");
#endif
    }

    public Color32[] GetPixels(int x, int y, int width, int height)
    {
#if UNITY_STANDALONE_WIN
        return Lib.GetWindowPixels(id, x, y, width, height);
#else
        return null;
#endif
    }

    public bool GetPixels(Color32[] colors, int x, int y, int width, int height)
    {
#if UNITY_STANDALONE_WIN
        return Lib.GetWindowPixels(id, colors, x, y, width, height);
#else
        return false;
#endif
    }

    public Color32 GetPixel(int x, int y)
    {
#if UNITY_STANDALONE_WIN
        return Lib.GetWindowPixel(id, x, y);
#else
        return default(Color32);
#endif
    }
}

}