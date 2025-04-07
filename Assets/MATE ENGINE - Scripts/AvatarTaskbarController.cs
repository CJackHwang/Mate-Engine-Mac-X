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
    public bool keepOriginalRotation = false;

    [Header("Debug")]
    public bool showDebugGizmo = true;
    public Color taskbarGizmoColor = Color.green;
    public Color detectionGizmoColor = Color.yellow;

    [Header("Spawn / Despawn Animation")]
    public float spawnScaleTime = 0.2f;
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
    private Transform detectionBoneTransform;
    private Camera cachedCam;

    private readonly Vector2[] vec2Cache = new Vector2[3];
    private readonly Vector3[] vec3Cache = new Vector3[4];

    void Start()
    {
        avatarAnimator ??= GetComponentInChildren<Animator>();
        cachedCam = Camera.main;

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
            if (avatarAnimator == null) return;
        }

        if (cachedCam == null)
        {
            cachedCam = Camera.main;
            if (cachedCam == null) return;
        }

        detectionBoneTransform ??= avatarAnimator.GetBoneTransform(detectionBone);
        if (detectionBoneTransform == null) return;

        bool shouldSit = wasSittingProximity;

        if (Application.isFocused && Screen.width > 0 && Screen.height > 0)
        {
            UpdateUnityWindowPosition();
            UpdateTaskbarWorldPosition();

            vec3Cache[0] = detectionBoneTransform.position;
            vec3Cache[1] = GetClosestPointOnRect(taskbarWorldPosition, taskbarSize, vec3Cache[0]);

            float sqrDist = (vec3Cache[1] - vec3Cache[0]).sqrMagnitude;
            shouldSit = sqrDist <= detectionRadius * detectionRadius;
            wasSittingProximity = shouldSit;
        }

        avatarAnimator.SetBool(IsSitting, shouldSit);

        bool animatorSitting = avatarAnimator.GetBool(IsSitting);
        bool isInSittingState = avatarAnimator.GetCurrentAnimatorStateInfo(0).IsName("Sitting");
        bool allowSpawn = animatorSitting && wasSittingAnimator && isInSittingState;

        if (attachTarget != null)
        {
            attachBoneTransform ??= avatarAnimator.GetBoneTransform(attachBone);

            if (allowSpawn)
            {
                if (!attachTarget.activeSelf)
                {
                    attachTarget.SetActive(true);
                    attachTarget.transform.localScale = Vector3.zero;
                    scaleLerpT = 0f;
                    scalingUp = true;
                    isScaling = true;
                }

                if (keepOriginalRotation && attachBoneTransform != null)
                    attachTarget.transform.position = attachBoneTransform.position;
                else if (!keepOriginalRotation && attachBoneTransform != null && attachTarget.transform.parent != attachBoneTransform)
                    attachTarget.transform.SetParent(attachBoneTransform, false);
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

            if (attachTarget.activeSelf && keepOriginalRotation && attachBoneTransform != null)
                attachTarget.transform.position = attachBoneTransform.position;
        }

        wasSittingAnimator = animatorSitting;
    }

    void OnDrawGizmos()
    {
        if (!showDebugGizmo || avatarAnimator == null)
            return;

        if (detectionBoneTransform == null)
            detectionBoneTransform = avatarAnimator.GetBoneTransform(detectionBone);

        if (detectionBoneTransform != null)
        {
            Gizmos.color = detectionGizmoColor;
            Gizmos.DrawWireSphere(detectionBoneTransform.position, detectionRadius);
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
        unityWindowPosition.x = rect.left;
        unityWindowPosition.y = rect.top;
    }

    private void UpdateTaskbarWorldPosition()
    {
        vec2Cache[0] = taskbarScreenRect.center;
        vec2Cache[1] = vec2Cache[0] - unityWindowPosition;

        vec2Cache[2].x = vec2Cache[1].x / Screen.width;
        vec2Cache[2].y = 1f - (vec2Cache[1].y / Screen.height);

        Vector3 world = cachedCam.ViewportToWorldPoint(new Vector3(vec2Cache[2].x, vec2Cache[2].y, cachedCam.nearClipPlane + 1));
        taskbarWorldPosition.x = world.x;
        taskbarWorldPosition.y = world.y;
        taskbarWorldPosition.z = 0;

        float screenRatio = cachedCam.orthographicSize * 2f / Screen.height;
        taskbarSize.x = taskbarScreenRect.width * screenRatio;
        taskbarSize.y = taskbarScreenRect.height * screenRatio;
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
        vec3Cache[2].Set(size.x * 0.5f, size.y * 0.5f, 0);
        vec3Cache[3] = point - rectCenter;

        vec3Cache[3].x = Mathf.Clamp(vec3Cache[3].x, -vec3Cache[2].x, vec3Cache[2].x);
        vec3Cache[3].y = Mathf.Clamp(vec3Cache[3].y, -vec3Cache[2].y, vec3Cache[2].y);
        vec3Cache[3].z = 0;

        return rectCenter + vec3Cache[3];
    }

    #endregion
}
