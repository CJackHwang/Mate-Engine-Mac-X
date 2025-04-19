using UnityEngine;
using UnityEngine.UI;
using TMPro;
using SFB;
using System;
using System.IO;
using System.IO.Compression;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;

public class MEModHandler : MonoBehaviour
{
    [Header("UI References")]
    public Button loadModButton;
    public Transform modListContainer;
    public GameObject modEntryPrefab;

    string modFolderPath;
    List<ModEntry> loadedMods = new List<ModEntry>();

    void Start()
    {
        modFolderPath = Path.Combine(Application.persistentDataPath, "Mods");
        Directory.CreateDirectory(modFolderPath);

        loadModButton.onClick.AddListener(OpenFileDialogAndLoadMod);
        LoadAllModsInFolder();
    }

    void LoadAllModsInFolder()
    {
        foreach (var file in Directory.GetFiles(modFolderPath, "*.me"))
            LoadMod(file);
    }

    void OpenFileDialogAndLoadMod()
    {
        var ext = new[] { new ExtensionFilter("Mod Files", "me") };
        var paths = StandaloneFileBrowser.OpenFilePanel("Select Mod", ".", ext, false);
        if (paths.Length > 0 && !string.IsNullOrEmpty(paths[0]))
        {
            var dest = Path.Combine(modFolderPath, Path.GetFileName(paths[0]));
            File.Copy(paths[0], dest, true);
            LoadMod(dest);
        }
    }

    void LoadMod(string path)
    {
        // Zip entpacken
        var tmp = Path.Combine(Application.temporaryCachePath, "ME_TempMod");
        if (Directory.Exists(tmp)) Directory.Delete(tmp, true);
        ZipFile.ExtractToDirectory(path, tmp);

        // JSON laden
        var structJson = File.ReadAllText(Path.Combine(tmp, "structure.json"));
        var valuesJson = File.ReadAllText(Path.Combine(tmp, "values.json"));

        var structure = JsonUtility.FromJson<ObjectList>(structJson);
        var fields = JsonUtility.FromJson<FieldList>(valuesJson);

        // GameObjects anlegen
        var created = new Dictionary<string, GameObject>();
        foreach (var o in structure.objects)
        {
            var go = new GameObject(o.name);
            created[o.path] = go;
            foreach (var tn in o.components)
            {
                var t = ResolveType(tn);
                if (t != null && t.IsSubclassOf(typeof(Component)))
                    go.AddComponent(t);
            }
        }
        // Eltern setzen
        foreach (var o in structure.objects)
            if (created.TryGetValue(o.path, out var go))
            {
                int i = o.path.LastIndexOf('/');
                if (i > 0 && created.TryGetValue(o.path.Substring(0, i), out var parent))
                    go.transform.SetParent(parent.transform, false);
            }

        // Felder setzen
        foreach (var f in fields.fields)
        {
            if (!created.TryGetValue(f.objectPath, out var go)) continue;
            var compType = ResolveType(f.componentType);
            if (compType == null) continue;
            var comp = go.GetComponent(compType);
            SetNestedField(comp, f.fieldName, f.value, created);
        }

        // Root ist das erste Objekt
        var root = created[structure.objects[0].path];
        var entry = new ModEntry { name = Path.GetFileNameWithoutExtension(path), instance = root, localPath = path };
        loadedMods.Add(entry);
        AddToModListUI(entry);
    }

    // Rekursives Setzen von Feld-Paths inklusive Listen
    private void SetNestedField(object obj, string fieldPath, string raw, Dictionary<string, GameObject> lookup)
    {
        var parts = fieldPath.Split('.');
        object target = obj;
        FieldInfo fieldInfo = null;
        Type currentType = obj.GetType();

        for (int i = 0; i < parts.Length; i++)
        {
            string part = parts[i];
            int index = -1;

            // Erkenne Liste/Array-Zugriff
            if (part.Contains("["))
            {
                int b = part.IndexOf('[');
                index = int.Parse(part.Substring(b + 1, part.IndexOf(']') - b - 1));
                part = part.Substring(0, b);
            }

            // Hole das FieldInfo
            fieldInfo = currentType.GetField(part, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (fieldInfo == null) return;

            // Der aktuelle Wert im Feld
            object fieldValue = fieldInfo.GetValue(target);

            bool isLast = (i == parts.Length - 1);
            Type fieldType = fieldInfo.FieldType;

            // LISTEN-/ARRAY-FALL
            if (index >= 0 && typeof(IList).IsAssignableFrom(fieldType))
            {
                // Liste initialisieren, falls null
                var list = fieldValue as IList;
                if (list == null)
                {
                    list = (IList)Activator.CreateInstance(fieldType);
                    fieldInfo.SetValue(target, list);
                }

                // Elementtyp herausfinden
                Type elemType = fieldType.IsArray
                    ? fieldType.GetElementType()
                    : fieldType.GetGenericArguments()[0];

                // Liste bis Index füllen
                while (list.Count <= index)
                {
                    var newElem = elemType.IsValueType ? Activator.CreateInstance(elemType) : Activator.CreateInstance(elemType);
                    list.Add(newElem);
                }

                if (isLast)
                {
                    // FINAL: setze den rohen Wert in die Liste
                    object finalVal = ParseValue(raw, elemType, lookup);
                    list[index] = finalVal;
                }
                else
                {
                    // zwischendrin: instanziertes Listenelement betreten
                    target = list[index];
                    if (target == null) return;
                    currentType = target.GetType();
                }
            }
            else if (isLast)
            {
                // EINZELFELD-FALL am Ende
                object finalVal = ParseValue(raw, fieldType, lookup);
                fieldInfo.SetValue(target, finalVal);
            }
            else
            {
                // normales Feld zwischendrin (z.B. verschachtelte Klasse)
                target = fieldValue;
                if (target == null) return;
                currentType = target.GetType();
            }
        }
    }


    object ParseValue(string raw, Type ft, Dictionary<string, GameObject> lookup)
    {
        if (raw.StartsWith("GO:"))
        {
            var p = raw.Substring(3);
            return lookup.TryGetValue(p, out var g) ? g : null;
        }
        if (raw.StartsWith("GO_GLOBAL:"))
        {
            var nm = raw.Substring("GO_GLOBAL:".Length);
            return GameObject.Find(nm);
        }
        if (raw.StartsWith("ASSET:"))
        {
            // Asset‑Lader hier ergänzen falls gebraucht
            return null;
        }
        if (raw.StartsWith("STR:")) return raw.Substring(4);
        if (raw.StartsWith("INT:")) return int.Parse(raw.Substring(4));
        if (raw.StartsWith("FLT:")) return float.Parse(raw.Substring(4), System.Globalization.CultureInfo.InvariantCulture);
        if (raw.StartsWith("BOOL:")) return raw.Substring(5) == "1";
        if (raw.StartsWith("ENUM:")) return Enum.Parse(ft, raw.Substring(5));
        if (raw.StartsWith("V3:"))
        {
            var a = raw.Substring(3).Split(',');
            return new Vector3(float.Parse(a[0]), float.Parse(a[1]), float.Parse(a[2]));
        }
        if (raw.StartsWith("COL:"))
        {
            if (ColorUtility.TryParseHtmlString("#" + raw.Substring(4), out var c))
                return c;
        }
        return null;
    }

    Type ResolveType(string name)
    {
        var t = Type.GetType(name);
        if (t != null) return t;
        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            if ((t = asm.GetType(name)) != null) return t;
        return null;
    }

    void AddToModListUI(ModEntry mod)
    {
        var entry = Instantiate(modEntryPrefab, modListContainer);
        entry.name = "Mod_" + mod.name;
        var nt = entry.transform.Find("ModNameText")?.GetComponent<TextMeshProUGUI>();
        if (nt != null) nt.text = mod.name;

        var tog = entry.GetComponentInChildren<Toggle>(true);
        if (tog != null)
        {
            var isOn = true;
            if (SaveLoadHandler.Instance.data.modStates.TryGetValue(mod.name, out var s)) isOn = s;
            tog.isOn = isOn;
            mod.instance.SetActive(isOn);
            tog.onValueChanged.AddListener(a => {
                mod.instance.SetActive(a);
                SaveLoadHandler.Instance.data.modStates[mod.name] = a;
                SaveLoadHandler.Instance.SaveToDisk();
            });
        }
        var btn = entry.GetComponentInChildren<Button>(true);
        if (btn != null) btn.onClick.AddListener(() => RemoveMod(mod, entry));
    }

    void RemoveMod(ModEntry mod, GameObject ui)
    {
        Destroy(mod.instance);
        if (File.Exists(mod.localPath)) File.Delete(mod.localPath);
        loadedMods.Remove(mod);
        Destroy(ui);
    }



    [Serializable]
    class ModEntry { public string name; public GameObject instance; public string localPath; }
    [Serializable]
    class ObjectInfo { public string name, path; public List<string> components; }
    [Serializable]
    class ObjectList { public List<ObjectInfo> objects; }
    [Serializable]
    class FieldValue { public string objectPath, componentType, fieldName, value; }
    [Serializable]
    class FieldList { public List<FieldValue> fields; }
}
