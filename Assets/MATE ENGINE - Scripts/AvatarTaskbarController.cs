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

    [Header("Attach Settings")]
    public GameObject attachTarget;
    public HumanBodyBones attachBone = HumanBodyBones.Head;
    [Tooltip("If true, the object will follow the bone's position but keep its own rotation.")]
    public bool keepOriginalRotation = false;

    [Header("Debug")]
    public bool showDebugGizmo = true;
    public Color taskbarGizmoColor = Color.green;
    public Color detectionGizmoColor = Color.yellow;

    [Header("Spawn / Despawn Animation")]
    [Tooltip("Time it takes to grow from 0 to full scale.")]
    public float spawnScaleTime = 0.2f;

    [Tooltip("Time it takes to shrink from full scale to 0.")]
    public float despawnScaleTime = 0.2f;

    private Vector2Int unityWindowPosition;
    private Rect taskbarScreenRect;
    private Vector3 taskbarWorldPosition;
    private Vector2 taskbarSize;

    private static readonly int IsSitting = Animator.StringToHash("isSitting");

    private Vector3 originalScale = Vector3.one;
    private float scaleLerpT = 0f;
    private bool isScaling = false;
    private bool scalingUp = false;

    private bool wasSittingProximity = false;
    private bool wasSittingAnimator = false;
    private Transform attachBoneTransform;
    private Transform originalAttachParent;

    void Start()
    {
        if (avatarAnimator == null)
            avatarAnimator = GetComponentInChildren<Animator>();

        if (attachTarget != null)
        {
            originalScale = attachTarget.transform.localScale;
            originalAttachParent = attachTarget.transform.parent;
            attachTarget.SetActive(false);
        }

        UpdateTaskbarRect();
    }

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

        Transform bone = avatarAnimator.GetBoneTransform(detectionBone);
        if (bone == null) return;

        bool shouldSit = wasSittingProximity;

        if (Application.isFocused && Screen.width > 0 && Screen.height > 0)
        {
            UpdateUnityWindowPosition();
            UpdateTaskbarWorldPosition();

            Vector3 closestPoint = GetClosestPointOnRect(taskbarWorldPosition, taskbarSize, bone.position);
            float distance = Vector3.Distance(bone.position, closestPoint);

            shouldSit = distance <= detectionRadius;
            wasSittingProximity = shouldSit;
        }

        avatarAnimator.SetBool(IsSitting, shouldSit);
        bool animatorSitting = avatarAnimator.GetBool(IsSitting);
        AnimatorStateInfo currentState = avatarAnimator.GetCurrentAnimatorStateInfo(0);
        bool isInSittingState = currentState.IsName("Sitting");
        bool allowSpawn = animatorSitting && wasSittingAnimator && isInSittingState;


        if (attachTarget != null)
        {
            if (avatarAnimator != null && attachBoneTransform == null)
                attachBoneTransform = avatarAnimator.GetBoneTransform(attachBone);

            if (allowSpawn)
            {
                if (keepOriginalRotation)
                {
                    if (!attachTarget.activeSelf)
                    {
                        attachTarget.SetActive(true);
                        attachTarget.transform.localScale = Vector3.zero;
                        scaleLerpT = 0f;
                        scalingUp = true;
                        isScaling = true;
                    }

                    if (attachBoneTransform != null)
                        attachTarget.transform.position = attachBoneTransform.position;
                }
                else
                {
                    if (attachTarget.transform.parent != attachBoneTransform && attachBoneTransform != null)
                        attachTarget.transform.SetParent(attachBoneTransform, false);

                    if (!attachTarget.activeSelf)
                    {
                        attachTarget.SetActive(true);
                        attachTarget.transform.localScale = Vector3.zero;
                        scaleLerpT = 0f;
                        scalingUp = true;
                        isScaling = true;
                    }
                }
            }
            else
            {
                if (!keepOriginalRotation && attachTarget.transform.parent != originalAttachParent)
                    attachTarget.transform.SetParent(originalAttachParent, false);

                if (attachTarget.activeSelf && !isScaling)
                {
                    scalingUp = false;
                    isScaling = true;
                    scaleLerpT = 0f;
                }
            }

            // Animate scale
            if (isScaling && attachTarget.activeSelf)
            {
                float duration = scalingUp ? spawnScaleTime : despawnScaleTime;
                scaleLerpT += Time.deltaTime / Mathf.Max(duration, 0.0001f);
                float t = Mathf.Clamp01(scaleLerpT);
                Vector3 from = scalingUp ? Vector3.zero : originalScale;
                Vector3 to = scalingUp ? originalScale : Vector3.zero;

                attachTarget.transform.localScale = Vector3.Lerp(from, to, t);

                if (t >= 1f)
                {
                    isScaling = false;

                    if (!scalingUp)
                    {
                        attachTarget.SetActive(false);
                        attachTarget.transform.localScale = originalScale;
                    }
                }
            }

            // Always update bone-following position even when unfocused
            if (attachTarget.activeSelf && keepOriginalRotation && attachBoneTransform != null)
                attachTarget.transform.position = attachBoneTransform.position;
        }

        wasSittingAnimator = animatorSitting;
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
