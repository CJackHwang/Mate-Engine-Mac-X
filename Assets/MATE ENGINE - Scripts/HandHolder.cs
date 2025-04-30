using UnityEngine;

[RequireComponent(typeof(Animator))]
public class HandHolder : MonoBehaviour
{
    [Header("Screen-Space Interaction")] public float screenInteractionRadius = 30f, preZoneMargin = 30f, followSpeed = 10f;
    [Header("Blending")] public float maxIKWeight = 1f, blendInTime = 1f, blendOutTime = 1f;
    [Header("Forward Reach Settings")] public float maxHandDistance = 0.8f, minForwardOffset = 0.2f, verticalOffset = 0.05f;
    [Header("Elbow Hint Settings")] public float elbowHintDistance = 0.25f, elbowHintBackOffset = 0.1f, elbowHintHeightOffset = -0.05f;

    [Header("Allowed Animator States")]
    public string[] allowedStates = { "Idle", "HoverReaction" };

    [Header("Animator Source")]
    public Animator avatarAnimator;

    [Header("Gizmos")]
    public bool showDebugGizmos = true;
    public Color gizmoColor = new Color(0.2f, 0.7f, 1f, 0.2f);

    [Header("Enable Hand Holding")]
    public bool enableHandHolding = true;


    private Camera mainCam;
    private Transform leftHand, rightHand, chest, leftShoulder, rightShoulder;
    private Vector3 leftTargetPos, rightTargetPos;
    private float leftIKWeight, rightIKWeight;

    private bool leftIsActive, rightIsActive;

    void Start()
    {
        mainCam = Camera.main;
        CacheTransforms();
    }

    void CacheTransforms()
    {
        leftHand = avatarAnimator.GetBoneTransform(HumanBodyBones.LeftHand);
        rightHand = avatarAnimator.GetBoneTransform(HumanBodyBones.RightHand);
        chest = avatarAnimator.GetBoneTransform(HumanBodyBones.Chest) ?? avatarAnimator.GetBoneTransform(HumanBodyBones.Spine);
        leftShoulder = avatarAnimator.GetBoneTransform(HumanBodyBones.LeftUpperArm);
        rightShoulder = avatarAnimator.GetBoneTransform(HumanBodyBones.RightUpperArm);
    }

    public void SetAnimator(Animator newAnimator)
    {
        avatarAnimator = newAnimator;
        CacheTransforms(); // Ensure hand/chest bones get refreshed after assignment
    }

    void Update()
    {
        if (!enableHandHolding || !IsValid()) return;

        if (!IsInAllowedState())
        {
            leftIKWeight = Mathf.MoveTowards(leftIKWeight, 0f, Time.deltaTime / blendOutTime);
            rightIKWeight = Mathf.MoveTowards(rightIKWeight, 0f, Time.deltaTime / blendOutTime);
            return;
        }

        Vector2 mousePos = Input.mousePosition;
        float leftWeight = ComputeScreenWeight((mousePos - (Vector2)mainCam.WorldToScreenPoint(leftHand.position)).sqrMagnitude);
        float rightWeight = ComputeScreenWeight((mousePos - (Vector2)mainCam.WorldToScreenPoint(rightHand.position)).sqrMagnitude);

        if (leftWeight > rightWeight)
        {
            leftIsActive = leftWeight > 0f;
            rightIsActive = false;
            rightWeight = 0f;
        }
        else
        {
            rightIsActive = rightWeight > 0f;
            leftIsActive = false;
            leftWeight = 0f;
        }

        leftIKWeight = Mathf.MoveTowards(leftIKWeight, leftWeight, Time.deltaTime / (leftWeight > leftIKWeight ? blendInTime : blendOutTime));
        rightIKWeight = Mathf.MoveTowards(rightIKWeight, rightWeight, Time.deltaTime / (rightWeight > rightIKWeight ? blendInTime : blendOutTime));

        Vector3 target = GetProjectedMouseTarget();
        if (leftIsActive) leftTargetPos = Vector3.Lerp(leftTargetPos, target, Time.deltaTime * followSpeed);
        if (rightIsActive) rightTargetPos = Vector3.Lerp(rightTargetPos, target, Time.deltaTime * followSpeed);
    }


    bool IsInAllowedState()
    {
        AnimatorStateInfo current = avatarAnimator.GetCurrentAnimatorStateInfo(0);
        for (int i = 0; i < allowedStates.Length; i++)
            if (current.IsName(allowedStates[i])) return true;
        return false;
    }

    float ComputeScreenWeight(float sqrDistPixels)
    {
        float mainZone = screenInteractionRadius * screenInteractionRadius;
        float outerZone = (screenInteractionRadius + preZoneMargin) * (screenInteractionRadius + preZoneMargin);

        if (sqrDistPixels <= mainZone) return maxIKWeight;
        if (sqrDistPixels >= outerZone) return 0f;

        return Mathf.Lerp(maxIKWeight, 0f, (Mathf.Sqrt(sqrDistPixels) - screenInteractionRadius) / preZoneMargin);
    }

    void OnAnimatorIK(int layerIndex)
    {
        if (!enableHandHolding || !IsValid() || !IsInAllowedState())
        {
            ResetIK();
            return;
        }

        Quaternion naturalRotation = Quaternion.LookRotation(avatarAnimator.transform.forward, avatarAnimator.transform.up);

        ApplyIK(AvatarIKGoal.LeftHand, AvatarIKHint.LeftElbow, leftIKWeight, leftTargetPos, leftShoulder, true, naturalRotation);
        ApplyIK(AvatarIKGoal.RightHand, AvatarIKHint.RightElbow, rightIKWeight, rightTargetPos, rightShoulder, false, naturalRotation);
    }

    void ApplyIK(AvatarIKGoal hand, AvatarIKHint elbow, float weight, Vector3 targetPos, Transform shoulder, bool isLeft, Quaternion rotation)
    {
        avatarAnimator.SetIKPositionWeight(hand, weight);
        avatarAnimator.SetIKRotationWeight(hand, weight);
        avatarAnimator.SetIKHintPositionWeight(elbow, weight);

        if (weight <= 0f) return;

        avatarAnimator.SetIKPosition(hand, targetPos);
        avatarAnimator.SetIKRotation(hand, rotation);
        avatarAnimator.SetIKHintPosition(elbow, GetElbowHint(shoulder, targetPos, isLeft));
    }

    Vector3 GetElbowHint(Transform shoulder, Vector3 target, bool isLeft)
    {
        Vector3 toTarget = (target - shoulder.position).normalized;
        Vector3 bendDir = Vector3.Cross(toTarget, avatarAnimator.transform.up).normalized;
        if (!isLeft) bendDir = -bendDir;

        return shoulder.position + bendDir * elbowHintDistance
            - avatarAnimator.transform.forward * elbowHintBackOffset
            + avatarAnimator.transform.up * elbowHintHeightOffset;
    }

    Vector3 GetProjectedMouseTarget()
    {
        Vector3 mouse = Input.mousePosition;
        mouse.z = mainCam.WorldToScreenPoint(chest.position).z;
        Vector3 world = mainCam.ScreenToWorldPoint(mouse);
        Vector3 local = avatarAnimator.transform.InverseTransformPoint(world);

        local.z = Mathf.Clamp(local.z, minForwardOffset, maxHandDistance);
        local.y += verticalOffset;
        return avatarAnimator.transform.TransformPoint(local);
    }

    void OnDrawGizmos()
    {
        if (!showDebugGizmos || !Application.isPlaying || !mainCam) return;

        Gizmos.color = gizmoColor;
        DrawRadiusGizmo(leftHand);
        DrawRadiusGizmo(rightHand);
    }

    void DrawRadiusGizmo(Transform hand)
    {
        if (!hand) return;

        Vector3 screenPos = mainCam.WorldToScreenPoint(hand.position);
        Vector3 offsetScreen = screenPos + new Vector3(screenInteractionRadius, 0f, 0f);
        Vector3 offsetWorld = mainCam.ScreenToWorldPoint(offsetScreen);
        float radiusWorld = Vector3.Distance(hand.position, offsetWorld);
        Gizmos.DrawWireSphere(hand.position, radiusWorld);

        offsetScreen = screenPos + new Vector3(screenInteractionRadius + preZoneMargin, 0f, 0f);
        offsetWorld = mainCam.ScreenToWorldPoint(offsetScreen);
        radiusWorld = Vector3.Distance(hand.position, offsetWorld);
        Gizmos.DrawWireSphere(hand.position, radiusWorld);
    }

    bool IsValid() => avatarAnimator && leftHand && rightHand && chest && leftShoulder && rightShoulder;

    void ResetIK()
    {
        ApplyIK(AvatarIKGoal.LeftHand, AvatarIKHint.LeftElbow, 0f, Vector3.zero, null, true, Quaternion.identity);
        ApplyIK(AvatarIKGoal.RightHand, AvatarIKHint.RightElbow, 0f, Vector3.zero, null, false, Quaternion.identity);
    }
}
