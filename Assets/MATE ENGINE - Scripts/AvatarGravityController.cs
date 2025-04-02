using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;
using VRM;

public class AvatarGravityController : MonoBehaviour
{
    [Header("Impact Settings")]
    [Tooltip("How much motion from window drag affects SpringBones")]
    public float impactMultiplier = 0.05f;

    [Header("Debug")]
    public bool showDebugForce = true;
    public Color debugColor = Color.cyan;

    private Vector2Int previousWindowPos;
    private Vector3 currentForce;
    private List<VRMSpringBone> springBones = new();

    void Start()
    {
        previousWindowPos = GetWindowPosition();
        springBones.AddRange(GetComponentsInChildren<VRMSpringBone>());
    }

    void Update()
    {
        Vector2Int currentWindowPos = GetWindowPosition();
        Vector2Int delta = currentWindowPos - previousWindowPos;

        // Calculate impact vector from window drag
        if (delta != Vector2Int.zero)
        {
            Vector3 impact = new Vector3(-delta.x, delta.y, 0).normalized * impactMultiplier;

            currentForce = impact;
        }
        else
        {
            currentForce = Vector3.zero;
        }

        foreach (var spring in springBones)
        {
            spring.ExternalForce = currentForce;
        }

        previousWindowPos = currentWindowPos;
    }

    void OnDrawGizmos()
    {
        if (!showDebugForce) return;

        Gizmos.color = debugColor;
        Gizmos.DrawLine(transform.position, transform.position + currentForce);
        Gizmos.DrawSphere(transform.position + currentForce, 0.02f);
    }

    #region Windows API

    private Vector2Int GetWindowPosition()
    {
        GetWindowRect(GetActiveWindow(), out RECT rect);
        return new Vector2Int(rect.left, rect.top);
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetActiveWindow();

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int left, top, right, bottom;
    }

    #endregion
}
