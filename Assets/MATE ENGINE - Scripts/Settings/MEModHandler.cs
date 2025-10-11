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
    List<ModEntry> loadedMods = new List<ModEntry>();

    void Start()
    {
        modFolderPath = Path.Combine(Application.persistentDataPath, "Mods");
        Directory.CreateDirectory(modFolderPath);
        loadModButton.onClick.AddListener(OpenFileDialogAndLoadMod);
        StartCoroutine(DeferredLoadAllMods());
    }

    IEnumerator DeferredLoadAllMods()
    {
        CustomDancePlayer.DanceResourceManager mgr = null;
        CustomDancePlayer.DancePlayerUIManager ui = null;
        float timeout = 5f, elapsed = 0f;
        while (elapsed < timeout)
        {
            mgr = FindFirstObjectByType<CustomDancePlayer.DanceResourceManager>();
            ui = FindFirstObjectByType<CustomDancePlayer.DancePlayerUIManager>();
            if (mgr != null && ui != null) break;
            elapsed += Time.deltaTime;
            yield return null;
        }
        if (mgr == null) yield break;
        LoadAllModsInFolder();
    }

    void LoadAllModsInFolder()
    {
        var mgr = FindFirstObjectByType<CustomDancePlayer.DanceResourceManager>();
        var ui = FindFirstObjectByType<CustomDancePlayer.DancePlayerUIManager>();

        for (int i = modListContainer.childCount - 1; i >= 0; i--)
            Destroy(modListContainer.GetChild(i).gameObject);
        loadedMods.Clear();

        foreach (var file in Directory.GetFiles(modFolderPath, "*.me"))
            LoadMod(file);

        foreach (var file in Directory.GetFiles(modFolderPath, "*.unity3d"))
            LoadUnity3D(file, addToUI: true, respectSavedState: true);

        mgr?.RefreshDanceFileList();
        ui?.RefreshDropdown();
    }

    void OpenFileDialogAndLoadMod()
    {
        var ext = new[] { new ExtensionFilter("MateEngine Files", "me", "unity3d") };
        var paths = StandaloneFileBrowser.OpenFilePanel("Select Mod or Dance Asset", ".", ext, false);
        if (paths.Length == 0 || string.IsNullOrEmpty(paths[0])) return;

        var src = paths[0];
        var dest = Path.Combine(modFolderPath, Path.GetFileName(src));
        File.Copy(src, dest, true);

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

        int exist = loadedMods.FindIndex(m => m.name == name && m.type == ModType.Unity3D);
        if (exist >= 0) loadedMods.RemoveAt(exist);

        bool enable = true;
        if (respectSavedState && SaveLoadHandler.Instance.data.modStates.TryGetValue(name, out var s)) enable = s;

        var mgr = FindFirstObjectByType<CustomDancePlayer.DanceResourceManager>();
        if (enable && mgr != null)
        {
            try { mgr.UnregisterInjected(name); } catch { }
            var bundle = AssetBundle.LoadFromFile(path);
            if (bundle != null) mgr.RegisterDanceBundle(bundle, name);
        }

        var entry = new ModEntry { name = name, localPath = path, type = ModType.Unity3D, instance = null };
        loadedMods.Add(entry);
        if (addToUI) AddToModListUI(entry, initialState: enable);
    }

    void LoadMod(string path)
    {
        string temp = Path.Combine(Application.temporaryCachePath, "ME_TempMod");
        if (Directory.Exists(temp)) Directory.Delete(temp, true);
        ZipFile.ExtractToDirectory(path, temp);

        string bundlePath = null;
        string modName = Path.GetFileNameWithoutExtension(path);

        string modInfoPath = Path.Combine(temp, "modinfo.json");
        string refPathJson = Path.Combine(temp, "reference_paths.json");
        string sceneLinksPath = Path.Combine(temp, "scene_links.json");

        Dictionary<string, string> refPaths = new();
        Dictionary<string, string> sceneLinks = new();

        if (File.Exists(refPathJson))
        {
            var json = File.ReadAllText(refPathJson);
            var obj = JsonUtility.FromJson<RefPathMap>(json);
            for (int i = 0; i < obj.keys.Count; i++)
                refPaths[obj.keys[i]] = obj.values[i];
        }

        if (File.Exists(sceneLinksPath))
        {
            var json = File.ReadAllText(sceneLinksPath);
            var obj = JsonUtility.FromJson<SceneLinkMap>(json);
            for (int i = 0; i < obj.keys.Count; i++)
                sceneLinks[obj.keys[i]] = obj.values[i];
        }

        foreach (var file in Directory.GetFiles(temp, "*.bundle"))
            bundlePath = file;

        if (!string.IsNullOrEmpty(bundlePath) && File.Exists(bundlePath))
        {
            var bundle = AssetBundle.LoadFromFile(bundlePath);
            if (bundle == null) return;

            var prefab = bundle.LoadAsset<GameObject>(modName);
            if (prefab == null) return;

            var instance = Instantiate(prefab);
            bundle.Unload(false);

            ApplyReferencePaths(instance, refPaths, sceneLinks);

            var entry = new ModEntry { name = modName, instance = instance, localPath = path, type = ModType.ME };
            loadedMods.Add(entry);
            AddToModListUI(entry, initialState: GetSavedStateOrDefault(modName, true));
            return;
        }
    }

    void ApplyReferencePaths(GameObject root, Dictionary<string, string> refPaths, Dictionary<string, string> sceneLinks)
    {
        var allBehaviours = root.GetComponentsInChildren<MonoBehaviour>(true);
        foreach (var mb in allBehaviours)
        {
            if (mb == null) continue;
            Type type = mb.GetType();
            string typeName = type.Name;

            foreach (var map in new[] { refPaths, sceneLinks })
            {
                foreach (var kv in map)
                {
                    if (!kv.Key.StartsWith(typeName + ".")) continue;

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
                            int start = part.IndexOf('[');
                            int end = part.IndexOf(']');
                            listIndex = int.Parse(part.Substring(start + 1, end - start - 1));
                            part = part.Substring(0, start);
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
        if (SaveLoadHandler.Instance.data.modStates.TryGetValue(key, out var state)) return state;
        return def;
    }

    void AddToModListUI(ModEntry mod, bool initialState)
    {
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
                    SaveLoadHandler.Instance.data.modStates[mod.name] = a;
                    SaveLoadHandler.Instance.SaveToDisk();
                });
            }
            else
            {
                tog.onValueChanged.AddListener(a =>
                {
                    var mgr = FindFirstObjectByType<CustomDancePlayer.DanceResourceManager>();
                    if (mgr == null) return;
                    if (a)
                    {
                        try { mgr.UnregisterInjected(mod.name); } catch { }
                        var bundle = AssetBundle.LoadFromFile(mod.localPath);
                        if (bundle != null) mgr.RegisterDanceBundle(bundle, mod.name);
                    }
                    else
                    {
                        mgr.UnregisterInjected(mod.name);
                    }
                    SaveLoadHandler.Instance.data.modStates[mod.name] = a;
                    SaveLoadHandler.Instance.SaveToDisk();
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

        if (File.Exists(mod.localPath)) File.Delete(mod.localPath);
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
            if ((t = asm.GetType(name)) != null) return t;
        return null;
    }

    [Serializable] class ModEntry { public string name; public GameObject instance; public string localPath; public ModType type; }
    enum ModType { ME, Unity3D }
    [Serializable] class RefPathMap { public List<string> keys = new(); public List<string> values = new(); }
    [Serializable] class SceneLinkMap { public List<string> keys = new(); public List<string> values = new(); }
    [Serializable] class ObjectInfo { public string name, path; public List<string> components; }
    [Serializable] class ObjectList { public List<ObjectInfo> objects; }
    [Serializable] class FieldValue { public string objectPath, componentType, fieldName, value; }
    [Serializable] class FieldList { public List<FieldValue> fields; }
}
