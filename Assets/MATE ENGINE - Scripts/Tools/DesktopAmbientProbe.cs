using UnityEngine;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;

public class DesktopAmbientProbe : MonoBehaviour
{
    public enum Band { Top, Bottom, Left, Right }

    [System.Serializable]
    public class BandTarget
    {
        public Band band = Band.Top;
        public string targetID = "";
    }

    // Instead of driving raw Light components directly (which would fight the
    // manual ColorController pipeline), the probe writes hue/saturation/intensity
    // into the ColorController targets so both use the same fade/enable path.
    public ColorController colorController;      // null → auto-found at runtime
    public List<BandTarget> bandTargets = new(); // empty → Top→ambi_1, Bottom→ambi_2, Left/Right→ambi_3

    public bool enabledAuto = true;
    public bool driveIntensity = true;
    [Range(1f, 60f)] public float captureHz = 10f;
    public int captureWidth = 160;
    public int captureHeight = 90;
    public int bandThicknessPx = 120;
    public int excludeMarginPx = 12;
    [Range(0f, 1f)] public float smoothing = 0.85f;
    public string saveKey = "auto_ambient";
    [Range(0f, 4f)] public float minGrayIntensity = 0.35f;
    [Range(0f, 4f)] public float maxColorIntensity = 0.7f;
    [Range(0.5f, 3f)] public float saturationGamma = 1.4f;
    [Range(0f, 1f), Tooltip("Lower bound of the desktop-brightness scale: how dim the ambient glow gets on a dark wallpaper (1 = ignore brightness). Lower = stronger dark/bright contrast.")]
    public float darkAmbientFloor = 0.35f;
    [Range(1f, 60f), Tooltip("If no desktop sample arrives within this many seconds (e.g. missing screen-recording permission), auto ambient switches itself off and falls back to the manual lights.")]
    public float noSampleGraceSeconds = 10f;

#if UNITY_STANDALONE_WIN
    const int SM_XVIRTUALSCREEN = 76;
    const int SM_YVIRTUALSCREEN = 77;
    const int SM_CXVIRTUALSCREEN = 78;
    const int SM_CYVIRTUALSCREEN = 79;
    const int SRCCOPY = 0x00CC0020;

    [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr hwnd);
    [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr hwnd, IntPtr hdc);
    [DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleDC(IntPtr hdc);
    [DllImport("gdi32.dll")] static extern bool DeleteDC(IntPtr hdc);
    [DllImport("gdi32.dll")] static extern IntPtr SelectObject(IntPtr hdc, IntPtr hgdiobj);
    [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr hObject);
    [DllImport("gdi32.dll")] static extern bool StretchBlt(IntPtr hdcDest, int xDest, int yDest, int wDest, int hDest, IntPtr hdcSrc, int xSrc, int ySrc, int wSrc, int hSrc, int rop);
    [DllImport("gdi32.dll")] static extern IntPtr CreateDIBSection(IntPtr hdc, ref BITMAPINFO pbmi, uint iUsage, out IntPtr ppvBits, IntPtr hSection, uint dwOffset);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);
    [DllImport("user32.dll")] static extern IntPtr GetActiveWindow();
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);
    [DllImport("user32.dll")] static extern int GetSystemMetrics(int nIndex);

    [StructLayout(LayoutKind.Sequential)]
    struct RECT { public int left; public int top; public int right; public int bottom; }

    [StructLayout(LayoutKind.Sequential)]
    struct BITMAPINFOHEADER
    {
        public uint biSize; public int biWidth; public int biHeight; public ushort biPlanes; public ushort biBitCount; public uint biCompression; public uint biSizeImage; public int biXPelsPerMeter; public int biYPelsPerMeter; public uint biClrUsed; public uint biClrImportant;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct BITMAPINFO { public BITMAPINFOHEADER bmiHeader; }

    IntPtr deskDC;
    IntPtr memDC;
    IntPtr dib;
    IntPtr dibBits;
    IntPtr oldObj;
    int virtX, virtY, virtW, virtH;
#endif

    byte[] pixelBytes;

    float nextTick;
    // Neutral white fallback keeps lights at a usable brightness before the
    // first capture succeeds (for example while screen recording is pending).
    static readonly Vector3 DefaultHsv = new Vector3(0f, 0f, 1f);
    Vector3 hsvTop = DefaultHsv;
    Vector3 hsvBot = DefaultHsv;
    Vector3 hsvLeft = DefaultHsv;
    Vector3 hsvRight = DefaultHsv;
    Vector3 hsvTopTarget = DefaultHsv;
    Vector3 hsvBotTarget = DefaultHsv;
    Vector3 hsvLeftTarget = DefaultHsv;
    Vector3 hsvRightTarget = DefaultHsv;
    bool inited;
    bool hasSample;
    float _noSampleGraceUntil;

    void Start()
    {
        TryLoadToggle();
        EnsureSelfConfig();
        _noSampleGraceUntil = Time.unscaledTime + noSampleGraceSeconds;
#if UNITY_STANDALONE_WIN || UNITY_STANDALONE_OSX
        if (enabledAuto) InitCapture();
#endif
        inited = true;
        UnityEngine.Debug.Log("[DesktopAmbientProbe] enabled=" + enabledAuto + ", bandTargets=" + (bandTargets != null ? bandTargets.Count : 0) + ", colorController=" + (colorController != null ? "ok" : "null"));
    }

    // Wire up at runtime when the scene leaves fields empty (scene YAML stays minimal).
    void EnsureSelfConfig()
    {
        if (colorController == null)
            colorController = FindAnyObjectByType<ColorController>();
        if (bandTargets == null) bandTargets = new List<BandTarget>();
        if (bandTargets.Count == 0)
        {
            bandTargets.Add(new BandTarget { band = Band.Top, targetID = "ambi_1" });
            bandTargets.Add(new BandTarget { band = Band.Bottom, targetID = "ambi_2" });
            bandTargets.Add(new BandTarget { band = Band.Left, targetID = "ambi_3" });
            bandTargets.Add(new BandTarget { band = Band.Right, targetID = "ambi_3" });
        }
    }

    void OnDestroy()
    {
#if UNITY_STANDALONE_WIN || UNITY_STANDALONE_OSX
        ReleaseCapture();
#endif
    }

    void TryLoadToggle()
    {
        var s = SaveLoadHandler.Instance;
        if (s != null && s.data != null && s.data.groupToggles != null)
        {
            if (s.data.groupToggles.TryGetValue(saveKey, out bool v)) enabledAuto = v;
        }
    }

    public void SetEnabled(bool v)
    {
        enabledAuto = v;
        var s = SaveLoadHandler.Instance;
        if (s != null && s.data != null)
        {
            s.data.groupToggles[saveKey] = v;
            s.SaveToDisk();
        }
        if (v) RetryCapturePermission();
    }

    public void RetryCapturePermission()
    {
        _noSampleGraceUntil = Time.unscaledTime + noSampleGraceSeconds;
#if UNITY_STANDALONE_WIN || UNITY_STANDALONE_OSX
        InitCapture();
#endif
        nextTick = Time.unscaledTime;
    }

    bool _isCapturing = false;

    void LateUpdate()
    {
        if (!inited) return;
        if (enabledAuto && !hasSample && Time.unscaledTime > _noSampleGraceUntil)
        {
            // Couldn't capture the desktop within the grace period — usually the
            // screen-recording permission is missing. Switch auto ambient off and
            // fall back to the regular manual lights so the lights aren't left in
            // a half-on state.
            SwitchToManualLights();
            return;
        }
        if (!enabledAuto) return;
        if (Time.unscaledTime >= nextTick)
        {
            nextTick = Time.unscaledTime + 1f / Mathf.Max(1f, captureHz);
#if UNITY_STANDALONE_WIN || UNITY_STANDALONE_OSX
            if (EnsureCaptureValid() && !_isCapturing) {
                if (TryGetVirtualScreen(out int vx, out int vy, out int vw, out int vh)) {
                    bool haveWnd = TryGetUnityWindowRects(out int wx0, out int wy0, out int wx1, out int wy1, vx, vy, vw, vh);
                    _isCapturing = true;
                    System.Threading.Tasks.Task.Run(() => {
                        try {
                            CaptureAndAnalyze(haveWnd, wx0, wy0, wx1, wy1, vx, vy, vw, vh);
                        } finally {
                            _isCapturing = false;
                        }
                    });
                }
            }
#endif
        }
        SmoothTowardsTargets(Time.unscaledDeltaTime);
        ApplyToTargets();
    }

    // Turns auto ambient off for THIS session (screen capture unavailable) so the
    // manual light sliders take over. The off state is intentionally NOT persisted
    // — auto ambient is on by default, so a later launch that has screen-recording
    // permission resumes desktop-following automatically.
    void SwitchToManualLights()
    {
        if (!enabledAuto) return;
        enabledAuto = false;
        var lights = FindAnyObjectByType<SettingsHandlerLights>();
        if (lights != null) lights.SyncAutoAmbientToggle(false);
        UnityEngine.Debug.Log("[DesktopAmbientProbe] No desktop sample within " + noSampleGraceSeconds + "s (screen-recording permission missing?) — switched to manual lights for this session.");
    }

#if UNITY_STANDALONE_WIN || UNITY_STANDALONE_OSX
    bool EnsureCaptureValid()
    {
#if UNITY_STANDALONE_WIN
        int vx = GetSystemMetrics(SM_XVIRTUALSCREEN);
        int vy = GetSystemMetrics(SM_YVIRTUALSCREEN);
        int vw = GetSystemMetrics(SM_CXVIRTUALSCREEN);
        int vh = GetSystemMetrics(SM_CYVIRTUALSCREEN);
        if (vw <= 0 || vh <= 0) return false;
        if (vw != virtW || vh != virtH || vx != virtX || vy != virtY) InitCapture();
        return memDC != IntPtr.Zero && dib != IntPtr.Zero && dibBits != IntPtr.Zero;
#else
        if (pixelBytes == null || pixelBytes.Length != captureWidth * captureHeight * 4)
            InitCapture();
        return MacSystemBridge.IsScreenCaptureAuthorized() && pixelBytes != null;
#endif
    }

    void InitCapture()
    {
        ReleaseCapture();
#if UNITY_STANDALONE_WIN
        virtX = GetSystemMetrics(SM_XVIRTUALSCREEN);
        virtY = GetSystemMetrics(SM_YVIRTUALSCREEN);
        virtW = GetSystemMetrics(SM_CXVIRTUALSCREEN);
        virtH = GetSystemMetrics(SM_CYVIRTUALSCREEN);
        deskDC = GetDC(IntPtr.Zero);
        memDC = CreateCompatibleDC(deskDC);
        BITMAPINFO bmi = new BITMAPINFO();
        bmi.bmiHeader.biSize = (uint)Marshal.SizeOf(typeof(BITMAPINFOHEADER));
        bmi.bmiHeader.biWidth = captureWidth;
        bmi.bmiHeader.biHeight = -captureHeight;
        bmi.bmiHeader.biPlanes = 1;
        bmi.bmiHeader.biBitCount = 32;
        bmi.bmiHeader.biCompression = 0;
        dib = CreateDIBSection(memDC, ref bmi, 0, out dibBits, IntPtr.Zero, 0);
        oldObj = SelectObject(memDC, dib);
        pixelBytes = new byte[captureWidth * captureHeight * 4];
#else
        pixelBytes = new byte[captureWidth * captureHeight * 4];
        if (!MacSystemBridge.IsScreenCaptureAuthorized())
            MacSystemBridge.RequestScreenCaptureAuthorization();
#endif
    }

    void ReleaseCapture()
    {
#if UNITY_STANDALONE_WIN
        if (memDC != IntPtr.Zero && oldObj != IntPtr.Zero) SelectObject(memDC, oldObj);
        if (dib != IntPtr.Zero) { DeleteObject(dib); dib = IntPtr.Zero; }
        if (memDC != IntPtr.Zero) { DeleteDC(memDC); memDC = IntPtr.Zero; }
        if (deskDC != IntPtr.Zero) { ReleaseDC(IntPtr.Zero, deskDC); deskDC = IntPtr.Zero; }
        dibBits = IntPtr.Zero;
#else
        pixelBytes = null;
#endif
    }

#if UNITY_STANDALONE_WIN
    IntPtr GetUnityHwnd()
    {
        IntPtr h = GetActiveWindow();
        if (h != IntPtr.Zero) return h;
        h = GetForegroundWindow();
        if (h != IntPtr.Zero)
        {
            GetWindowThreadProcessId(h, out uint pid);
            var p = Process.GetCurrentProcess();
            if (pid == (uint)p.Id) return h;
        }
        return IntPtr.Zero;
    }
#endif

    bool CapturePixels()
    {
#if UNITY_STANDALONE_WIN
        StretchBlt(memDC, 0, 0, captureWidth, captureHeight, deskDC, virtX, virtY, virtW, virtH, SRCCOPY);
        Marshal.Copy(dibBits, pixelBytes, 0, pixelBytes.Length);
        return true;
#else
        return MacSystemBridge.CaptureDesktop(captureWidth, captureHeight, pixelBytes);
#endif
    }

    bool TryGetVirtualScreen(out int vx, out int vy, out int vw, out int vh)
    {
        vx = 0; vy = 0; vw = 0; vh = 0;
#if UNITY_STANDALONE_WIN
        vx = virtX; vy = virtY; vw = virtW; vh = virtH;
        return vw > 0 && vh > 0;
#else
        RectInt v = MacWindowHelper.GetVirtualScreenRect();
        vx = v.x; vy = v.y; vw = v.width; vh = v.height;
        return vw > 0 && vh > 0;
#endif
    }

    bool TryGetUnityWindowRects(out int wx0, out int wy0, out int wx1, out int wy1, int vx, int vy, int vw, int vh)
    {
        wx0 = 0; wy0 = 0; wx1 = 0; wy1 = 0;
        int winLeft, winTop, winRight, winBottom;
#if UNITY_STANDALONE_WIN
        IntPtr hwnd = GetUnityHwnd();
        if (hwnd == IntPtr.Zero || !GetWindowRect(hwnd, out RECT wr))
            return false;
        winLeft = wr.left; winTop = wr.top; winRight = wr.right; winBottom = wr.bottom;
#else
        if (!MacWindowHelper.TryGetWindowRect(out RectInt wr))
            return false;
        winLeft = wr.x; winTop = wr.y; winRight = wr.x + wr.width; winBottom = wr.y + wr.height;
#endif
        wx0 = Mathf.RoundToInt(((winLeft - vx) / (float)vw) * captureWidth);
        wy0 = Mathf.RoundToInt(((winTop - vy) / (float)vh) * captureHeight);
        wx1 = Mathf.RoundToInt(((winRight - vx) / (float)vw) * captureWidth);
        wy1 = Mathf.RoundToInt(((winBottom - vy) / (float)vh) * captureHeight);
        return true;
    }

    void CaptureAndAnalyze(bool haveWnd, int wx0, int wy0, int wx1, int wy1, int vx, int vy, int vw, int vh)
    {
        if (!CapturePixels())
            return;

        int band = Mathf.Max(1, Mathf.RoundToInt(bandThicknessPx * (captureHeight / (float)Mathf.Max(1, vh))));
        int margin = Mathf.Max(0, Mathf.RoundToInt(excludeMarginPx * (captureHeight / (float)Mathf.Max(1, vh))));

        RectInt topRect = new RectInt(0, Mathf.Max(0, wy0 - band), captureWidth, Mathf.Clamp(band, 1, captureHeight));
        RectInt botRect = new RectInt(0, Mathf.Min(captureHeight - band, wy1 + 0), captureWidth, Mathf.Clamp(band, 1, captureHeight));
        RectInt leftRect = new RectInt(Mathf.Max(0, wx0 - band), Mathf.Clamp(wy0, 0, captureHeight - 1), Mathf.Clamp(band, 1, captureWidth), Mathf.Clamp(wy1 - wy0, 1, captureHeight));
        RectInt rightRect = new RectInt(Mathf.Min(captureWidth - band, wx1 + 0), Mathf.Clamp(wy0, 0, captureHeight - 1), Mathf.Clamp(band, 1, captureWidth), Mathf.Clamp(wy1 - wy0, 1, captureHeight));

        if (haveWnd)
        {
            topRect = ClampRect(topRect, captureWidth, captureHeight);
            botRect = ClampRect(botRect, captureWidth, captureHeight);
            leftRect = ClampRect(leftRect, captureWidth, captureHeight);
            rightRect = ClampRect(rightRect, captureWidth, captureHeight);
            RectInt inside = new RectInt(Mathf.Clamp(wx0 - margin, 0, captureWidth - 1), Mathf.Clamp(wy0 - margin, 0, captureHeight - 1), Mathf.Clamp((wx1 - wx0) + 2 * margin, 1, captureWidth), Mathf.Clamp((wy1 - wy0) + 2 * margin, 1, captureHeight));
            Exclude(ref topRect, inside);
            Exclude(ref botRect, inside);
            Exclude(ref leftRect, inside);
            Exclude(ref rightRect, inside);
        }
        else
        {
            int hband = Mathf.Max(1, captureHeight / 5);
            int wband = Mathf.Max(1, captureWidth / 8);
            topRect = new RectInt(0, hband, captureWidth, hband);
            botRect = new RectInt(0, captureHeight - hband * 2, captureWidth, hband);
            leftRect = new RectInt(wband, hband, wband, captureHeight - 2 * hband);
            rightRect = new RectInt(captureWidth - wband * 2, hband, wband, captureHeight - 2 * hband);
        }

        Color ct = AvgColor(topRect);
        Color cb = AvgColor(botRect);
        Color cl = AvgColor(leftRect);
        Color cr = AvgColor(rightRect);

        Color.RGBToHSV(ct, out float hTop, out float sTop, out float vTop);
        Color.RGBToHSV(cb, out float hBot, out float sBot, out float vBot);
        Color.RGBToHSV(cl, out float hLeft, out float sLeft, out float vLeft);
        Color.RGBToHSV(cr, out float hRight, out float sRight, out float vRight);

        Vector3 tTop = new Vector3(hTop, sTop, vTop);
        Vector3 tBot = new Vector3(hBot, sBot, vBot);
        Vector3 tLeft = new Vector3(hLeft, sLeft, vLeft);
        Vector3 tRight = new Vector3(hRight, sRight, vRight);

        if (!hasSample)
        {
            hsvTop = tTop; hsvBot = tBot; hsvLeft = tLeft; hsvRight = tRight;
            hsvTopTarget = tTop; hsvBotTarget = tBot; hsvLeftTarget = tLeft; hsvRightTarget = tRight;
            hasSample = true;
            UnityEngine.Debug.Log($"[DesktopAmbientProbe] first sample ok: top=({tTop.x:F2},{tTop.y:F2},{tTop.z:F2}) bot=({tBot.x:F2},{tBot.y:F2},{tBot.z:F2}) left=({tLeft.x:F2},{tLeft.y:F2},{tLeft.z:F2}) right=({tRight.x:F2},{tRight.y:F2},{tRight.z:F2})");
        }
        else
        {
            hsvTopTarget = tTop;
            hsvBotTarget = tBot;
            hsvLeftTarget = tLeft;
            hsvRightTarget = tRight;
        }
    }

    RectInt ClampRect(RectInt r, int w, int h)
    {
        int x = Mathf.Clamp(r.x, 0, w - 1);
        int y = Mathf.Clamp(r.y, 0, h - 1);
        int rw = Mathf.Clamp(r.width, 1, w - x);
        int rh = Mathf.Clamp(r.height, 1, h - y);
        return new RectInt(x, y, rw, rh);
    }

    void Exclude(ref RectInt r, RectInt inside)
    {
        if (!r.Overlaps(inside)) return;
        int left = Mathf.Max(r.x, inside.x);
        int right = Mathf.Min(r.x + r.width, inside.x + inside.width);
        int top = Mathf.Max(r.y, inside.y);
        int bottom = Mathf.Min(r.y + r.height, inside.y + inside.height);
        RectInt a = new RectInt(r.x, r.y, r.width, Mathf.Max(0, top - r.y));
        RectInt b = new RectInt(r.x, bottom, r.width, Mathf.Max(0, (r.y + r.height) - bottom));
        RectInt c = new RectInt(r.x, top, Mathf.Max(0, left - r.x), Mathf.Max(0, bottom - top));
        RectInt d = new RectInt(right, top, Mathf.Max(0, (r.x + r.width) - right), Mathf.Max(0, bottom - top));
        RectInt best = a;
        if (b.width * b.height > best.width * best.height) best = b;
        if (c.width * c.height > best.width * best.height) best = c;
        if (d.width * d.height > best.width * best.height) best = d;
        r = best.width > 0 && best.height > 0 ? best : new RectInt(r.x, r.y, 1, 1);
    }

    Color AvgColor(RectInt r)
    {
        long rb = 0, gb = 0, bb = 0;
        int count = 0;
        int stride = captureWidth * 4;
        int x0 = r.x; int x1 = r.x + r.width;
        int y0 = r.y; int y1 = r.y + r.height;
        for (int y = y0; y < y1; y++)
        {
            int row = y * stride;
            for (int x = x0; x < x1; x++)
            {
                int i = row + x * 4;
                byte b = pixelBytes[i + 0];
                byte g = pixelBytes[i + 1];
                byte a = pixelBytes[i + 3];
                byte r8 = pixelBytes[i + 2];
                if (a == 0) continue;
                rb += r8; gb += g; bb += b; count++;
            }
        }
        if (count == 0) return Color.black;
        float fr = rb / (255f * count);
        float fg = gb / (255f * count);
        float fb = bb / (255f * count);
        return new Color(fr, fg, fb, 1f);
    }
#endif

    void SmoothTowardsTargets(float dt)
    {
        if (!hasSample) return;
        float tau = 0.05f + 1.5f * Mathf.Clamp01(smoothing);
        float a = 1f - Mathf.Exp(-dt / Mathf.Max(0.0001f, tau));
        hsvTop = DampHSV(hsvTop, hsvTopTarget, a);
        hsvBot = DampHSV(hsvBot, hsvBotTarget, a);
        hsvLeft = DampHSV(hsvLeft, hsvLeftTarget, a);
        hsvRight = DampHSV(hsvRight, hsvRightTarget, a);
    }

    Vector3 DampHSV(Vector3 cur, Vector3 target, float a)
    {
        float dh = Mathf.DeltaAngle(cur.x * 360f, target.x * 360f) / 360f;
        float h = Mathf.Repeat(cur.x + a * dh, 1f);
        float s = Mathf.Lerp(cur.y, target.y, a);
        float v = Mathf.Lerp(cur.z, target.z, a);
        return new Vector3(h, s, v);
    }

    void ApplyToTargets()
    {
        if (colorController == null) return;
        // No desktop capture yet (e.g. screen-recording permission missing/pending)
        // — do NOT overwrite the lights. Without a sample there is no real desktop
        // color to follow, and forcing a "neutral white" fallback here would turn
        // the user's configured ambient lights dim/white, which looks like the
        // ambient light got switched off. Leave the ColorController targets at
        // their current (manual) values instead.
        if (!hasSample) return;
        for (int i = 0; i < bandTargets.Count; i++)
        {
            var bt = bandTargets[i];
            var target = FindTarget(bt.targetID);
            if (target == null) continue;
            ApplyTarget(target, GetBandHsv(bt.band));
        }
    }

    ColorController.ColorTarget FindTarget(string id)
    {
        if (string.IsNullOrEmpty(id) || colorController == null) return null;
        var targets = colorController.targets;
        for (int i = 0; i < targets.Count; i++)
            if (targets[i].id == id) return targets[i];
        return null;
    }

    Vector3 GetBandHsv(Band b)
    {
        switch (b)
        {
            case Band.Top: return hsvTop;
            case Band.Bottom: return hsvBot;
            case Band.Left: return hsvLeft;
            case Band.Right: return hsvRight;
            default: return hsvTop;
        }
    }

    void ApplyTarget(ColorController.ColorTarget target, Vector3 hsv)
    {
        target.hue = hsv.x;
        target.saturation = hsv.y;
        if (driveIntensity)
        {
            // Saturation-driven base: gray desktop → dim glow, saturated → brighter.
            float satCurve = Mathf.Pow(Mathf.Clamp01(hsv.y), saturationGamma);
            float baseI = Mathf.Lerp(minGrayIntensity, maxColorIntensity, satCurve);
            // Scale by the sampled band's brightness (hsv.z) so dark wallpapers give
            // a subtle glow and bright ones stay bounded — the light blends into the
            // background instead of glaring.
            float v = Mathf.Clamp01(hsv.z);
            float intensity = baseI * Mathf.Lerp(darkAmbientFloor, 1f, v);
            float maxI = target.intensityOverride ? target.maxIntensity : 1f;
            target.intensity = Mathf.Clamp(intensity * maxI, 0f, Mathf.Max(0.01f, maxI));
        }
    }
}
