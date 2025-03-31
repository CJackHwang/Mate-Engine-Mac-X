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
            byte[] vrmData = await Task.Run(() => File.ReadAllBytes(path));
            if (vrmData == null || vrmData.Length == 0) return;

            using var gltfData = new GlbBinaryParser(vrmData, path).Parse();
            var importer = new VRMImporterContext(new VRMData(gltfData));
            var instance = await importer.LoadAsync(new ImmediateCaller());

            if (instance.Root == null) return;

            DisableMainModel();
            ClearPreviousCustomModel();

            instance.Root.transform.SetParent(customModelOutput.transform, false);
            instance.Root.transform.localPosition = Vector3.zero;
            instance.Root.transform.localRotation = Quaternion.identity;
            instance.Root.transform.localScale = Vector3.one;

            currentModel = instance.Root;

            EnableSkinnedMeshRenderers(currentModel);
            FixMaterials(currentModel);
            AssignAnimatorController(currentModel);
            InjectComponentsFromPrefab(componentTemplatePrefab, currentModel);

            var avatarSettingsMenu = FindObjectOfType<AvatarSettingsMenu>();
            if (avatarSettingsMenu != null)
            {
                avatarSettingsMenu.LoadSettings();
                avatarSettingsMenu.ApplySettings();
            }

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

    private void FixMaterials(GameObject model)
    {
        foreach (var r in model.GetComponentsInChildren<Renderer>())
        {
            foreach (var mat in r.sharedMaterials)
            {
                if (mat != null && mat.shader.name != "VRM/MToon")
                    mat.shader = Shader.Find("VRM/MToon");
            }
        }
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
        foreach (var templateComp in templateObj.GetComponents<MonoBehaviour>())
        {
            var type = templateComp.GetType();
            if (targetModel.GetComponent(type) != null)
                continue; // Already exists

            var newComp = targetModel.AddComponent(type);
            CopyComponentValues(templateComp, newComp);

            // Automatically rebind Animator if field exists
            var animator = targetModel.GetComponentInChildren<Animator>();
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
}
