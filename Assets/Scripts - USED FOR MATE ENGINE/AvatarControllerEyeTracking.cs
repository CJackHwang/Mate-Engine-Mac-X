using UnityEngine;

public class AvatarControllerEyeTracking : MonoBehaviour
{
    [Header("Eye Mouse Tracking Settings")]
    [SerializeField]public bool enableEyeTracking = true;

    [SerializeField] private Transform leftEyeBone;
    [SerializeField] private Transform rightEyeBone;
    [SerializeField, Range(0f, 90f)] private float eyeYawLimit = 45f;
    [SerializeField, Range(0f, 90f)] private float eyePitchLimit = 30f;
    [SerializeField, Range(1f, 20f)] private float smoothness = 10f;
    private Transform eyeCenter;

    GameObject eyeCenterObj;

    private Animator animator;
    private Quaternion lastRotation;

    void Start()
    {
        animator = GetComponent<Animator>();

        // 🔥 **Rename the head bone first before doing anything else!**
        RenameEyeBone();

        // ✅ Now apply head detection & tracking
        FindAndAssignEyeBone();



    }

    void LateUpdate() // Override animation AFTER Animator updates
    {
        if (leftEyeBone == null || rightEyeBone == null)
        {
            // FindAndAssignEyeBone();
            (leftEyeBone, rightEyeBone) = FindEyeBone();
        }

        if (leftEyeBone == null || rightEyeBone == null || !enableEyeTracking) return;

        // Stop Animator from overriding the head bone
        // animator.SetBoneLocalRotation(HumanBodyBones.Head, lastRotation); // Disabled to prevent CPU HEAPS. No Need for Injection Models either.
        eyeCenterObj = new GameObject("EyeCenter");
        eyeCenterObj.transform.position = (leftEyeBone.position + rightEyeBone.position) / 2f;
        eyeCenterObj.transform.parent = leftEyeBone.parent; // Lo agrupamos con el resto del esqueleto
        eyeCenter = eyeCenterObj.transform;
        UpdateEyeTracking();
    }

    private void UpdateEyeTracking()
    {

        Vector3 mousePos = Input.mousePosition;
        Vector3 viewportPos = new Vector3(
            Mathf.Clamp(mousePos.x / Screen.width, 0f, 1f),
            Mathf.Clamp(mousePos.y / Screen.height, 0f, 1f),
            0f
        );

        Vector3 worldMousePos = Camera.main.ViewportToWorldPoint(new Vector3(viewportPos.x, viewportPos.y, Camera.main.nearClipPlane));
        Vector3 targetDirection = worldMousePos - eyeCenter.position;
        targetDirection.Normalize();

        Vector3 localDirection = eyeCenter.parent.InverseTransformDirection(targetDirection);

        float yaw = Mathf.Atan2(localDirection.x, localDirection.z) * Mathf.Rad2Deg;
        float pitch = Mathf.Asin(localDirection.y) * Mathf.Rad2Deg;
        yaw = Mathf.Clamp(yaw, -eyeYawLimit, eyeYawLimit);
        pitch = Mathf.Clamp(pitch, -eyePitchLimit, eyePitchLimit);

        Quaternion targetRotation = Quaternion.Euler(-pitch, yaw, 0f);
        leftEyeBone.localRotation = Quaternion.Slerp(leftEyeBone.localRotation, targetRotation, Time.deltaTime * smoothness);
        rightEyeBone.localRotation = Quaternion.Slerp(rightEyeBone.localRotation, targetRotation, Time.deltaTime * smoothness);
    }

    private void FindAndAssignEyeBone()
    {
        //(leftEyeBone, rightEyeBone) = FindEyeBone();

        if (leftEyeBone != null || rightEyeBone != null)
        {
            Debug.Log($"✅ Eye bone");
        }
        else
        {
            Debug.LogWarning("⚠️ No Eye bone detected! Head tracking disabled.");
            enableEyeTracking = false;
        }
    }

    private (Transform leftEye, Transform rightEye) FindEyeBone()
    {
        if (animator == null || !animator.isHuman)
        {
            return (null, null);
        }

        // ✅ Always use Unity Humanoid system to find the head correctly
        Transform detectedLeftEyeBone = animator.GetBoneTransform(HumanBodyBones.LeftEye);
        Transform detectedRightEyeBone = animator.GetBoneTransform(HumanBodyBones.RightEye);

        if (detectedLeftEyeBone != null || detectedRightEyeBone != null)
        {
            return (detectedLeftEyeBone, detectedRightEyeBone);
        }

        // 🔎 If automatic detection fails, scan the hierarchy for a head bone
        Transform root = animator.transform;
        foreach (Transform child in root.GetComponentsInChildren<Transform>())
        {
            string lowerName = child.name.ToLower();
            
            if (lowerName.Contains("lefteye") || lowerName.Contains("eye.l") || (lowerName.Contains("eye") && lowerName.Contains("l")))
            {
                detectedLeftEyeBone = child;
            }
            else if (lowerName.Contains("righteye") || lowerName.Contains("eye.r") || (lowerName.Contains("eye") && lowerName.Contains("r")))
            {
                detectedRightEyeBone = child;
            }
            
            // Si encontramos ambos ojos, salimos del bucle
            if (detectedLeftEyeBone != null && detectedRightEyeBone != null)
            {
                break;
            }

        }
        return (detectedLeftEyeBone, detectedRightEyeBone);

    }

    private void RenameEyeBone()
    {
        (leftEyeBone, rightEyeBone) = FindEyeBone();

        if (leftEyeBone != null && rightEyeBone != null)
        {

            Debug.Log($"🔄 Renaming eye bone...");
            leftEyeBone.name = "LEFT_EYE";
            rightEyeBone.name = "RIGHT_EYE";
            Debug.Log("✅ Eyes bone renamed successfully!");

        }
        else
        {
            Debug.LogWarning("⚠️ No eye bone found to rename!");
        }
    }
}
