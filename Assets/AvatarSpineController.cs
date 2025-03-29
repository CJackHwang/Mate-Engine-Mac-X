using UnityEngine;
using System.Collections.Generic;
using System.Linq;

[RequireComponent(typeof(Animator))]
public class AvatarSpineController : MonoBehaviour
{
    [Header("Spine Mouse Tracking Settings")]
    public bool enableSpineTracking = true;

    [Tooltip("Max rotation angle (degrees).")]
    [Range(-90f, 90f)] public float minRotation = -15f;
    [Range(-90f, 90f)] public float maxRotation = 15f;

    [Tooltip("How quickly the spine follows the target.")]
    [Range(1f, 50f)] public float smoothness = 25f;

    [Tooltip("How quickly the spine resets when leaving an allowed state.")]
    [Range(1f, 50f)] public float resetSmoothness = 10f;

    [Header("Allowed Animator States")]
    [Tooltip("Only allow spine tracking in these states.")]
    public List<string> allowedStates = new List<string> { "Idle", "HoverReaction" };

    private Animator animator;
    private Camera mainCam;
    private Transform spineBone;
    private Transform chestBone;
    private Transform upperChestBone;
    private Transform spineDriver; // Proxy driver like head tracking
    private Quaternion defaultRotation; // Stores default spine rotation

    void Start()
    {
        animator = GetComponent<Animator>();
        mainCam = Camera.main;

        if (animator != null && animator.isHuman)
        {
            spineBone = animator.GetBoneTransform(HumanBodyBones.Spine);
            chestBone = animator.GetBoneTransform(HumanBodyBones.Chest);
            upperChestBone = animator.GetBoneTransform(HumanBodyBones.UpperChest);

            if (spineBone != null)
            {
                // Create spine driver
                spineDriver = new GameObject("SpineDriver").transform;
                spineDriver.SetParent(spineBone.parent);
                spineDriver.localPosition = spineBone.localPosition;
                spineDriver.localRotation = spineBone.localRotation;

                // Store default rotation
                defaultRotation = spineBone.localRotation;
            }
        }

        if (spineBone == null)
        {
            Debug.LogWarning("⚠️ No spine bone found for spine tracking.");
            enableSpineTracking = false;
        }
    }

    void LateUpdate()
    {
        if (!enableSpineTracking || spineBone == null || spineDriver == null) return;

        if (IsInAllowedState())
        {
            // Get mouse position in world space
            Vector3 mouseScreen = new Vector3(Input.mousePosition.x, Input.mousePosition.y, mainCam.nearClipPlane);
            Vector3 targetWorldPosition = mainCam.ScreenToWorldPoint(mouseScreen);

            // Calculate direction to the mouse
            Vector3 directionToMouse = (targetWorldPosition - spineDriver.position).normalized;
            directionToMouse.y = 0f; // Keep it horizontal

            // Calculate target rotation
            float targetRotationY = Mathf.Lerp(minRotation, maxRotation, Input.mousePosition.x / Screen.width);
            Quaternion targetRotation = Quaternion.Euler(0f, -targetRotationY, 0f);

            // Smoothly rotate the spine
            spineDriver.localRotation = Quaternion.Slerp(spineDriver.localRotation, targetRotation, Time.deltaTime * smoothness);
        }
        else
        {
            // Smoothly return to default rotation when leaving an allowed state
            spineDriver.localRotation = Quaternion.Slerp(spineDriver.localRotation, defaultRotation, Time.deltaTime * resetSmoothness);
        }

        // Apply the rotation to the bones
        spineBone.localRotation = spineDriver.localRotation;
        if (chestBone != null)
            chestBone.localRotation = Quaternion.Slerp(Quaternion.identity, spineDriver.localRotation, 0.8f);
        if (upperChestBone != null)
            upperChestBone.localRotation = Quaternion.Slerp(Quaternion.identity, spineDriver.localRotation, 0.6f);
    }

    bool IsInAllowedState()
    {
        if (animator == null || allowedStates.Count == 0) return false;

        var currentState = animator.GetCurrentAnimatorStateInfo(0);
        return allowedStates.Any(state => currentState.IsName(state));
    }

    void OnDestroy()
    {
        if (spineDriver != null)
            Destroy(spineDriver.gameObject);
    }
}
