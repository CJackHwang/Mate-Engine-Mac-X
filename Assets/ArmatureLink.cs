using UnityEngine;
using System.Collections.Generic;

public class ArmatureLink : MonoBehaviour
{
    [Header("Setup")]
    public Transform targetAvatarArmature; // The avatar's root armature (usually the Hips bone)
    public Transform propArmatureRoot; // The root of the armature being linked

    [Header("Options")]
    public bool maintainOriginalTransforms = true; // Whether to maintain transforms after linking

    private Dictionary<string, Transform> avatarBoneMap = new Dictionary<string, Transform>();

    void Start()
    {
        if (targetAvatarArmature == null || propArmatureRoot == null)
        {
            Debug.LogError("ArmatureLink: Target avatar armature or prop armature root is not assigned!");
            return;
        }

        BuildBoneMap(targetAvatarArmature, avatarBoneMap);
        LinkArmature(propArmatureRoot);
    }

    private void BuildBoneMap(Transform root, Dictionary<string, Transform> boneMap)
    {
        foreach (Transform bone in root.GetComponentsInChildren<Transform>())
        {
            if (!boneMap.ContainsKey(bone.name))
            {
                boneMap.Add(bone.name, bone);
            }
        }
    }

    private void LinkArmature(Transform propRoot)
    {
        foreach (Transform propBone in propRoot.GetComponentsInChildren<Transform>())
        {
            if (avatarBoneMap.TryGetValue(propBone.name, out Transform matchingBone))
            {
                // Preserve transform if option is enabled
                Vector3 localPosition = propBone.localPosition;
                Quaternion localRotation = propBone.localRotation;
                Vector3 localScale = propBone.localScale;

                propBone.SetParent(matchingBone, true);

                if (maintainOriginalTransforms)
                {
                    propBone.localPosition = localPosition;
                    propBone.localRotation = localRotation;
                    propBone.localScale = localScale;
                }

                Debug.Log($"Linked bone {propBone.name} to {matchingBone.name}");
            }
        }
    }
}
