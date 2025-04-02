using System;
using System.Runtime.InteropServices;
using UnityEngine;

[ExecuteAlways]
public class AvatarTaskbarController : MonoBehaviour
{
    [Header("Animator")]
    public Animator avatarAnimator;

    [Header("Detection Settings")]
    public HumanBodyBones detectionBone = HumanBodyBones.Hips;
    public float detectionRadius = 0.2f;

    [Header("Debug")]
    public bool showDebugGizmo = true;
    public Color taskbarGizmoColor = Color.green;
    public Color detectionGizmoColor = Color.yellow;

    private Vector2Int unityWindowPosition;
    private Rect taskbarScreenRect;
    private Vector3 taskbarWorldPosition;
    private Vector2 taskbarSize;

    private static readonly int IsSitting = Animator.StringToHash("isSitting");

    void Start()
    {
        if (avatarAnimator == null)
            avatarAnimator = GetComponentInChildren<Animator>();

        UpdateTaskbarRect();
    }


    private bool wasSitting = false;

    void Update()
    {
        if (avatarAnimator == null)
        {
            avatarAnimator = GetComponentInChildren<Animator>();
            if (avatarAnimator == null)
                return;
        }

        if (Camera.main == null)
            return;

        if (!Application.isFocused)
        {
            // Maintain previous state while unfocused
            avatarAnimator.SetBool(IsSitting, wasSitting);
            return;
        }

        Transform bone = avatarAnimator.GetBoneTransform(detectionBone);
        if (bone == null) return;

        UpdateUnityWindowPosition();
        UpdateTaskbarWorldPosition();

        Vector3 closestPoint = GetClosestPointOnRect(taskbarWorldPosition, taskbarSize, bone.position);
        float distance = Vector3.Distance(bone.position, closestPoint);

        bool shouldSit = distance <= detectionRadius;
        avatarAnimator.SetBool(IsSitting, shouldSit);
        wasSitting = shouldSit;
    }


    void OnDrawGizmos()
    {
        if (!showDebugGizmo || Camera.main == null || avatarAnimator == null)
            return;

        Transform bone = avatarAnimator.GetBoneTransform(detectionBone);
        if (bone != null)
        {
            Gizmos.color = detectionGizmoColor;
            Gizmos.DrawWireSphere(bone.position, detectionRadius);
        }

        Gizmos.color = taskbarGizmoColor;
        Gizmos.DrawWireCube(taskbarWorldPosition, new Vector3(taskbarSize.x, taskbarSize.y, 0.01f));
    }

    #region Taskbar Detection

    private void UpdateTaskbarRect()
    {
        APPBARDATA data = new APPBARDATA();
        data.cbSize = Marshal.SizeOf(data);
        SHAppBarMessage(ABM_GETTASKBARPOS, ref data);

        taskbarScreenRect = new Rect(
            data.rc.left,
            data.rc.top,
            data.rc.right - data.rc.left,
            data.rc.bottom - data.rc.top
        );
    }

    private void UpdateUnityWindowPosition()
    {
        GetWindowRect(GetActiveWindow(), out RECT rect);
        unityWindowPosition = new Vector2Int(rect.left, rect.top);
    }

    private void UpdateTaskbarWorldPosition()
    {
        Vector2 taskbarScreenCenter = taskbarScreenRect.center;
        Vector2 relativeToWindow = taskbarScreenCenter - unityWindowPosition;

        Vector2 normalized = new Vector2(
            relativeToWindow.x / Screen.width,
            1f - (relativeToWindow.y / Screen.height)
        );

        Vector3 world = Camera.main.ViewportToWorldPoint(new Vector3(normalized.x, normalized.y, Camera.main.nearClipPlane + 1));
        taskbarWorldPosition = new Vector3(world.x, world.y, 0);

        taskbarSize = new Vector2(
            taskbarScreenRect.width * (Camera.main.orthographicSize * 2 / Screen.height),
            taskbarScreenRect.height * (Camera.main.orthographicSize * 2 / Screen.height)
        );
    }

    #endregion

    #region WinAPI

    private const int ABM_GETTASKBARPOS = 0x00000005;

    [StructLayout(LayoutKind.Sequential)]
    private struct APPBARDATA
    {
        public int cbSize;
        public IntPtr hWnd;
        public uint uCallbackMessage;
        public uint uEdge;
        public RECT rc;
        public int lParam;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int left;
        public int top;
        public int right;
        public int bottom;
    }

    [DllImport("shell32.dll", SetLastError = true)]
    private static extern UInt32 SHAppBarMessage(UInt32 dwMessage, ref APPBARDATA pData);

    [DllImport("user32.dll")]
    private static extern IntPtr GetActiveWindow();

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    private Vector3 GetClosestPointOnRect(Vector3 rectCenter, Vector2 size, Vector3 point)
    {
        Vector3 halfSize = new Vector3(size.x / 2, size.y / 2, 0);
        Vector3 local = point - rectCenter;

        local.x = Mathf.Clamp(local.x, -halfSize.x, halfSize.x);
        local.y = Mathf.Clamp(local.y, -halfSize.y, halfSize.y);
        local.z = 0;

        return rectCenter + local;
    }


    #endregion
}
