using UnityEngine;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
public class AvatarWindowHandler : MonoBehaviour
{
    public int snapThreshold = 30;
    public int verticalOffset = 0;
    public float desktopScale = 1f;

    [Header("Pink Snap Zone (Unity-side)")]
    public Vector2 snapZoneOffset = new Vector2(0, -5);
    public Vector2 snapZoneSize = new Vector2(100, 10);

    private IntPtr snappedHWND = IntPtr.Zero;
    private Vector2 snapOffset;
    private IntPtr unityHWND;
    private readonly List<WindowEntry> cachedWindows = new List<WindowEntry>();
    private Rect pinkZoneDesktopRect;
    private float snapFraction = 0f;

    private float baseScale = 1f;
    private float baseOffset = 40f;

    private Animator animator;
    private AvatarAnimatorController controller;
    private readonly System.Text.StringBuilder classNameBuffer = new System.Text.StringBuilder(256);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    static extern int GetClassName(IntPtr hWnd, System.Text.StringBuilder lpClassName, int nMaxCount);
    [DllImport("user32.dll")]
    static extern IntPtr WindowFromPoint(POINT pt);
    [DllImport("user32.dll")]
    static extern IntPtr GetAncestor(IntPtr hwnd, uint gaFlags);

    struct POINT { public int X, Y; }

    const uint GA_ROOT = 2;

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

        Vector2 unityPos = GetUnityWindowPosition();
        UpdateCachedWindows();
        UpdatePinkZone(unityPos);

        if (controller.isDragging && !controller.animator.GetBool("isSitting") && snappedHWND == IntPtr.Zero)
        {
            TrySnap(unityPos);
        }
        else if (controller.isDragging && !controller.animator.GetBool("isSitting") && snappedHWND != IntPtr.Zero)
        {
            if (!IsStillNearSnappedWindow())
            {
                snappedHWND = IntPtr.Zero;
                animator.SetBool("isWindowSit", false);
                SetTopMost(true);
            }
            else
            {
                FollowSnappedWindowWhileDragging();
            }
        }
        else if (!controller.isDragging && snappedHWND != IntPtr.Zero)
        {
            FollowSnappedWindow();
        }
    }

    void UpdateCachedWindows()
    {
        cachedWindows.Clear();
        EnumWindows((hWnd, lParam) =>
        {
            if (!IsWindowVisible(hWnd)) return true;
            if (!GetWindowRect(hWnd, out RECT r)) return true;

            classNameBuffer.Clear();
            GetClassName(hWnd, classNameBuffer, classNameBuffer.Capacity);
            string className = classNameBuffer.ToString();

            bool isTaskbar = className == "Shell_TrayWnd" || className == "Shell_SecondaryTrayWnd";

            if (!isTaskbar)
            {
                if ((r.Right - r.Left) < 100 || (r.Bottom - r.Top) < 100)
                    return true;
                if (GetParent(hWnd) != IntPtr.Zero)
                    return true;
                if (GetWindowTextLength(hWnd) == 0)
                    return true;
                if (className == "Progman" || className == "WorkerW" ||
                    className == "DV2ControlHost" || className == "MsgrIMEWindowClass" ||
                    className.StartsWith("#") || className.Contains("Desktop"))
                    return true;
            }
            cachedWindows.Add(new WindowEntry { hwnd = hWnd, rect = r });
            return true;
        }, IntPtr.Zero);
    }

    void UpdatePinkZone(Vector2 unityPos)
    {
        float centerX = unityPos.x + GetUnityWindowWidth() / 2 + snapZoneOffset.x;
        float bottomY = unityPos.y + GetUnityWindowHeight() + snapZoneOffset.y;
        pinkZoneDesktopRect = new Rect(centerX - snapZoneSize.x / 2, bottomY, snapZoneSize.x, snapZoneSize.y);
    }

    void TrySnap(Vector2 unityWindowPosition)
    {
        for (int i = 0; i < cachedWindows.Count; i++)
        {
            var win = cachedWindows[i];
            if (win.hwnd == unityHWND) continue;
            int barMidX = win.rect.Left + (win.rect.Right - win.rect.Left) / 2;
            int barY = win.rect.Top + 2;
            var pt = new POINT { X = barMidX, Y = barY };
            IntPtr hwndAtPoint = GetAncestor(WindowFromPoint(pt), GA_ROOT);
            if (hwndAtPoint != win.hwnd) continue;

            var topBarRect = new Rect(win.rect.Left, win.rect.Top,
                                      win.rect.Right - win.rect.Left, 5);
            if (!pinkZoneDesktopRect.Overlaps(topBarRect)) continue;
            snappedHWND = win.hwnd;
            int winWidth = win.rect.Right - win.rect.Left;
            int unityWidth = GetUnityWindowWidth();
            float petCenterX = unityWindowPosition.x + unityWidth * 0.5f;
            snapFraction = (petCenterX - win.rect.Left) / winWidth;
            snapOffset.y = GetUnityWindowHeight()
                         + snapZoneOffset.y
                         + snapZoneSize.y * 0.5f;

            animator.SetBool("isWindowSit", true);
            SetTopMost(false);
            return;
        }
    }

    void FollowSnappedWindowWhileDragging()
    {
        for (int i = 0; i < cachedWindows.Count; i++)
        {
            var win = cachedWindows[i];
            if (win.hwnd != snappedHWND) continue;
            Vector2 unityPos = GetUnityWindowPosition();
            int currentWidth = win.rect.Right - win.rect.Left;
            int unityWidth = GetUnityWindowWidth();
            float petCenterX = unityPos.x + unityWidth * 0.5f;
            snapFraction = (petCenterX - win.rect.Left) / (float)currentWidth;
            float newCenterX = win.rect.Left + snapFraction * currentWidth;
            int targetX = Mathf.RoundToInt(newCenterX - unityWidth * 0.5f);
            float dynamicOffsetY = GetUnityWindowHeight()
                                 + snapZoneOffset.y
                                 + snapZoneSize.y * 0.5f;
            float scale = transform.localScale.y;
            float scaleOffset = (baseScale - scale) * baseOffset;
            int targetY = win.rect.Top
                         - (int)(dynamicOffsetY + scaleOffset)
                         + verticalOffset;


            SetUnityWindowPosition(targetX, targetY);
            return;
        }
    }

    void FollowSnappedWindow()
    {
        for (int i = 0; i < cachedWindows.Count; i++)
        {
            var win = cachedWindows[i];
            if (win.hwnd != snappedHWND) continue;

            int currentWidth = win.rect.Right - win.rect.Left;
            int unityWidth = GetUnityWindowWidth();

            float newCenterX = win.rect.Left + snapFraction * currentWidth;
            int targetX = Mathf.RoundToInt(newCenterX - unityWidth * 0.5f);

            float dynamicOffsetY = GetUnityWindowHeight()
                                 + snapZoneOffset.y
                                 + snapZoneSize.y * 0.5f;
            float scale = transform.localScale.y;
            float scaleOffset = (baseScale - scale) * baseOffset;
            int targetY = win.rect.Top
                         - (int)(dynamicOffsetY + scaleOffset)
                         + verticalOffset;


            SetUnityWindowPosition(targetX, targetY);
            SetWindowPos(unityHWND, win.hwnd,
                         0, 0, 0, 0,
                         SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
            return;
        }

        snappedHWND = IntPtr.Zero;
        animator.SetBool("isWindowSit", false);
        SetTopMost(true);
    }


    bool IsStillNearSnappedWindow()
    {
        for (int i = 0; i < cachedWindows.Count; i++)
        {
            var win = cachedWindows[i];
            if (win.hwnd == snappedHWND)
            {
                Rect topBar = new Rect(win.rect.Left, win.rect.Top, win.rect.Right - win.rect.Left, 5);
                return pinkZoneDesktopRect.Overlaps(topBar);
            }
        }
        return false;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT { public int Left, Top, Right, Bottom; }

    struct WindowEntry { public IntPtr hwnd; public RECT rect; }
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);
    [DllImport("user32.dll")] static extern bool MoveWindow(IntPtr hWnd, int X, int Y, int nWidth, int nHeight, bool bRepaint);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr hWnd);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);
    [DllImport("user32.dll", SetLastError = true)] static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);
    [DllImport("user32.dll", SetLastError = true)] static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);
    [DllImport("user32.dll")] static extern IntPtr GetParent(IntPtr hWnd);
    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)] static extern int GetWindowTextLength(IntPtr hWnd);

    delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
    static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
    static readonly IntPtr HWND_NOTOPMOST = new IntPtr(-2);
    const uint SWP_NOMOVE = 0x0002;
    const uint SWP_NOSIZE = 0x0001;
    const uint SWP_NOACTIVATE = 0x0010;

    void SetTopMost(bool enable)
    {
        SetWindowPos(unityHWND, enable ? HWND_TOPMOST : HWND_NOTOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
    }

    Vector2 GetUnityWindowPosition()
    {
        GetWindowRect(unityHWND, out RECT rect);
        return new Vector2(rect.Left, rect.Top);
    }

    int GetUnityWindowWidth()
    {
        GetWindowRect(unityHWND, out RECT rect);
        return rect.Right - rect.Left;
    }

    int GetUnityWindowHeight()
    {
        GetWindowRect(unityHWND, out RECT rect);
        return rect.Bottom - rect.Top;
    }

    void SetUnityWindowPosition(int x, int y)
    {
        MoveWindow(unityHWND, x, y, GetUnityWindowWidth(), GetUnityWindowHeight(), true);
    }

    void OnDrawGizmos()
    {
        if (!Application.isPlaying) return;
        float basePixel = 1000f / desktopScale;

        Gizmos.color = Color.magenta;
        DrawDesktopRect(pinkZoneDesktopRect, basePixel);

        GetWindowRect(unityHWND, out RECT uRect);
        Gizmos.color = Color.green;
        DrawDesktopRect(new Rect(uRect.Left, uRect.Bottom - 5, uRect.Right - uRect.Left, 5), basePixel);

        for (int i = 0; i < cachedWindows.Count; i++)
        {
            var win = cachedWindows[i];
            if (win.hwnd == unityHWND) continue;
            int w = win.rect.Right - win.rect.Left;
            int h = win.rect.Bottom - win.rect.Top;

            Gizmos.color = Color.red;
            DrawDesktopRect(new Rect(win.rect.Left, win.rect.Top, w, 5), basePixel);
            Gizmos.color = Color.yellow;
            DrawDesktopRect(new Rect(win.rect.Left, win.rect.Top, w, h), basePixel);
        }
    }

    void DrawDesktopRect(Rect desktopRect, float basePixel)
    {
        float cx = desktopRect.x + desktopRect.width / 2f;
        float cy = desktopRect.y + desktopRect.height / 2f;
        int screenWidth = Display.main.systemWidth;
        int screenHeight = Display.main.systemHeight;

        float unityX = (cx - screenWidth / 2f) / basePixel;
        float unityY = -(cy - screenHeight / 2f) / basePixel;
        Vector3 worldPos = new Vector3(unityX, unityY, 0);
        Vector3 worldSize = new Vector3(desktopRect.width / basePixel, desktopRect.height / basePixel, 0);

        Gizmos.DrawWireCube(worldPos, worldSize);
    }

}