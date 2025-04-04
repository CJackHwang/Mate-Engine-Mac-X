using UnityEngine;
using System.Collections.Generic;

public class AvatarParticleHandler : MonoBehaviour
{
    [System.Serializable]
    public class ParticleRule
    {
        public string stateOrParameterName;
        public bool useParameter = false;
        public HumanBodyBones targetBone;
        public List<GameObject> linkedObjects = new List<GameObject>();
    }

    public Animator animator;
    public List<ParticleRule> rules = new List<ParticleRule>();

    private Dictionary<ParticleRule, Transform> boneTargets = new Dictionary<ParticleRule, Transform>();

    void Start()
    {
        if (animator == null) animator = GetComponent<Animator>();

        foreach (var rule in rules)
        {
            Transform boneTransform = animator.GetBoneTransform(rule.targetBone);
            if (boneTransform != null)
            {
                boneTargets[rule] = boneTransform;

                foreach (var obj in rule.linkedObjects)
                {
                    if (obj != null)
                    {
                        obj.transform.SetParent(boneTransform, worldPositionStays: false);
                        obj.transform.localPosition = Vector3.zero;
                        obj.transform.localRotation = Quaternion.identity;
                        obj.SetActive(false); // Initially inactive
                    }
                }
            }
        }
    }

    void Update()
    {
        foreach (var rule in rules)
        {
            bool shouldBeActive = false;

            if (rule.useParameter)
            {
                if (animator.HasParameter(rule.stateOrParameterName, AnimatorControllerParameterType.Bool))
                {
                    shouldBeActive = animator.GetBool(rule.stateOrParameterName);
                }
            }
            else
            {
                AnimatorStateInfo state = animator.GetCurrentAnimatorStateInfo(0);
                shouldBeActive = state.IsName(rule.stateOrParameterName);
            }

            foreach (var obj in rule.linkedObjects)
            {
                if (obj != null)
                    obj.SetActive(shouldBeActive);
            }
        }
    }
}

public static class AnimatorExtensions
{
    public static bool HasParameter(this Animator animator, string paramName, AnimatorControllerParameterType type)
    {
        foreach (var param in animator.parameters)
        {
            if (param.name == paramName && param.type == type)
                return true;
        }
        return false;
    }
}
