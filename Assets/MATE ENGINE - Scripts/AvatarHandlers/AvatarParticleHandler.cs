using UnityEngine;
using System.Collections.Generic;

public class AvatarParticleHandler : MonoBehaviour
{
    [System.Serializable]
    public class ParticleRule
    {
        public List<string> stateOrParameterNames = new();
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
        public int[] paramIndices;
        public bool useParameter;
        public List<string> stateNameList;
    }

    private RuleCache[] cache = System.Array.Empty<RuleCache>();
    private AnimatorControllerParameter[] animParams;

    void Start()
    {
        animator ??= GetComponent<Animator>();
        animParams = animator.parameters;
        var tmp = new List<RuleCache>(rules.Count);

        foreach (var rule in rules)
        {
            var bone = animator.GetBoneTransform(rule.targetBone);
            if (!bone) continue;

            var objs = rule.linkedObjects.FindAll(o => o != null);
            foreach (var o in objs) o.SetActive(false);

            var indices = new List<int>();
            if (rule.useParameter)
            {
                foreach (var name in rule.stateOrParameterNames)
                {
                    for (int i = 0; i < animParams.Length; i++)
                    {
                        if (animParams[i].type == AnimatorControllerParameterType.Bool &&
                            animParams[i].name == name)
                        {
                            indices.Add(i);
                            break;
                        }
                    }
                }
            }

            tmp.Add(new RuleCache
            {
                bone = bone,
                objects = objs.ToArray(),
                paramIndices = indices.ToArray(),
                useParameter = rule.useParameter,
                stateNameList = new List<string>(rule.stateOrParameterNames)
            });
        }

        cache = tmp.ToArray();
    }

    void Update()
    {
        if (!featureEnabled || !animator) return;
        var state = animator.GetCurrentAnimatorStateInfo(0);

        for (int i = 0; i < cache.Length; i++)
        {
            var r = cache[i];
            bool active = false;

            if (r.useParameter && r.paramIndices != null && r.paramIndices.Length > 0)
            {
                foreach (int idx in r.paramIndices)
                {
                    if (idx >= 0 && animator.GetBool(animParams[idx].name))
                    {
                        active = true;
                        break;
                    }
                }
            }
            else
            {
                foreach (var s in r.stateNameList)
                {
                    if (state.IsName(s))
                    {
                        active = true;
                        break;
                    }
                }
            }

            var bone = r.bone;
            var arr = r.objects;
            for (int j = 0; j < arr.Length; j++)
            {
                var o = arr[j];
                if (!o) continue;
                o.SetActive(active);
                if (active)
                    o.transform.SetPositionAndRotation(bone.position, bone.rotation);
            }
        }
    }
}
