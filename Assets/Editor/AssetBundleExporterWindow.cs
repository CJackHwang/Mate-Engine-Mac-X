using UnityEditor;
using UnityEngine;
using System.IO;

public class AssetBundleExporterWindow : EditorWindow
{
    private GameObject prefabToExport;
    private string bundleName = "custommodel";

    [MenuItem("MateEngine/AssetBundle Exporter")]
    public static void ShowWindow()
    {
        GetWindow<AssetBundleExporterWindow>("AssetBundle Exporter");
    }

    private void OnGUI()
    {
        GUILayout.Label("Export Custom Prefab to AssetBundle", EditorStyles.boldLabel);
        EditorGUILayout.Space();

        prefabToExport = (GameObject)EditorGUILayout.ObjectField("Prefab or Scene Object", prefabToExport, typeof(GameObject), true);
        bundleName = EditorGUILayout.TextField("Bundle Name", bundleName);

        EditorGUILayout.Space();

        GUI.enabled = prefabToExport != null && !string.IsNullOrEmpty(bundleName);
        if (GUILayout.Button("Export"))
        {
            ExportAssetBundle();
        }
        GUI.enabled = true;
    }

    private void ExportAssetBundle()
    {
        if (prefabToExport == null)
        {
            Debug.LogError("[AssetBundle Exporter] No object assigned.");
            return;
        }

        string exportFolder = "Assets/ExportedModel";
        if (!Directory.Exists(exportFolder))
            Directory.CreateDirectory(exportFolder);

        string tempBuildPath = "TempBundleBuild";
        if (!Directory.Exists(tempBuildPath))
            Directory.CreateDirectory(tempBuildPath);

        string assetPath = AssetDatabase.GetAssetPath(prefabToExport);
        bool isSceneObject = string.IsNullOrEmpty(assetPath);

        if (isSceneObject)
        {
            // Create a temporary prefab in memory (not saved to disk)
            string tempPrefabPath = "Assets/__TempExport.prefab";
            PrefabUtility.SaveAsPrefabAsset(prefabToExport, tempPrefabPath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            assetPath = tempPrefabPath;
        }

        // Build using AssetBundleBuild (manual mapping)
        AssetBundleBuild build = new AssetBundleBuild
        {
            assetBundleName = bundleName,
            assetNames = new[] { assetPath }
        };

        BuildPipeline.BuildAssetBundles(tempBuildPath, new[] { build }, BuildAssetBundleOptions.ChunkBasedCompression, EditorUserBuildSettings.activeBuildTarget);

        // Move built bundle to final path
        string builtFilePath = Path.Combine(tempBuildPath, bundleName);
        string finalFilePath = Path.Combine(exportFolder, bundleName + ".me");

        if (File.Exists(builtFilePath))
        {
            File.Copy(builtFilePath, finalFilePath, true);
            Debug.Log($"[AssetBundle Exporter] Exported to: {Path.GetFullPath(finalFilePath)}");
        }
        else
        {
            Debug.LogError("[AssetBundle Exporter] Export failed: .me file not found.");
        }

        // Cleanup
        if (isSceneObject && File.Exists(assetPath))
        {
            AssetDatabase.DeleteAsset(assetPath); // Delete temp prefab
        }

        if (Directory.Exists(tempBuildPath)) Directory.Delete(tempBuildPath, true);

        EditorUtility.RevealInFinder(exportFolder);
    }
}
