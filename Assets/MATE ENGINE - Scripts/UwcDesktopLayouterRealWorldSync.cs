using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEditor;
using System.Diagnostics;
using System.Text;

public class WindowDetector : MonoBehaviour
{
    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll")]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left, Top, Right, Bottom;
    }

    public List<string> blocker = new List<string>(); // List to block windows by name

    private Dictionary<IntPtr, GameObject> windowObjects = new Dictionary<IntPtr, GameObject>();
    private Dictionary<IntPtr, string> windowNames = new Dictionary<IntPtr, string>();
    private List<RECT> windowRects = new List<RECT>();
    private IntPtr unityWindowHandle;
    private RECT unityWindowRect;
    private const float TITLE_BAR_HEIGHT_PX = 30f;

    private void Start()
    {
        uint currentProcessId = (uint)Process.GetCurrentProcess().Id;
        EnumWindows((hWnd, lParam) =>
        {
            uint processId;
            GetWindowThreadProcessId(hWnd, out processId);
            if (processId == currentProcessId)
            {
                unityWindowHandle = hWnd;
                return false;
            }
            return true;
        }, IntPtr.Zero);
    }

    void Update()
    {
        if (unityWindowHandle == IntPtr.Zero) return;
        GetWindowRect(unityWindowHandle, out unityWindowRect);

        List<IntPtr> detectedWindows = new List<IntPtr>();
        windowRects.Clear();

        EnumWindows((hWnd, lParam) =>
        {
            if (hWnd == unityWindowHandle || !IsWindowVisible(hWnd) || !GetWindowRect(hWnd, out RECT rect)) return true;

            string name = GetWindowName(hWnd);
            if (blocker.Contains(name)) return true; // Skip blocked windows

            detectedWindows.Add(hWnd);
            windowRects.Add(rect);
            UpdateOrCreateWindowObject(hWnd, rect, name);
            return true;
        }, IntPtr.Zero);

        // Remove GameObjects for closed windows
        List<IntPtr> toRemove = new List<IntPtr>();
        foreach (var kvp in windowObjects)
        {
            if (!detectedWindows.Contains(kvp.Key))
            {
                Destroy(kvp.Value);
                toRemove.Add(kvp.Key);
            }
        }
        foreach (var hWnd in toRemove)
        {
            windowObjects.Remove(hWnd);
            windowNames.Remove(hWnd);
        }
    }

    private string GetWindowName(IntPtr hWnd)
    {
        StringBuilder windowTitle = new StringBuilder(256);
        GetWindowText(hWnd, windowTitle, windowTitle.Capacity);
        string title = windowTitle.ToString();

        if (!string.IsNullOrEmpty(title)) return title; // If a valid title is found, use it.

        // If no title, get process name instead
        GetWindowThreadProcessId(hWnd, out uint processId);
        try
        {
            Process process = Process.GetProcessById((int)processId);
            return process.ProcessName + ".exe"; // Example: "firefox.exe"
        }
        catch
        {
            return "Unknown Process"; // Fallback in case the process is not found
        }
    }

    private void UpdateOrCreateWindowObject(IntPtr hWnd, RECT rect, string name)
    {
        if (!windowObjects.TryGetValue(hWnd, out GameObject windowObj))
        {
            windowObj = new GameObject(name);
            windowObj.transform.SetParent(transform);
            BoxCollider collider = windowObj.AddComponent<BoxCollider>();
            collider.isTrigger = true;
            windowObjects[hWnd] = windowObj;
        }

        windowNames[hWnd] = name;

        // Convert Windows coordinates to Unity world space
        Vector3 worldTopLeft = ScreenToWorld(rect.Left, rect.Top);
        Vector3 worldBottomRight = ScreenToWorld(rect.Right, rect.Bottom);

        float width = Mathf.Abs(worldBottomRight.x - worldTopLeft.x);
        float height = Mathf.Abs(worldBottomRight.y - worldTopLeft.y);
        float titleBarWorldHeight = TITLE_BAR_HEIGHT_PX * (height / (rect.Bottom - rect.Top));

        Vector3 colliderSize = new Vector3(width, titleBarWorldHeight, 0.1f);
        Vector3 colliderPosition = new Vector3(
            (worldTopLeft.x + worldBottomRight.x) / 2f,
            worldTopLeft.y - (titleBarWorldHeight / 2f),
            0
        );

        windowObj.transform.position = colliderPosition;
        windowObj.transform.localScale = Vector3.one;

        BoxCollider boxCollider = windowObj.GetComponent<BoxCollider>();
        boxCollider.size = colliderSize;
        boxCollider.center = Vector3.zero;
    }

    private Vector3 ScreenToWorld(int x, int y)
    {
        float relX = (x - unityWindowRect.Left) / (float)(unityWindowRect.Right - unityWindowRect.Left);
        float relY = 1.0f - ((y - unityWindowRect.Top) / (float)(unityWindowRect.Bottom - unityWindowRect.Top));
        Vector3 viewportPos = new Vector3(Mathf.Clamp01(relX), Mathf.Clamp01(relY), Camera.main.nearClipPlane + 2f);
        return Camera.main.ViewportToWorldPoint(viewportPos);
    }

    private void OnDrawGizmos()
    {
        if (!Application.isPlaying) return;

        Gizmos.color = Color.cyan;
        foreach (var rect in windowRects)
        {
            Vector3 topLeft = ScreenToWorld(rect.Left, rect.Top);
            Vector3 topRight = ScreenToWorld(rect.Right, rect.Top);
            Vector3 bottomRight = ScreenToWorld(rect.Right, rect.Bottom);
            Vector3 bottomLeft = ScreenToWorld(rect.Left, rect.Bottom);

            Gizmos.DrawLine(topLeft, topRight);
            Gizmos.DrawLine(topRight, bottomRight);
            Gizmos.DrawLine(bottomRight, bottomLeft);
            Gizmos.DrawLine(bottomLeft, topLeft);

            Gizmos.color = Color.magenta;
            float titleBarWorldHeight = TITLE_BAR_HEIGHT_PX * (Mathf.Abs(bottomRight.y - topLeft.y) / (rect.Bottom - rect.Top));
            Vector3 titleBarBottomLeft = topLeft + new Vector3(0, -titleBarWorldHeight, 0);
            Vector3 titleBarBottomRight = topRight + new Vector3(0, -titleBarWorldHeight, 0);

            Gizmos.DrawLine(topLeft, topRight);
            Gizmos.DrawLine(topRight, titleBarBottomRight);
            Gizmos.DrawLine(titleBarBottomRight, titleBarBottomLeft);
            Gizmos.DrawLine(titleBarBottomLeft, topLeft);

            Gizmos.color = Color.cyan;
        }
    }

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        if (!Application.isPlaying) return;

        foreach (var kvp in windowNames)
        {
            IntPtr hWnd = kvp.Key;
            string name = kvp.Value;

            if (!windowObjects.TryGetValue(hWnd, out GameObject windowObj)) continue;

            Vector3 textPosition = windowObj.transform.position + new Vector3(-0.5f, 0.2f, 0);
            Handles.Label(textPosition, name);
        }
    }
#endif
}
