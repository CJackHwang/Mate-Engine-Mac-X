using UnityEngine;
using System.Runtime.InteropServices;
using System;

public class SwingController : MonoBehaviour
{
    [Header("Target Object to Swing")]
    public GameObject targetObject;

    [Header("Swing Settings")]
    [Range(0f, 1f)] public float smoothness = 0.9f;
    public float velocityMultiplier = 1.0f;

    private Vector2Int previousWindowPos;
    private Vector3 currentVelocity;

    [SerializeField, HideInInspector] private Vector3 anchorWorldPosition;
    private bool initialized = false;
    private bool playModeAnchorCaptured = false;

    private bool wasFocusedLastFrame = true;
    private bool skipNextUpdate = false;

    private void Awake()
    {
        if (targetObject == null)
            targetObject = gameObject;
    }

    private void Update()
    {
        if (!Application.isPlaying)
            return;

        // Detect focus loss and regain
        if (!Application.isFocused)
        {
            wasFocusedLastFrame = false;
            return;
        }

        if (!wasFocusedLastFrame)
        {
            // We just regained focus, skip next update to avoid swing spike
            skipNextUpdate = true;
            wasFocusedLastFrame = true;
        }

        if (skipNextUpdate)
        {
            previousWindowPos = GetWindowPosition();
            skipNextUpdate = false;
            return;
        }

        if (!playModeAnchorCaptured)
        {
            anchorWorldPosition = targetObject.transform.position;
            previousWindowPos = GetWindowPosition();
            currentVelocity = Vector3.zero;
            initialized = true;
            playModeAnchorCaptured = true;
        }

        if (!initialized || targetObject == null)
            return;

        Vector2Int currentWindowPos = GetWindowPosition();
        Vector2Int delta = currentWindowPos - previousWindowPos;
        previousWindowPos = currentWindowPos;

        Vector3 offset = new Vector3(-delta.x, delta.y, 0f) * velocityMultiplier;
        currentVelocity = Vector3.Lerp(currentVelocity, offset, 1f - smoothness);

        Vector3 desiredPosition = anchorWorldPosition + currentVelocity;
        targetObject.transform.position = Vector3.Lerp(
            targetObject.transform.position,
            desiredPosition,
            1f - smoothness
        );
    }

    [ContextMenu("Reset Anchor (Play Mode Only)")]
    public void ResetAnchor()
    {
        if (!Application.isPlaying) return;

        if (targetObject != null)
        {
            anchorWorldPosition = targetObject.transform.position;
            previousWindowPos = GetWindowPosition();
            currentVelocity = Vector3.zero;
            initialized = true;
        }
    }

    #region WinAPI
    private Vector2Int GetWindowPosition()
    {
        GetWindowRect(GetActiveWindow(), out RECT rect);
        return new Vector2Int(rect.left, rect.top);
    }

    [DllImport("user32.dll")] private static extern IntPtr GetActiveWindow();
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    private struct RECT
    {
        public int left, top, right, bottom;
    }
    #endregion
}