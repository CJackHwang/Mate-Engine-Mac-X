#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using VRM;
using System.Collections.Generic;
using System.Linq;
using UnityEngine.Animations;

[CustomEditor(typeof(VrmVersionChecker))]
public class VrmVersionCheckerEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        VrmVersionChecker checker = (VrmVersionChecker)target;

        if (checker.vrmObject != null)
        {
            GameObject model = checker.vrmObject;
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("VRM Debug Info", EditorStyles.boldLabel);

            string version = GetVrmVersion(model, out string metaTitle, out string metaAuthor);
            EditorGUILayout.LabelField("VRM Version", version);
            if (!string.IsNullOrEmpty(metaTitle)) EditorGUILayout.LabelField("Title", metaTitle);
            if (!string.IsNullOrEmpty(metaAuthor)) EditorGUILayout.LabelField("Author", metaAuthor);

            DrawSeparator();

            string[] shaders = GetShaders(model);
            EditorGUILayout.LabelField("Shaders Used", string.Join(", ", shaders));

            string[] materials = GetMaterialNames(model);
            EditorGUILayout.LabelField("Materials", string.Join(", ", materials));

            DrawSeparator();

            int boneCount = GetBoneCount(model);
            EditorGUILayout.LabelField("Bone Count", boneCount.ToString());

            int springCount = GetSpringBoneCount(model);
            EditorGUILayout.LabelField("Spring Bones", springCount.ToString());

            DrawSeparator();

            GetMeshStats(model, out int meshCount, out int polyCount);
            EditorGUILayout.LabelField("Mesh Count", meshCount.ToString());
            EditorGUILayout.LabelField("Total Triangles", polyCount.ToString());

            int blendshapeCount = GetBlendshapeCount(model);
            EditorGUILayout.LabelField("BlendShapes", blendshapeCount.ToString());

            DrawSeparator();

            DrawHumanoidMapping(model);

            DrawSeparator();

            DrawRuntimeComponents(model);
        }
    }

    private string GetVrmVersion(GameObject obj, out string title, out string author)
    {
        title = "";
        author = "";

        var meta0 = obj.GetComponent<VRMMeta>();
        if (meta0 != null)
        {
            if (meta0.Meta != null)
            {
                title = meta0.Meta.Title;
                author = meta0.Meta.Author;
            }
            return "0.x";
        }

#if VRM10_EXISTS
        var meta10 = obj.GetComponent<VRM10.VRM10Meta>();
        if (meta10 != null)
        {
            title = meta10.Title;
            author = meta10.Author;
            return meta10.SpecVersion;
        }
#endif
        return "Unknown";
    }

    private string[] GetShaders(GameObject obj)
    {
        var renderers = obj.GetComponentsInChildren<Renderer>(true);
        HashSet<string> shaders = new HashSet<string>();

        foreach (var renderer in renderers)
        {
            foreach (var mat in renderer.sharedMaterials)
            {
                if (mat != null && mat.shader != null)
                {
                    shaders.Add(mat.shader.name);
                }
            }
        }

        return shaders.ToArray();
    }

    private string[] GetMaterialNames(GameObject obj)
    {
        var renderers = obj.GetComponentsInChildren<Renderer>(true);
        HashSet<string> materials = new HashSet<string>();

        foreach (var renderer in renderers)
        {
            foreach (var mat in renderer.sharedMaterials)
            {
                if (mat != null)
                {
                    materials.Add(mat.name);
                }
            }
        }

        return materials.ToArray();
    }

    private int GetBoneCount(GameObject obj)
    {
        return obj.GetComponentsInChildren<Transform>(true).Length;
    }

    private int GetSpringBoneCount(GameObject obj)
    {
        int count = 0;
        count += obj.GetComponentsInChildren<VRMSpringBone>(true).Length;

#if VRM10_EXISTS
        count += obj.GetComponentsInChildren<VRM10.VRM10SpringBone>(true).Length;
#endif

        return count;
    }

    private void GetMeshStats(GameObject obj, out int meshCount, out int polyCount)
    {
        meshCount = 0;
        polyCount = 0;

        var meshes = obj.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        meshCount = meshes.Length;

        foreach (var smr in meshes)
        {
            if (smr.sharedMesh != null)
            {
                polyCount += smr.sharedMesh.triangles.Length / 3;
            }
        }
    }

    private int GetBlendshapeCount(GameObject obj)
    {
        int total = 0;
        var meshes = obj.GetComponentsInChildren<SkinnedMeshRenderer>(true);

        foreach (var smr in meshes)
        {
            if (smr.sharedMesh != null)
            {
                total += smr.sharedMesh.blendShapeCount;
            }
        }

        return total;
    }

    private void DrawHumanoidMapping(GameObject obj)
    {
        var animator = obj.GetComponent<Animator>();
        if (animator == null || !animator.isHuman)
        {
            EditorGUILayout.LabelField("Humanoid Mapping", "No humanoid avatar");
            return;
        }

        EditorGUILayout.LabelField("Humanoid Bone Mappings", EditorStyles.boldLabel);

        foreach (HumanBodyBones bone in System.Enum.GetValues(typeof(HumanBodyBones)))
        {
            if (bone == HumanBodyBones.LastBone) continue;

            Transform t = animator.GetBoneTransform(bone);
            if (t != null)
            {
                EditorGUILayout.LabelField(bone.ToString(), t.name);
            }
        }
    }

    private void DrawRuntimeComponents(GameObject obj)
    {
        EditorGUILayout.LabelField("Runtime VRM Components", EditorStyles.boldLabel);

        var components = obj.GetComponents<MonoBehaviour>();
        foreach (var c in components)
        {
            if (c == null) continue;
            EditorGUILayout.LabelField(c.GetType().Name);
        }
    }

    private void DrawSeparator()
    {
        EditorGUILayout.Space(5);
        EditorGUILayout.LabelField("", GUI.skin.horizontalSlider);
    }


}


#endif
