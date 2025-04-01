using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UniVRM10; // <- Required for VRM10 support

[RequireComponent(typeof(Animator))]
public class AvatarMouseTracking : MonoBehaviour
{
    [Header("Global Mouse Tracking Settings")]
    public bool enableMouseTracking = true;

    [Header("Head Tracking")]
    [SerializeField, Range(0f, 90f)] public float headYawLimit = 45f;
    [SerializeField, Range(0f, 90f)] public float headPitchLimit = 30f;
    [SerializeField, Range(1f, 20f)] public float headSmoothness = 10f;

    [Header("Spine Tracking")]
    [Range(-90f, 90f)] public float spineMinRotation = -15f;
    [Range(-90f, 90f)] public float spineMaxRotation = 15f;
    [Range(1f, 50f)] public float spineSmoothness = 25f;
    [Range(1f, 50f)] public float spineResetSmoothness = 10f;
    public List<string> allowedStates = new List<string> { "Idle", "HoverReaction" };

    [Header("Eye Tracking")]
    [SerializeField, Range(0f, 90f)] public float eyeYawLimit = 12f;
    [SerializeField, Range(0f, 90f)] public float eyePitchLimit = 12f;
    [SerializeField, Range(1f, 20f)] public float eyeSmoothness = 10f;

    private Animator animator;
    private Camera mainCam;

    // Bones
    private Transform headBone, spineBone, chestBone, upperChestBone;
    private Transform leftEyeBone, rightEyeBone;

    // Drivers
    private Transform headDriver, spineDriver;
    private Transform leftEyeDriver, rightEyeDriver, eyeCenter;

    private Quaternion spineDefaultRotation;

    // VRM 1.0 Eye Control
    private Vrm10Instance vrm10;
    private Transform vrmLookAtTarget;


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
        else Debug.LogWarning("Head bone not found!");
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
        else Debug.LogWarning("Spine bone not found!");
    }

    void InitializeEyeTracking()
    {
        leftEyeBone = animator.GetBoneTransform(HumanBodyBones.LeftEye);
        rightEyeBone = animator.GetBoneTransform(HumanBodyBones.RightEye);
        vrm10.LookAtTargetType = VRM10ObjectLookAt.LookAtTargetTypes.YawPitchValue;

        // VRM 1.0 look-at setup
        vrm10 = GetComponentInChildren<Vrm10Instance>();
        if (vrm10 != null)
        {
            vrmLookAtTarget = new GameObject("VRMLookAtTarget").transform;
            vrmLookAtTarget.SetParent(transform);
            vrm10.LookAtTarget = vrmLookAtTarget;
            vrm10.LookAtTargetType = VRM10ObjectLookAt.LookAtTargetTypes.YawPitchValue;
            Debug.Log("[AvatarMouseTracking] Using VRM 1.0 LookAtTarget + clamped smoothing.");
        }

        // Fallback bone search
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
        else Debug.LogWarning("Eye bones not found!");
    }



    void LateUpdate()
    {
        if (!enableMouseTracking) return;

        HandleHeadTracking();
        HandleSpineTracking();
        HandleEyeTracking();
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

        Quaternion targetRot = spineDefaultRotation;

        if (allowedStates.Any(state => animator.GetCurrentAnimatorStateInfo(0).IsName(state)))
        {
            float targetRotationY = Mathf.Lerp(spineMinRotation, spineMaxRotation, Input.mousePosition.x / Screen.width);
            targetRot = Quaternion.Euler(0f, -targetRotationY, 0f);
            spineDriver.localRotation = Quaternion.Slerp(spineDriver.localRotation, targetRot, Time.deltaTime * spineSmoothness);
        }
        else
        {
            spineDriver.localRotation = Quaternion.Slerp(spineDriver.localRotation, spineDefaultRotation, Time.deltaTime * spineResetSmoothness);
        }

        spineBone.localRotation = spineDriver.localRotation;
        if (chestBone)
            chestBone.localRotation = Quaternion.Slerp(Quaternion.identity, spineDriver.localRotation, 0.8f);
        if (upperChestBone)
            upperChestBone.localRotation = Quaternion.Slerp(Quaternion.identity, spineDriver.localRotation, 0.6f);
    }

    void HandleEyeTracking()
    {
        Vector3 mousePos = Input.mousePosition;
        Vector3 worldMousePos = mainCam.ScreenToWorldPoint(new Vector3(mousePos.x, mousePos.y, mainCam.nearClipPlane));

        if (vrm10 != null && vrmLookAtTarget != null)
        {
            // Step 1: Move the target to follow mouse
            vrmLookAtTarget.position = worldMousePos;

            // Step 2: Calculate yaw/pitch for clamping and smoothing
            Vector3 eyeOrigin = vrmLookAtTarget.parent != null ? vrmLookAtTarget.parent.position : transform.position;
            Quaternion eyeRotation = vrmLookAtTarget.parent != null ? vrmLookAtTarget.parent.rotation : transform.rotation;
            Matrix4x4 eyeMatrix = Matrix4x4.TRS(eyeOrigin, eyeRotation, Vector3.one);

            var (rawYaw, rawPitch) = eyeMatrix.CalcYawPitch(worldMousePos);

            float clampedYaw = Mathf.Clamp(rawYaw, -eyeYawLimit, eyeYawLimit);
            float clampedPitch = Mathf.Clamp(rawPitch, -eyePitchLimit, eyePitchLimit);

            // Step 3: Smooth the LookAtTarget rotation (yaw/pitch)
            Vector3 currentForward = vrmLookAtTarget.forward;
            Vector3 targetForward = Quaternion.Euler(-clampedPitch, clampedYaw, 0f) * Vector3.forward;
            Vector3 smoothed = Vector3.Slerp(currentForward, targetForward, Time.deltaTime * eyeSmoothness);
            vrmLookAtTarget.rotation = Quaternion.LookRotation(smoothed);

            return;
        }

        // Fallback for VRM 0.x or generic rigs
        if (!leftEyeBone || !rightEyeBone || !eyeCenter) return;

        eyeCenter.position = (leftEyeBone.position + rightEyeBone.position) / 2f;
        Vector3 localDir = eyeCenter.parent.InverseTransformDirection((worldMousePos - eyeCenter.position).normalized);
        float yawFallback = Mathf.Clamp(Mathf.Atan2(localDir.x, localDir.z) * Mathf.Rad2Deg, -eyeYawLimit, eyeYawLimit);
        float pitchFallback = Mathf.Clamp(Mathf.Asin(localDir.y) * Mathf.Rad2Deg, -eyePitchLimit, eyePitchLimit);
        Quaternion eyeRot = Quaternion.Euler(-pitchFallback, yawFallback, 0f);

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
}
