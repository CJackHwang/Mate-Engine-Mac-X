using UnityEngine;

public class AvatarControllerHeadTracking : MonoBehaviour
{
    [Header("Head Mouse Tracking Settings")]
    public bool enableHeadTracking = true;

    [SerializeField] private Transform headBone;
    [SerializeField, Range(0f, 90f)] private float yawLimit = 45f;
    [SerializeField, Range(0f, 90f)] private float pitchLimit = 30f;
    [SerializeField, Range(1f, 20f)] private float smoothness = 10f;

    private Animator animator;
    private Quaternion lastRotation;

    void Start()
    {
        animator = GetComponent<Animator>();

        // 🔥 **Rename the head bone first before doing anything else!**
        RenameHeadBone();

        // ✅ Now apply head detection & tracking
        FindAndAssignHeadBone();
    }

    void LateUpdate() // Override animation AFTER Animator updates
    {
        if (headBone == null)
        {
            FindAndAssignHeadBone();
        }

        if (headBone == null || !enableHeadTracking) return;

        // Stop Animator from overriding the head bone
        // animator.SetBoneLocalRotation(HumanBodyBones.Head, lastRotation); // Disabled to prevent CPU HEAPS. No Need for Injection Models either.

        UpdateHeadTracking();
    }

    private void UpdateHeadTracking()
    {
        Vector3 mousePos = Input.mousePosition;
        Vector3 viewportPos = new Vector3(
            Mathf.Clamp(mousePos.x / Screen.width, 0f, 1f),
            Mathf.Clamp(mousePos.y / Screen.height, 0f, 1f),
            0f
        );

        Vector3 worldMousePos = Camera.main.ViewportToWorldPoint(new Vector3(viewportPos.x, viewportPos.y, Camera.main.nearClipPlane));
        Vector3 targetDirection = worldMousePos - headBone.position;
        targetDirection.Normalize();

        Vector3 localDirection = headBone.parent.InverseTransformDirection(targetDirection);

        float yaw = Mathf.Atan2(localDirection.x, localDirection.z) * Mathf.Rad2Deg;
        yaw = Mathf.Clamp(yaw, -yawLimit, yawLimit);

        float pitch = Mathf.Asin(localDirection.y) * Mathf.Rad2Deg;
        pitch = Mathf.Clamp(pitch, -pitchLimit, pitchLimit);

        Quaternion targetRotation = Quaternion.Euler(-pitch, yaw, 0f);

        // Smooth transition and store the last applied rotation
        lastRotation = Quaternion.Slerp(headBone.localRotation, targetRotation, Time.deltaTime * smoothness);
        headBone.localRotation = lastRotation;
    }

    private void FindAndAssignHeadBone()
    {
        headBone = FindHeadBone();

        if (headBone != null)
        {
            Debug.Log($"✅ Head bone assigned: {headBone.name}");
        }
        else
        {
            Debug.LogWarning("⚠️ No head bone detected! Head tracking disabled.");
            enableHeadTracking = false;
        }
    }

    private Transform FindHeadBone()
    {
        if (animator == null || !animator.isHuman)
        {
            return null;
        }

        // ✅ Always use Unity Humanoid system to find the head correctly
        Transform detectedHead = animator.GetBoneTransform(HumanBodyBones.Head);
        if (detectedHead != null)
        {
            return detectedHead;
        }

        // 🔎 If automatic detection fails, scan the hierarchy for a head bone
        Transform root = animator.transform;
        foreach (Transform child in root.GetComponentsInChildren<Transform>())
        {
            if (child.name.ToLower().Contains("head")) // Works regardless of "HEAD" or "Head"
            {
                return child;
            }
        }

        return null; // No head found
    }

    private void RenameHeadBone()
    {
        Transform detectedHead = FindHeadBone();

        if (detectedHead != null)
        {
            if (detectedHead.name != "HEAD")
            {
                Debug.Log($"🔄 Renaming head bone from {detectedHead.name} to HEAD...");
                detectedHead.name = "HEAD"; // ✅ Rename the head bone first! Side Note; We may need to Implement an Revert for Head to HEAD. because VRM Doesnt support "HEAD" Value as ROOT BONE
                Debug.Log("✅ Head bone renamed successfully!");
            }
        }
        else
        {
            Debug.LogWarning("⚠️ No head bone found to rename!");
        }
    }
}
