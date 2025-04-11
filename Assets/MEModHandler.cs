using UnityEngine;
using UnityEngine.UI;
using TMPro;
using SFB;
using System.Collections.Generic;
using System.IO;
using System.Linq;

public class MEModHandler : MonoBehaviour
{
    [Header("UI References")]
    public Button loadModButton;
    public Transform modListContainer;
    public GameObject modEntryPrefab;

    private string modFolderPath;
    private List<ModEntry> loadedMods = new List<ModEntry>();

    private void Start()
    {
        modFolderPath = Path.Combine(Application.persistentDataPath, "Mods");
        Directory.CreateDirectory(modFolderPath);

        if (loadModButton != null)
            loadModButton.onClick.AddListener(OpenFileDialogAndLoadMod);

        LoadAllModsInFolder();
    }

    private void LoadAllModsInFolder()
    {
        string[] modFiles = Directory.GetFiles(modFolderPath, "*.me", SearchOption.TopDirectoryOnly);

        foreach (var path in modFiles)
        {
            LoadModFromPath(path);
        }

        Debug.Log($"[MEModHandler] Loaded {modFiles.Length} mods from Mods/ folder.");
    }



    public void OpenFileDialogAndLoadMod()
    {
        var extensions = new[] { new ExtensionFilter("Mod Files", "me") };
        string[] paths = StandaloneFileBrowser.OpenFilePanel("Select Mod (.me)", "", extensions, false);
        if (paths.Length > 0 && !string.IsNullOrEmpty(paths[0]))
        {
            string sourcePath = paths[0];
            string fileName = Path.GetFileName(sourcePath);
            string targetPath = Path.Combine(modFolderPath, fileName);

            File.Copy(sourcePath, targetPath, true);
            LoadModFromPath(targetPath);
        }
    }

    private void LoadModFromPath(string path)
    {
        if (!File.Exists(path))
        {
            Debug.LogError("[MEModHandler] Mod file not found: " + path);
            return;
        }

        var bundle = AssetBundle.LoadFromFile(path);
        if (bundle == null)
        {
            Debug.LogError("[MEModHandler] Failed to load AssetBundle: " + path);
            return;
        }

        var prefab = bundle.LoadAllAssets<GameObject>().FirstOrDefault();
        if (prefab == null)
        {
            Debug.LogError("[MEModHandler] No prefab found in AssetBundle: " + path);
            bundle.Unload(false);
            return;
        }

        var instance = Instantiate(prefab);
        bundle.Unload(false);

        ModEntry newMod = new ModEntry
        {
            name = Path.GetFileNameWithoutExtension(path),
            instance = instance,
            localPath = path
        };

        loadedMods.Add(newMod);
        AddToModListUI(newMod);
        Debug.Log("[MEModHandler] Loaded mod: " + newMod.name);
    }

    private void AddToModListUI(ModEntry mod)
    {
        if (modEntryPrefab == null || modListContainer == null) return;

        var entry = Instantiate(modEntryPrefab, modListContainer);
        entry.name = "Mod_" + mod.name;

        // Set the mod name text
        var nameText = entry.transform.Find("ModNameText")?.GetComponent<TextMeshProUGUI>();
        if (nameText != null)
            nameText.text = mod.name;

        // Set up the toggle
        var toggle = entry.GetComponentInChildren<Toggle>(true);
        if (toggle != null)
        {
            // Load saved toggle state if available, default to true
            bool isActive = true;
            if (SaveLoadHandler.Instance.data.modStates.TryGetValue(mod.name, out bool savedState))
                isActive = savedState;

            toggle.isOn = isActive;
            if (mod.instance != null)
                mod.instance.SetActive(isActive);

            // On toggle change, update mod state and save
            toggle.onValueChanged.AddListener(active =>
            {
                if (mod.instance != null)
                    mod.instance.SetActive(active);

                SaveLoadHandler.Instance.data.modStates[mod.name] = active;
                SaveLoadHandler.Instance.SaveToDisk();
            });
        }

        // Set up the remove button
        var button = entry.GetComponentInChildren<Button>(true);
        if (button != null)
        {
            button.onClick.AddListener(() =>
            {
                RemoveMod(mod, entry);
            });
        }
    }



    private void RemoveMod(ModEntry mod, GameObject uiEntry)
    {
        if (mod.instance != null)
            Destroy(mod.instance);

        if (File.Exists(mod.localPath))
            File.Delete(mod.localPath);

        loadedMods.Remove(mod);
        if (uiEntry != null)
            Destroy(uiEntry);

        Debug.Log("[MEModHandler] Removed mod: " + mod.name);
    }

    [System.Serializable]
    private class ModEntry
    {
        public string name;
        public GameObject instance;
        public string localPath;
    }
}
