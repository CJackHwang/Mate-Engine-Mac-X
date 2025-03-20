using UnityEngine;
using System.Collections.Generic;

#if UNITY_EDITOR
using UnityEditor;
#endif

public class ArmaturePortal : MonoBehaviour
{
    [Header("Drag and drop armatures here")]
    public Transform avatarArmature; // The avatar's armature
    public Transform objectArmature; // The object/clothing's armature

    private Dictionary<string, Transform> avatarBoneMap = new Dictionary<string, Transform>();

    void Start()
    {
        if (avatarArmature == null || objectArmature == null)
        {
            Debug.LogError("[ArmaturePortal] Please assign both armatures.");
            return;
        }

        // Store avatar's bones in a dictionary for quick lookup
        avatarBoneMap.Clear();
        foreach (Transform bone in avatarArmature.GetComponentsInChildren<Transform>())
        {
            avatarBoneMap[bone.name] = bone;
        }

        // Assign bones
        MatchBones(objectArmature);
    }

    private void MatchBones(Transform objectRoot)
    {
        foreach (Transform objBone in objectRoot.GetComponentsInChildren<Transform>())
        {
            if (avatarBoneMap.TryGetValue(objBone.name, out Transform matchingBone))
            {
                objBone.SetParent(matchingBone, true);
                Debug.Log($"[ArmaturePortal] Linked: {objBone.name} -> {matchingBone.name}");
            }
            else
            {
                Debug.LogWarning($"[ArmaturePortal] No match found for: {objBone.name}");
            }
        }
    }

#if UNITY_EDITOR
    [CustomEditor(typeof(ArmaturePortal))]
    public class ArmaturePortalEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            ArmaturePortal script = (ArmaturePortal)target;
            if (GUILayout.Button("Link Armatures Now"))
            {
                script.Start();
                EditorUtility.SetDirty(script);
            }
        }
    }
#endif
}
