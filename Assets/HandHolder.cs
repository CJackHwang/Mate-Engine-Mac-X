using UnityEngine;

[RequireComponent(typeof(Animator))]
public class HandHolder : MonoBehaviour
{
    [Header("Interaction Settings")]
    [Tooltip("Radius around hand where interaction starts")]
    public float interactionRadius = 0.25f;
    [Tooltip("Extra buffer zone before deactivation to prevent flickering")]
    public float hysteresisBuffer = 0.05f;
    [Tooltip("How quickly the hand moves toward the mouse target")]
    public float followSpeed = 5f;

    [Header("Blending")]
    [Tooltip("Max weight for IK blending")]
    public float maxIKWeight = 1f;
    [Tooltip("Time to fully blend in IK after activation")]
    public float blendInTime = 0.2f;
    [Tooltip("Time to fully blend out IK after deactivation")]
    public float blendOutTime = 0.4f;

    [Header("Positioning")]
    [Tooltip("Max distance in front of the chest the hand can move")]
    public float maxHandDistance = 0.6f;
    [Tooltip("How far in front (or behind) the body hands are allowed to move (positive = front, negative = behind)")]
    public float handZOffset = 0.05f;
    [Tooltip("Offset to guide elbow direction (pole vector)")]
    public Vector3 elbowHintOffset = new Vector3(-0.2f, -0.1f, 0.2f);

    [Header("Animator Flags")]
    public string isDancingParam = "isDancing";
    public string isDraggingParam = "isDragging";
    public string hoverTriggerParam = "HoverTrigger";

    [Header("Animator Source")]
    public Animator avatarAnimator;

    [Header("Gizmos")]
    public bool showDebugGizmos = true;
    public Color gizmoColor = new Color(0.2f, 0.7f, 1f, 0.2f);

    private Camera mainCam;
    private Transform leftHand, rightHand, chest;
    private Vector3 leftTargetPos, rightTargetPos;
    private float leftIKWeight = 0f, rightIKWeight = 0f;
    private bool leftWantsIK = false, rightWantsIK = false;

    void Start()
    {
        if (avatarAnimator == null)
            avatarAnimator = GetComponent<Animator>();

        mainCam = Camera.main;
        leftHand = avatarAnimator.GetBoneTransform(HumanBodyBones.LeftHand);
        rightHand = avatarAnimator.GetBoneTransform(HumanBodyBones.RightHand);
        chest = avatarAnimator.GetBoneTransform(HumanBodyBones.Chest) ?? avatarAnimator.GetBoneTransform(HumanBodyBones.Spine);
    }

    public void SetAnimator(Animator newAnimator)
    {
        avatarAnimator = newAnimator;
        mainCam = Camera.main;
        leftHand = avatarAnimator.GetBoneTransform(HumanBodyBones.LeftHand);
        rightHand = avatarAnimator.GetBoneTransform(HumanBodyBones.RightHand);
        chest = avatarAnimator.GetBoneTransform(HumanBodyBones.Chest) ?? avatarAnimator.GetBoneTransform(HumanBodyBones.Spine);
    }

    void Update()
    {
        if (avatarAnimator == null || leftHand == null || rightHand == null || chest == null)
            return;

        bool isDancing = avatarAnimator.GetBool(isDancingParam);
        bool isDragging = avatarAnimator.GetBool(isDraggingParam);
        bool isHovering = avatarAnimator.GetBool(hoverTriggerParam);

        if (isDancing || isDragging || isHovering)
        {
            leftWantsIK = false;
            rightWantsIK = false;
            return;
        }

        Vector3 mouseWorld = GetClampedMouseTarget();

        float distLeft = Vector3.Distance(mouseWorld, leftHand.position);
        float distRight = Vector3.Distance(mouseWorld, rightHand.position);

        float triggerIn = interactionRadius;
        float triggerOut = interactionRadius + hysteresisBuffer;

        // If already holding left and enter right zone, switch to right
        if (leftWantsIK && distRight < triggerIn)
        {
            leftWantsIK = false;
            rightWantsIK = true;
        }
        else if (rightWantsIK && distLeft < triggerIn)
        {
            rightWantsIK = false;
            leftWantsIK = true;
        }
        else
        {
            // Otherwise, apply hysteresis-based toggling
            leftWantsIK = leftWantsIK ? distLeft < triggerOut : distLeft < triggerIn;
            rightWantsIK = rightWantsIK ? distRight < triggerOut : distRight < triggerIn;

            // Ensure exclusivity: if one activates, deactivate the other
            if (leftWantsIK) rightWantsIK = false;
            else if (rightWantsIK) leftWantsIK = false;
        }


        if (leftWantsIK)
            leftTargetPos = Vector3.Lerp(leftTargetPos, mouseWorld, Time.deltaTime * followSpeed);

        if (rightWantsIK)
            rightTargetPos = Vector3.Lerp(rightTargetPos, mouseWorld, Time.deltaTime * followSpeed);

        float inSpeed = 1f / Mathf.Max(blendInTime, 0.01f);
        float outSpeed = 1f / Mathf.Max(blendOutTime, 0.01f);

        leftIKWeight = Mathf.MoveTowards(leftIKWeight, leftWantsIK ? maxIKWeight : 0f, Time.deltaTime * (leftWantsIK ? inSpeed : outSpeed));
        rightIKWeight = Mathf.MoveTowards(rightIKWeight, rightWantsIK ? maxIKWeight : 0f, Time.deltaTime * (rightWantsIK ? inSpeed : outSpeed));
    }

    void OnAnimatorIK(int layerIndex)
    {
        if (avatarAnimator == null) return;

        bool isDancing = avatarAnimator.GetBool(isDancingParam);
        bool isDragging = avatarAnimator.GetBool(isDraggingParam);
        bool isHovering = avatarAnimator.GetBool(hoverTriggerParam);

        if (isDancing || isDragging || isHovering)
        {
            avatarAnimator.SetIKPositionWeight(AvatarIKGoal.LeftHand, 0f);
            avatarAnimator.SetIKRotationWeight(AvatarIKGoal.LeftHand, 0f);
            avatarAnimator.SetIKHintPositionWeight(AvatarIKHint.LeftElbow, 0f);

            avatarAnimator.SetIKPositionWeight(AvatarIKGoal.RightHand, 0f);
            avatarAnimator.SetIKRotationWeight(AvatarIKGoal.RightHand, 0f);
            avatarAnimator.SetIKHintPositionWeight(AvatarIKHint.RightElbow, 0f);
            return;
        }

        Vector3 bodyForward = avatarAnimator.transform.forward;
        Vector3 bodyUp = avatarAnimator.transform.up;
        Quaternion naturalRotation = Quaternion.LookRotation(bodyForward, bodyUp);

        if (leftIKWeight > 0f)
        {
            avatarAnimator.SetIKPositionWeight(AvatarIKGoal.LeftHand, leftIKWeight);
            avatarAnimator.SetIKRotationWeight(AvatarIKGoal.LeftHand, leftIKWeight);
            avatarAnimator.SetIKPosition(AvatarIKGoal.LeftHand, leftTargetPos);
            avatarAnimator.SetIKRotation(AvatarIKGoal.LeftHand, naturalRotation);

            Vector3 leftHint = leftHand.position + avatarAnimator.transform.TransformDirection(elbowHintOffset);
            avatarAnimator.SetIKHintPositionWeight(AvatarIKHint.LeftElbow, leftIKWeight);
            avatarAnimator.SetIKHintPosition(AvatarIKHint.LeftElbow, leftHint);
        }
        else
        {
            avatarAnimator.SetIKPositionWeight(AvatarIKGoal.LeftHand, 0f);
            avatarAnimator.SetIKRotationWeight(AvatarIKGoal.LeftHand, 0f);
            avatarAnimator.SetIKHintPositionWeight(AvatarIKHint.LeftElbow, 0f);
        }

        if (rightIKWeight > 0f)
        {
            avatarAnimator.SetIKPositionWeight(AvatarIKGoal.RightHand, rightIKWeight);
            avatarAnimator.SetIKRotationWeight(AvatarIKGoal.RightHand, rightIKWeight);
            avatarAnimator.SetIKPosition(AvatarIKGoal.RightHand, rightTargetPos);
            avatarAnimator.SetIKRotation(AvatarIKGoal.RightHand, naturalRotation);

            Vector3 rightHint = rightHand.position + avatarAnimator.transform.TransformDirection(new Vector3(-elbowHintOffset.x, elbowHintOffset.y, elbowHintOffset.z));
            avatarAnimator.SetIKHintPositionWeight(AvatarIKHint.RightElbow, rightIKWeight);
            avatarAnimator.SetIKHintPosition(AvatarIKHint.RightElbow, rightHint);
        }
        else
        {
            avatarAnimator.SetIKPositionWeight(AvatarIKGoal.RightHand, 0f);
            avatarAnimator.SetIKRotationWeight(AvatarIKGoal.RightHand, 0f);
            avatarAnimator.SetIKHintPositionWeight(AvatarIKHint.RightElbow, 0f);
        }
    }

    Vector3 GetMouseWorldPosition()
    {
        Vector3 mouse = Input.mousePosition;
        mouse.z = Mathf.Abs(mainCam.transform.position.z);
        return mainCam.ScreenToWorldPoint(mouse);
    }

    Vector3 GetClampedMouseTarget()
    {
        Vector3 worldPos = GetMouseWorldPosition();
        if (chest == null) return worldPos;

        Vector3 local = avatarAnimator.transform.InverseTransformPoint(worldPos);
        local.z = Mathf.Max(handZOffset, local.z);
        local = Vector3.ClampMagnitude(local, maxHandDistance);
        return avatarAnimator.transform.TransformPoint(local);
    }

    void OnDrawGizmos()
    {
        if (!showDebugGizmos || !Application.isPlaying) return;

        Gizmos.color = gizmoColor;
        if (leftHand) Gizmos.DrawWireSphere(leftHand.position, interactionRadius);
        if (rightHand) Gizmos.DrawWireSphere(rightHand.position, interactionRadius);
    }
}