using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;

public static class MonitorHelper
{
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
    // -- Monitor lookup ----------------------------------------------------
    public const uint MONITOR_DEFAULTTONEAREST = 0x00000002;

    [DllImport("user32.dll")]
    public static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

    [StructLayout(LayoutKind.Sequential)]
    struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct RECT
    {
        public int Left, Top, Right, Bottom;
    }

    /// <summary>
    /// Returns the taskbar Rect on whichever monitor contains the given window handle.
    /// </summary>
    public static Rect GetTaskbarRectForWindow(IntPtr windowHandle)
    {
        var hMon = MonitorFromWindow(windowHandle, MONITOR_DEFAULTTONEAREST);
        var mi = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        if (!GetMonitorInfo(hMon, ref mi))
            return new Rect(0, 0, 0, 0);

        var mon = new Rect(mi.rcMonitor.Left,
                            mi.rcMonitor.Top,
                            mi.rcMonitor.Right - mi.rcMonitor.Left,
                            mi.rcMonitor.Bottom - mi.rcMonitor.Top);
        var work = new Rect(mi.rcWork.Left,
                            mi.rcWork.Top,
                            mi.rcWork.Right - mi.rcWork.Left,
                            mi.rcWork.Bottom - mi.rcWork.Top);

        // whichever edge of work is smaller than mon is the taskbar
        if (work.yMin > mon.yMin)
            return new Rect(mon.xMin, mon.yMin, mon.width, work.yMin - mon.yMin);
        if (work.xMin > mon.xMin)
            return new Rect(mon.xMin, mon.yMin, work.xMin - mon.xMin, mon.height);
        if (work.xMax < mon.xMax)
            return new Rect(work.xMax, mon.yMin, mon.xMax - work.xMax, mon.height);
        if (work.yMax < mon.yMax)
            return new Rect(mon.xMin, work.yMax, mon.width, mon.yMax - work.yMax);

        return new Rect(0, 0, 0, 0);
    }

    // -- DPI / scaling lookup ----------------------------------------------
    enum MONITOR_DPI_TYPE
    {
        MDT_EFFECTIVE_DPI = 0,
        MDT_ANGULAR_DPI = 1,
        MDT_RAW_DPI = 2
    }

    [DllImport("Shcore.dll")]
    static extern int GetDpiForMonitor(
        IntPtr hmonitor,
        MONITOR_DPI_TYPE dpiType,
        out uint dpiX,
        out uint dpiY
    );

    /// <summary>
    /// Returns the scale factor (e.g. 1.5 for 150% DPI) for the monitor containing the given window.
    /// </summary>
    public static float GetScaleForWindow(IntPtr windowHandle)
    {
        var hMon = MonitorFromWindow(windowHandle, MONITOR_DEFAULTTONEAREST);
        if (GetDpiForMonitor(hMon, MONITOR_DPI_TYPE.MDT_EFFECTIVE_DPI, out var dpiX, out var dpiY) == 0)
            return dpiX / 96f;

        return 1f;
    }
#endif

#if UNITY_STANDALONE_OSX || UNITY_EDITOR_OSX
    public static Rect GetTaskbarRectForWindow(IntPtr windowHandle)
    {
        return GetDockTaskbarRectForWindow();
    }

    public static float GetScaleForWindow(IntPtr windowHandle)
    {
        return 1f;
    }

    public static Rect GetDockTaskbarRectForWindow()
    {
        RectInt window = default;
        if (!MacWindowHelper.TryGetWindowRect(out window))
            window = new RectInt(0, 0, Screen.width, Screen.height);

        List<RectInt> monitors = GetMonitorRects();
        RectInt monitor = GetCurrentMonitorRect(window, monitors);

        int index = monitors.IndexOf(monitor);
        if (index < 0)
            index = 0;

        try
        {
            MacSystemBridge.MacSys_GetScreenVisibleRect(index, out int vx, out int vy, out int vw, out int vh);
            if (vw <= 0 || vh <= 0) return new Rect(0, 0, 0, 0);

            int x = monitor.x, y = monitor.y, w = monitor.width, h = monitor.height;
            if (vy > y)
                return new Rect(x, y, w, vy - y);
            if (vy + vh < y + h)
                return new Rect(x, vy + vh, w, y + h - (vy + vh));
            if (vx > x)
                return new Rect(x, y, vx - x, h);
            if (vx + vw < x + w)
                return new Rect(vx + vw, y, x + w - (vx + vw), h);
        }
        catch (Exception)
        {
        }

        return new Rect(0, 0, 0, 0);
    }

    public static List<RectInt> GetMonitorRects()
    {
        return MacWindowHelper.GetMonitors();
    }

    public static RectInt GetPrimaryMonitorRect()
    {
        return MacWindowHelper.GetPrimaryMonitorRect();
    }

    public static RectInt GetVirtualScreenRect()
    {
        return MacWindowHelper.GetVirtualScreenRect();
    }

    public static RectInt GetCurrentMonitorRect(RectInt window)
    {
        return GetCurrentMonitorRect(window, GetMonitorRects());
    }

    private static RectInt GetCurrentMonitorRect(RectInt window, List<RectInt> monitors)
    {
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
        return bestOverlap >= 0 ? best : (monitors.Count > 0 ? monitors[0] : new RectInt(0, 0, Screen.width, Screen.height));
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
#endif
}
