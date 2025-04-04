using System.IO;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;
using VRM;
using UniGLTF;
using SFB;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UniVRM10;
using System;

public class VRMLoader : MonoBehaviour
{
    public Button loadVRMButton;
    public GameObject mainModel;
    public GameObject customModelOutput;
    public RuntimeAnimatorController animatorController;
    public GameObject componentTemplatePrefab; // 🔁 NEW: Holds all desired components

    private GameObject currentModel;
    private bool isLoading = false;
    private string modelPathKey = "SavedPathModel";

    void Start()
    {
        EnsureShadersAreIncluded();

        if (PlayerPrefs.HasKey(modelPathKey))
        {
            string savedPath = PlayerPrefs.GetString(modelPathKey);
            if (!string.IsNullOrEmpty(savedPath))
                LoadVRM(savedPath);
        }
    }

    private void EnsureShadersAreIncluded()
    {
        Shader.Find("VRM/MToon");
    }

    public void OpenFileDialogAndLoadVRM()
    {
        if (isLoading) return;

        isLoading = true;
        var extensions = new[] { new ExtensionFilter("VRM Files", "vrm") };
        string[] paths = StandaloneFileBrowser.OpenFilePanel("Select VRM Model", "", extensions, false);

        if (paths.Length > 0 && !string.IsNullOrEmpty(paths[0]))
            LoadVRM(paths[0]);

        isLoading = false;
    }

    public async void LoadVRM(string path)
    {
        if (!File.Exists(path)) return;

        try
        {
            byte[] fileData = await Task.Run(() => File.ReadAllBytes(path));
            if (fileData == null || fileData.Length == 0) return;

            GameObject loadedModel = null;

            // First, try parsing as VRM 1.0
            try
            {
                var glbData = new GlbFileParser(path).Parse();
                var vrm10Data = Vrm10Data.Parse(glbData);

                if (vrm10Data != null)
                {
                    using var importer10 = new Vrm10Importer(vrm10Data);
                    var instance10 = await importer10.LoadAsync(new ImmediateCaller());

                    if (instance10.Root != null)
                    {
                        loadedModel = instance10.Root;
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("[VRMLoader] VRM 1.0 parsing failed, trying VRM 0.x loader: " + e.Message);
            }

            // If not 1.0, fallback to 0.x
            if (loadedModel == null)
            {
                try
                {
                    using var gltfData = new GlbBinaryParser(fileData, path).Parse();
                    var importer = new VRMImporterContext(new VRMData(gltfData));
                    var instance = await importer.LoadAsync(new ImmediateCaller());

                    if (instance.Root != null)
                    {
                        loadedModel = instance.Root;
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogError("[VRMLoader] VRM 0.x loading failed: " + ex.Message);
                    return;
                }
            }

            if (loadedModel == null) return;

            DisableMainModel();
            ClearPreviousCustomModel();

            loadedModel.transform.SetParent(customModelOutput.transform, false);
            loadedModel.transform.localPosition = Vector3.zero;
            loadedModel.transform.localRotation = Quaternion.identity;
            loadedModel.transform.localScale = Vector3.one;

            currentModel = loadedModel;

            EnableSkinnedMeshRenderers(currentModel);
            AssignAnimatorController(currentModel);
            InjectComponentsFromPrefab(componentTemplatePrefab, currentModel);

            var avatarSettingsMenu = FindObjectOfType<AvatarSettingsMenu>();
            if (avatarSettingsMenu != null)
            {
                avatarSettingsMenu.LoadSettings();
                avatarSettingsMenu.ApplySettings();
            }

            StartCoroutine(DelayedRefreshStats());

            PlayerPrefs.SetString(modelPathKey, path);
            PlayerPrefs.Save();

            Directory.CreateDirectory(Path.Combine(Application.persistentDataPath, "VRM"));
        }
        catch (System.Exception ex)
        {
            Debug.LogError("[VRMLoader] Failed to load VRM: " + ex.Message);
        }
    }


    public void ResetModel()
    {
        string vrmFolder = Path.Combine(Application.persistentDataPath, "VRM");
        if (Directory.Exists(vrmFolder))
        {
            Directory.Delete(vrmFolder, true);
            Debug.Log("[VRMLoader] VRM folder deleted successfully.");
        }

        ClearPreviousCustomModel();
        EnableMainModel();

        PlayerPrefs.DeleteKey(modelPathKey);
        PlayerPrefs.Save();
    }

    private void DisableMainModel()
    {
        if (mainModel != null)
            mainModel.SetActive(false);
    }

    private void EnableMainModel()
    {
        if (mainModel != null)
            mainModel.SetActive(true);
    }

    private void ClearPreviousCustomModel()
    {
        if (customModelOutput != null)
        {
            foreach (Transform child in customModelOutput.transform)
            {
                if (child.gameObject == mainModel)
                    continue;
                Destroy(child.gameObject);
            }
        }
    }

    private void EnableSkinnedMeshRenderers(GameObject model)
    {
        foreach (var skinnedMesh in model.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            skinnedMesh.enabled = true;
    }

    private void AssignAnimatorController(GameObject model)
    {
        var animator = model.GetComponentInChildren<Animator>();
        if (animator != null && animatorController != null)
            animator.runtimeAnimatorController = animatorController;
    }

    private void InjectComponentsFromPrefab(GameObject prefabTemplate, GameObject targetModel)
    {
        if (prefabTemplate == null || targetModel == null) return;

        var templateObj = Instantiate(prefabTemplate);
        var animator = targetModel.GetComponentInChildren<Animator>();

        foreach (var templateComp in templateObj.GetComponents<MonoBehaviour>())
        {
            var type = templateComp.GetType();
            if (targetModel.GetComponent(type) != null)
                continue; // Skip if already exists

            var newComp = targetModel.AddComponent(type);
            CopyComponentValues(templateComp, newComp);

            // Call SetAnimator(animator) if available
            if (animator != null)
            {
                var setAnimMethod = type.GetMethod("SetAnimator", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (setAnimMethod != null)
                    setAnimMethod.Invoke(newComp, new object[] { animator });

                var animatorField = type.GetField("animator", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (animatorField != null && animatorField.FieldType == typeof(Animator))
                    animatorField.SetValue(newComp, animator);
            }
        }

        Destroy(templateObj);
    }


    private void CopyComponentValues(Component source, Component destination)
    {
        var type = source.GetType();

        var fields = type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        foreach (var field in fields)
        {
            if (field.IsDefined(typeof(SerializeField), true) || field.IsPublic)
            {
                field.SetValue(destination, field.GetValue(source));
            }
        }

        var props = type.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                        .Where(p => p.CanWrite && p.GetSetMethod(true) != null);
        foreach (var prop in props)
        {
            try
            {
                prop.SetValue(destination, prop.GetValue(source));
            }
            catch { }
        }
    }

    private System.Collections.IEnumerator DelayedRefreshStats()
    {
        yield return null; // wait 1 frame

        var stats = FindObjectOfType<RuntimeModelStats>();
        if (stats != null)
        {
            Debug.Log("[VRMLoader] Delayed refresh of RuntimeModelStats.");
            stats.RefreshNow();
        }
    }

}