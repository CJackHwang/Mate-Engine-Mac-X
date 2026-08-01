using UnityEngine;
using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Debug = UnityEngine.Debug;

public class MoveToPrimaryScreen : MonoBehaviour
{
    private IntPtr unityHWND = IntPtr.Zero;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool MoveWindow(IntPtr hWnd, int X, int Y, int nWidth, int nHeight, bool bRepaint);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    void Start()
    {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        unityHWND = Process.GetCurrentProcess().MainWindowHandle;
#endif
    }

    public void MoveToPrimary()
    {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        if (unityHWND == IntPtr.Zero) return;

        if (!GetWindowRect(unityHWND, out RECT rect)) return;

        int currentWidth = rect.Right - rect.Left;
        int currentHeight = rect.Bottom - rect.Top;

        var screen = System.Windows.Forms.Screen.PrimaryScreen;
        var bounds = screen.Bounds;

        int x = bounds.Left + (bounds.Width - currentWidth) / 2;
        int y = bounds.Top + (bounds.Height - currentHeight) / 2;

        MoveWindow(unityHWND, x, y, currentWidth, currentHeight, true);

        Debug.Log($"[MoveToPrimaryScreen] moved window {currentWidth}x{currentHeight} to {x},{y}");
#elif UNITY_STANDALONE_OSX || UNITY_EDITOR_OSX
        var uwc = Kirurobo.UniWindowController.current;
        if (uwc == null) return;

        Vector2 size = uwc.windowSize;
        if (size.x <= 0f || size.y <= 0f) return;

        RectInt primary = MacWindowHelper.GetPrimaryMonitorRect();
        float x = primary.x + (primary.width - size.x) * 0.5f;
        float y = primary.y + (primary.height - size.y) * 0.5f;
        MacWindowHelper.MoveWindowTopLeft(Mathf.RoundToInt(x), Mathf.RoundToInt(y));

        Debug.Log($"[MoveToPrimaryScreen] moved window {(int)size.x}x{(int)size.y} to {x:F0},{y:F0}");
#else
        Debug.Log("[MoveToPrimaryScreen] no-op on this platform.");
#endif
    }
}
