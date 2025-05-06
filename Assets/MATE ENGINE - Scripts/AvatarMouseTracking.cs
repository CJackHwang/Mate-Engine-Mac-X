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
    public List<TrackingPermission> trackingPermissions = new();

    [Header("Head Tracking")]
    [Range(0f, 90f)] public float headYawLimit = 45f;
    [Range(0f, 90f)] public float headPitchLimit = 30f;
    [Range(1f, 20f)] public float headSmoothness = 10f;

    [Header("Spine Tracking")]
    [Range(-90f, 90f)] public float spineMinRotation = -15f;
    [Range(-90f, 90f)] public float spineMaxRotation = 15f;
    [Range(1f, 50f)] public float spineSmoothness = 25f;
    [Range(1f, 10f)] public float spineFadeSpeed = 5f;

    [Header("Eye Tracking")]
    [Range(0f, 90f)] public float eyeYawLimit = 12f;
    [Range(0f, 90f)] public float eyePitchLimit = 12f;
    [Range(1f, 20f)] public float eyeSmoothness = 10f;

    [Header("Animator vs. Tracking Blend")]
    [Range(0f, 1f)]
    public float headBlend = 1f;   // 0 = pure Animator, 1 = pure Tracking

    [Range(0f, 1f)]
    public float spineBlend = 1f;  // 0 = pure Animator, 1 = pure Tracking

    [Range(0f, 1f)]
    public float eyeBlend = 1f;   // 0 = pure Animator, 1 = pure Tracking



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

    private int currentStateHash = 0;
    private int nextStateHash = 0;
    private bool wasInTransitionLastFrame = false;

    private readonly Vector3[] vectorCache = new Vector3[4];
    private readonly Quaternion[] quatCache = new Quaternion[4];

    private Quaternion headInitialLocalRotation;
    private Quaternion spineInitialLocalRotation;

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
            // create driver
            headDriver = new GameObject("HeadDriver").transform;
            headDriver.SetParent(headBone.parent, false);
            headDriver.localPosition = headBone.localPosition;
            headDriver.localRotation = headBone.localRotation;

            // **store initial local rotation** (Animation “base” pose)
            headInitialLocalRotation = headBone.localRotation;
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
            spineDriver.SetParent(spineBone.parent, false);
            spineDriver.localPosition = spineBone.localPosition;
            spineDriver.localRotation = spineBone.localRotation;

            // **store initial local rotation** for additive blending
            spineInitialLocalRotation = spineBone.localRotation;
        }
    }


    void InitializeEyeTracking()
    {
        leftEyeBone = animator.GetBoneTransform(HumanBodyBones.LeftEye);
        rightEyeBone = animator.GetBoneTransform(HumanBodyBones.RightEye);

        if (vrm10 != null)
        {
            vrmLookAtTarget = new GameObject("VRMLookAtTarget").transform;
            vrmLookAtTarget.SetParent(transform, false);
            vrm10.LookAtTarget = vrmLookAtTarget;
            vrm10.LookAtTargetType = VRM10ObjectLookAt.LookAtTargetTypes.YawPitchValue;
        }

        if (!leftEyeBone || !rightEyeBone)
        {
            foreach (Transform t in animator.GetComponentsInChildren<Transform>())
            {
                var lname = t.name.ToLower();
                if (leftEyeBone == null && (lname.Contains("lefteye") || lname.Contains("eye.l"))) leftEyeBone = t;
                else if (rightEyeBone == null && (lname.Contains("righteye") || lname.Contains("eye.r"))) rightEyeBone = t;
            }
        }

        if (leftEyeBone && rightEyeBone)
        {
            eyeCenter = new GameObject("EyeCenter").transform;
            eyeCenter.SetParent(leftEyeBone.parent, false);
            eyeCenter.position = (leftEyeBone.position + rightEyeBone.position) * 0.5f;

            leftEyeDriver = new GameObject("LeftEyeDriver").transform;
            leftEyeDriver.SetParent(leftEyeBone.parent, false);
            leftEyeDriver.localPosition = leftEyeBone.localPosition;
            leftEyeDriver.localRotation = leftEyeBone.localRotation;

            rightEyeDriver = new GameObject("RightEyeDriver").transform;
            rightEyeDriver.SetParent(rightEyeBone.parent, false);
            rightEyeDriver.localPosition = rightEyeBone.localPosition;
            rightEyeDriver.localRotation = rightEyeBone.localRotation;
        }
    }

    void LateUpdate()
    {
        if (!enableMouseTracking || mainCam == null || animator == null) return;

        var currentInfo = animator.GetCurrentAnimatorStateInfo(0);
        var nextInfo = animator.GetNextAnimatorStateInfo(0);
        bool isInTransition = animator.IsInTransition(0);

        if (isInTransition)
            nextStateHash = nextInfo.shortNameHash;
        else
        {
            currentStateHash = currentInfo.shortNameHash;
            nextStateHash = 0;
        }

        wasInTransitionLastFrame = isInTransition;

        if (IsFeatureAllowed("Head")) HandleHeadTracking();
        HandleSpineTracking();
        if (IsFeatureAllowed("Eye")) HandleEyeTracking();
    }


    bool IsFeatureAllowed(string feature)
    {
        bool? currentResult = null;
        bool? nextResult = null;

        foreach (var entry in trackingPermissions)
        {
            if (entry.isParameter)
            {
                if (animator.GetBool(entry.stateOrParameterName))
                    return GetFeature(entry, feature);
            }
            else
            {
                int hash = Animator.StringToHash(entry.stateOrParameterName);

                if (currentStateHash == hash)
                    currentResult = GetFeature(entry, feature);
                if (animator.IsInTransition(0) && nextStateHash == hash)
                    nextResult = GetFeature(entry, feature);
            }
        }

        if (animator.IsInTransition(0) && nextResult.HasValue)
            return nextResult.Value;

        return currentResult ?? false;
    }

    void HandleHeadTracking()
    {
        if (headBone == null || headDriver == null) return;

        // compute driver rotation (unchanged)…
        vectorCache[0] = Input.mousePosition;
        vectorCache[1] = mainCam.ScreenToWorldPoint(
            new Vector3(vectorCache[0].x, vectorCache[0].y, mainCam.nearClipPlane)
        );
        vectorCache[2] = (vectorCache[1] - headDriver.position).normalized;
        vectorCache[3] = headDriver.parent.InverseTransformDirection(vectorCache[2]);

        float yaw = Mathf.Clamp(
            Mathf.Atan2(vectorCache[3].x, vectorCache[3].z) * Mathf.Rad2Deg,
            -headYawLimit, headYawLimit
        );
        float pitch = Mathf.Clamp(
            Mathf.Asin(vectorCache[3].y) * Mathf.Rad2Deg,
            -headPitchLimit, headPitchLimit
        );

        headDriver.localRotation = Quaternion.Slerp(
            headDriver.localRotation,
            Quaternion.Euler(-pitch, yaw, 0f),
            Time.deltaTime * headSmoothness
        );

        // — additive delta, then blend with base by headBlend —
        Quaternion baseLocal = headBone.localRotation;
        Quaternion delta = headDriver.localRotation * Quaternion.Inverse(headInitialLocalRotation);
        Quaternion fullOffset = delta * baseLocal;

        headBone.localRotation = Quaternion.Slerp(
            baseLocal,     // 0 = pure animation
            fullOffset,    // 1 = full tracking
            headBlend      // slider
        );
    }

    void HandleSpineTracking()
    {
        if (spineBone == null || spineDriver == null) return;

        // dynamic on/off weight from permissions
        float targetW = IsFeatureAllowed("Spine") ? 1f : 0f;
        spineTrackingWeight = Mathf.MoveTowards(
            spineTrackingWeight, targetW, Time.deltaTime * spineFadeSpeed
        );

        // compute driver rotation
        float normY = Mathf.Clamp01(Input.mousePosition.x / Screen.width);
        float targetY = Mathf.Lerp(spineMinRotation, spineMaxRotation, normY);
        spineDriver.localRotation = Quaternion.Slerp(
            spineDriver.localRotation,
            Quaternion.Euler(0f, -targetY, 0f),
            Time.deltaTime * spineSmoothness
        );

        // — additive delta
        Quaternion baseLocal = spineBone.localRotation;
        Quaternion delta = spineDriver.localRotation * Quaternion.Inverse(spineInitialLocalRotation);

        // combine dynamic weight * user slider
        float appliedW = spineTrackingWeight * spineBlend;
        Quaternion offset = Quaternion.Slerp(
            Quaternion.identity,
            delta,
            appliedW
        );

        spineBone.localRotation = offset * baseLocal;

        // chest + upper chest layered similarly
        if (chestBone)
        {
            Quaternion chestBase = chestBone.localRotation;
            Quaternion chestOffset = Quaternion.Slerp(
                Quaternion.identity, delta, 0.8f * appliedW
            );
            chestBone.localRotation = chestOffset * chestBase;
        }
        if (upperChestBone)
        {
            Quaternion upperBase = upperChestBone.localRotation;
            Quaternion upperOffset = Quaternion.Slerp(
                Quaternion.identity, delta, 0.6f * appliedW
            );
            upperChestBone.localRotation = upperOffset * upperBase;
        }
    }



    void HandleEyeTracking()
    {
        vectorCache[0] = Input.mousePosition;
        vectorCache[1] = mainCam.ScreenToWorldPoint(new Vector3(vectorCache[0].x, vectorCache[0].y, mainCam.nearClipPlane));

        if (vrm10 != null && vrmLookAtTarget != null)
        {
            vrmLookAtTarget.position = vectorCache[1];
            var parent = vrmLookAtTarget.parent ?? transform;
            Matrix4x4 mtx = Matrix4x4.TRS(parent.position, parent.rotation, Vector3.one);
            var (rawYaw, rawPitch) = mtx.CalcYawPitch(vectorCache[1]);

            float yaw = Mathf.Clamp(-rawYaw, -eyeYawLimit, eyeYawLimit);
            float pitch = Mathf.Clamp(rawPitch, -eyePitchLimit, eyePitchLimit);
            Vector3 currentFwd = vrmLookAtTarget.forward;
            Vector3 targetFwd = Quaternion.Euler(-pitch, yaw, 0f) * Vector3.forward;
            Vector3 smoothed = Vector3.Slerp(currentFwd, targetFwd, Time.deltaTime * eyeSmoothness);
            vrmLookAtTarget.rotation = Quaternion.LookRotation(smoothed);
            return;
        }

        if (leftEyeBone == null || rightEyeBone == null || eyeCenter == null) return;

        eyeCenter.position = (leftEyeBone.position + rightEyeBone.position) * 0.5f;
        vectorCache[2] = (vectorCache[1] - eyeCenter.position).normalized;
        vectorCache[3] = eyeCenter.parent.InverseTransformDirection(vectorCache[2]);

        float eyeYaw = Mathf.Clamp(Mathf.Atan2(vectorCache[3].x, vectorCache[3].z) * Mathf.Rad2Deg, -eyeYawLimit, eyeYawLimit);
        float eyePitch = Mathf.Clamp(Mathf.Asin(vectorCache[3].y) * Mathf.Rad2Deg, -eyePitchLimit, eyePitchLimit);
        Quaternion eyeRot = Quaternion.Euler(-eyePitch, eyeYaw, 0f);

        leftEyeDriver.localRotation = Quaternion.Slerp(leftEyeDriver.localRotation, eyeRot, Time.deltaTime * eyeSmoothness);
        rightEyeDriver.localRotation = Quaternion.Slerp(rightEyeDriver.localRotation, eyeRot, Time.deltaTime * eyeSmoothness);

        leftEyeBone.localRotation = Quaternion.Slerp(leftEyeBone.localRotation, leftEyeDriver.localRotation, eyeBlend);
        rightEyeBone.localRotation = Quaternion.Slerp(rightEyeBone.localRotation, rightEyeDriver.localRotation, eyeBlend);
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

    void OnDestroy()
    {
        Destroy(headDriver?.gameObject);
        Destroy(spineDriver?.gameObject);
        Destroy(leftEyeDriver?.gameObject);
        Destroy(rightEyeDriver?.gameObject);
        Destroy(eyeCenter?.gameObject);
        Destroy(vrmLookAtTarget?.gameObject);
    }
}