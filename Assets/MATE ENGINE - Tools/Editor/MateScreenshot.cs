using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using System;
using System.IO;
using System.Reflection;

public class MateScreenshot : EditorWindow
{
    Camera targetCamera;
    bool nativeScreenshot = true;
    int width = 2048;
    int height = 2048;
    int antiAliasing = 1;
    string outputDirectory;

    [MenuItem("Tools/MateEngine/Mate Screenshot")]
    static void Open() { GetWindow<MateScreenshot>("Mate Screenshot"); }

    void OnEnable()
    {
        if (string.IsNullOrEmpty(outputDirectory))
            outputDirectory = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Screenshots");
    }

    void OnGUI()
    {
        targetCamera = (Camera)EditorGUILayout.ObjectField("Camera", targetCamera, typeof(Camera), true);
        nativeScreenshot = EditorGUILayout.ToggleLeft("Native Screenshot (Game View)", nativeScreenshot);

        if (nativeScreenshot)
        {
            var gv = Handles.GetMainGameViewSize();
            int aa = ResolveNativeMsaaSamples(targetCamera);
            EditorGUILayout.LabelField("Game View", ((int)gv.x) + " x " + ((int)gv.y) + "  MSAA " + aa + "x");
        }

        EditorGUI.BeginDisabledGroup(nativeScreenshot);
        width = Mathf.Max(1, EditorGUILayout.IntField("Width", width));
        height = Mathf.Max(1, EditorGUILayout.IntField("Height", height));
        antiAliasing = Mathf.ClosestPowerOfTwo(Mathf.Max(1, EditorGUILayout.IntField("MSAA (1/2/4/8)", antiAliasing)));
        EditorGUI.EndDisabledGroup();

        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField("Output Directory", GUILayout.Width(110));
        outputDirectory = EditorGUILayout.TextField(outputDirectory);
        if (GUILayout.Button("Browse", GUILayout.Width(70)))
        {
            var picked = EditorUtility.OpenFolderPanel("Select Output Directory", outputDirectory, "");
            if (!string.IsNullOrEmpty(picked)) outputDirectory = picked;
        }
        EditorGUILayout.EndHorizontal();

        EditorGUI.BeginDisabledGroup(targetCamera == null);
        if (GUILayout.Button("Screenshot")) TakeScreenshot();
        EditorGUI.EndDisabledGroup();
    }

    void TakeScreenshot()
    {
        Directory.CreateDirectory(outputDirectory);
        var cam = targetCamera;
        var oldTarget = cam.targetTexture;
        var oldFlags = cam.clearFlags;
        var oldBg = cam.backgroundColor;

        int w = width;
        int h = height;
        int aa = antiAliasing;

        if (nativeScreenshot)
        {
            var gv = Handles.GetMainGameViewSize();
            w = Mathf.Max(1, Mathf.RoundToInt(gv.x));
            h = Mathf.Max(1, Mathf.RoundToInt(gv.y));
            aa = ResolveNativeMsaaSamples(cam);
        }

        var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32);
        rt.antiAliasing = Mathf.Clamp(aa, 1, 8);

        var prevActive = RenderTexture.active;
        try
        {
            cam.targetTexture = rt;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0f, 0f, 0f, 0f);

            RenderTexture.active = rt;
            cam.Render();

            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false, false);
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            tex.Apply();

            var bytes = tex.EncodeToPNG();
            var name = "MateScreenshot_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".png";
            var path = Path.Combine(outputDirectory, name);
            File.WriteAllBytes(path, bytes);
            AssetDatabase.Refresh();
            EditorUtility.RevealInFinder(path);

            DestroyImmediate(tex);
        }
        finally
        {
            cam.targetTexture = oldTarget;
            cam.clearFlags = oldFlags;
            cam.backgroundColor = oldBg;
            RenderTexture.active = prevActive;
            rt.Release();
            DestroyImmediate(rt);
        }
    }

    int ResolveNativeMsaaSamples(Camera cam)
    {
        int srpSamples = GetSrpMsaaSamples(cam);
        int qSamples = QualitySettings.antiAliasing;
        int samples = srpSamples > 0 ? srpSamples : qSamples;
        if (!cam.allowMSAA) samples = 1;
        if (samples < 1) samples = 1;
        return samples;
    }

    int GetSrpMsaaSamples(Camera cam)
    {
        var rp = GraphicsSettings.currentRenderPipeline;
        if (rp == null) return 0;
        var t = rp.GetType();
        var p = t.GetProperty("msaaSampleCount", BindingFlags.Public | BindingFlags.Instance);
        if (p == null) return 0;
        var val = p.GetValue(rp, null);
        if (val is int i && i > 0)
        {
            bool allow = cam != null && cam.allowMSAA;
            try
            {
                var uacdType = Type.GetType("UnityEngine.Rendering.Universal.UniversalAdditionalCameraData, Unity.RenderPipelines.Universal.Runtime");
                if (uacdType != null && cam != null)
                {
                    var comp = cam.GetComponent(uacdType);
                    if (comp != null)
                    {
                        var allowProp = uacdType.GetProperty("allowMSAA", BindingFlags.Public | BindingFlags.Instance);
                        if (allowProp != null)
                        {
                            var allowVal = allowProp.GetValue(comp, null);
                            if (allowVal is bool b) allow = allow && b;
                        }
                    }
                }
            }
            catch { }
            return allow ? i : 1;
        }
        return 0;
    }
}
