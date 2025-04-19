using UnityEditor;
using UnityEngine;
using System.Reflection;

public class MESDK : EditorWindow
{
    private enum Tab { ModExporter, ModelExporter, BoneMerger, VRMValidator }
    private Tab currentTab;

    private ScriptableObject modExporterInstance;
    private ScriptableObject modelExporterInstance;
    private ScriptableObject boneMergerInstance;
    private ScriptableObject vrmValidatorInstance;

    private Texture2D bannerTexture;

    [MenuItem("MateEngine/ME SDK")]
    public static void ShowWindow()
    {
        var window = GetWindow<MESDK>("ME SDK");
        window.minSize = new Vector2(500, 400);
    }

    private void OnEnable()
    {
        modExporterInstance = CreateInstance("ModExporterWindow");
        modelExporterInstance = CreateInstance("MEModelExporter");
        boneMergerInstance = CreateInstance("MateEngine.MEBoneMerger"); // namespaced
        vrmValidatorInstance = CreateInstance("VrmValidatorWindow");

        bannerTexture = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Editor/sdk.png");
    }

    private void OnGUI()
    {
        DrawBanner();

        currentTab = (Tab)GUILayout.Toolbar((int)currentTab, new[] {
            "Mod Exporter", "Model Exporter", "Bone Merger", "VRM Validator"
        });

        EditorGUILayout.Space();

        switch (currentTab)
        {
            case Tab.ModExporter:
                CallOnGUI(modExporterInstance);
                break;
            case Tab.ModelExporter:
                CallOnGUI(modelExporterInstance);
                break;
            case Tab.BoneMerger:
                CallOnGUI(boneMergerInstance);
                break;
            case Tab.VRMValidator:
                CallOnGUI(vrmValidatorInstance);
                break;
        }
    }

    private void DrawBanner()
    {
        if (bannerTexture == null) return;

        float bannerWidth = position.width;
        float bannerHeight = bannerTexture.height * (bannerWidth / bannerTexture.width);
        Rect bannerRect = GUILayoutUtility.GetRect(bannerWidth, bannerHeight, GUILayout.ExpandWidth(true));
        GUI.DrawTexture(bannerRect, bannerTexture, ScaleMode.ScaleToFit);
        EditorGUILayout.Space(5);
    }

    private void CallOnGUI(ScriptableObject instance)
    {
        if (instance == null)
        {
            EditorGUILayout.HelpBox("Tool failed to initialize.", MessageType.Error);
            return;
        }

        var method = instance.GetType().GetMethod("OnGUI", BindingFlags.Instance | BindingFlags.NonPublic);
        if (method != null)
        {
            method.Invoke(instance, null);
        }
        else
        {
            EditorGUILayout.HelpBox("Unable to render tool (OnGUI not found).", MessageType.Error);
        }
    }
}
