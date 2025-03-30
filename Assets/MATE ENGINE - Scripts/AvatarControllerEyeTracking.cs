using UnityEngine;

[RequireComponent(typeof(Animator))]
public class AvatarControllerEyeTracking : MonoBehaviour
{
    [Header("Eye Mouse Tracking Settings")]
    [SerializeField] public bool enableEyeTracking = true;

    [SerializeField] private Transform leftEyeBone;
    [SerializeField] private Transform rightEyeBone;
    [SerializeField, Range(0f, 90f)] private float eyeYawLimit = 12f;
    [SerializeField, Range(0f, 90f)] private float eyePitchLimit = 12f;
    [SerializeField, Range(1f, 20f)] private float smoothness = 10f;

    private Transform eyeCenter;
    private Transform leftEyeDriver;
    private Transform rightEyeDriver;

    private Animator animator;
    private Camera mainCam;

    void Start()
    {
        animator = GetComponent<Animator>();
        mainCam = Camera.main;

        if (animator != null && animator.isHuman)
        {
            (leftEyeBone, rightEyeBone) = FindEyeBone();
            if (leftEyeBone != null && rightEyeBone != null)
            {
                // Create a virtual center point between the eyes
                GameObject centerObj = new GameObject("EyeCenter");
                centerObj.transform.parent = leftEyeBone.parent;
                eyeCenter = centerObj.transform;
                eyeCenter.position = (leftEyeBone.position + rightEyeBone.position) / 2f;

                // Create driver transforms to control the eye rotation independently
                leftEyeDriver = new GameObject("LeftEyeDriver").transform;
                leftEyeDriver.SetParent(leftEyeBone.parent);
                leftEyeDriver.localPosition = leftEyeBone.localPosition;
                leftEyeDriver.localRotation = leftEyeBone.localRotation;

                rightEyeDriver = new GameObject("RightEyeDriver").transform;
                rightEyeDriver.SetParent(rightEyeBone.parent);
                rightEyeDriver.localPosition = rightEyeBone.localPosition;
                rightEyeDriver.localRotation = rightEyeBone.localRotation;
            }
        }

        if (leftEyeBone == null || rightEyeBone == null)
        {
            enableEyeTracking = false;
        }
    }

    void LateUpdate()
    {
        if (!enableEyeTracking || leftEyeBone == null || rightEyeBone == null || eyeCenter == null)
            return;

        // Update the center point between both eyes
        eyeCenter.position = (leftEyeBone.position + rightEyeBone.position) / 2f;

        Vector3 mousePos = Input.mousePosition;
        Vector3 worldMousePos = mainCam.ViewportToWorldPoint(new Vector3(
            Mathf.Clamp01(mousePos.x / Screen.width),
            Mathf.Clamp01(mousePos.y / Screen.height),
            mainCam.nearClipPlane));

        Vector3 targetDirection = (worldMousePos - eyeCenter.position).normalized;
        Vector3 localDirection = eyeCenter.parent.InverseTransformDirection(targetDirection);

        float yaw = Mathf.Clamp(Mathf.Atan2(localDirection.x, localDirection.z) * Mathf.Rad2Deg, -eyeYawLimit, eyeYawLimit);
        float pitch = Mathf.Clamp(Mathf.Asin(localDirection.y) * Mathf.Rad2Deg, -eyePitchLimit, eyePitchLimit);

        Quaternion targetRotation = Quaternion.Euler(-pitch, yaw, 0f);

        // Smoothly rotate the driver transforms toward the target
        leftEyeDriver.localRotation = Quaternion.Slerp(leftEyeDriver.localRotation, targetRotation, Time.deltaTime * smoothness);
        rightEyeDriver.localRotation = Quaternion.Slerp(rightEyeDriver.localRotation, targetRotation, Time.deltaTime * smoothness);

        // Apply driver rotations to the real eye bones
        leftEyeBone.localRotation = leftEyeDriver.localRotation;
        rightEyeBone.localRotation = rightEyeDriver.localRotation;
    }

    private (Transform leftEye, Transform rightEye) FindEyeBone()
    {
        if (animator == null || !animator.isHuman)
            return (null, null);

        Transform detectedLeft = animator.GetBoneTransform(HumanBodyBones.LeftEye);
        Transform detectedRight = animator.GetBoneTransform(HumanBodyBones.RightEye);

        if (detectedLeft != null || detectedRight != null)
            return (detectedLeft, detectedRight);

        // Fallback: scan the hierarchy by name if humanoid mapping fails
        foreach (Transform t in animator.GetComponentsInChildren<Transform>())
        {
            string name = t.name.ToLower();
            if (name.Contains("lefteye") || name.Contains("eye.l") || (name.Contains("eye") && name.Contains("l")))
                detectedLeft = t;
            else if (name.Contains("righteye") || name.Contains("eye.r") || (name.Contains("eye") && name.Contains("r")))
                detectedRight = t;

            if (detectedLeft != null && detectedRight != null)
                break;
        }

        return (detectedLeft, detectedRight);
    }

    void OnDestroy()
    {
        if (eyeCenter != null) Destroy(eyeCenter.gameObject);
        if (leftEyeDriver != null) Destroy(leftEyeDriver.gameObject);
        if (rightEyeDriver != null) Destroy(rightEyeDriver.gameObject);
    }
}
