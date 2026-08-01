#if UNITY_STANDALONE_OSX || UNITY_EDITOR_OSX
using System.Collections.Generic;
using Kirurobo;
using UnityEngine;

// All rects and cursor positions use Windows-style screen coordinates:
// origin at the top-left of the main display, Y grows downward. UniWindowController
// speaks AppKit coordinates (bottom-left origin, Y up), so every boundary converts.
public static class MacWindowHelper
{
    public static bool TryGetWindowRect(out RectInt rect)
    {
        rect = default;
        UniWindowController uwc = UniWindowController.current;
        if (uwc == null) return false;

        Vector2 pos = uwc.windowPosition;
        Vector2 size = uwc.windowSize;
        if (size.x <= 0f || size.y <= 0f) return false;

        float screenH = GetGlobalScreenHeight();
        float top = screenH - (pos.y + size.y);
        rect = new RectInt(Mathf.RoundToInt(pos.x), Mathf.RoundToInt(top),
                           Mathf.RoundToInt(size.x), Mathf.RoundToInt(size.y));
        return true;
    }

    public static bool TryGetClientRect(out RectInt rect)
    {
        rect = default;
        UniWindowController uwc = UniWindowController.current;
        if (uwc == null) return false;
        if (!TryGetWindowRect(out RectInt frame)) return false;

        Vector2 client = uwc.clientSize;
        if (client.x <= 0f || client.y <= 0f)
        {
            rect = frame;
            return true;
        }

        float titleBar = Mathf.Max(0f, frame.height - client.y);
        rect = new RectInt(frame.x, frame.y + Mathf.RoundToInt(titleBar),
                           Mathf.RoundToInt(client.x), Mathf.RoundToInt(client.y));
        return true;
    }

    public static bool TryGetCursorPosition(out Vector2Int pos)
    {
        pos = default;
        UniWindowController uwc = UniWindowController.current;
        if (uwc == null) return false;
        try
        {
            Vector2 cursor = uwc.cursorPosition;
            float screenH = GetGlobalScreenHeight();
            pos = new Vector2Int(Mathf.RoundToInt(cursor.x), Mathf.RoundToInt(screenH - cursor.y));
            return true;
        }
        catch (System.Exception)
        {
            return false;
        }
    }

    public static void MoveWindowTopLeft(int x, int y)
    {
        UniWindowController uwc = UniWindowController.current;
        if (uwc == null) return;
        Vector2 size = uwc.windowSize;
        float screenH = GetGlobalScreenHeight();
        uwc.windowPosition = new Vector2(x, screenH - y - size.y);
    }

    public static void ResizeWindow(int width, int height)
    {
        UniWindowController uwc = UniWindowController.current;
        if (uwc == null || width <= 0 || height <= 0) return;
        uwc.windowSize = new Vector2(width, height);
    }

    public static void MoveAndResize(RectInt rect)
    {
        UniWindowController uwc = UniWindowController.current;
        if (uwc == null || rect.width <= 0 || rect.height <= 0) return;
        float screenH = GetGlobalScreenHeight();
        uwc.windowPosition = new Vector2(rect.x, screenH - rect.y - rect.height);
        uwc.windowSize = new Vector2(rect.width, rect.height);
    }

    public static void SetTopMost(bool enabled)
    {
        UniWindowController uwc = UniWindowController.current;
        if (uwc != null) uwc.isTopmost = enabled;
    }

    public static void BringSelfToFront()
    {
        try { MacWindowListBinding.MacWin_BringSelfToFront(); }
        catch (System.Exception) { }
    }

    public static bool TryGetFrontNormalWindow(out RectInt rect, out int pid, out int windowNumber)
    {
        rect = default;
        pid = 0;
        windowNumber = 0;
        try
        {
            if (MacWindowListBinding.MacWin_GetFrontNormalWindow(
                    out int x, out int y, out int w, out int h,
                    out pid, out windowNumber) == 0)
                return false;
            rect = new RectInt(x, y, w, h);
            return true;
        }
        catch (System.Exception)
        {
            return false;
        }
    }

    public static bool IsFrontWindowFullscreen()
    {
        if (!TryGetFrontNormalWindow(out RectInt rect, out int pid, out _))
            return false;
        if (pid == System.Diagnostics.Process.GetCurrentProcess().Id)
            return false;

        var monitors = GetMonitors();
        int tolerance = 2;
        for (int i = 0; i < monitors.Count; i++)
        {
            RectInt m = monitors[i];
            if (Mathf.Abs(rect.x - m.x) <= tolerance &&
                Mathf.Abs(rect.y - m.y) <= tolerance &&
                Mathf.Abs(rect.width - m.width) <= tolerance &&
                Mathf.Abs(rect.height - m.height) <= tolerance)
                return true;
        }
        return false;
    }

    public static bool ConstrainWindowToScreens()
    {
        if (!TryGetWindowRect(out RectInt rect))
            return false;

        var monitors = GetMonitors();
        RectInt best = GetCurrentMonitorRect(rect);
        int overlap = OverlapArea(rect, best);
        int centerX = rect.x + rect.width / 2;
        int centerY = rect.y + rect.height / 2;
        bool centerVisible = false;
        for (int i = 0; i < monitors.Count; i++)
        {
            RectInt m = monitors[i];
            if (centerX >= m.x && centerX < m.x + m.width &&
                centerY >= m.y && centerY < m.y + m.height)
            {
                centerVisible = true;
                break;
            }
        }
        if (overlap > 0 && centerVisible)
            return false;

        int x = best.x + Mathf.RoundToInt((best.width - rect.width) * 0.5f);
        int y = best.y + Mathf.RoundToInt((best.height - rect.height) * 0.4f);
        MoveWindowTopLeft(x, y);
        return true;
    }

    public static List<RectInt> GetMonitors()
    {
        var list = new List<RectInt>();
        int count = 0;
        try { count = MacSystemBridge.MacSys_GetScreenCount(); }
        catch (System.Exception) { count = 0; }

        for (int i = 0; i < count; i++)
        {
            try
            {
                MacSystemBridge.MacSys_GetScreenRect(i, out int x, out int y, out int w, out int h);
                if (w > 0 && h > 0) list.Add(new RectInt(x, y, w, h));
            }
            catch (System.Exception)
            {
                break;
            }
        }

        if (list.Count == 0)
            list.Add(new RectInt(0, 0, Display.main.systemWidth, Display.main.systemHeight));
        return list;
    }

    public static RectInt GetVirtualScreenRect()
    {
        try
        {
            MacSystemBridge.MacSys_GetVirtualScreenRect(out int x, out int y, out int w, out int h);
            if (w > 0 && h > 0) return new RectInt(x, y, w, h);
        }
        catch (System.Exception)
        {
        }
        return new RectInt(0, 0, Display.main.systemWidth, Display.main.systemHeight);
    }

    public static RectInt GetPrimaryMonitorRect()
    {
        try
        {
            MacSystemBridge.MacSys_GetMainScreenRect(out int x, out int y, out int w, out int h);
            if (w > 0 && h > 0) return new RectInt(x, y, w, h);
        }
        catch (System.Exception)
        {
        }
        var monitors = GetMonitors();
        return monitors.Count > 0 ? monitors[0] : new RectInt(0, 0, Display.main.systemWidth, Display.main.systemHeight);
    }

    public static RectInt GetCurrentMonitorRect(RectInt window)
    {
        var monitors = GetMonitors();
        RectInt best = default;
        int bestOverlap = -1;
        for (int i = 0; i < monitors.Count; i++)
        {
            int overlap = OverlapArea(window, monitors[i]);
            if (overlap > bestOverlap)
            {
                bestOverlap = overlap;
                best = monitors[i];
            }
        }
        return bestOverlap >= 0 ? best : GetPrimaryMonitorRect();
    }

    public static bool IsAppFocused()
    {
        try { return MacSystemBridge.MacSys_IsAppActive() != 0; }
        catch (System.Exception) { return Application.isFocused; }
    }

    public static bool IsWindowOccludedAtCursor()
    {
        try { return MacSystemBridge.MacSys_IsWindowOccludedAtCursor() != 0; }
        catch (System.Exception) { return false; }
    }

    public static float GetGlobalScreenHeight()
    {
        try
        {
            float h = MacSystemBridge.MacSys_GetMainDisplayHeight();
            if (h > 0f) return h;
        }
        catch (System.Exception)
        {
        }
        return GetVirtualScreenRect().height;
    }

    private static int OverlapArea(RectInt a, RectInt b)
    {
        int x1 = Mathf.Max(a.x, b.x);
        int x2 = Mathf.Min(a.x + a.width, b.x + b.width);
        int y1 = Mathf.Max(a.y, b.y);
        int y2 = Mathf.Min(a.y + a.height, b.y + b.height);
        int w = x2 - x1;
        int h = y2 - y1;
        return w > 0 && h > 0 ? w * h : 0;
    }
}
#endif
