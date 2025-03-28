using UnityEngine;
using System.Collections.Generic;
using System.Linq;

[RequireComponent(typeof(Animator))]
public class HandHolder : MonoBehaviour
{
    [Header("Screen-Space Interaction")]
    [Tooltip("Main radius (pixels) for full IK grabbing.")]
    public float screenInteractionRadius = 30f;

    [Tooltip("Extra margin (pixels) for partial IK fade-out beyond main radius.")]
    public float preZoneMargin = 30f;

    [Tooltip("Speed for the hand to follow the mouse target.")]
    public float followSpeed = 5f;

    [Header("Blending")]
    public float maxIKWeight = 1f;
    public float blendInTime = 1f;
    public float blendOutTime = 1f;

    [Header("Forward Reach Settings")]
    public float maxHandDistance = 0.8f;
    public float minForwardOffset = 0.2f;
    public float verticalOffset = 0.05f;

    [Header("Elbow Hint Settings (Outward Bend)")]
    public float elbowHintDistance = 0.25f;
    public float elbowHintBackOffset = 0.1f;
    public float elbowHintHeightOffset = -0.05f;

    [Header("Allowed Animator States")]
    [Tooltip("Any animator states in this list will allow hand IK.")]
    public List<string> allowedStates = new List<string> { "Idle", "HoverReaction" };

    [Header("Animator Source")]
    public Animator avatarAnimator;

    [Header("Gizmos")]
    public bool showDebugGizmos = true;
    public Color gizmoColor = new Color(0.2f, 0.7f, 1f, 0.2f);

    private Camera mainCam;
    private Transform leftHand, rightHand, chest, leftShoulder, rightShoulder;
    private Vector3 leftTargetPos, rightTargetPos;
    private float leftIKWeight = 0f, rightIKWeight = 0f;

    // Exclusive control: only one hand can be grabbed at once
    private bool leftIsActive = false;
    private bool rightIsActive = false;

    void Start()
    {
        if (avatarAnimator == null)
            avatarAnimator = GetComponent<Animator>();

        mainCam = Camera.main;
        leftHand = avatarAnimator.GetBoneTransform(HumanBodyBones.LeftHand);
        rightHand = avatarAnimator.GetBoneTransform(HumanBodyBones.RightHand);
        chest = avatarAnimator.GetBoneTransform(HumanBodyBones.Chest)
                      ?? avatarAnimator.GetBoneTransform(HumanBodyBones.Spine);
        leftShoulder = avatarAnimator.GetBoneTransform(HumanBodyBones.LeftUpperArm);
        rightShoulder = avatarAnimator.GetBoneTransform(HumanBodyBones.RightUpperArm);
    }

    public void SetAnimator(Animator newAnimator)
    {
        avatarAnimator = newAnimator;
        mainCam = Camera.main;
        leftHand = avatarAnimator.GetBoneTransform(HumanBodyBones.LeftHand);
        rightHand = avatarAnimator.GetBoneTransform(HumanBodyBones.RightHand);
        chest = avatarAnimator.GetBoneTransform(HumanBodyBones.Chest)
                      ?? avatarAnimator.GetBoneTransform(HumanBodyBones.Spine);
        leftShoulder = avatarAnimator.GetBoneTransform(HumanBodyBones.LeftUpperArm);
        rightShoulder = avatarAnimator.GetBoneTransform(HumanBodyBones.RightUpperArm);
    }

    void Update()
    {
        if (!IsValid()) return;

        // Check if current animator state is in the allowed list
        if (!IsInAllowedState())
        {
            // Fade out both hands
            leftIKWeight = Mathf.MoveTowards(leftIKWeight, 0f, Time.deltaTime / blendOutTime);
            rightIKWeight = Mathf.MoveTowards(rightIKWeight, 0f, Time.deltaTime / blendOutTime);
            return;
        }

        // Convert each hand's world position to screen coords
        Vector2 leftScreenPos = mainCam.WorldToScreenPoint(leftHand.position);
        Vector2 rightScreenPos = mainCam.WorldToScreenPoint(rightHand.position);
        Vector2 mousePos = Input.mousePosition;

        // We'll compute a desired weight for each hand
        float leftDesiredWeight = ComputeScreenWeight(Vector2.Distance(mousePos, leftScreenPos));
        float rightDesiredWeight = ComputeScreenWeight(Vector2.Distance(mousePos, rightScreenPos));

        // EXCLUSIVE control
        if (leftDesiredWeight > rightDesiredWeight)
        {
            leftIsActive = leftDesiredWeight > 0f;
            rightIsActive = false;
            rightDesiredWeight = 0f;
        }
        else
        {
            rightIsActive = rightDesiredWeight > 0f;
            leftIsActive = false;
            leftDesiredWeight = 0f;
        }

        float dt = Time.deltaTime;
        leftIKWeight = Mathf.MoveTowards(leftIKWeight, leftDesiredWeight, dt / (leftDesiredWeight > leftIKWeight ? blendInTime : blendOutTime));
        rightIKWeight = Mathf.MoveTowards(rightIKWeight, rightDesiredWeight, dt / (rightDesiredWeight > rightIKWeight ? blendInTime : blendOutTime));

        Vector3 worldTarget = GetProjectedMouseTarget();
        if (leftIsActive)
            leftTargetPos = Vector3.Lerp(leftTargetPos, worldTarget, dt * followSpeed);
        if (rightIsActive)
            rightTargetPos = Vector3.Lerp(rightTargetPos, worldTarget, dt * followSpeed);
    }

    bool IsInAllowedState()
    {
        if (avatarAnimator == null || allowedStates.Count == 0) return false;

        var currentInfo = avatarAnimator.GetCurrentAnimatorStateInfo(0);

        // Check if the current state's name matches any item in allowedStates
        // (This requires the exact state name in your Animator)
        return allowedStates.Any(stateName => currentInfo.IsName(stateName));
    }

    private float ComputeScreenWeight(float distPixels)
    {
        float mainZone = screenInteractionRadius;
        float outerZone = screenInteractionRadius + preZoneMargin;

        if (distPixels <= mainZone)
            return maxIKWeight;
        if (distPixels >= outerZone)
            return 0f;

        float t = (distPixels - mainZone) / (outerZone - mainZone);
        return Mathf.Lerp(maxIKWeight, 0f, t);
    }

    void OnAnimatorIK(int layerIndex)
    {
        if (!IsValid()) return;

        if (!IsInAllowedState())
        {
            ResetIK();
            return;
        }

        Quaternion naturalRotation = Quaternion.LookRotation(avatarAnimator.transform.forward, avatarAnimator.transform.up);

        // LEFT
        if (leftIKWeight > 0f)
        {
            avatarAnimator.SetIKPositionWeight(AvatarIKGoal.LeftHand, leftIKWeight);
            avatarAnimator.SetIKRotationWeight(AvatarIKGoal.LeftHand, leftIKWeight);
            avatarAnimator.SetIKPosition(AvatarIKGoal.LeftHand, leftTargetPos);
            avatarAnimator.SetIKRotation(AvatarIKGoal.LeftHand, naturalRotation);

            Vector3 hint = GetElbowHint(leftShoulder, leftTargetPos, true);
            avatarAnimator.SetIKHintPositionWeight(AvatarIKHint.LeftElbow, leftIKWeight);
            avatarAnimator.SetIKHintPosition(AvatarIKHint.LeftElbow, hint);
        }
        else
        {
            avatarAnimator.SetIKPositionWeight(AvatarIKGoal.LeftHand, 0f);
            avatarAnimator.SetIKRotationWeight(AvatarIKGoal.LeftHand, 0f);
            avatarAnimator.SetIKHintPositionWeight(AvatarIKHint.LeftElbow, 0f);
        }

        // RIGHT
        if (rightIKWeight > 0f)
        {
            avatarAnimator.SetIKPositionWeight(AvatarIKGoal.RightHand, rightIKWeight);
            avatarAnimator.SetIKRotationWeight(AvatarIKGoal.RightHand, rightIKWeight);
            avatarAnimator.SetIKPosition(AvatarIKGoal.RightHand, rightTargetPos);
            avatarAnimator.SetIKRotation(AvatarIKGoal.RightHand, naturalRotation);

            Vector3 hint = GetElbowHint(rightShoulder, rightTargetPos, false);
            avatarAnimator.SetIKHintPositionWeight(AvatarIKHint.RightElbow, rightIKWeight);
            avatarAnimator.SetIKHintPosition(AvatarIKHint.RightElbow, hint);
        }
        else
        {
            avatarAnimator.SetIKPositionWeight(AvatarIKGoal.RightHand, 0f);
            avatarAnimator.SetIKRotationWeight(AvatarIKGoal.RightHand, 0f);
            avatarAnimator.SetIKHintPositionWeight(AvatarIKHint.RightElbow, 0f);
        }
    }

    private Vector3 GetElbowHint(Transform shoulder, Vector3 target, bool isLeft)
    {
        Vector3 toTarget = (target - shoulder.position).normalized;
        Vector3 up = avatarAnimator.transform.up;

        // Flip cross so arm always bends outward
        Vector3 bendDir = Vector3.Cross(toTarget, up).normalized;
        if (!isLeft) bendDir = -bendDir;

        return shoulder.position
            + bendDir * elbowHintDistance
            - avatarAnimator.transform.forward * elbowHintBackOffset
            + avatarAnimator.transform.up * elbowHintHeightOffset;
    }

    private Vector3 GetProjectedMouseTarget()
    {
        Vector3 mouse = Input.mousePosition;
        mouse.z = mainCam.WorldToScreenPoint(chest.position).z;
        Vector3 world = mainCam.ScreenToWorldPoint(mouse);

        Vector3 local = avatarAnimator.transform.InverseTransformPoint(world);
        local.z = Mathf.Clamp(local.z, minForwardOffset, maxHandDistance);
        local.y += verticalOffset;
        return avatarAnimator.transform.TransformPoint(local);
    }

    private bool IsValid()
    {
        return avatarAnimator && leftHand && rightHand && chest && leftShoulder && rightShoulder;
    }

    private void ResetIK()
    {
        avatarAnimator.SetIKPositionWeight(AvatarIKGoal.LeftHand, 0f);
        avatarAnimator.SetIKRotationWeight(AvatarIKGoal.LeftHand, 0f);
        avatarAnimator.SetIKHintPositionWeight(AvatarIKHint.LeftElbow, 0f);

        avatarAnimator.SetIKPositionWeight(AvatarIKGoal.RightHand, 0f);
        avatarAnimator.SetIKRotationWeight(AvatarIKGoal.RightHand, 0f);
        avatarAnimator.SetIKHintPositionWeight(AvatarIKHint.RightElbow, 0f);
    }

    // Same screen gizmo logic as before
    void OnDrawGizmos()
    {
        if (!showDebugGizmos || !Application.isPlaying || !mainCam) return;
        Gizmos.color = gizmoColor;

        DrawScreenRadiusSphere(leftHand, screenInteractionRadius);
        DrawScreenRadiusSphere(leftHand, screenInteractionRadius + preZoneMargin);

        DrawScreenRadiusSphere(rightHand, screenInteractionRadius);
        DrawScreenRadiusSphere(rightHand, screenInteractionRadius + preZoneMargin);
    }

    private void DrawScreenRadiusSphere(Transform hand, float screenRadius)
    {
        if (hand == null) return;
        Vector3 screenPos = mainCam.WorldToScreenPoint(hand.position);
        if (screenPos.z < 0f) return; // behind camera

        Vector3 offsetScreen = screenPos + new Vector3(screenRadius, 0f, 0f);
        Vector3 offsetWorld = mainCam.ScreenToWorldPoint(offsetScreen);

        float radiusWorld = Vector3.Distance(hand.position, offsetWorld);
        Gizmos.DrawWireSphere(hand.position, radiusWorld);
    }
}
