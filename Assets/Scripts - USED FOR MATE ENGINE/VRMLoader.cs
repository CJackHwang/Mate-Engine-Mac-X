using System.IO;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;
using VRM;
using UniGLTF;
using SFB;

public class VRMLoader : MonoBehaviour
{
    public Button loadVRMButton;
    public GameObject injectModelHere;
    public RuntimeAnimatorController animatorController;
    public FixedPosition fixedPositionScript;
    public AvatarControllerHeadTracking headTrackingScript;
    public AvatarAnimatorController avatarAnimatorScript;
    public AvatarDragSoundHandler avatarDragSoundHandlerScript;
    public PetVoiceReactionHandler voiceReactionHandlerScript;
    public HandHolder handHolderScript;
    [Tooltip("Drag your default .vrm file here")]
    public TextAsset defaultModelAsset;

    private GameObject currentModel;
    private bool isLoading = false;
    private string modelPathKey = "SavedPathModel";

    void Start()
    {
        EnsureShadersAreIncluded();
        if (PlayerPrefs.HasKey(modelPathKey))
        {
            string savedPath = PlayerPrefs.GetString(modelPathKey);
            if (!string.IsNullOrEmpty(savedPath)) LoadVRM(savedPath);
        }
    }

    public async void LoadDefaultModel()
    {
        if (defaultModelAsset == null)
        {
            Debug.LogWarning("[VRMLoader] No default model asset assigned.");
            return;
        }
        try
        {
            byte[] vrmData = defaultModelAsset.bytes;
            using var gltfData = new GlbBinaryParser(vrmData, defaultModelAsset.name).Parse();
            var importer = new VRMImporterContext(new VRMData(gltfData));
            var instance = await importer.LoadAsync(new ImmediateCaller());

            if (injectModelHere != null)
                foreach (Transform child in injectModelHere.transform) Destroy(child.gameObject);

            instance.Root.transform.SetParent(injectModelHere.transform, false);
            instance.Root.transform.localPosition = Vector3.zero;
            instance.Root.transform.localRotation = Quaternion.identity;
            instance.Root.transform.localScale = Vector3.one;

            currentModel = instance.Root;
            EnableSkinnedMeshRenderers(currentModel);
            FixMaterials(currentModel);
            AssignAnimatorController(currentModel);
            AddRequiredComponents(currentModel);
            RenameHeadBone(currentModel);

            var animator = currentModel.GetComponentInChildren<Animator>();
            if (voiceReactionHandlerScript != null && animator != null)
                voiceReactionHandlerScript.SetAnimator(animator);

            PlayerPrefs.DeleteKey(modelPathKey);
            PlayerPrefs.Save();
            Debug.Log("[VRMLoader] Default model loaded from TextAsset");
        }
        catch (System.Exception ex)
        {
            Debug.LogError("[VRMLoader] Failed to load default model: " + ex.Message);
        }
    }

    private void EnsureShadersAreIncluded() => Shader.Find("VRM/MToon");

    public void OpenFileDialogAndLoadVRM()
    {
        if (isLoading) return;
        isLoading = true;
        var extensions = new[] { new ExtensionFilter("VRM Files", "vrm") };
        string[] paths = StandaloneFileBrowser.OpenFilePanel("Select VRM Model", "", extensions, false);
        if (paths.Length > 0 && !string.IsNullOrEmpty(paths[0])) LoadVRM(paths[0]);
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

            if (injectModelHere != null)
                foreach (Transform child in injectModelHere.transform) Destroy(child.gameObject);

            instance.Root.transform.SetParent(injectModelHere.transform, false);
            instance.Root.transform.localPosition = Vector3.zero;
            instance.Root.transform.localRotation = Quaternion.identity;
            instance.Root.transform.localScale = Vector3.one;

            currentModel = instance.Root;
            EnableSkinnedMeshRenderers(currentModel);
            FixMaterials(currentModel);
            AssignAnimatorController(currentModel);
            AddRequiredComponents(currentModel);
            RenameHeadBone(currentModel);

            var animator = currentModel.GetComponentInChildren<Animator>();
            if (voiceReactionHandlerScript != null && animator != null)
                voiceReactionHandlerScript.SetAnimator(animator);

            PlayerPrefs.SetString(modelPathKey, path);
            PlayerPrefs.Save();
        }
        catch (System.Exception ex)
        {
            Debug.LogError("[VRMLoader] Failed to load VRM: " + ex.Message);
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
            foreach (var mat in r.sharedMaterials)
                if (mat != null && mat.shader.name != "VRM/MToon")
                    mat.shader = Shader.Find("VRM/MToon");
    }

    private void AssignAnimatorController(GameObject model)
    {
        var animator = model.GetComponentInChildren<Animator>();
        if (animator != null && animatorController != null)
            animator.runtimeAnimatorController = animatorController;
    }

    private void AddRequiredComponents(GameObject model)
    {
        if (fixedPositionScript != null && model.GetComponent<FixedPosition>() == null)
            model.AddComponent<FixedPosition>();

        if (headTrackingScript != null && model.GetComponent<AvatarControllerHeadTracking>() == null)
            model.AddComponent<AvatarControllerHeadTracking>();

        if (avatarAnimatorScript != null && model.GetComponent<AvatarAnimatorController>() == null)
            model.AddComponent<AvatarAnimatorController>();

        if (avatarDragSoundHandlerScript != null && model.GetComponent<AvatarDragSoundHandler>() == null)
            model.AddComponent<AvatarDragSoundHandler>();

        if (handHolderScript != null && model.GetComponent<HandHolder>() == null)
        {
            var newHandHolder = model.AddComponent<HandHolder>();
            var anim = model.GetComponentInChildren<Animator>();
            if (anim != null) newHandHolder.SetAnimator(anim);

            newHandHolder.interactionRadius = handHolderScript.interactionRadius;
            newHandHolder.hysteresisBuffer = handHolderScript.hysteresisBuffer;
            newHandHolder.followSpeed = handHolderScript.followSpeed;
            newHandHolder.maxIKWeight = handHolderScript.maxIKWeight;
            newHandHolder.blendInTime = handHolderScript.blendInTime;
            newHandHolder.blendOutTime = handHolderScript.blendOutTime;
            newHandHolder.maxHandDistance = handHolderScript.maxHandDistance;
            newHandHolder.handZOffset = handHolderScript.handZOffset;
            newHandHolder.elbowHintOffset = handHolderScript.elbowHintOffset;
            newHandHolder.isDancingParam = handHolderScript.isDancingParam;
            newHandHolder.isDraggingParam = handHolderScript.isDraggingParam;
            newHandHolder.hoverTriggerParam = handHolderScript.hoverTriggerParam;
            newHandHolder.showDebugGizmos = handHolderScript.showDebugGizmos;
            newHandHolder.gizmoColor = handHolderScript.gizmoColor;
        }
    }

    private void RenameHeadBone(GameObject model)
    {
        var animator = model.GetComponent<Animator>();
        if (animator == null || !animator.isHuman) return;
        var headBone = animator.GetBoneTransform(HumanBodyBones.Head);
        if (headBone != null && headBone.name != "HEAD") headBone.name = "HEAD";
    }
}
