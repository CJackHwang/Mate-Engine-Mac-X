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
        public List<GameObject> linkedObjects = new();
    }

    public Animator animator;
    public List<ParticleRule> rules = new();
    public bool featureEnabled = true;

    private struct RuleCache
    {
        public Transform bone;
        public GameObject[] objects;
        public int parameterIndex;
        public bool useParameter;
        public string stateName;
    }

    private RuleCache[] ruleCache = System.Array.Empty<RuleCache>();
    private AnimatorControllerParameter[] animatorParams;

    void Start()
    {
        if (animator == null) animator = GetComponent<Animator>();
        animatorParams = animator.parameters;

        var cacheList = new List<RuleCache>(rules.Count);
        for (int i = 0; i < rules.Count; i++)
        {
            var rule = rules[i];
            var bone = animator.GetBoneTransform(rule.targetBone);
            if (bone == null) continue;

            var objs = new List<GameObject>();
            for (int j = 0; j < rule.linkedObjects.Count; j++)
            {
                var obj = rule.linkedObjects[j];
                if (obj != null)
                {
                    obj.SetActive(false);
                    objs.Add(obj);
                }
            }

            int paramIndex = -1;
            if (rule.useParameter)
            {
                for (int p = 0; p < animatorParams.Length; p++)
                {
                    if (animatorParams[p].type == AnimatorControllerParameterType.Bool &&
                        animatorParams[p].name == rule.stateOrParameterName)
                    {
                        paramIndex = p;
                        break;
                    }
                }
            }

            cacheList.Add(new RuleCache
            {
                bone = bone,
                objects = objs.ToArray(),
                parameterIndex = paramIndex,
                useParameter = rule.useParameter,
                stateName = rule.stateOrParameterName
            });
        }

        ruleCache = cacheList.ToArray();
    }

    void Update()
    {
        if (!featureEnabled || animator == null) return;

        var stateInfo = animator.GetCurrentAnimatorStateInfo(0);

        for (int i = 0; i < ruleCache.Length; i++)
        {
            ref var rule = ref ruleCache[i];
            bool isActive = false;

            if (rule.useParameter && rule.parameterIndex >= 0)
            {
                isActive = animator.GetBool(animatorParams[rule.parameterIndex].name);
            }
            else
            {
                isActive = stateInfo.IsName(rule.stateName);
            }

            for (int j = 0; j < rule.objects.Length; j++)
            {
                var obj = rule.objects[j];
                if (obj == null) continue;

                obj.SetActive(isActive);
                if (isActive)
                {
                    obj.transform.position = rule.bone.position;
                    obj.transform.rotation = rule.bone.rotation;
                }
            }
        }
    }
}