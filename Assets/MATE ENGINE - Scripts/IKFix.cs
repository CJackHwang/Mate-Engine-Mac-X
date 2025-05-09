using System.Collections.Generic;
using UnityEngine;

[ExecuteInEditMode]
[RequireComponent(typeof(Animator))]
public class IKFix : MonoBehaviour
{
    [System.Serializable]
    public class IKFixState
    {
        public string stateName;
        public bool fixFeetIK;
    }

    public List<IKFixState> ikFixStates = new List<IKFixState>();
    public float blendSpeed = 5f; 
    private Animator animator;
    private float currentIKWeight = 0f; 

    private void Awake()
    {
        animator = GetComponent<Animator>();
    }

    private void OnAnimatorIK(int layerIndex)
    {
        if (animator == null || !animator.isActiveAndEnabled)
            return;

        AnimatorStateInfo stateInfo = animator.GetCurrentAnimatorStateInfo(layerIndex);
        bool shouldApplyIK = false;
        foreach (var state in ikFixStates)
        {
            if (state.fixFeetIK && stateInfo.IsName(state.stateName))
            {
                shouldApplyIK = true;
                break;
            }
        }

        float targetWeight = shouldApplyIK ? 1f : 0f;
        currentIKWeight = Mathf.MoveTowards(currentIKWeight, targetWeight, Time.deltaTime * blendSpeed);

        if (currentIKWeight > 0f)
        {
            ApplyFootIK(AvatarIKGoal.LeftFoot, currentIKWeight);
            ApplyFootIK(AvatarIKGoal.RightFoot, currentIKWeight);
        }
        else
        {
            animator.SetIKPositionWeight(AvatarIKGoal.LeftFoot, 0f);
            animator.SetIKRotationWeight(AvatarIKGoal.LeftFoot, 0f);
            animator.SetIKPositionWeight(AvatarIKGoal.RightFoot, 0f);
            animator.SetIKRotationWeight(AvatarIKGoal.RightFoot, 0f);
        }
    }

    private void ApplyFootIK(AvatarIKGoal foot, float weight)
    {
        animator.SetIKPositionWeight(foot, weight);
        animator.SetIKRotationWeight(foot, weight);
        animator.SetIKPosition(foot, animator.GetIKPosition(foot));
        animator.SetIKRotation(foot, animator.GetIKRotation(foot));
    }
}
