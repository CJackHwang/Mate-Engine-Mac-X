using UnityEditor;
using UnityEngine;
using System;
using System.IO;
using System.IO.Compression;
using System.Collections.Generic;

public class ModExporterWindow : EditorWindow
{
    enum ExportMode { StandardMod, DanceMod }

    GameObject exportObject;
    string modName = "MyMod";
    string author = "";
    string description = "";
    string weblink = "";
    BuildTarget buildTarget = BuildTarget.StandaloneWindows64;
    bool showMoreInfo = false;

    ExportMode mode = ExportMode.StandardMod;
    MEDanceModCreator danceCreator;
    string danceModName = "MyDance";
    string danceAuthor = "";
    BuildTarget danceBuildTarget = BuildTarget.StandaloneWindows64;

    [MenuItem("MateEngine/ME Mod Exporter")]
    public static void ShowWindow() => GetWindow<ModExporterWindow>("ME Mod Exporter");

    [MenuItem("MateEngine/Export Scene Registry")]
    public static void ExportSceneRegistry()
    {
        var allRoots = UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects();
        List<SceneObjectInfo> all = new List<SceneObjectInfo>();
        foreach (var root in allRoots) CollectSceneObjects(root.transform, "", all);
        var wrapper = new SceneRegistry { SceneObjects = all };
        var json = JsonUtility.ToJson(wrapper, true);
        File.WriteAllText(Path.Combine(Application.dataPath, "scene_registry.json"), json);
        Debug.Log("[ModExporter] scene_registry.json written.");
    }

    static void CollectSceneObjects(Transform tr, string parentPath, List<SceneObjectInfo> list)
    {
        string path = string.IsNullOrEmpty(parentPath) ? tr.name : parentPath + "/" + tr.name;
        var comps = new List<string>();
        foreach (var c in tr.GetComponents<Component>()) if (c != null) comps.Add(c.GetType().Name);
        list.Add(new SceneObjectInfo { name = tr.name, path = path, components = comps });
        foreach (Transform child in tr) CollectSceneObjects(child, path, list);
    }

    void OnGUI()
    {
        mode = (ExportMode)GUILayout.Toolbar((int)mode, new[] { "Standard Mod", "Dance Mod" });
        GUILayout.Space(8);
        if (mode == ExportMode.StandardMod) DrawStandardModGUI();
        else DrawDanceModGUI();
    }

    void DrawStandardModGUI()
    {
        GUILayout.Label("Export Mod (.me with Scene Linking)", EditorStyles.boldLabel);
        GUILayout.Space(8);
        EditorGUILayout.HelpBox("Modding Limitations\nThe MateEngine SDK is limited and only supports a few modding aspects. Creating your own C# assemblies is not allowed, as we aim to prevent any potential malware distribution.", MessageType.Info);
        GUILayout.Space(12);
        showMoreInfo = EditorGUILayout.Foldout(showMoreInfo, "Display the mod limitations", true);
        if (showMoreInfo)
        {
            EditorGUILayout.HelpBox("- You can create GameObjects that hold existing components\n- You can use SDK scripts to remove things at runtime\n- You can use prefabs with Audio Sources, Particle Systems, Meshes", MessageType.None);
        }
        exportObject = (GameObject)EditorGUILayout.ObjectField("Root GameObject", exportObject, typeof(GameObject), true);
        modName = EditorGUILayout.TextField("Mod Name", modName);
        author = EditorGUILayout.TextField("Author", author);
        GUILayout.Label("Description");
        description = EditorGUILayout.TextArea(description, GUILayout.Height(60));
        weblink = EditorGUILayout.TextField("Weblink", weblink);
        buildTarget = (BuildTarget)EditorGUILayout.EnumPopup("Build Target", buildTarget);
        GUI.enabled = exportObject != null && !string.IsNullOrEmpty(modName);
        if (GUILayout.Button("Export Mod", GUILayout.Height(40))) ExportMod();
        GUI.enabled = true;
    }

    void DrawDanceModGUI()
    {
        GUILayout.Label("Export Dance Mod (.me with bundle + metadata)", EditorStyles.boldLabel);
        GUILayout.Space(8);
        danceCreator = (MEDanceModCreator)EditorGUILayout.ObjectField("Dance Creator", danceCreator, typeof(MEDanceModCreator), true);
        danceModName = EditorGUILayout.TextField("Dance Name", danceModName);
        danceAuthor = EditorGUILayout.TextField("Author", danceAuthor);
        danceBuildTarget = (BuildTarget)EditorGUILayout.EnumPopup("Build Target", danceBuildTarget);
        GUI.enabled = danceCreator != null && !string.IsNullOrEmpty(danceModName);
        if (GUILayout.Button("Export Dance Mod", GUILayout.Height(40))) ExportDanceMod();
        GUI.enabled = true;
    }

    void ExportMod()
    {
        string tempDir = Path.Combine("Assets/__ModExportTemp__");
        if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        Directory.CreateDirectory(tempDir);

        string prefabPath = Path.Combine(tempDir, modName + ".prefab").Replace("\\", "/");
        var prefab = PrefabUtility.SaveAsPrefabAssetAndConnect(exportObject, prefabPath, InteractionMode.UserAction);
        if (prefab == null) { Debug.LogError("[ModExporter] Failed to create prefab."); return; }

        var sceneRefs = new Dictionary<string, string>();
        ExtractSceneLinks(exportObject, sceneRefs);

        string buildDir = Path.Combine("TempModBuild", modName);
        if (Directory.Exists(buildDir)) Directory.Delete(buildDir, true);
        Directory.CreateDirectory(buildDir);

        AssetImporter.GetAtPath(prefabPath).assetBundleName = modName.ToLower() + ".bundle";
        BuildPipeline.BuildAssetBundles(buildDir, BuildAssetBundleOptions.None, buildTarget);

        File.WriteAllText(Path.Combine(buildDir, "modinfo.json"),
            "{\n" +
            $"  \"name\": \"{modName}\",\n" +
            $"  \"author\": \"{author}\",\n" +
            $"  \"description\": \"{description}\",\n" +
            $"  \"weblink\": \"{weblink}\",\n" +
            $"  \"buildTarget\": \"{buildTarget}\",\n" +
            $"  \"timestamp\": \"{DateTime.UtcNow:O}\"\n" +
            "}");

        if (sceneRefs.Count > 0)
        {
            File.WriteAllText(Path.Combine(buildDir, "scene_links.json"),
                JsonUtility.ToJson(new SerializableRefMap(sceneRefs), true));
        }

        string finalDir = Path.Combine("ExportedMods");
        Directory.CreateDirectory(finalDir);
        string mePath = Path.Combine(finalDir, modName + ".me");
        if (File.Exists(mePath)) File.Delete(mePath);
        ZipFile.CreateFromDirectory(buildDir, mePath);

        AssetDatabase.RemoveAssetBundleName(modName.ToLower() + ".bundle", true);
        AssetDatabase.DeleteAsset(prefabPath);
        if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        if (Directory.Exists(buildDir)) Directory.Delete(buildDir, true);

        AssetDatabase.Refresh();
        EditorUtility.RevealInFinder(mePath);
        Debug.Log("[ModExporter] Export finished: " + mePath);
    }

    void ExportDanceMod()
    {
        var meta = danceCreator.GetMetadata();
        var clip = danceCreator.GetDanceClip();
        var audio = danceCreator.GetAudioClip();

        string tempRoot = "Assets/__DanceExportTemp__";
        if (Directory.Exists(tempRoot)) Directory.Delete(tempRoot, true);
        Directory.CreateDirectory(tempRoot);

        string buildDir = Path.Combine("TempDanceBuild", danceModName);
        if (Directory.Exists(buildDir)) Directory.Delete(buildDir, true);
        Directory.CreateDirectory(buildDir);

        string clipAssetPath = null;
        if (clip != null)
        {
            var copy = new AnimationClip();
            EditorUtility.CopySerialized(clip, copy);
            clipAssetPath = Path.Combine(tempRoot, danceModName + "_dance.anim").Replace("\\", "/");
            AssetDatabase.CreateAsset(copy, clipAssetPath);
            AssetImporter.GetAtPath(clipAssetPath).assetBundleName = "dance.bundle";
        }

        string audioAssetPath = null;
        if (audio != null)
        {
            string src = AssetDatabase.GetAssetPath(audio);
            if (!string.IsNullOrEmpty(src))
            {
                audioAssetPath = Path.Combine(tempRoot, Path.GetFileName(src)).Replace("\\", "/");
                AssetDatabase.CopyAsset(src, audioAssetPath);
                AssetImporter.GetAtPath(audioAssetPath).assetBundleName = "dance.bundle";
            }
        }

        BuildPipeline.BuildAssetBundles(buildDir, BuildAssetBundleOptions.None, danceBuildTarget);

        var danceInfo = new DanceExportMeta
        {
            songName = meta.songName,
            songAuthor = meta.songAuthor,
            mmdAuthor = meta.mmdAuthor,
            songLength = meta.songLength,
            placeholderClipName = meta.placeholderClipName
        };
        File.WriteAllText(Path.Combine(buildDir, "dance_meta.json"), JsonUtility.ToJson(danceInfo, true));

        string finalDir = Path.Combine("ExportedMods");
        Directory.CreateDirectory(finalDir);
        string mePath = Path.Combine(finalDir, danceModName + ".me");
        if (File.Exists(mePath)) File.Delete(mePath);
        ZipFile.CreateFromDirectory(buildDir, mePath);

        if (!string.IsNullOrEmpty(clipAssetPath)) AssetDatabase.DeleteAsset(clipAssetPath);
        if (!string.IsNullOrEmpty(audioAssetPath)) AssetDatabase.DeleteAsset(audioAssetPath);
        if (Directory.Exists(tempRoot)) Directory.Delete(tempRoot, true);
        if (Directory.Exists(buildDir)) Directory.Delete(buildDir, true);

        AssetDatabase.Refresh();
        EditorUtility.RevealInFinder(mePath);
        Debug.Log("[ModExporter] Dance export finished: " + mePath);
    }

    void ExtractSceneLinks(GameObject root, Dictionary<string, string> output)
    {
        var prefabObjs = new HashSet<GameObject>();
        foreach (var t in root.GetComponentsInChildren<Transform>(true)) prefabObjs.Add(t.gameObject);

        foreach (var mb in root.GetComponentsInChildren<MonoBehaviour>(true))
        {
            if (mb == null) continue;
            Type mbType = mb.GetType();
            var fields = mbType.GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            foreach (var field in fields)
            {
                string basePath = mbType.Name + "." + field.Name;
                object value = field.GetValue(mb);
                if (value == null) continue;

                Type fieldType = field.FieldType;

                if (fieldType == typeof(GameObject) || fieldType.IsSubclassOf(typeof(Component)))
                {
                    TryRecordReference(value, basePath, prefabObjs, output);
                }
                else if (typeof(System.Collections.IEnumerable).IsAssignableFrom(fieldType) || fieldType.IsArray)
                {
                    int index = 0;
                    foreach (var item in (System.Collections.IEnumerable)value)
                    {
                        if (item == null) continue;
                        string indexedPath = basePath + "[" + index + "]";
                        ScanObjectFields(item, indexedPath, prefabObjs, output);
                        index++;
                    }
                }
                else
                {
                    ScanObjectFields(value, basePath, prefabObjs, output);
                }
            }
        }
    }

    void ScanObjectFields(object obj, string parentPath, HashSet<GameObject> prefabObjs, Dictionary<string, string> output)
    {
        var subFields = obj.GetType().GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        foreach (var subField in subFields)
        {
            object val = subField.GetValue(obj);
            if (val == null) continue;

            string path = parentPath + "." + subField.Name;
            if (val is GameObject || val is Component) TryRecordReference(val, path, prefabObjs, output);
        }
    }

    void TryRecordReference(object val, string path, HashSet<GameObject> prefabObjs, Dictionary<string, string> output)
    {
        GameObject go = val as GameObject;
        if (val is Component comp) go = comp.gameObject;
        if (go != null && !prefabObjs.Contains(go)) output[path] = GetHierarchyPath(go.transform);
    }

    string GetHierarchyPath(Transform t)
    {
        var path = t.name;
        while (t.parent != null)
        {
            t = t.parent;
            path = t.name + "/" + path;
        }
        return path;
    }

    [Serializable] public class SceneRegistry { public List<SceneObjectInfo> SceneObjects; }
    [Serializable] public class SceneObjectInfo { public string name; public string path; public List<string> components; }
    [Serializable]
    public class SerializableRefMap
    {
        public List<string> keys = new List<string>();
        public List<string> values = new List<string>();
        public SerializableRefMap(Dictionary<string, string> dict) { foreach (var kv in dict) { keys.Add(kv.Key); values.Add(kv.Value); } }
    }
    [Serializable]
    class DanceExportMeta
    {
        public string songName;
        public string songAuthor;
        public string mmdAuthor;
        public float songLength;
        public string placeholderClipName;
    }
}
