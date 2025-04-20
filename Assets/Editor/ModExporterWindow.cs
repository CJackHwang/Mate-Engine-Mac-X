using UnityEditor;
using UnityEngine;
using System;
using System.IO;
using System.IO.Compression;
using System.Collections.Generic;
using System.Reflection;

public class ModExporterWindow : EditorWindow
{
    private GameObject exportObject;
    private string modName = "MyMod";
    private BuildTarget buildTarget = BuildTarget.StandaloneWindows64;

    [MenuItem("MateEngine/ME Mod Exporter")]
    public static void ShowWindow() => GetWindow<ModExporterWindow>("ME Mod Exporter");

    void OnGUI()
    {
        GUILayout.Label("Export Mod (.me Style with TargetPath)", EditorStyles.boldLabel);
        exportObject = (GameObject)EditorGUILayout.ObjectField("Root GameObject", exportObject, typeof(GameObject), true);
        modName = EditorGUILayout.TextField("Mod Name", modName);
        buildTarget = (BuildTarget)EditorGUILayout.EnumPopup("Build Target", buildTarget);

        GUI.enabled = exportObject != null && !string.IsNullOrEmpty(modName);
        if (GUILayout.Button("Export Mod")) ExportMod();
        GUI.enabled = true;
    }

    void ExportMod()
    {
        string tempDir = Path.Combine("Assets/__ModExportTemp__");
        if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        Directory.CreateDirectory(tempDir);

        string prefabPath = Path.Combine(tempDir, modName + ".prefab").Replace("\\", "/");
        var prefab = PrefabUtility.SaveAsPrefabAssetAndConnect(exportObject, prefabPath, InteractionMode.UserAction);
        if (prefab == null)
        {
            Debug.LogError("[ModExporter] Failed to create prefab.");
            return;
        }

        // Suche GameObject-Referenzpfade
        Dictionary<string, string> referencePaths = new Dictionary<string, string>();
        ExtractGameObjectReferences(exportObject, referencePaths);

        // AssetBundle vorbereiten
        string buildDir = Path.Combine("TempModBuild", modName);
        if (Directory.Exists(buildDir)) Directory.Delete(buildDir, true);
        Directory.CreateDirectory(buildDir);

        AssetImporter.GetAtPath(prefabPath).assetBundleName = modName.ToLower() + ".bundle";
        BuildPipeline.BuildAssetBundles(buildDir, BuildAssetBundleOptions.None, buildTarget);

        // modinfo schreiben
        File.WriteAllText(Path.Combine(buildDir, "modinfo.json"),
            "{\n" +
            $"  \"name\": \"{modName}\",\n" +
            $"  \"buildTarget\": \"{buildTarget}\",\n" +
            $"  \"timestamp\": \"{DateTime.UtcNow:O}\"\n" +
            "}");

        // Referenzpfade speichern
        string refPath = Path.Combine(buildDir, "reference_paths.json");
        File.WriteAllText(refPath, JsonUtility.ToJson(new SerializableRefMap(referencePaths), true));

        // ZIP als .me
        string finalDir = Path.Combine("ExportedMods");
        Directory.CreateDirectory(finalDir);
        string mePath = Path.Combine(finalDir, modName + ".me");
        if (File.Exists(mePath)) File.Delete(mePath);
        ZipFile.CreateFromDirectory(buildDir, mePath);

        // Aufräumen
        AssetDatabase.RemoveAssetBundleName(modName.ToLower() + ".bundle", true);
        AssetDatabase.DeleteAsset(prefabPath);
        if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        if (Directory.Exists(buildDir)) Directory.Delete(buildDir, true);

        AssetDatabase.Refresh();
        EditorUtility.RevealInFinder(mePath);
        Debug.Log("[ModExporter] Export abgeschlossen: " + mePath);
    }

    void ExtractGameObjectReferences(GameObject root, Dictionary<string, string> map)
    {
        foreach (var comp in root.GetComponentsInChildren<MonoBehaviour>(true))
        {
            if (comp == null) continue;
            var so = new SerializedObject(comp);
            var iter = so.GetIterator();
            if (iter.NextVisible(true))
            {
                do
                {
                    if (iter.propertyType == SerializedPropertyType.ObjectReference &&
                        iter.objectReferenceValue is GameObject go)
                    {
                        string fieldPath = comp.GetType().Name + "." + iter.propertyPath;
                        map[fieldPath] = go.name;
                    }
                } while (iter.NextVisible(false));
            }
        }
    }

    [Serializable]
    public class SerializableRefMap
    {
        public List<string> keys = new List<string>();
        public List<string> values = new List<string>();

        public SerializableRefMap(Dictionary<string, string> dict)
        {
            foreach (var kv in dict)
            {
                keys.Add(kv.Key);
                values.Add(kv.Value);
            }
        }
    }
}
