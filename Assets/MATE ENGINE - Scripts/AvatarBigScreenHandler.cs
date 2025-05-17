using UnityEngine;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;

public class AvatarBigScreenHandler : MonoBehaviour
{
    [Header("Keybinds")]
    public List<KeyCode> ToggleKeys = new List<KeyCode> { KeyCode.B };

    [Header("Animator & Bone Selection")]
    public Animator avatarAnimator;
    public HumanBodyBones attachBone = HumanBodyBones.Head;

    [Header("Camera")]
    public Camera MainCamera;
    [Tooltip("Override for Zoom: Camera FOV (Perspective) or Size (Orthographic). 0 = auto.")]
    public float TargetZoom = 0f;
    public float ZoomMoveSpeed = 10f;
    [Tooltip("Y-Offset to bone position (meters, before scaling)")]
    public float YOffset = 0.08f;

    [Header("Fade Animation")]
    public float FadeYOffset = 0.5f;
    public float FadeInDuration = 0.5f;
    public float FadeOutDuration = 0.5f;

    [Header("Canvas Blocking")]
    public GameObject moveCanvas;

    private IntPtr unityHWND = IntPtr.Zero;
    private bool isBigScreenActive = false;
    private Vector3 originalCamPos;
    private Quaternion originalCamRot;
    private float originalFOV;
    private float originalOrthoSize;
    private float originalCamX, originalCamZ;
    private RECT originalWindowRect;
    private bool originalRectSet = false;
    private Transform bone;
    private AvatarAnimatorController avatarAnimatorController;
    private bool moveCanvasWasActive = false;
    private Coroutine fadeCoroutine;
    private bool isFading = false;

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int left, top, right, bottom; }

    private delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdcMonitor, ref RECT lprcMonitor, IntPtr dwData);

    [DllImport("user32.dll")]
    private static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr lprcClip, MonitorEnumProc lpfnEnum, IntPtr dwData);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    private static extern bool MoveWindow(IntPtr hWnd, int X, int Y, int nWidth, int nHeight, bool bRepaint);

    public static List<AvatarBigScreenHandler> ActiveHandlers = new List<AvatarBigScreenHandler>();

    void OnEnable()
    {
        if (!ActiveHandlers.Contains(this))
            ActiveHandlers.Add(this);
    }
    void OnDisable()
    {
        ActiveHandlers.Remove(this);
    }
    public void ToggleBigScreenFromUI()
    {
        var isActiveField = GetType().GetField("isBigScreenActive", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        bool isActive = isActiveField != null && (bool)isActiveField.GetValue(this);

        if (!isActive)
            SendMessage("ActivateBigScreen");
        else
            SendMessage("DeactivateBigScreen");
    }

    void Start()
    {
        unityHWND = Process.GetCurrentProcess().MainWindowHandle;
        if (MainCamera == null) MainCamera = Camera.main;
        if (avatarAnimator == null) avatarAnimator = GetComponent<Animator>();
        if (MainCamera != null)
        {
            originalCamPos = MainCamera.transform.position;
            originalCamRot = MainCamera.transform.rotation;
            originalFOV = MainCamera.fieldOfView;
            originalOrthoSize = MainCamera.orthographicSize;
            originalCamX = MainCamera.transform.position.x;
            originalCamZ = MainCamera.transform.position.z;
        }
        if (unityHWND != IntPtr.Zero && GetWindowRect(unityHWND, out RECT r))
        {
            originalWindowRect = r;
            originalRectSet = true;
        }
        avatarAnimatorController = GetComponent<AvatarAnimatorController>();
    }

    public void SetAnimator(Animator a)
    {
        avatarAnimator = a;
    }

    void Update()
    {
        foreach (var key in ToggleKeys)
        {
            if (Input.GetKeyDown(key))
            {
                if (!isBigScreenActive && !isFading)
                    ActivateBigScreen();
                else if (isBigScreenActive && !isFading)
                    DeactivateBigScreen();
                break;
            }
        }

        if (isBigScreenActive && MainCamera != null && bone != null && avatarAnimator != null && !isFading)
        {
            Vector3 avatarScale = avatarAnimator.transform.lossyScale;
            float scaleFactor = avatarScale.y;

            Vector3 headPos = bone.position;
            float headHeight = 0.25f;
            var neck = avatarAnimator.GetBoneTransform(HumanBodyBones.Neck);
            if (neck != null)
                headHeight = Mathf.Max(0.12f, Mathf.Abs(headPos.y - neck.position.y));
            headHeight *= scaleFactor;

            float buffer = 1.4f;
            float yOffsetScaled = YOffset * scaleFactor;
            Vector3 camPos = MainCamera.transform.position;
            camPos.x = originalCamX;
            camPos.z = originalCamZ;
            camPos.y = headPos.y + yOffsetScaled;
            MainCamera.transform.position = camPos;
            MainCamera.transform.rotation = Quaternion.identity;

            if (TargetZoom > 0f)
            {
                if (MainCamera.orthographic)
                    MainCamera.orthographicSize = TargetZoom * scaleFactor;
                else
                    MainCamera.fieldOfView = TargetZoom;
            }
            else
            {
                if (MainCamera.orthographic)
                    MainCamera.orthographicSize = headHeight * buffer;
                else
                {
                    float dist = Mathf.Abs(MainCamera.transform.position.z - headPos.z);
                    MainCamera.fieldOfView = Mathf.Clamp(
                        2f * Mathf.Atan((headHeight * buffer) / (2f * dist)) * Mathf.Rad2Deg,
                        10f, 60f);
                }
            }
        }
    }

    void ActivateBigScreen()
    {
        isBigScreenActive = true;
        if (avatarAnimator != null)
            avatarAnimator.SetBool("isBigScreen", true);
        if (avatarAnimatorController != null)
            avatarAnimatorController.BlockDraggingOverride = true;

        if (moveCanvas != null)
        {
            moveCanvasWasActive = moveCanvas.activeSelf;
            if (moveCanvas.activeSelf)
                moveCanvas.SetActive(false);
        }

        if (unityHWND != IntPtr.Zero)
        {
            RECT windowRect;
            if (GetWindowRect(unityHWND, out windowRect))
            {
                RECT targetScreen = FindBestMonitorRect(windowRect);
                int screenWidth = targetScreen.right - targetScreen.left;
                int screenHeight = targetScreen.bottom - targetScreen.top;
                int targetX = targetScreen.left;
                int targetY = targetScreen.top;
                originalWindowRect = windowRect;
                originalRectSet = true;
                MoveWindow(unityHWND, targetX, targetY, screenWidth, screenHeight, true);
            }
        }
        if (avatarAnimator != null)
            bone = avatarAnimator.GetBoneTransform(attachBone);
        else
            bone = null;

        if (MainCamera != null)
        {
            originalCamPos = MainCamera.transform.position;
            originalCamRot = MainCamera.transform.rotation;
            originalFOV = MainCamera.fieldOfView;
            originalOrthoSize = MainCamera.orthographicSize;
            originalCamX = MainCamera.transform.position.x;
            originalCamZ = MainCamera.transform.position.z;
        }

        if (fadeCoroutine != null)
            StopCoroutine(fadeCoroutine);
        fadeCoroutine = StartCoroutine(FadeCameraY(true));
    }

    void DeactivateBigScreen()
    {
        if (fadeCoroutine != null)
            StopCoroutine(fadeCoroutine);
        fadeCoroutine = StartCoroutine(FadeCameraY(false));
    }

    IEnumerator FadeCameraY(bool fadeIn)
    {
        isFading = true;

        if (avatarAnimator == null || bone == null || MainCamera == null)
        {
            isFading = false;
            yield break;
        }

        Vector3 avatarScale = avatarAnimator.transform.lossyScale;
        float scaleFactor = avatarScale.y;
        Vector3 headPos = bone.position;
        float baseY = headPos.y + YOffset * scaleFactor;
        float fadeY = baseY + FadeYOffset;

        Vector3 camPos = MainCamera.transform.position;
        float camX = originalCamX;
        float camZ = originalCamZ;

        float fromY = fadeIn ? fadeY : baseY;
        float toY = fadeIn ? baseY : fadeY;

        float duration = fadeIn ? FadeInDuration : FadeOutDuration;
        float time = 0f;

        float headHeight = 0.25f;
        var neck = avatarAnimator.GetBoneTransform(HumanBodyBones.Neck);
        if (neck != null)
            headHeight = Mathf.Max(0.12f, Mathf.Abs(headPos.y - neck.position.y));
        headHeight *= scaleFactor;

        float buffer = 1.4f;

        while (time < duration)
        {
            float t = time / duration;
            float curve = Mathf.SmoothStep(0, 1, t);
            camPos.x = camX;
            camPos.z = camZ;
            camPos.y = Mathf.Lerp(fromY, toY, curve);
            MainCamera.transform.position = camPos;
            MainCamera.transform.rotation = Quaternion.identity;

            if (TargetZoom > 0f)
            {
                if (MainCamera.orthographic)
                    MainCamera.orthographicSize = TargetZoom * scaleFactor;
                else
                    MainCamera.fieldOfView = TargetZoom;
            }
            else
            {
                if (MainCamera.orthographic)
                    MainCamera.orthographicSize = headHeight * buffer;
                else
                {
                    float dist = Mathf.Abs(MainCamera.transform.position.z - headPos.z);
                    MainCamera.fieldOfView = Mathf.Clamp(
                        2f * Mathf.Atan((headHeight * buffer) / (2f * dist)) * Mathf.Rad2Deg,
                        10f, 60f);
                }
            }
            time += Time.deltaTime;
            yield return null;
        }

        camPos.x = camX;
        camPos.z = camZ;
        camPos.y = toY;
        MainCamera.transform.position = camPos;
        MainCamera.transform.rotation = Quaternion.identity;

        if (TargetZoom > 0f)
        {
            if (MainCamera.orthographic)
                MainCamera.orthographicSize = TargetZoom * scaleFactor;
            else
                MainCamera.fieldOfView = TargetZoom;
        }
        else
        {
            if (MainCamera.orthographic)
                MainCamera.orthographicSize = headHeight * buffer;
            else
            {
                float dist = Mathf.Abs(MainCamera.transform.position.z - headPos.z);
                MainCamera.fieldOfView = Mathf.Clamp(
                    2f * Mathf.Atan((headHeight * buffer) / (2f * dist)) * Mathf.Rad2Deg,
                    10f, 60f);
            }
        }

        isFading = false;

        if (!fadeIn)
        {
            isBigScreenActive = false;
            if (avatarAnimator != null)
                avatarAnimator.SetBool("isBigScreen", false);
            if (avatarAnimatorController != null)
                avatarAnimatorController.BlockDraggingOverride = false;

            if (moveCanvas != null && moveCanvasWasActive)
                moveCanvas.SetActive(true);

            if (unityHWND != IntPtr.Zero && originalRectSet)
            {
                int w = originalWindowRect.right - originalWindowRect.left;
                int h = originalWindowRect.bottom - originalWindowRect.top;
                MoveWindow(unityHWND, originalWindowRect.left, originalWindowRect.top, w, h, true);
            }
            if (MainCamera != null)
            {
                MainCamera.transform.position = originalCamPos;
                MainCamera.transform.rotation = originalCamRot;
                MainCamera.fieldOfView = originalFOV;
                MainCamera.orthographicSize = originalOrthoSize;
            }
        }
    }

    RECT FindBestMonitorRect(RECT windowRect)
    {
        List<RECT> monitorRects = new List<RECT>();
        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr hMonitor, IntPtr hdc, ref RECT lprcMonitor, IntPtr data) =>
        {
            monitorRects.Add(lprcMonitor);
            return true;
        }, IntPtr.Zero);

        int idx = 0, maxArea = 0;
        for (int i = 0; i < monitorRects.Count; i++)
        {
            int overlap = OverlapArea(windowRect, monitorRects[i]);
            if (overlap > maxArea)
            {
                idx = i;
                maxArea = overlap;
            }
        }
        return monitorRects.Count > 0 ? monitorRects[idx] : new RECT { left = 0, top = 0, right = Screen.currentResolution.width, bottom = Screen.currentResolution.height };
    }

    int OverlapArea(RECT a, RECT b)
    {
        int x1 = Math.Max(a.left, b.left);
        int x2 = Math.Min(a.right, b.right);
        int y1 = Math.Max(a.top, b.top);
        int y2 = Math.Min(a.bottom, b.bottom);
        int w = x2 - x1;
        int h = y2 - y1;
        return (w > 0 && h > 0) ? w * h : 0;
    }
}


/*

using UnityEngine;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;

public class AvatarBigScreenHandler : MonoBehaviour
{
    [Header("Keybinds")]
    public List<KeyCode> ToggleKeys = new List<KeyCode> { KeyCode.B };

    [Header("Animator & Bone Selection")]
    public Animator avatarAnimator;
    public HumanBodyBones attachBone = HumanBodyBones.Head;

    [Header("Camera")]
    public Camera MainCamera;
    [Tooltip("Override für Zoom: Camera FOV (Perspective) oder Size (Orthographic). 0 = automatisch.")]
    public float TargetZoom = 0f;
    public float ZoomMoveSpeed = 10f;
    [Tooltip("Y-Offset zur Bone-Position (in Metern, vor Skalierung)")]
    public float YOffset = 0.08f;

    [Header("Canvas-Blocking")]
    public GameObject moveCanvas; // Zieh hier dein Canvas rein

    private IntPtr unityHWND = IntPtr.Zero;
    private bool isBigScreenActive = false;
    private Vector3 originalCamPos;
    private Quaternion originalCamRot;
    private float originalFOV;
    private float originalOrthoSize;
    private float originalCamX, originalCamZ;
    private RECT originalWindowRect;
    private bool originalRectSet = false;
    private Transform bone;
    private AvatarAnimatorController avatarAnimatorController;

    // Für sauberes Canvas-Restore
    private bool moveCanvasWasActive = false;

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int left, top, right, bottom; }

    private delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdcMonitor, ref RECT lprcMonitor, IntPtr dwData);

    [DllImport("user32.dll")]
    private static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr lprcClip, MonitorEnumProc lpfnEnum, IntPtr dwData);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    private static extern bool MoveWindow(IntPtr hWnd, int X, int Y, int nWidth, int nHeight, bool bRepaint);


    public static List<AvatarBigScreenHandler> ActiveHandlers = new List<AvatarBigScreenHandler>();

    void OnEnable()
    {
        if (!ActiveHandlers.Contains(this))
            ActiveHandlers.Add(this);
    }
    void OnDisable()
    {
        ActiveHandlers.Remove(this);
    }
    public void ToggleBigScreenFromUI()
    {
        var isActiveField = GetType().GetField("isBigScreenActive", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        bool isActive = isActiveField != null && (bool)isActiveField.GetValue(this);

        if (!isActive)
            SendMessage("ActivateBigScreen");
        else
            SendMessage("DeactivateBigScreen");
    }



    void Start()
    {
        unityHWND = Process.GetCurrentProcess().MainWindowHandle;
        if (MainCamera == null) MainCamera = Camera.main;
        if (avatarAnimator == null) avatarAnimator = GetComponent<Animator>();
        if (MainCamera != null)
        {
            originalCamPos = MainCamera.transform.position;
            originalCamRot = MainCamera.transform.rotation;
            originalFOV = MainCamera.fieldOfView;
            originalOrthoSize = MainCamera.orthographicSize;
            originalCamX = MainCamera.transform.position.x;
            originalCamZ = MainCamera.transform.position.z;
        }
        if (unityHWND != IntPtr.Zero && GetWindowRect(unityHWND, out RECT r))
        {
            originalWindowRect = r;
            originalRectSet = true;
        }
        avatarAnimatorController = GetComponent<AvatarAnimatorController>();
    }

    public void SetAnimator(Animator a)
    {
        avatarAnimator = a;
    }

    void Update()
    {
        foreach (var key in ToggleKeys)
        {
            if (Input.GetKeyDown(key))
            {
                if (!isBigScreenActive)
                    ActivateBigScreen();
                else
                    DeactivateBigScreen();
                break;
            }
        }

        if (isBigScreenActive && MainCamera != null && bone != null && avatarAnimator != null)
        {
            Vector3 avatarScale = avatarAnimator.transform.lossyScale;
            float scaleFactor = avatarScale.y;

            Vector3 headPos = bone.position;
            float headHeight = 0.25f;
            var neck = avatarAnimator.GetBoneTransform(HumanBodyBones.Neck);
            if (neck != null)
                headHeight = Mathf.Max(0.12f, Mathf.Abs(headPos.y - neck.position.y));
            headHeight *= scaleFactor;

            float buffer = 1.4f;
            float yOffsetScaled = YOffset * scaleFactor;
            Vector3 camPos = MainCamera.transform.position;
            camPos.x = originalCamX;
            camPos.z = originalCamZ;
            camPos.y = headPos.y + yOffsetScaled;
            MainCamera.transform.position = camPos;
            MainCamera.transform.rotation = Quaternion.identity;

            if (TargetZoom > 0f)
            {
                if (MainCamera.orthographic)
                    MainCamera.orthographicSize = TargetZoom * scaleFactor;
                else
                    MainCamera.fieldOfView = TargetZoom;
            }
            else
            {
                if (MainCamera.orthographic)
                    MainCamera.orthographicSize = headHeight * buffer;
                else
                {
                    float dist = Mathf.Abs(MainCamera.transform.position.z - headPos.z);
                    MainCamera.fieldOfView = Mathf.Clamp(
                        2f * Mathf.Atan((headHeight * buffer) / (2f * dist)) * Mathf.Rad2Deg,
                        10f, 60f);
                }
            }
        }
    }

    void ActivateBigScreen()
    {
        isBigScreenActive = true;
        if (avatarAnimator != null)
            avatarAnimator.SetBool("isBigScreen", true);
        if (avatarAnimatorController != null)
            avatarAnimatorController.BlockDraggingOverride = true;

        // Canvas nur hier deaktivieren, und merken ob es aktiv war!
        if (moveCanvas != null)
        {
            moveCanvasWasActive = moveCanvas.activeSelf;
            if (moveCanvas.activeSelf)
                moveCanvas.SetActive(false);
        }

        if (unityHWND != IntPtr.Zero)
        {
            RECT windowRect;
            if (GetWindowRect(unityHWND, out windowRect))
            {
                RECT targetScreen = FindBestMonitorRect(windowRect);
                int screenWidth = targetScreen.right - targetScreen.left;
                int screenHeight = targetScreen.bottom - targetScreen.top;
                int targetX = targetScreen.left;
                int targetY = targetScreen.top;
                originalWindowRect = windowRect;
                originalRectSet = true;
                MoveWindow(unityHWND, targetX, targetY, screenWidth, screenHeight, true);
            }
        }
        if (avatarAnimator != null)
            bone = avatarAnimator.GetBoneTransform(attachBone);
        else
            bone = null;

        if (MainCamera != null)
        {
            originalCamPos = MainCamera.transform.position;
            originalCamRot = MainCamera.transform.rotation;
            originalFOV = MainCamera.fieldOfView;
            originalOrthoSize = MainCamera.orthographicSize;
            originalCamX = MainCamera.transform.position.x;
            originalCamZ = MainCamera.transform.position.z;
        }
    }

    void DeactivateBigScreen()
    {
        isBigScreenActive = false;
        if (avatarAnimator != null)
            avatarAnimator.SetBool("isBigScreen", false);
        if (avatarAnimatorController != null)
            avatarAnimatorController.BlockDraggingOverride = false;

        // Canvas nur hier wieder aktivieren, wenn wir es vorher deaktiviert haben!
        if (moveCanvas != null && moveCanvasWasActive)
            moveCanvas.SetActive(true);

        if (unityHWND != IntPtr.Zero && originalRectSet)
        {
            int w = originalWindowRect.right - originalWindowRect.left;
            int h = originalWindowRect.bottom - originalWindowRect.top;
            MoveWindow(unityHWND, originalWindowRect.left, originalWindowRect.top, w, h, true);
        }
        if (MainCamera != null)
        {
            MainCamera.transform.position = originalCamPos;
            MainCamera.transform.rotation = originalCamRot;
            MainCamera.fieldOfView = originalFOV;
            MainCamera.orthographicSize = originalOrthoSize;
        }
    }

    RECT FindBestMonitorRect(RECT windowRect)
    {
        List<RECT> monitorRects = new List<RECT>();
        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr hMonitor, IntPtr hdc, ref RECT lprcMonitor, IntPtr data) =>
        {
            monitorRects.Add(lprcMonitor);
            return true;
        }, IntPtr.Zero);

        int idx = 0, maxArea = 0;
        for (int i = 0; i < monitorRects.Count; i++)
        {
            int overlap = OverlapArea(windowRect, monitorRects[i]);
            if (overlap > maxArea)
            {
                idx = i;
                maxArea = overlap;
            }
        }
        return monitorRects.Count > 0 ? monitorRects[idx] : new RECT { left = 0, top = 0, right = Screen.currentResolution.width, bottom = Screen.currentResolution.height };
    }

    int OverlapArea(RECT a, RECT b)
    {
        int x1 = Math.Max(a.left, b.left);
        int x2 = Math.Min(a.right, b.right);
        int y1 = Math.Max(a.top, b.top);
        int y2 = Math.Min(a.bottom, b.bottom);
        int w = x2 - x1;
        int h = y2 - y1;
        return (w > 0 && h > 0) ? w * h : 0;
    }
}
*/