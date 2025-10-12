using UnityEngine;
using UnityEngine.UI;
using TMPro;
using SFB;
using System;
using System.IO;
using System.IO.Compression;
using System.Collections.Generic;
using System.Reflection;
using System.Collections;

public class MEModHandler : MonoBehaviour
{
    public Button loadModButton;
    public Transform modListContainer;
    public GameObject modEntryPrefab;

    string modFolderPath;
    readonly List<ModEntry> loadedMods = new List<ModEntry>();

    void Start()
    {
        modFolderPath = Path.Combine(Application.persistentDataPath, "Mods");
        Directory.CreateDirectory(modFolderPath);
        if (loadModButton != null) loadModButton.onClick.AddListener(OpenFileDialogAndLoadMod);
        StartCoroutine(BootLoadMods());
    }

    IEnumerator BootLoadMods()
    {
        yield return null;
        LoadAllModsInFolder();
    }

    void LoadAllModsInFolder()
    {
        for (int i = modListContainer.childCount - 1; i >= 0; i--) Destroy(modListContainer.GetChild(i).gameObject);
        loadedMods.Clear();

        var files = new List<string>();
        try
        {
            foreach (var f in Directory.EnumerateFiles(modFolderPath, "*", SearchOption.AllDirectories))
            {
                var ext = Path.GetExtension(f);
                if (string.IsNullOrEmpty(ext)) continue;
                if (ext.Equals(".me", StringComparison.OrdinalIgnoreCase) || ext.Equals(".unity3d", StringComparison.OrdinalIgnoreCase))
                    files.Add(f);
            }
        }
        catch { }

        for (int i = 0; i < files.Count; i++)
        {
            var path = files[i];
            if (path.EndsWith(".me", StringComparison.OrdinalIgnoreCase))
                LoadMod(path);
            else if (path.EndsWith(".unity3d", StringComparison.OrdinalIgnoreCase))
                LoadUnity3D(path, addToUI: true, respectSavedState: true);
        }

        var mgr = FindFirstObjectByType<CustomDancePlayer.DancePlayerUIManager>();
        if (mgr != null) mgr.RefreshDropdown();
    }

    void OpenFileDialogAndLoadMod()
    {
        var ext = new[] { new ExtensionFilter("MateEngine Files", "me", "unity3d") };
        var paths = StandaloneFileBrowser.OpenFilePanel("Select Mod or Dance Asset", ".", ext, false);
        if (paths.Length == 0 || string.IsNullOrEmpty(paths[0])) return;

        var src = paths[0];
        var dest = Path.Combine(modFolderPath, Path.GetFileName(src));
        try { File.Copy(src, dest, true); } catch { }

        if (dest.EndsWith(".me", StringComparison.OrdinalIgnoreCase))
        {
            LoadMod(dest);
        }
        else if (dest.EndsWith(".unity3d", StringComparison.OrdinalIgnoreCase))
        {
            LoadUnity3D(dest, addToUI: true, respectSavedState: false);
            FindFirstObjectByType<CustomDancePlayer.DancePlayerUIManager>()?.RefreshDropdown();
        }
    }

    void LoadUnity3D(string path, bool addToUI, bool respectSavedState)
    {
        var name = Path.GetFileNameWithoutExtension(path);

        int exist = loadedMods.FindIndex(m => string.Equals(m.name, name, StringComparison.OrdinalIgnoreCase) && m.type == ModType.Unity3D);
        if (exist >= 0) loadedMods.RemoveAt(exist);

        bool enable = true;
        if (respectSavedState && SaveLoadHandler.Instance != null && SaveLoadHandler.Instance.data != null)
        {
            if (SaveLoadHandler.Instance.data.modStates.TryGetValue(name, out var s)) enable = s;
        }

        var resMgr = FindFirstObjectByType<CustomDancePlayer.DanceResourceManager>();
        if (enable && resMgr != null)
        {
            try { resMgr.UnregisterInjected(name); } catch { }
            try
            {
                var bundle = AssetBundle.LoadFromFile(path);
                if (bundle != null) resMgr.RegisterDanceBundle(bundle, name);
            }
            catch { }
        }

        var entry = new ModEntry { name = name, localPath = path, type = ModType.Unity3D, instance = null };
        loadedMods.Add(entry);
        if (addToUI) AddToModListUI(entry, initialState: enable);
    }

    void LoadMod(string path)
    {
        string tempRoot = Path.Combine(Application.temporaryCachePath, "ME_TempMod");
        try { if (Directory.Exists(tempRoot)) Directory.Delete(tempRoot, true); } catch { }
        try { ZipFile.ExtractToDirectory(path, tempRoot); } catch { return; }

        string bundlePath = null;
        string modName = Path.GetFileNameWithoutExtension(path);

        string modInfoPath = Path.Combine(tempRoot, "modinfo.json");
        string refPathJson = Path.Combine(tempRoot, "reference_paths.json");
        string sceneLinksPath = Path.Combine(tempRoot, "scene_links.json");

        var refPaths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var sceneLinks = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            if (File.Exists(refPathJson))
            {
                var json = File.ReadAllText(refPathJson);
                var obj = JsonUtility.FromJson<RefPathMap>(json);
                for (int i = 0; i < obj.keys.Count; i++) refPaths[obj.keys[i]] = obj.values[i];
            }
        }
        catch { }

        try
        {
            if (File.Exists(sceneLinksPath))
            {
                var json = File.ReadAllText(sceneLinksPath);
                var obj = JsonUtility.FromJson<SceneLinkMap>(json);
                for (int i = 0; i < obj.keys.Count; i++) sceneLinks[obj.keys[i]] = obj.values[i];
            }
        }
        catch { }

        try
        {
            foreach (var file in Directory.GetFiles(tempRoot, "*.bundle", SearchOption.AllDirectories))
            {
                bundlePath = file;
                break;
            }
        }
        catch { }

        if (string.IsNullOrEmpty(bundlePath) || !File.Exists(bundlePath)) return;

        AssetBundle bundle = null;
        try { bundle = AssetBundle.LoadFromFile(bundlePath); } catch { }
        if (bundle == null) return;

        GameObject prefab = null;
        try { prefab = bundle.LoadAsset<GameObject>(modName); } catch { }

        if (prefab == null)
        {
            var all = bundle.LoadAllAssets<GameObject>();
            if (all != null && all.Length > 0) prefab = all[0];
        }

        if (prefab == null) return;

        GameObject instance = null;
        try { instance = Instantiate(prefab); } catch { }

        try { bundle.Unload(false); } catch { }

        if (instance == null) return;

        ApplyReferencePaths(instance, refPaths, sceneLinks);

        bool initialState = GetSavedStateOrDefault(modName, true);
        instance.SetActive(initialState);

        var entry = new ModEntry { name = modName, instance = instance, localPath = path, type = ModType.ME };
        loadedMods.Add(entry);
        AddToModListUI(entry, initialState);
    }

    void ApplyReferencePaths(GameObject root, Dictionary<string, string> refPaths, Dictionary<string, string> sceneLinks)
    {
        var allBehaviours = root.GetComponentsInChildren<MonoBehaviour>(true);
        for (int b = 0; b < allBehaviours.Length; b++)
        {
            var mb = allBehaviours[b];
            if (mb == null) continue;
            var type = mb.GetType();
            var typeName = type.Name;

            foreach (var map in new[] { refPaths, sceneLinks })
            {
                foreach (var kv in map)
                {
                    if (!kv.Key.StartsWith(typeName + ".", StringComparison.Ordinal)) continue;

                    string rawPath = kv.Key.Substring(typeName.Length + 1);
                    GameObject sceneGO = GameObject.Find(kv.Value);
                    if (sceneGO == null) continue;

                    object current = mb;
                    Type currentType = type;

                    var parts = rawPath.Split('.');
                    for (int i = 0; i < parts.Length; i++)
                    {
                        string part = parts[i];
                        int listIndex = -1;

                        if (part.Contains("["))
                        {
                            int s = part.IndexOf('[');
                            int e = part.IndexOf(']');
                            listIndex = int.Parse(part.Substring(s + 1, e - s - 1));
                            part = part.Substring(0, s);
                        }

                        FieldInfo field = currentType.GetField(part, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                        if (field == null) break;

                        bool isLast = (i == parts.Length - 1);

                        if (isLast)
                        {
                            if (field.FieldType == typeof(GameObject))
                                field.SetValue(current, sceneGO);
                            else if (typeof(Component).IsAssignableFrom(field.FieldType))
                            {
                                var comp = sceneGO.GetComponent(field.FieldType);
                                if (comp != null) field.SetValue(current, comp);
                            }
                        }
                        else
                        {
                            object next = field.GetValue(current);
                            if (next == null) break;

                            if (listIndex >= 0 && next is IList list)
                            {
                                if (listIndex >= list.Count) break;
                                current = list[listIndex];
                            }
                            else
                            {
                                current = next;
                            }

                            if (current == null) break;
                            currentType = current.GetType();
                        }
                    }
                }
            }
        }
    }

    bool GetSavedStateOrDefault(string key, bool def)
    {
        if (SaveLoadHandler.Instance == null || SaveLoadHandler.Instance.data == null) return def;
        if (SaveLoadHandler.Instance.data.modStates.TryGetValue(key, out var state)) return state;
        return def;
    }

    void AddToModListUI(ModEntry mod, bool initialState)
    {
        if (modEntryPrefab == null || modListContainer == null) return;

        var entry = Instantiate(modEntryPrefab, modListContainer);
        entry.name = "Mod_" + mod.name;

        var nt = entry.transform.Find("ModNameText")?.GetComponent<TextMeshProUGUI>();
        if (nt != null) nt.text = mod.name;

        var tog = entry.GetComponentInChildren<Toggle>(true);
        if (tog != null)
        {
            tog.isOn = initialState;
            if (mod.type == ModType.ME)
            {
                if (mod.instance != null) mod.instance.SetActive(initialState);
                tog.onValueChanged.AddListener(a =>
                {
                    if (mod.instance != null) mod.instance.SetActive(a);
                    if (SaveLoadHandler.Instance != null && SaveLoadHandler.Instance.data != null)
                    {
                        SaveLoadHandler.Instance.data.modStates[mod.name] = a;
                        SaveLoadHandler.Instance.SaveToDisk();
                    }
                });
            }
            else
            {
                tog.onValueChanged.AddListener(a =>
                {
                    var mgr = FindFirstObjectByType<CustomDancePlayer.DanceResourceManager>();
                    if (a)
                    {
                        if (mgr != null)
                        {
                            try { mgr.UnregisterInjected(mod.name); } catch { }
                            try
                            {
                                var bundle = AssetBundle.LoadFromFile(mod.localPath);
                                if (bundle != null) mgr.RegisterDanceBundle(bundle, mod.name);
                            }
                            catch { }
                        }
                    }
                    else
                    {
                        if (mgr != null) mgr.UnregisterInjected(mod.name);
                    }

                    if (SaveLoadHandler.Instance != null && SaveLoadHandler.Instance.data != null)
                    {
                        SaveLoadHandler.Instance.data.modStates[mod.name] = a;
                        SaveLoadHandler.Instance.SaveToDisk();
                    }
                    FindFirstObjectByType<CustomDancePlayer.DancePlayerUIManager>()?.RefreshDropdown();
                });
            }
        }

        var btn = entry.GetComponentInChildren<Button>(true);
        if (btn != null) btn.onClick.AddListener(() => RemoveMod(mod, entry));

        var hueShifter = FindFirstObjectByType<MenuHueShift>();
        if (hueShifter != null) hueShifter.RefreshNewGraphicsAndSelectables(entry.transform);
    }

    void RemoveMod(ModEntry mod, GameObject ui)
    {
        if (mod.type == ModType.ME)
        {
            if (mod.instance != null) Destroy(mod.instance);
        }
        else
        {
            var mgr = FindFirstObjectByType<CustomDancePlayer.DanceResourceManager>();
            if (mgr != null) mgr.UnregisterInjected(mod.name);
            FindFirstObjectByType<CustomDancePlayer.DancePlayerUIManager>()?.RefreshDropdown();
        }

        try { if (File.Exists(mod.localPath)) File.Delete(mod.localPath); } catch { }
        loadedMods.Remove(mod);
        Destroy(ui);
    }

    private void SetNestedField(object obj, string fieldPath, string raw, Dictionary<string, GameObject> lookup) { }
    object ParseValue(string raw, Type ft, Dictionary<string, GameObject> lookup) { return null; }

    Type ResolveType(string name)
    {
        var t = Type.GetType(name);
        if (t != null) return t;
        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            t = asm.GetType(name);
            if (t != null) return t;
        }
        return null;
    }

    [Serializable] class ModEntry { public string name; public GameObject instance; public string localPath; public ModType type; }
    enum ModType { ME, Unity3D }
    [Serializable] class RefPathMap { public List<string> keys = new List<string>(); public List<string> values = new List<string>(); }
    [Serializable] class SceneLinkMap { public List<string> keys = new List<string>(); public List<string> values = new List<string>(); }
    [Serializable] class ObjectInfo { public string name; public string path; public List<string> components; }
    [Serializable] class ObjectList { public List<ObjectInfo> objects; }
    [Serializable] class FieldValue { public string objectPath; public string componentType; public string fieldName; public string value; }
    [Serializable] class FieldList { public List<FieldValue> fields; }
}
