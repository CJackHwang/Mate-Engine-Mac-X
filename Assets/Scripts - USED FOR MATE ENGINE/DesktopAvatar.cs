using UnityEngine;
using System;
using System.Runtime.InteropServices;

public class DesktopAvatar : MonoBehaviour
{
    [DllImport("user32.dll")] private static extern IntPtr GetActiveWindow();
    [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hWnd, int nIndex);
    [DllImport("user32.dll")] private static extern bool SetLayeredWindowAttributes(IntPtr hwnd, uint crKey, byte bAlpha, uint dwFlags);

    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_LAYERED = 0x80000;
    private const int WS_EX_TRANSPARENT = 0x20;
    private const int LWA_COLORKEY = 0x1;

    private IntPtr _windowHandle;
    private bool _isDragging = false;
    private Vector3 _dragOffset;

    void Start()
    {
        _windowHandle = GetActiveWindow();
        int style = GetWindowLong(_windowHandle, GWL_EXSTYLE);
        SetWindowLong(_windowHandle, GWL_EXSTYLE, style | WS_EX_LAYERED | WS_EX_TRANSPARENT);
        SetLayeredWindowAttributes(_windowHandle, 0, 0, LWA_COLORKEY);

        // Make sure the camera has a transparent background
        Camera.main.clearFlags = CameraClearFlags.SolidColor;
        Camera.main.backgroundColor = new Color(0, 0, 0, 0);
    }

    void Update()
    {
        HandleDrag();
    }

    void HandleDrag()
    {
        if (Input.GetMouseButtonDown(0)) // Left-click starts dragging
        {
            _isDragging = true;
            _dragOffset = GetMouseWorldPosition() - transform.position;
        }

        if (Input.GetMouseButtonUp(0)) // Release left-click stops dragging
        {
            _isDragging = false;
        }

        if (_isDragging)
        {
            transform.position = GetMouseWorldPosition() - _dragOffset;
        }
    }

    Vector3 GetMouseWorldPosition()
    {
        Vector3 mousePos = Input.mousePosition;
        mousePos.z = Camera.main.nearClipPlane; // Ensure it's in front of the camera
        return Camera.main.ScreenToWorldPoint(mousePos);
    }
}
