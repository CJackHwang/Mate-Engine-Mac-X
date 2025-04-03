using System;
using System.Collections.Generic;
using UnityEngine;
using UniVRM10;

[Serializable]
public class TrackingPermission
{
    public string stateOrParameterName;
    public bool isParameter = false;

    public bool allowHead = true;
    public bool allowSpine = true;
    public bool allowEye = true;
}

[RequireComponent(typeof(Animator))]
public class AvatarMouseTracking : MonoBehaviour
{
    [Header("Global Mouse Tracking Settings")]
    public bool enableMouseTracking = true;

    [Header("Feature Toggle (Per State/Parameter)")]
    public List<TrackingPermission> trackingPermissions = new List<TrackingPermission>();

    [Header("Head Tracking")]
    [SerializeField, Range(0f, 90f)] public float headYawLimit = 45f;
    [SerializeField, Range(0f, 90f)] public float headPitchLimit = 30f;
    [SerializeField, Range(1f, 20f)] public float headSmoothness = 10f;

    [Header("Spine Tracking")]
    [Range(-90f, 90f)] public float spineMinRotation = -15f;
    [Range(-90f, 90f)] public float spineMaxRotation = 15f;
    [Range(1f, 50f)] public float spineSmoothness = 25f;
    [Range(1f, 10f)] public float spineFadeSpeed = 5f;

    [Header("Eye Tracking")]
    [SerializeField, Range(0f, 90f)] public float eyeYawLimit = 12f;
    [SerializeField, Range(0f, 90f)] public float eyePitchLimit = 12f;
    [SerializeField, Range(1f, 20f)] public float eyeSmoothness = 10f;

    private Animator animator;
    private Camera mainCam;

    private Transform headBone, spineBone, chestBone, upperChestBone;
    private Transform leftEyeBone, rightEyeBone;

    private Transform headDriver, spineDriver;
    private Transform leftEyeDriver, rightEyeDriver, eyeCenter;

    private Quaternion spineDefaultRotation;
    private float spineTrackingWeight = 0f;

    private Vrm10Instance vrm10;
    private Transform vrmLookAtTarget;

    private string currentStateName = "";
    private string nextStateName = "";
    private bool wasInTransitionLastFrame = false;


    void Start()
    {
        animator = GetComponent<Animator>();
        mainCam = Camera.main;

        if (animator == null || !animator.isHuman)
        {
            Debug.LogError("Animator not found or not humanoid!");
            enableMouseTracking = false;
            return;
        }

        vrm10 = GetComponentInChildren<Vrm10Instance>();

        InitializeHeadTracking();
        InitializeSpineTracking();
        InitializeEyeTracking();
    }

    void InitializeHeadTracking()
    {
        headBone = animator.GetBoneTransform(HumanBodyBones.Head);
        if (headBone)
        {
            headDriver = new GameObject("HeadDriver").transform;
            headDriver.SetParent(headBone.parent);
            headDriver.localPosition = headBone.localPosition;
            headDriver.localRotation = headBone.localRotation;
        }
    }

    void InitializeSpineTracking()
    {
        spineBone = animator.GetBoneTransform(HumanBodyBones.Spine);
        chestBone = animator.GetBoneTransform(HumanBodyBones.Chest);
        upperChestBone = animator.GetBoneTransform(HumanBodyBones.UpperChest);

        if (spineBone)
        {
            spineDriver = new GameObject("SpineDriver").transform;
            spineDriver.SetParent(spineBone.parent);
            spineDriver.localPosition = spineBone.localPosition;
            spineDriver.localRotation = spineBone.localRotation;
            spineDefaultRotation = spineBone.localRotation;
        }
    }

    void InitializeEyeTracking()
    {
        leftEyeBone = animator.GetBoneTransform(HumanBodyBones.LeftEye);
        rightEyeBone = animator.GetBoneTransform(HumanBodyBones.RightEye);

        if (vrm10 != null)
        {
            vrmLookAtTarget = new GameObject("VRMLookAtTarget").transform;
            vrmLookAtTarget.SetParent(transform);
            vrm10.LookAtTarget = vrmLookAtTarget;
            vrm10.LookAtTargetType = VRM10ObjectLookAt.LookAtTargetTypes.YawPitchValue;
        }

        if (!leftEyeBone || !rightEyeBone)
        {
            foreach (Transform t in animator.GetComponentsInChildren<Transform>())
            {
                string name = t.name.ToLower();
                if (leftEyeBone == null && (name.Contains("lefteye") || name.Contains("eye.l")))
                    leftEyeBone = t;
                else if (rightEyeBone == null && (name.Contains("righteye") || name.Contains("eye.r")))
                    rightEyeBone = t;
            }
        }

        if (leftEyeBone && rightEyeBone)
        {
            eyeCenter = new GameObject("EyeCenter").transform;
            eyeCenter.SetParent(leftEyeBone.parent);
            eyeCenter.position = (leftEyeBone.position + rightEyeBone.position) / 2f;

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

    void LateUpdate()
    {

        AnimatorStateInfo currentInfo = animator.GetCurrentAnimatorStateInfo(0);
        AnimatorStateInfo nextInfo = animator.GetNextAnimatorStateInfo(0);
        bool isInTransition = animator.IsInTransition(0);

        // Update state names during transition
        if (isInTransition)
        {
            nextStateName = nextInfo.IsName("") ? "" : nextInfo.shortNameHash.ToString();
        }
        else
        {
            currentStateName = currentInfo.shortNameHash.ToString();
            nextStateName = "";
        }

        wasInTransitionLastFrame = isInTransition;

        if (!enableMouseTracking) return;

        if (IsFeatureAllowed("Head")) HandleHeadTracking();
        HandleSpineTracking(); // always runs to allow smooth transitions
        if (IsFeatureAllowed("Eye")) HandleEyeTracking();
    }

    bool IsFeatureAllowed(string feature)
    {
        // Evaluate both current and next state permissions
        bool? currentResult = null;
        bool? nextResult = null;

        foreach (var entry in trackingPermissions)
        {
            if (entry.isParameter)
            {
                if (animator.GetBool(entry.stateOrParameterName))
                {
                    return GetFeature(entry, feature);
                }
            }
            else
            {
                int stateHash = Animator.StringToHash(entry.stateOrParameterName);

                if (animator.GetCurrentAnimatorStateInfo(0).shortNameHash == stateHash)
                    currentResult = GetFeature(entry, feature);

                if (animator.IsInTransition(0) && animator.GetNextAnimatorStateInfo(0).shortNameHash == stateHash)
                    nextResult = GetFeature(entry, feature);
            }
        }

        if (animator.IsInTransition(0))
        {
            // If transitioning to a state that disables the feature, fade out now
            if (nextResult.HasValue)
                return nextResult.Value;
        }

        return currentResult ?? false;
    }


    void HandleHeadTracking()
    {
        if (!headBone || !headDriver) return;

        Vector3 mousePos = Input.mousePosition;
        Vector3 worldMousePos = mainCam.ScreenToWorldPoint(new Vector3(mousePos.x, mousePos.y, mainCam.nearClipPlane));
        Vector3 targetDir = (worldMousePos - headDriver.position).normalized;
        Vector3 localDir = headDriver.parent.InverseTransformDirection(targetDir);

        float yaw = Mathf.Clamp(Mathf.Atan2(localDir.x, localDir.z) * Mathf.Rad2Deg, -headYawLimit, headYawLimit);
        float pitch = Mathf.Clamp(Mathf.Asin(localDir.y) * Mathf.Rad2Deg, -headPitchLimit, headPitchLimit);

        Quaternion targetRot = Quaternion.Euler(-pitch, yaw, 0f);
        headDriver.localRotation = Quaternion.Slerp(headDriver.localRotation, targetRot, Time.deltaTime * headSmoothness);

        headBone.localRotation = headDriver.localRotation;
    }

    void HandleSpineTracking()
    {
        if (!spineBone || !spineDriver) return;

        bool shouldTrack = IsFeatureAllowed("Spine");
        float targetWeight = shouldTrack ? 1f : 0f;
        spineTrackingWeight = Mathf.MoveTowards(spineTrackingWeight, targetWeight, Time.deltaTime * spineFadeSpeed);

        // Calculate driver target
        float targetRotationY = Mathf.Lerp(spineMinRotation, spineMaxRotation, Input.mousePosition.x / Screen.width);
        Quaternion targetRot = Quaternion.Euler(0f, -targetRotationY, 0f);
        spineDriver.localRotation = Quaternion.Slerp(spineDriver.localRotation, targetRot, Time.deltaTime * spineSmoothness);

        // Blend from animation to tracking
        Quaternion animatedSpineRotation = animator.GetBoneTransform(HumanBodyBones.Spine).localRotation;
        Quaternion blended = Quaternion.Slerp(animatedSpineRotation, spineDriver.localRotation, spineTrackingWeight);
        spineBone.localRotation = blended;

        if (chestBone)
        {
            Quaternion chestAnimated = animator.GetBoneTransform(HumanBodyBones.Chest).localRotation;
            chestBone.localRotation = Quaternion.Slerp(chestAnimated, spineDriver.localRotation, 0.8f * spineTrackingWeight);
        }

        if (upperChestBone)
        {
            Quaternion upperAnimated = animator.GetBoneTransform(HumanBodyBones.UpperChest).localRotation;
            upperChestBone.localRotation = Quaternion.Slerp(upperAnimated, spineDriver.localRotation, 0.6f * spineTrackingWeight);
        }
    }

    void HandleEyeTracking()
    {
        Vector3 mousePos = Input.mousePosition;
        Vector3 worldMousePos = mainCam.ScreenToWorldPoint(new Vector3(mousePos.x, mousePos.y, mainCam.nearClipPlane));

        if (vrm10 != null && vrmLookAtTarget != null)
        {
            vrmLookAtTarget.position = worldMousePos;

            Vector3 eyeOrigin = vrmLookAtTarget.parent != null ? vrmLookAtTarget.parent.position : transform.position;
            Quaternion eyeRotation = vrmLookAtTarget.parent != null ? vrmLookAtTarget.parent.rotation : transform.rotation;
            Matrix4x4 eyeMatrix = Matrix4x4.TRS(eyeOrigin, eyeRotation, Vector3.one);
            var (rawYaw, rawPitch) = eyeMatrix.CalcYawPitch(worldMousePos);

            float clampedYaw = Mathf.Clamp(-rawYaw, -eyeYawLimit, eyeYawLimit);
            float clampedPitch = Mathf.Clamp(rawPitch, -eyePitchLimit, eyePitchLimit);

            Vector3 currentForward = vrmLookAtTarget.forward;
            Vector3 targetForward = Quaternion.Euler(-clampedPitch, clampedYaw, 0f) * Vector3.forward;
            Vector3 smoothed = Vector3.Slerp(currentForward, targetForward, Time.deltaTime * eyeSmoothness);
            vrmLookAtTarget.rotation = Quaternion.LookRotation(smoothed);
            return;
        }

        if (!leftEyeBone || !rightEyeBone || !eyeCenter) return;

        eyeCenter.position = (leftEyeBone.position + rightEyeBone.position) / 2f;
        Vector3 localDir = eyeCenter.parent.InverseTransformDirection((worldMousePos - eyeCenter.position).normalized);

        float yaw = Mathf.Clamp(Mathf.Atan2(localDir.x, localDir.z) * Mathf.Rad2Deg, -eyeYawLimit, eyeYawLimit);
        float pitch = Mathf.Clamp(Mathf.Asin(localDir.y) * Mathf.Rad2Deg, -eyePitchLimit, eyePitchLimit);
        Quaternion eyeRot = Quaternion.Euler(-pitch, yaw, 0f);

        leftEyeDriver.localRotation = Quaternion.Slerp(leftEyeDriver.localRotation, eyeRot, Time.deltaTime * eyeSmoothness);
        rightEyeDriver.localRotation = Quaternion.Slerp(rightEyeDriver.localRotation, eyeRot, Time.deltaTime * eyeSmoothness);

        leftEyeBone.localRotation = leftEyeDriver.localRotation;
        rightEyeBone.localRotation = rightEyeDriver.localRotation;
    }

    void OnDestroy()
    {
        Destroy(headDriver?.gameObject);
        Destroy(spineDriver?.gameObject);
        Destroy(leftEyeDriver?.gameObject);
        Destroy(rightEyeDriver?.gameObject);
        Destroy(eyeCenter?.gameObject);
    }

    bool GetFeature(TrackingPermission entry, string feature)
    {
        return feature switch
        {
            "Head" => entry.allowHead,
            "Spine" => entry.allowSpine,
            "Eye" => entry.allowEye,
            _ => false
        };
    }

}
