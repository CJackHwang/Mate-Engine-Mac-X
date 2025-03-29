using UnityEngine;

[RequireComponent(typeof(Animator))]
public class AvatarControllerHeadTracking : MonoBehaviour
{
    [Header("Head Mouse Tracking Settings")]
    public bool enableHeadTracking = true;

    [SerializeField] private Transform headBone;
    [SerializeField, Range(0f, 90f)] private float yawLimit = 45f;
    [SerializeField, Range(0f, 90f)] private float pitchLimit = 30f;
    [SerializeField, Range(1f, 20f)] private float smoothness = 10f;

    private Animator animator;
    private Transform headDriver;
    private Camera mainCam;

    void Start()
    {
        animator = GetComponent<Animator>();
        mainCam = Camera.main;

        if (animator != null && animator.isHuman)
        {
            headBone = animator.GetBoneTransform(HumanBodyBones.Head);
            if (headBone != null)
            {
                // Create head driver (proxy)
                headDriver = new GameObject("HeadDriver").transform;
                headDriver.SetParent(headBone.parent);
                headDriver.localPosition = headBone.localPosition;
                headDriver.localRotation = headBone.localRotation;
            }
        }

        if (headBone == null)
        {
            Debug.LogWarning("⚠️ No head bone found for head tracking.");
            enableHeadTracking = false;
        }
    }

    void LateUpdate()
    {
        if (!enableHeadTracking || headBone == null || headDriver == null)
            return;

        Vector3 mousePos = Input.mousePosition;
        Vector3 worldMousePos = mainCam.ScreenToWorldPoint(new Vector3(mousePos.x, mousePos.y, mainCam.nearClipPlane));

        Vector3 targetDirection = worldMousePos - headDriver.position;
        targetDirection.Normalize();

        Vector3 localDirection = headDriver.parent.InverseTransformDirection(targetDirection);

        float yaw = Mathf.Atan2(localDirection.x, localDirection.z) * Mathf.Rad2Deg;
        yaw = Mathf.Clamp(yaw, -yawLimit, yawLimit);

        float pitch = Mathf.Asin(localDirection.y) * Mathf.Rad2Deg;
        pitch = Mathf.Clamp(pitch, -pitchLimit, pitchLimit);

        Quaternion targetRotation = Quaternion.Euler(-pitch, yaw, 0f);
        headDriver.localRotation = Quaternion.Slerp(headDriver.localRotation, targetRotation, Time.deltaTime * smoothness);

        // Apply the result to the head bone
        headBone.localRotation = headDriver.localRotation;
    }

    void OnDestroy()
    {
        if (headDriver != null)
            Destroy(headDriver.gameObject);
    }
}
