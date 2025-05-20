using UnityEngine;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;

public class AvatarWindowHandler : MonoBehaviour
{
    public int snapThreshold = 30, verticalOffset = 0;
    public float desktopScale = 1f;
    [Header("Pink Snap Zone (Unity-side)")]
    public Vector2 snapZoneOffset = new(0, -5);
    public Vector2 snapZoneSize = new(100, 10);

    IntPtr snappedHWND = IntPtr.Zero, unityHWND = IntPtr.Zero;
    Vector2 snapOffset;
    readonly List<WindowEntry> cachedWindows = new();
    Rect pinkZoneDesktopRect;
    float snapFraction, baseScale = 1f, baseOffset = 40f;
    Animator animator;
    AvatarAnimatorController controller;
    readonly System.Text.StringBuilder classNameBuffer = new(256);

    void Start()
    {
        unityHWND = Process.GetCurrentProcess().MainWindowHandle;
        animator = GetComponent<Animator>();
        controller = GetComponent<AvatarAnimatorController>();
        SetTopMost(true);
    }

    void Update()
    {
        if (unityHWND == IntPtr.Zero || animator == null || controller == null) return;
        if (!SaveLoadHandler.Instance.data.enableWindowSitting) return;

        var unityPos = GetUnityWindowPosition();
        UpdateCachedWindows();
        UpdatePinkZone(unityPos);

        if (controller.isDragging && !controller.animator.GetBool("isSitting"))
        {
            if (snappedHWND == IntPtr.Zero)
                TrySnap(unityPos);
            else if (!IsStillNearSnappedWindow())
            {
                snappedHWND = IntPtr.Zero;
                animator.SetBool("isWindowSit", false);
                SetTopMost(true);
            }
            else
                FollowSnappedWindowWhileDragging();
        }
        else if (!controller.isDragging && snappedHWND != IntPtr.Zero)
            FollowSnappedWindow();
    }

    void UpdateCachedWindows()
    {
        cachedWindows.Clear();
        EnumWindows((hWnd, lParam) =>
        {
            if (!IsWindowVisible(hWnd) || !GetWindowRect(hWnd, out RECT r)) return true;
            classNameBuffer.Clear();
            GetClassName(hWnd, classNameBuffer, classNameBuffer.Capacity);
            string cls = classNameBuffer.ToString();
            bool isTaskbar = cls == "Shell_TrayWnd" || cls == "Shell_SecondaryTrayWnd";
            if (!isTaskbar)
            {
                if ((r.Right - r.Left) < 100 || (r.Bottom - r.Top) < 100) return true;
                if (GetParent(hWnd) != IntPtr.Zero || GetWindowTextLength(hWnd) == 0) return true;
                if (cls == "Progman" || cls == "WorkerW" || cls == "DV2ControlHost" || cls == "MsgrIMEWindowClass" ||
                    cls.StartsWith("#") || cls.Contains("Desktop")) return true;
            }
            cachedWindows.Add(new WindowEntry { hwnd = hWnd, rect = r });
            return true;
        }, IntPtr.Zero);
    }

    void UpdatePinkZone(Vector2 unityPos)
    {
        float cx = unityPos.x + GetUnityWindowWidth() * 0.5f + snapZoneOffset.x;
        float by = unityPos.y + GetUnityWindowHeight() + snapZoneOffset.y;
        pinkZoneDesktopRect = new Rect(cx - snapZoneSize.x * 0.5f, by, snapZoneSize.x, snapZoneSize.y);
    }

    void TrySnap(Vector2 unityWindowPosition)
    {
        foreach (var win in cachedWindows)
        {
            if (win.hwnd == unityHWND) continue;
            int barMidX = win.rect.Left + (win.rect.Right - win.rect.Left) / 2, barY = win.rect.Top + 2;
            var pt = new POINT { X = barMidX, Y = barY };
            if (GetAncestor(WindowFromPoint(pt), GA_ROOT) != win.hwnd) continue;
            var topBar = new Rect(win.rect.Left, win.rect.Top, win.rect.Right - win.rect.Left, 5);
            if (!pinkZoneDesktopRect.Overlaps(topBar)) continue;
            snappedHWND = win.hwnd;
            float winWidth = win.rect.Right - win.rect.Left, unityWidth = GetUnityWindowWidth();
            float petCenterX = unityWindowPosition.x + unityWidth * 0.5f;
            snapFraction = (petCenterX - win.rect.Left) / winWidth;
            snapOffset.y = GetUnityWindowHeight() + snapZoneOffset.y + snapZoneSize.y * 0.5f;
            animator.SetBool("isWindowSit", true);
            SetTopMost(false);
            return;
        }
    }

    void FollowSnappedWindowWhileDragging()
    {
        foreach (var win in cachedWindows)
        {
            if (win.hwnd != snappedHWND) continue;
            var unityPos = GetUnityWindowPosition();
            float winWidth = win.rect.Right - win.rect.Left, unityWidth = GetUnityWindowWidth();
            float petCenterX = unityPos.x + unityWidth * 0.5f;
            snapFraction = (petCenterX - win.rect.Left) / winWidth;
            float newCenterX = win.rect.Left + snapFraction * winWidth;
            int targetX = Mathf.RoundToInt(newCenterX - unityWidth * 0.5f);
            float yOffset = GetUnityWindowHeight() + snapZoneOffset.y + snapZoneSize.y * 0.5f;
            float scale = transform.localScale.y, scaleOffset = (baseScale - scale) * baseOffset;
            int targetY = win.rect.Top - (int)(yOffset + scaleOffset) + verticalOffset;
            SetUnityWindowPosition(targetX, targetY);
            return;
        }
    }

    void FollowSnappedWindow()
    {
        foreach (var win in cachedWindows)
        {
            if (win.hwnd != snappedHWND) continue;
            float winWidth = win.rect.Right - win.rect.Left, unityWidth = GetUnityWindowWidth();
            float newCenterX = win.rect.Left + snapFraction * winWidth;
            int targetX = Mathf.RoundToInt(newCenterX - unityWidth * 0.5f);
            float yOffset = GetUnityWindowHeight() + snapZoneOffset.y + snapZoneSize.y * 0.5f;
            float scale = transform.localScale.y, scaleOffset = (baseScale - scale) * baseOffset;
            int targetY = win.rect.Top - (int)(yOffset + scaleOffset) + verticalOffset;
            SetUnityWindowPosition(targetX, targetY);
            SetWindowPos(unityHWND, win.hwnd, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
            return;
        }
        snappedHWND = IntPtr.Zero;
        animator.SetBool("isWindowSit", false);
        SetTopMost(true);
    }

    bool IsStillNearSnappedWindow()
    {
        foreach (var win in cachedWindows)
            if (win.hwnd == snappedHWND)
                return pinkZoneDesktopRect.Overlaps(new Rect(win.rect.Left, win.rect.Top, win.rect.Right - win.rect.Left, 5));
        return false;
    }

    // Windows API Interop
    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)] static extern int GetClassName(IntPtr hWnd, System.Text.StringBuilder lpClassName, int nMaxCount);
    [DllImport("user32.dll")] static extern IntPtr WindowFromPoint(POINT pt);
    [DllImport("user32.dll")] static extern IntPtr GetAncestor(IntPtr hwnd, uint gaFlags);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);
    [DllImport("user32.dll")] static extern bool MoveWindow(IntPtr hWnd, int X, int Y, int nWidth, int nHeight, bool bRepaint);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr hWnd);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);
    [DllImport("user32.dll", SetLastError = true)] static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);
    [DllImport("user32.dll", SetLastError = true)] static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);
    [DllImport("user32.dll")] static extern IntPtr GetParent(IntPtr hWnd);
    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)] static extern int GetWindowTextLength(IntPtr hWnd);

    public struct RECT { public int Left, Top, Right, Bottom; }
    struct POINT { public int X, Y; }
    struct WindowEntry { public IntPtr hwnd; public RECT rect; }
    delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
    static readonly IntPtr HWND_TOPMOST = new(-1), HWND_NOTOPMOST = new(-2);
    const uint GA_ROOT = 2, SWP_NOMOVE = 0x0002, SWP_NOSIZE = 0x0001, SWP_NOACTIVATE = 0x0010;

    void SetTopMost(bool en) => SetWindowPos(unityHWND, en ? HWND_TOPMOST : HWND_NOTOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
    Vector2 GetUnityWindowPosition() { GetWindowRect(unityHWND, out RECT r); return new(r.Left, r.Top); }
    int GetUnityWindowWidth() { GetWindowRect(unityHWND, out RECT r); return r.Right - r.Left; }
    int GetUnityWindowHeight() { GetWindowRect(unityHWND, out RECT r); return r.Bottom - r.Top; }
    void SetUnityWindowPosition(int x, int y) => MoveWindow(unityHWND, x, y, GetUnityWindowWidth(), GetUnityWindowHeight(), true);

    void OnDrawGizmos()
    {
        if (!Application.isPlaying) return;
        float basePixel = 1000f / desktopScale;
        Gizmos.color = Color.magenta; DrawDesktopRect(pinkZoneDesktopRect, basePixel);
        GetWindowRect(unityHWND, out RECT uRect);
        Gizmos.color = Color.green; DrawDesktopRect(new Rect(uRect.Left, uRect.Bottom - 5, uRect.Right - uRect.Left, 5), basePixel);
        foreach (var win in cachedWindows)
        {
            if (win.hwnd == unityHWND) continue;
            int w = win.rect.Right - win.rect.Left, h = win.rect.Bottom - win.rect.Top;
            Gizmos.color = Color.red; DrawDesktopRect(new Rect(win.rect.Left, win.rect.Top, w, 5), basePixel);
            Gizmos.color = Color.yellow; DrawDesktopRect(new Rect(win.rect.Left, win.rect.Top, w, h), basePixel);
        }
    }

    void DrawDesktopRect(Rect r, float basePixel)
    {
        float cx = r.x + r.width * 0.5f, cy = r.y + r.height * 0.5f;
        int screenWidth = Display.main.systemWidth, screenHeight = Display.main.systemHeight;
        float unityX = (cx - screenWidth * 0.5f) / basePixel, unityY = -(cy - screenHeight * 0.5f) / basePixel;
        Vector3 worldPos = new(unityX, unityY, 0), worldSize = new(r.width / basePixel, r.height / basePixel, 0);
        Gizmos.DrawWireCube(worldPos, worldSize);
    }
}
