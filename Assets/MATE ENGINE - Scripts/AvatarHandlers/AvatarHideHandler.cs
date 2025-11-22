using UnityEngine;
using System;
using System.Runtime.InteropServices;
using System.Diagnostics;

public class AvatarHideHandler : MonoBehaviour
{
    public int snapThresholdPx = 12;
    public int unsnapThresholdPx = 24;
    public int edgeInsetPx = 0;
    public bool enableSmoothing = true;
    [Range(0.01f, 0.5f)] public float smoothingTime = 0.10f;
    public float smoothingMaxSpeed = 6000f;
    public bool keepTopmostWhileSnapped = true;
    public float unsnapGraceTime = 0.12f;

    Animator animator;
    AvatarAnimatorController controller;
    IntPtr unityHWND;

    Transform leftHand;
    Transform rightHand;
    Camera cam;

    enum Side { None, Left, Right }
    Side snappedSide = Side.None;

    int cursorOffsetY;
    int windowW, windowH;
    float velX, velY;
    bool smoothingActive;
    bool wasDragging;
    float snappedAt;

    void Start()
    {
#if UNITY_STANDALONE_WIN
        unityHWND = Process.GetCurrentProcess().MainWindowHandle;
#endif
        animator = GetComponent<Animator>();
        controller = GetComponent<AvatarAnimatorController>();
        if (animator != null && animator.isHuman && animator.avatar != null)
        {
            leftHand = animator.GetBoneTransform(HumanBodyBones.LeftHand);
            rightHand = animator.GetBoneTransform(HumanBodyBones.RightHand);
        }
        cam = Camera.main;
        if (cam == null) cam = FindObjectOfType<Camera>();
    }

    void OnDisable()
    {
        SetHide(false, false);
        snappedSide = Side.None;
    }

    void Update()
    {
#if !UNITY_STANDALONE_WIN
        return;
#else
        if (unityHWND == IntPtr.Zero || animator == null || controller == null) return;

        if (controller.isDragging && !wasDragging)
        {
            if (GetWindowRect(unityHWND, out RECT wr) && GetCursorPos(out POINT cp))
            {
                windowW = Math.Max(1, wr.Right - wr.Left);
                windowH = Math.Max(1, wr.Bottom - wr.Top);
                cursorOffsetY = cp.y - wr.Top;
                smoothingActive = false;
                velX = velY = 0f;
            }
        }

        if (controller.isDragging)
        {
            if (!GetCursorPos(out POINT cp)) { wasDragging = controller.isDragging; return; }
            if (!GetWindowRect(unityHWND, out RECT wrCur)) { wasDragging = controller.isDragging; return; }
            RECT mon = GetCurrentMonitorRect(cp);

            int anchorLeftDesk = GetAnchorDesktopX(Side.Left);
            int anchorRightDesk = GetAnchorDesktopX(Side.Right);
            if (anchorLeftDesk < 0) anchorLeftDesk = wrCur.Left + Math.Max(1, (wrCur.Right - wrCur.Left) / 2);
            if (anchorRightDesk < 0) anchorRightDesk = wrCur.Left + Math.Max(1, (wrCur.Right - wrCur.Left) / 2);

            bool nearLeft = anchorLeftDesk - mon.Left <= Math.Max(1, snapThresholdPx);
            bool nearRight = mon.Right - anchorRightDesk <= Math.Max(1, snapThresholdPx);

            if (snappedSide == Side.None)
            {
                if (nearLeft) SnapTo(Side.Left, cp, mon);
                else if (nearRight) SnapTo(Side.Right, cp, mon);
            }
            else
            {
                if (Time.unscaledTime >= snappedAt + unsnapGraceTime)
                {
                    if (snappedSide == Side.Left && (cp.x - mon.Left) > Math.Max(1, unsnapThresholdPx)) Unsnap();
                    else if (snappedSide == Side.Right && (mon.Right - cp.x) > Math.Max(1, unsnapThresholdPx)) Unsnap();
                }
            }

            if (snappedSide != Side.None)
            {
                if (!GetWindowRect(unityHWND, out RECT wr2)) { wasDragging = controller.isDragging; return; }
                RECT monNow = GetCurrentMonitorRect(cp);

                int anchorDesk = GetAnchorDesktopX(snappedSide);
                if (anchorDesk < 0) anchorDesk = wr2.Left + Math.Max(1, (wr2.Right - wr2.Left) / 2);
                int anchorWinX = Mathf.Clamp(anchorDesk - wr2.Left, 0, Math.Max(1, wr2.Right - wr2.Left));

                int desiredAnchorDesk = snappedSide == Side.Left ? monNow.Left + edgeInsetPx : monNow.Right - edgeInsetPx;
                int tx = desiredAnchorDesk - anchorWinX;

                int ty = cp.y - cursorOffsetY;

                MoveSmooth(wr2.Left, wr2.Top, tx, ty, wr2.Right - wr2.Left, wr2.Bottom - wr2.Top);
                if (keepTopmostWhileSnapped) SetTopMost(true);
            }
        }
        else
        {
            if (snappedSide != Side.None)
            {
                if (!GetWindowRect(unityHWND, out RECT wr)) return;
                RECT mon = GetMonitorFromWindow(unityHWND);

                int anchorDesk = GetAnchorDesktopX(snappedSide);
                if (anchorDesk < 0) anchorDesk = wr.Left + Math.Max(1, (wr.Right - wr.Left) / 2);
                int anchorWinX = Mathf.Clamp(anchorDesk - wr.Left, 0, Math.Max(1, wr.Right - wr.Left));

                int desiredAnchorDesk = snappedSide == Side.Left ? mon.Left + edgeInsetPx : mon.Right - edgeInsetPx;
                int tx = desiredAnchorDesk - anchorWinX;

                int ty = wr.Top;

                MoveSmooth(wr.Left, wr.Top, tx, ty, wr.Right - wr.Left, wr.Bottom - wr.Top);
                if (keepTopmostWhileSnapped) SetTopMost(true);
            }
        }

        wasDragging = controller.isDragging;
#endif
    }

#if UNITY_STANDALONE_WIN
    int GetAnchorDesktopX(Side side)
    {
        Transform t = side == Side.Left ? leftHand : rightHand;
        if (t == null || cam == null) return -1;
        if (!GetUnityClientRect(out RECT uCli)) return -1;

        Vector3 sp = cam.WorldToScreenPoint(t.position);
        if (sp.z < 0.01f) return -1;

        float clientW = Mathf.Max(1f, uCli.Right - uCli.Left);
        float pxW = Mathf.Max(1, cam.pixelWidth);
        float sx = Mathf.Clamp(sp.x, 0, cam.pixelWidth) * (clientW / pxW);
        int desktopX = uCli.Left + Mathf.RoundToInt(sx);
        return desktopX;
    }

    void SnapTo(Side side, POINT cp, RECT mon)
    {
        if (!GetWindowRect(unityHWND, out RECT wr)) return;

        windowW = Math.Max(1, wr.Right - wr.Left);
        windowH = Math.Max(1, wr.Bottom - wr.Top);
        cursorOffsetY = cp.y - wr.Top;
        snappedSide = side;
        SetHide(side == Side.Left, side == Side.Right);

        int anchorDesk = GetAnchorDesktopX(side);
        if (anchorDesk < 0) anchorDesk = wr.Left + Math.Max(1, (wr.Right - wr.Left) / 2);
        int anchorWinX = Mathf.Clamp(anchorDesk - wr.Left, 0, Math.Max(1, wr.Right - wr.Left));

        int desiredAnchorDesk = side == Side.Left ? mon.Left + edgeInsetPx : mon.Right - edgeInsetPx;
        int tx = desiredAnchorDesk - anchorWinX;

        int ty = cp.y - cursorOffsetY;

        MoveWindow(unityHWND, tx, ty, windowW, windowH, true);
        smoothingActive = enableSmoothing;
        velX = velY = 0f;
        snappedAt = Time.unscaledTime;
        if (keepTopmostWhileSnapped) SetTopMost(true);
    }

    void Unsnap()
    {
        snappedSide = Side.None;
        SetHide(false, false);
        smoothingActive = false;
        velX = velY = 0f;
        SetTopMost(false);
    }

    void SetHide(bool left, bool right)
    {
        animator.SetBool("HideLeft", left);
        animator.SetBool("HideRight", right);
    }

    void MoveSmooth(int curX, int curY, int targetX, int targetY, int w, int h)
    {
        if (!enableSmoothing || !smoothingActive)
        {
            if (curX != targetX || curY != targetY) MoveWindow(unityHWND, targetX, targetY, w, h, true);
            return;
        }
        float dt = Time.unscaledDeltaTime;
        float nx = Mathf.SmoothDamp(curX, targetX, ref velX, smoothingTime, smoothingMaxSpeed, dt);
        float ny = Mathf.SmoothDamp(curY, targetY, ref velY, smoothingTime, smoothingMaxSpeed, dt);
        int ix = Mathf.RoundToInt(nx);
        int iy = Mathf.RoundToInt(ny);
        if (Mathf.Abs(targetX - ix) <= 1 && Mathf.Abs(targetY - iy) <= 1)
        {
            ix = targetX; iy = targetY; smoothingActive = false; velX = velY = 0f;
        }
        if (ix != curX || iy != curY) MoveWindow(unityHWND, ix, iy, w, h, true);
    }

    RECT GetCurrentMonitorRect(POINT cp)
    {
        RECT fallback = GetVirtualScreenRect();
        IntPtr hmon = MonitorFromPoint(cp, MONITOR_DEFAULTTONEAREST);
        if (hmon == IntPtr.Zero) return fallback;
        MONITORINFO mi = new MONITORINFO();
        mi.cbSize = Marshal.SizeOf(typeof(MONITORINFO));
        if (!GetMonitorInfo(hmon, ref mi)) return fallback;
        return mi.rcMonitor;
    }

    RECT GetMonitorFromWindow(IntPtr hwnd)
    {
        RECT fallback = GetVirtualScreenRect();
        IntPtr hmon = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
        if (hmon == IntPtr.Zero) return fallback;
        MONITORINFO mi = new MONITORINFO();
        mi.cbSize = Marshal.SizeOf(typeof(MONITORINFO));
        if (!GetMonitorInfo(hmon, ref mi)) return fallback;
        return mi.rcMonitor;
    }

    RECT GetVirtualScreenRect()
    {
        RECT r = new RECT();
        r.Left = GetSystemMetrics(SM_XVIRTUALSCREEN);
        r.Top = GetSystemMetrics(SM_YVIRTUALSCREEN);
        r.Right = r.Left + GetSystemMetrics(SM_CXVIRTUALSCREEN);
        r.Bottom = r.Top + GetSystemMetrics(SM_CYVIRTUALSCREEN);
        return r;
    }

    bool GetUnityClientRect(out RECT r)
    {
        r = new RECT();
        if (!GetClientRect(unityHWND, out RECT client)) return false;
        POINT p = new POINT { X = 0, Y = 0 };
        if (!ClientToScreen(unityHWND, ref p)) return false;
        r.Left = p.X; r.Top = p.Y; r.Right = p.X + client.Right; r.Bottom = p.Y + client.Bottom;
        return true;
    }

    void SetTopMost(bool on)
    {
        SetWindowPos(unityHWND, on ? HWND_TOPMOST : HWND_NOTOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
    }

    [StructLayout(LayoutKind.Sequential)] struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Explicit)]
    struct POINT
    {
        [FieldOffset(0)] public int x;
        [FieldOffset(0)] public int X;
        [FieldOffset(4)] public int y;
        [FieldOffset(4)] public int Y;
    }

    [StructLayout(LayoutKind.Sequential)] struct MONITORINFO { public int cbSize; public RECT rcMonitor; public RECT rcWork; public int dwFlags; }

    [DllImport("user32.dll")] static extern bool GetCursorPos(out POINT lpPoint);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);
    [DllImport("user32.dll")] static extern bool MoveWindow(IntPtr hWnd, int X, int Y, int nWidth, int nHeight, bool bRepaint);
    [DllImport("user32.dll")] static extern int GetSystemMetrics(int nIndex);
    [DllImport("user32.dll")] static extern IntPtr MonitorFromPoint(POINT pt, uint dwFlags);
    [DllImport("user32.dll")] static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);
    [DllImport("user32.dll", SetLastError = true)] static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);
    [DllImport("user32.dll", SetLastError = true)] static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);
    [DllImport("user32.dll")] static extern bool GetClientRect(IntPtr hWnd, out RECT lpRect);
    [DllImport("user32.dll")] static extern bool ClientToScreen(IntPtr hWnd, ref POINT lpPoint);

    static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
    static readonly IntPtr HWND_NOTOPMOST = new IntPtr(-2);

    const uint MONITOR_DEFAULTTONEAREST = 2;
    const uint SWP_NOMOVE = 0x0002;
    const uint SWP_NOSIZE = 0x0001;
    const uint SWP_NOACTIVATE = 0x0010;

    const int SM_CXVIRTUALSCREEN = 78;
    const int SM_CYVIRTUALSCREEN = 79;
    const int SM_XVIRTUALSCREEN = 76;
    const int SM_YVIRTUALSCREEN = 77;
#endif
}