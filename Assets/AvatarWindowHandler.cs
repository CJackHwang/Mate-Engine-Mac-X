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
    private List<WindowEntry> cachedWindows = new List<WindowEntry>();
    private Rect pinkZoneDesktopRect;

    private Animator animator;
    private AvatarAnimatorController controller;

    System.Text.StringBuilder classNameBuffer = new System.Text.StringBuilder(256);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    static extern int GetClassName(IntPtr hWnd, System.Text.StringBuilder lpClassName, int nMaxCount);



    void Start()
    {
        unityHWND = Process.GetCurrentProcess().MainWindowHandle;
        animator = GetComponent<Animator>();
        controller = GetComponent<AvatarAnimatorController>();
        SetTopMost(true); // Default topmost until snapped
    }

    void Update()
    {
        if (unityHWND == IntPtr.Zero || animator == null || controller == null) return;

        if (!controller.isDragging && snappedHWND == IntPtr.Zero) return;

        Vector2 unityPos = GetUnityWindowPosition();
        cachedWindows = GetOpenWindows();

        UpdatePinkZone(unityPos);

        if (controller.isDragging && snappedHWND == IntPtr.Zero)
        {
            TrySnap(unityPos);
        }
        else if (controller.isDragging && snappedHWND != IntPtr.Zero)
        {
            if (!IsStillNearSnappedWindow())
            {
                animator.SetBool("isWindowSit", false);
                SetTopMost(true);
                snappedHWND = IntPtr.Zero;
            }
        }
        else if (snappedHWND != IntPtr.Zero)
        {
            FollowSnappedWindow();
        }
    }

    void UpdatePinkZone(Vector2 unityPos)
    {
        float centerX = unityPos.x + GetUnityWindowWidth() / 2 + snapZoneOffset.x;
        float bottomY = unityPos.y + GetUnityWindowHeight() + snapZoneOffset.y;

        pinkZoneDesktopRect = new Rect(
            centerX - snapZoneSize.x / 2,
            bottomY,
            snapZoneSize.x,
            snapZoneSize.y
        );
    }

    void TrySnap(Vector2 unityWindowPosition)
    {
        foreach (var win in cachedWindows)
        {
            if (win.hwnd == unityHWND) continue;

            Rect topBar = new Rect(win.rect.Left, win.rect.Top, win.rect.Right - win.rect.Left, 5);

            // If the pink zone overlaps this window's top bar
            if (pinkZoneDesktopRect.Overlaps(topBar))
            {
                // This is the topmost window that matches — snap to it
                snappedHWND = win.hwnd;

                snapOffset = new Vector2(
                    unityWindowPosition.x - win.rect.Left,
                    win.rect.Top - unityWindowPosition.y
                );

                controller.isDragging = false;
                animator.SetBool("isDragging", false);
                animator.SetBool("isWindowSit", true);

                SetTopMost(false);
                return;
            }

            // If pink zone overlaps *this* window's full rect, it means it's covering that area
            Rect fullWindow = new Rect(
                win.rect.Left,
                win.rect.Top,
                win.rect.Right - win.rect.Left,
                win.rect.Bottom - win.rect.Top
            );

            if (fullWindow.Overlaps(pinkZoneDesktopRect))
            {
                // Something is blocking — we stop here and refuse to snap to anything behind
                return;
            }
        }
    }



    void FollowSnappedWindow()
    {
        foreach (var win in cachedWindows)
        {
            if (win.hwnd == snappedHWND)
            {
                int targetX = win.rect.Left + (int)snapOffset.x;
                int targetY = win.rect.Top - (int)snapOffset.y + verticalOffset;
                SetUnityWindowPosition(targetX, targetY);

                SetWindowPos(unityHWND, win.hwnd, 0, 0, 0, 0,
                    SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
                return;
            }
        }

        animator.SetBool("isWindowSit", false);
        SetTopMost(true);
        snappedHWND = IntPtr.Zero;
    }

    bool IsStillNearSnappedWindow()
    {
        foreach (var win in cachedWindows)
        {
            if (win.hwnd == snappedHWND)
            {
                Rect topBar = new Rect(win.rect.Left, win.rect.Top, win.rect.Right - win.rect.Left, 5);
                return pinkZoneDesktopRect.Overlaps(topBar);
            }
        }

        return false;
    }

    #region WinAPI

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT { public int Left, Top, Right, Bottom; }

    struct WindowEntry
    {
        public IntPtr hwnd;
        public RECT rect;
    }

    [DllImport("user32.dll")]
    static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    static extern bool MoveWindow(IntPtr hWnd, int X, int Y, int nWidth, int nHeight, bool bRepaint);

    [DllImport("user32.dll")]
    static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll", SetLastError = true)]
    static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

    [DllImport("user32.dll")]
    static extern IntPtr GetParent(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    static extern int GetWindowTextLength(IntPtr hWnd);

    delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
    static readonly IntPtr HWND_NOTOPMOST = new IntPtr(-2);

    const uint SWP_NOMOVE = 0x0002;
    const uint SWP_NOSIZE = 0x0001;
    const uint SWP_NOACTIVATE = 0x0010;

    void SetTopMost(bool enable)
    {
        SetWindowPos(unityHWND, enable ? HWND_TOPMOST : HWND_NOTOPMOST,
            0, 0, 0, 0,
            SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
    }

    List<WindowEntry> GetOpenWindows()
    {
        List<WindowEntry> windows = new List<WindowEntry>();

        EnumWindows((hWnd, lParam) =>
        {
            if (!IsWindowVisible(hWnd)) return true;
            if (!GetWindowRect(hWnd, out RECT r)) return true;
            if ((r.Right - r.Left) < 100 || (r.Bottom - r.Top) < 100) return true;
            if (GetParent(hWnd) != IntPtr.Zero) return true;
            if (GetWindowTextLength(hWnd) == 0) return true;

            // Get class name to filter system/desktop shells
            classNameBuffer.Clear();
            GetClassName(hWnd, classNameBuffer, classNameBuffer.Capacity);
            string className = classNameBuffer.ToString();

            // Filter known shell windows that shouldn't be snap targets
            if (className == "Progman" || className == "WorkerW" || className == "Shell_TrayWnd" ||
                className == "DV2ControlHost" || className == "MsgrIMEWindowClass" ||
                className.StartsWith("#") || className.Contains("Desktop"))
                return true;

            windows.Add(new WindowEntry { hwnd = hWnd, rect = r });
            return true;
        }, IntPtr.Zero);

        return windows;
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

    #endregion

    #region Gizmos

    void OnDrawGizmos()
    {
        if (!Application.isPlaying || cachedWindows == null) return;

        float basePixel = 1000f / desktopScale;

        Gizmos.color = Color.magenta;
        DrawDesktopRect(pinkZoneDesktopRect, basePixel);

        GetWindowRect(unityHWND, out RECT uRect);
        Gizmos.color = Color.green;
        DrawDesktopRect(new Rect(uRect.Left, uRect.Bottom - 5, uRect.Right - uRect.Left, 5), basePixel);

        foreach (var win in cachedWindows)
        {
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

    #endregion
}
