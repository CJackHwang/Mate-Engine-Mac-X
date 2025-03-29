using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;
using uWindowCapture;

#if UNITY_EDITOR
using UnityEditor;
#endif

public class UwcDesktopLayouterRealWorldSync : MonoBehaviour
{
    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern int GetWindowText(IntPtr hWnd, System.Text.StringBuilder lpString, int nMaxCount);

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    private struct RECT { public int left, top, right, bottom; }

    public UwcDesktopLayouter desktopLayouter;
    public Color gizmoColor = Color.yellow;
    public bool showTextInBuild = true;
    public GameObject injectorPrefab;

    private Dictionary<IntPtr, GameObject> windowObjects = new Dictionary<IntPtr, GameObject>();
    private IntPtr unityWindowHandle;
    private RECT unityWindowRect;
    private List<(RECT, string, IntPtr)> visibleWindows = new List<(RECT, string, IntPtr)>();

    private void Start()
    {
        if (desktopLayouter == null)
        {
            desktopLayouter = FindObjectOfType<UwcDesktopLayouter>();
        }
        FindUnityWindow();
    }

    private void Update()
    {
        if (desktopLayouter == null) return;
        GetWindowRect(unityWindowHandle, out unityWindowRect);
        SyncWindowsWithRealPositions();
    }

    private void SyncWindowsWithRealPositions()
    {
        visibleWindows.Clear();

        EnumWindows((hWnd, lParam) =>
        {
            if (!IsWindowVisible(hWnd) || !GetWindowRect(hWnd, out RECT rect)) return true;
            if ((rect.right - rect.left) < 50 || (rect.bottom - rect.top) < 50) return true;

            System.Text.StringBuilder windowTitle = new System.Text.StringBuilder(256);
            GetWindowText(hWnd, windowTitle, windowTitle.Capacity);
            string title = windowTitle.ToString().Trim();
            if (string.IsNullOrEmpty(title) || title == "Program Manager") return true;

            visibleWindows.Add((rect, title, hWnd));

            if (!windowObjects.ContainsKey(hWnd))
            {
                GameObject windowObj = new GameObject(title);
                windowObj.AddComponent<BoxCollider>();

                if (injectorPrefab != null)
                {
                    GameObject injectedObject = Instantiate(injectorPrefab, windowObj.transform);
                    injectedObject.SetActive(true);
                }

                windowObjects[hWnd] = windowObj;
            }

            float width = rect.right - rect.left;
            float topBarHeight = 30f; // Approximate height of the window's top bar

            Vector3 topLeft = ScreenToUnityWorld(rect.left, rect.top);
            Vector3 topRight = ScreenToUnityWorld(rect.right, rect.top);
            Vector3 centerTop = (topLeft + topRight) / 2f; // Center of the top bar
            float worldWidth = Vector3.Distance(topLeft, topRight); // Ensure Unity world space width

            GameObject obj = windowObjects[hWnd];
            obj.transform.position = centerTop;

            BoxCollider collider = obj.GetComponent<BoxCollider>();
            collider.size = new Vector3(worldWidth, 0.05f, 0.1f); // Thin height, only length matters
            collider.center = new Vector3(0, -0.025f, 0); // Position it correctly on top of the window

            return true;
        }, IntPtr.Zero);
    }

    private void FindUnityWindow()
    {
        EnumWindows((hWnd, lParam) =>
        {
            GetWindowRect(hWnd, out RECT rect);
            if (rect.right - rect.left > 100 && rect.bottom - rect.top > 100)
            {
                unityWindowHandle = hWnd;
                return false;
            }
            return true;
        }, IntPtr.Zero);
    }

    private Vector3 ScreenToUnityWorld(int x, int y)
    {
        float relX = (x - unityWindowRect.left) / (float)(unityWindowRect.right - unityWindowRect.left);
        float relY = 1.0f - ((y - unityWindowRect.top) / (float)(unityWindowRect.bottom - unityWindowRect.top));
        return Camera.main.ViewportToWorldPoint(new Vector3(relX, relY, Camera.main.nearClipPlane + 2f));
    }

    private void OnDrawGizmos()
    {
        if (!Application.isPlaying) return;
        Gizmos.color = gizmoColor;

        foreach (var (rect, title, hWnd) in visibleWindows)
        {
            Vector3 topLeft = ScreenToUnityWorld(rect.left, rect.top);
            Vector3 topRight = ScreenToUnityWorld(rect.right, rect.top);
            Vector3 bottomRight = ScreenToUnityWorld(rect.right, rect.bottom);
            Vector3 bottomLeft = ScreenToUnityWorld(rect.left, rect.bottom);

            Gizmos.DrawLine(topLeft, topRight);
            Gizmos.DrawLine(topRight, bottomRight);
            Gizmos.DrawLine(bottomRight, bottomLeft);
            Gizmos.DrawLine(bottomLeft, topLeft);

#if UNITY_EDITOR
            Handles.Label(topLeft + Vector3.up * 0.02f, title);
#else
            if (showTextInBuild && windowObjects.ContainsKey(hWnd))
            {
                TextMesh textMesh = windowObjects[hWnd].GetComponent<TextMesh>();
                if (textMesh == null)
                {
                    textMesh = windowObjects[hWnd].AddComponent<TextMesh>();
                    textMesh.fontSize = 20;
                    textMesh.color = Color.white;
                    textMesh.alignment = TextAlignment.Center;
                }
                textMesh.text = title;
                windowObjects[hWnd].transform.position = topLeft + Vector3.up * 0.05f;
            }
#endif
        }
    }
}