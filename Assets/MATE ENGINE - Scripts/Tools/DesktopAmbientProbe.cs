using UnityEngine;
using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

public class DesktopAmbientProbe : MonoBehaviour
{
    public Light topLight;
    public Light bottomLight;
    public Light leftLight;
    public Light rightLight;

    public bool enabledAuto = true;
    public bool driveIntensity = true;
    [Range(1f, 60f)] public float captureHz = 10f;
    public int captureWidth = 160;
    public int captureHeight = 90;
    public int bandThicknessPx = 120;
    public int excludeMarginPx = 12;
    [Range(0f, 1f)] public float smoothing = 0.85f;
    public string saveKey = "auto_ambient";
    [Range(0f, 4f)] public float minGrayIntensity = 0.3f;
    [Range(0f, 4f)] public float maxColorIntensity = 0.8f;
    [Range(0.5f, 3f)] public float saturationGamma = 1.3f;

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
    byte[] pixelBytes;
#endif

    float nextTick;
    Vector3 hsvTop;
    Vector3 hsvBot;
    Vector3 hsvLeft;
    Vector3 hsvRight;
    Vector3 hsvTopTarget;
    Vector3 hsvBotTarget;
    Vector3 hsvLeftTarget;
    Vector3 hsvRightTarget;
    bool inited;
    bool hasSample;

    void Start()
    {
        TryLoadToggle();
#if UNITY_STANDALONE_WIN
        InitCapture();
#endif
        inited = true;
    }

    void OnDestroy()
    {
#if UNITY_STANDALONE_WIN
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
    }

    void LateUpdate()
    {
        if (!inited) return;
        if (!enabledAuto) return;
        if (Time.unscaledTime >= nextTick)
        {
            nextTick = Time.unscaledTime + 1f / Mathf.Max(1f, captureHz);
#if UNITY_STANDALONE_WIN
            if (EnsureCaptureValid()) CaptureAndAnalyze();
#endif
        }
        SmoothTowardsTargets(Time.unscaledDeltaTime);
        ApplyToLights();
    }

#if UNITY_STANDALONE_WIN
    bool EnsureCaptureValid()
    {
        int vx = GetSystemMetrics(SM_XVIRTUALSCREEN);
        int vy = GetSystemMetrics(SM_YVIRTUALSCREEN);
        int vw = GetSystemMetrics(SM_CXVIRTUALSCREEN);
        int vh = GetSystemMetrics(SM_CYVIRTUALSCREEN);
        if (vw <= 0 || vh <= 0) return false;
        if (vw != virtW || vh != virtH || vx != virtX || vy != virtY) InitCapture();
        return memDC != IntPtr.Zero && dib != IntPtr.Zero && dibBits != IntPtr.Zero;
    }

    void InitCapture()
    {
        ReleaseCapture();
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
    }

    void ReleaseCapture()
    {
        if (memDC != IntPtr.Zero && oldObj != IntPtr.Zero) SelectObject(memDC, oldObj);
        if (dib != IntPtr.Zero) { DeleteObject(dib); dib = IntPtr.Zero; }
        if (memDC != IntPtr.Zero) { DeleteDC(memDC); memDC = IntPtr.Zero; }
        if (deskDC != IntPtr.Zero) { ReleaseDC(IntPtr.Zero, deskDC); deskDC = IntPtr.Zero; }
        dibBits = IntPtr.Zero;
    }

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

    void CaptureAndAnalyze()
    {
        StretchBlt(memDC, 0, 0, captureWidth, captureHeight, deskDC, virtX, virtY, virtW, virtH, SRCCOPY);
        Marshal.Copy(dibBits, pixelBytes, 0, pixelBytes.Length);

        RECT wr = new RECT();
        var hwnd = GetUnityHwnd();
        bool haveWnd = hwnd != IntPtr.Zero && GetWindowRect(hwnd, out wr);
        int wx0 = 0, wy0 = 0, wx1 = 0, wy1 = 0;
        if (haveWnd)
        {
            wx0 = Mathf.RoundToInt(((wr.left - virtX) / (float)virtW) * captureWidth);
            wy0 = Mathf.RoundToInt(((wr.top - virtY) / (float)virtH) * captureHeight);
            wx1 = Mathf.RoundToInt(((wr.right - virtX) / (float)virtW) * captureWidth);
            wy1 = Mathf.RoundToInt(((wr.bottom - virtY) / (float)virtH) * captureHeight);
        }
        int band = Mathf.Max(1, Mathf.RoundToInt(bandThicknessPx * (captureHeight / (float)Mathf.Max(1, virtH))));
        int margin = Mathf.Max(0, Mathf.RoundToInt(excludeMarginPx * (captureHeight / (float)Mathf.Max(1, virtH))));

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

    void ApplyToLights()
    {
        ApplyLight(topLight, hsvTop);
        ApplyLight(bottomLight, hsvBot);
        ApplyLight(leftLight, hsvLeft);
        ApplyLight(rightLight, hsvRight);
    }

    void ApplyLight(Light L, Vector3 hsv)
    {
        if (L == null) return;
        Color c = Color.HSVToRGB(hsv.x, hsv.y, 1f);
        L.color = c;
        if (driveIntensity)
        {
            float i = Mathf.Lerp(minGrayIntensity, maxColorIntensity, Mathf.Pow(Mathf.Clamp01(hsv.y), saturationGamma));
            L.intensity = Mathf.Clamp(i, 0f, 4f);
        }
    }
}
