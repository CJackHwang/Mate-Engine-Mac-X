// Assets/…/Editor/ModExporterWindow.cs
using UnityEditor;
using UnityEngine;
using UnityEngine.Video;
using System;
using System.IO;
using System.IO.Compression;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;

public class ModExporterWindow : EditorWindow
{
    private GameObject rootObject;
    private string modName = "MyMod";

    [MenuItem("MateEngine/ME Mod Exporter")]
    public static void ShowWindow()
    {
        GetWindow<ModExporterWindow>("ME Mod Exporter");
    }

    void OnGUI()
    {
        GUILayout.Label("Export Scene Object to .me", EditorStyles.boldLabel);
        rootObject = (GameObject)EditorGUILayout.ObjectField("Scene Root", rootObject, typeof(GameObject), true);
        modName = EditorGUILayout.TextField("Mod Name", modName);

        GUI.enabled = rootObject != null && !string.IsNullOrEmpty(modName);
        if (GUILayout.Button("Export Mod")) ExportMod();
        GUI.enabled = true;
    }

    void ExportMod()
    {
        // Temp-Ordner leeren
        var tmp = Path.Combine(Path.GetTempPath(), "ME_TempMod");
        if (Directory.Exists(tmp)) Directory.Delete(tmp, true);
        Directory.CreateDirectory(tmp);

        // Struktur und Werte sammeln
        var structure = new List<ObjectInfo>();
        var values = new List<FieldValue>();
        var objPaths = new Dictionary<UnityEngine.Object, string>();

        SerializeHierarchy(rootObject, structure, values, objPaths);

        // JSON schreiben
        File.WriteAllText(Path.Combine(tmp, "structure.json"), JsonUtility.ToJson(new ObjectList { objects = structure }, true));
        File.WriteAllText(Path.Combine(tmp, "values.json"), JsonUtility.ToJson(new FieldList { fields = values }, true));

        // Medien kopieren (Audio/Video)
        CopyMedia(rootObject, tmp);

        // ZIP erstellen
        var outDir = Path.Combine(Application.dataPath, "../ExportedMods");
        Directory.CreateDirectory(outDir);
        var zip = Path.Combine(outDir, modName + ".me");
        if (File.Exists(zip)) File.Delete(zip);
        ZipFile.CreateFromDirectory(tmp, zip);

        EditorUtility.RevealInFinder(zip);
        Debug.Log($"[ModExporter] Mod exportiert: {zip}");
    }

    void SerializeHierarchy(GameObject root,
        List<ObjectInfo> structure,
        List<FieldValue> values,
        Dictionary<UnityEngine.Object, string> objPaths)
    {
        // für jedes Transform im Baum
        foreach (var tf in root.GetComponentsInChildren<Transform>(true))
        {
            var go = tf.gameObject;
            var path = GetPath(tf);
            objPaths[go] = path;

            var info = new ObjectInfo { name = go.name, path = path, components = new List<string>() };
            foreach (var comp in go.GetComponents<Component>())
            {
                if (comp == null || comp is Transform) continue;
                var typeName = comp.GetType().FullName;
                info.components.Add(typeName);
                SerializeFields(path, typeName, comp, "", values, objPaths);
            }
            structure.Add(info);
        }
    }

    void SerializeFields(string goPath,
        string compType,
        object instance,
        string prefix,
        List<FieldValue> values,
        Dictionary<UnityEngine.Object, string> objPaths)
    {
        if (instance == null) return;
        var t = instance.GetType();
        foreach (var f in t.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
        {
            if (f.IsNotSerialized) continue;
            var name = string.IsNullOrEmpty(prefix) ? f.Name : prefix + "." + f.Name;
            var val = f.GetValue(instance);
            if (val == null) continue;

            var ft = f.FieldType;

            // UnityEngine.Object?
            if (typeof(UnityEngine.Object).IsAssignableFrom(ft))
            {
                if (val is GameObject goVal)
                {
                    if (objPaths.TryGetValue(goVal, out var p))
                        values.Add(new FieldValue(goPath, compType, name, "GO:" + p));
                    else
                        values.Add(new FieldValue(goPath, compType, name, "GO_GLOBAL:" + goVal.name));
                }
                else if (val is UnityEngine.Object ue)
                {
                    var assetPath = AssetDatabase.GetAssetPath(ue);
                    if (!string.IsNullOrEmpty(assetPath))
                        values.Add(new FieldValue(goPath, compType, name, "ASSET:" + assetPath));
                }
                continue;
            }

            // Enums
            if (ft.IsEnum)
            {
                values.Add(new FieldValue(goPath, compType, name, "ENUM:" + val));
                continue;
            }

            // Primitive / Structs
            if (ft == typeof(string)) values.Add(new FieldValue(goPath, compType, name, "STR:" + val));
            else if (ft == typeof(int)) values.Add(new FieldValue(goPath, compType, name, "INT:" + val));
            else if (ft == typeof(float)) values.Add(new FieldValue(goPath, compType, name, "FLT:" + ((float)val).ToString(System.Globalization.CultureInfo.InvariantCulture)));
            else if (ft == typeof(bool)) values.Add(new FieldValue(goPath, compType, name, "BOOL:" + ((bool)val ? "1" : "0")));
            else if (ft == typeof(Vector3))
            {
                var v = (Vector3)val;
                values.Add(new FieldValue(goPath, compType, name, $"V3:{v.x},{v.y},{v.z}"));
            }
            else if (ft == typeof(Color))
            {
                var c = (Color)val;
                values.Add(new FieldValue(goPath, compType, name, "COL:" + ColorUtility.ToHtmlStringRGBA(c)));
            }
            // Liste/Array
            else if (typeof(IList).IsAssignableFrom(ft))
            {
                var list = val as IList;
                if (list != null)
                    for (int i = 0; i < list.Count; i++)
                        SerializeFields(goPath, compType, list[i] ?? list, $"{name}[{i}]", values, objPaths);
            }
            // Objekt-Referenz (z.B. verschachtelte Klassen)
            else if (ft.IsClass)
            {
                SerializeFields(goPath, compType, val, name, values, objPaths);
            }
        }
    }

    string GetPath(Transform t)
    {
        var p = t.name;
        while (t.parent != null)
        {
            t = t.parent;
            p = t.name + "/" + p;
        }
        return p;
    }

    void CopyMedia(GameObject root, string tmp)
    {
        var f = Path.Combine(tmp, "assets");
        Directory.CreateDirectory(f);

        // VideoPlayer.url
        foreach (var vp in root.GetComponentsInChildren<VideoPlayer>(true))
            if (!string.IsNullOrEmpty(vp.url) && File.Exists(vp.url))
                File.Copy(vp.url, Path.Combine(f, Path.GetFileName(vp.url)), true);

        // AudioSource.clip
        foreach (var ap in root.GetComponentsInChildren<UnityEngine.AudioSource>(true))
            if (ap.clip != null)
            {
                var p2 = AssetDatabase.GetAssetPath(ap.clip);
                if (!string.IsNullOrEmpty(p2))
                    File.Copy(p2, Path.Combine(f, Path.GetFileName(p2)), true);
            }
    }

    [Serializable]
    class ObjectInfo { public string name, path; public List<string> components; }
    [Serializable]
    class ObjectList { public List<ObjectInfo> objects; }
    [Serializable]
    class FieldValue
    {
        public string objectPath, componentType, fieldName, value;
        public FieldValue(string o, string c, string f, string v)
        { objectPath = o; componentType = c; fieldName = f; value = v; }
    }
    [Serializable]
    class FieldList { public List<FieldValue> fields; }
}
