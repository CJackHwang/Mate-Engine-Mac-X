using UnityEngine;
using UnityEngine.UI;
using TMPro;
using SFB;
using System;
using System.IO;
using System.IO.Compression;
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
        string temp = Path.Combine(Application.temporaryCachePath, "ME_TempMod");
        if (Directory.Exists(temp)) Directory.Delete(temp, true);
        ZipFile.ExtractToDirectory(path, temp);

        string bundlePath = null;
        string modName = Path.GetFileNameWithoutExtension(path);

        // === VRC-style mod detection ===
        string modInfoPath = Path.Combine(temp, "modinfo.json");
        string refPathJson = Path.Combine(temp, "reference_paths.json");
        Dictionary<string, string> refPaths = new();

        if (File.Exists(modInfoPath))
        {
            if (File.Exists(refPathJson))
            {
                var json = File.ReadAllText(refPathJson);
                var obj = JsonUtility.FromJson<RefPathMap>(json);
                for (int i = 0; i < obj.keys.Count; i++)
                    refPaths[obj.keys[i]] = obj.values[i];
            }

            foreach (var file in Directory.GetFiles(temp, "*.bundle"))
                bundlePath = file;

            if (!string.IsNullOrEmpty(bundlePath) && File.Exists(bundlePath))
            {
                var bundle = AssetBundle.LoadFromFile(bundlePath);
                if (bundle == null)
                {
                    Debug.LogError("[MEModHandler] Failed to load AssetBundle: " + bundlePath);
                    return;
                }

                var prefab = bundle.LoadAsset<GameObject>(modName);
                if (prefab == null)
                {
                    Debug.LogError("[MEModHandler] Could not find prefab named " + modName + " inside bundle.");
                    return;
                }

                var instance = Instantiate(prefab);
                bundle.Unload(false);

                if (refPaths.Count > 0)
                    ApplyReferencePaths(instance, refPaths);

                var entry = new ModEntry { name = modName, instance = instance, localPath = path };
                loadedMods.Add(entry);
                AddToModListUI(entry);
                return;
            }
        }

        // === legacy fallback skipped ===
    }


    void ApplyReferencePaths(GameObject root, Dictionary<string, string> paths)
    {
        var allBehaviours = root.GetComponentsInChildren<MonoBehaviour>(true);
        foreach (var mb in allBehaviours)
        {
            if (mb == null) continue;
            Type type = mb.GetType();

            foreach (var kv in paths)
            {
                if (!kv.Key.StartsWith(type.Name + ".")) continue;

                string fieldName = kv.Key.Substring(type.Name.Length + 1);
                var field = type.GetField(fieldName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (field == null) continue;

                if (field.FieldType == typeof(GameObject))
                {
                    var go = GameObject.Find(kv.Value);
                    if (go != null) field.SetValue(mb, go);
                }
                else if (typeof(Component).IsAssignableFrom(field.FieldType))
                {
                    var go = GameObject.Find(kv.Value);
                    if (go != null)
                    {
                        var comp = go.GetComponent(field.FieldType);
                        if (comp != null) field.SetValue(mb, comp);
                    }
                }
            }
        }
    }

    [Serializable]
    class RefPathMap
    {
        public List<string> keys = new();
        public List<string> values = new();
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

    private void SetNestedField(object obj, string fieldPath, string raw, Dictionary<string, GameObject> lookup)
    {
        // ... (dein bestehender Feldsetter bleibt hier unverändert)
    }

    object ParseValue(string raw, Type ft, Dictionary<string, GameObject> lookup)
    {
        // ... (dein bestehender Parser bleibt hier ebenfalls gleich)
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

    [Serializable] class ModEntry { public string name; public GameObject instance; public string localPath; }
    [Serializable] class ObjectInfo { public string name, path; public List<string> components; }
    [Serializable] class ObjectList { public List<ObjectInfo> objects; }
    [Serializable] class FieldValue { public string objectPath, componentType, fieldName, value; }
    [Serializable] class FieldList { public List<FieldValue> fields; }
}
