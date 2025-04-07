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
    public bool featureEnabled = true;

    private class BoneTracking
    {
        public Transform bone;
        public List<GameObject> objects = new List<GameObject>();
    }

    private Dictionary<ParticleRule, BoneTracking> trackingMap = new Dictionary<ParticleRule, BoneTracking>();

    void Start()
    {
        if (animator == null) animator = GetComponent<Animator>();

        foreach (var rule in rules)
        {
            Transform boneTransform = animator.GetBoneTransform(rule.targetBone);
            if (boneTransform != null)
            {
                BoneTracking tracking = new BoneTracking
                {
                    bone = boneTransform,
                    objects = new List<GameObject>(rule.linkedObjects)
                };

                foreach (var obj in tracking.objects)
                {
                    if (obj != null)
                    {
                        obj.SetActive(false); // Initially inactive
                    }
                }

                trackingMap[rule] = tracking;
            }
        }
    }

    void Update()
    {
        if (!featureEnabled) return;

        foreach (var kvp in trackingMap)
        {
            ParticleRule rule = kvp.Key;
            BoneTracking tracking = kvp.Value;

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

            foreach (var obj in tracking.objects)
            {
                if (obj != null)
                {
                    obj.SetActive(shouldBeActive);

                    if (shouldBeActive)
                    {
                        obj.transform.position = tracking.bone.position;
                        obj.transform.rotation = tracking.bone.rotation;
                    }
                }
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
