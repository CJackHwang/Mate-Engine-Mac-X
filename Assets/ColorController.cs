using UnityEngine;
using System.Collections.Generic;
using System.Linq;

public class ColorController : MonoBehaviour
{
    public enum TargetType { Light, ParticleSystem }

    [System.Serializable]
    public class ColorTarget
    {
        public TargetType type = TargetType.Light;
        public GameObject target;
        public string id = "new-id";
        public bool enabled = false;
        public bool allowEnableControl = true;
        [Range(0, 1)] public float hue = 0;
        [Range(0, 1)] public float saturation = 1;
        [Range(0, 100)] public float intensity = 1;
        public bool intensityOverride = false;
        public float maxIntensity = 10f;
        public bool swingMode = false;
        public HumanBodyBones targetBone = HumanBodyBones.Head;
        [Range(0, 1)] public float swingSmoothness = 0.15f;
        public bool blockYMovement = false;
        [HideInInspector] public Vector3 baseOffset;
        [HideInInspector] public bool hasBaseOffset = false;
        [HideInInspector] public Vector3 currentPosition;
        [HideInInspector] public float originalY;
        public string groupID = "";
        public List<string> exclusiveTags = new List<string>();
        [HideInInspector] public float fadeCurrentValue = 0f;
        [HideInInspector] public float fadeTarget = 0f;
        [HideInInspector] public bool isFading = false;
    }

    public List<ColorTarget> targets = new List<ColorTarget>();
    [Range(0f, 6f)]
    public float fadeDuration = 1f;

    private Transform modelRoot;
    private GameObject currentModel;
    private AvatarAnimatorReceiver currentReceiver;

    public void SetGroupEnabled(string groupID, bool state)
    {
        var sameGroup = targets.Where(t => t.groupID == groupID).ToList();
        var allTags = new HashSet<string>();
        foreach (var t in sameGroup)
            foreach (var tag in t.exclusiveTags)
                allTags.Add(tag);

        if (state)
        {
            foreach (var tag in allTags)
            {
                foreach (var t in targets)
                {
                    if (t.groupID != groupID && t.exclusiveTags != null && t.exclusiveTags.Contains(tag))
                    {
                        t.fadeTarget = 0f;
                        t.isFading = true;
                        t.enabled = false;
                    }
                }
            }
        }

        foreach (var t in sameGroup)
        {
            float maxVal = t.intensityOverride ? t.maxIntensity : 1f;
            float tgt = Mathf.Clamp(t.intensity, 0, maxVal);
            t.fadeTarget = state ? 1f : 0f;
            t.isFading = true;
            if (state) t.enabled = true;
            if (!state) t.enabled = false;
        }
    }

    void Awake()
    {
        modelRoot = GameObject.Find("Model")?.transform;
    }

    void UpdateCurrentAvatar()
    {
        if (!modelRoot) return;
        for (int i = 0; i < modelRoot.childCount; i++)
        {
            var child = modelRoot.GetChild(i);
            if (child.gameObject.activeInHierarchy)
            {
                if (currentModel != child.gameObject)
                {
                    currentModel = child.gameObject;
                    currentReceiver = currentModel.GetComponent<AvatarAnimatorReceiver>();
                    foreach (var t in targets) t.hasBaseOffset = false;
                }
                return;
            }
        }
    }

    void LateUpdate()
    {
        UpdateCurrentAvatar();
        float dt = Application.isPlaying ? Time.deltaTime : 1f / 60f;
        foreach (var t in targets)
        {
            if (t.target == null) continue;

            float maxVal = t.intensityOverride ? t.maxIntensity : 1f;
            float targetVal = Mathf.Clamp(t.intensity, 0, maxVal);

            if (t.isFading)
            {
                float tgt = t.fadeTarget;
                float speed = fadeDuration > 0.01f ? Mathf.Abs(tgt - t.fadeCurrentValue) / fadeDuration : 10000f;
                t.fadeCurrentValue = Mathf.MoveTowards(t.fadeCurrentValue, tgt, speed * dt);

                float displayValue = targetVal * t.fadeCurrentValue;
                ApplyFadeToTarget(t, displayValue);

                if (Mathf.Approximately(t.fadeCurrentValue, tgt))
                {
                    t.isFading = false;
                    if (Mathf.Approximately(tgt, 0f))
                    {
                        t.enabled = false;
                        SetObjectActiveEditorSafe(t.target, false);
                    }
                    else
                    {
                        t.enabled = true;
                        SetObjectActiveEditorSafe(t.target, true);
                    }
                }
                else
                {
                    SetObjectActiveEditorSafe(t.target, true);
                }
            }
            else
            {
                if (t.allowEnableControl)
                {
                    SetObjectActiveEditorSafe(t.target, t.enabled);
                    if (!t.enabled) continue;
                }
                else
                {
                    if (!t.enabled) continue;
                }
                t.fadeCurrentValue = t.fadeTarget = 1f;
                ApplyFadeToTarget(t, targetVal);
            }

            if (t.swingMode)
            {
                if (currentReceiver == null || currentReceiver.avatarAnimator == null) continue;
                Transform bone = currentReceiver.avatarAnimator.GetBoneTransform(t.targetBone);
                if (bone == null) continue;

                Vector3 bonePos = bone.position;
                if (!t.hasBaseOffset)
                {
                    t.baseOffset = t.target.transform.position - bonePos;
                    t.currentPosition = t.target.transform.position;
                    t.originalY = t.target.transform.position.y;
                    t.hasBaseOffset = true;
                }

                Vector3 finalTarget = bonePos + t.baseOffset;
                if (t.blockYMovement)
                    finalTarget.y = t.originalY;

                t.currentPosition = Vector3.Lerp(t.currentPosition, finalTarget, 1f - t.swingSmoothness);
                t.target.transform.position = t.currentPosition;
            }
            else
            {
                t.hasBaseOffset = false;
            }
        }
    }

    void ApplyFadeToTarget(ColorTarget t, float value)
    {
        if (t.type == TargetType.Light)
        {
            var light = t.target.GetComponent<Light>();
            if (light)
            {
                Color.RGBToHSV(light.color, out float _, out float _, out float v);
                light.color = Color.HSVToRGB(t.hue, t.saturation, v);
                float maxVal = t.intensityOverride ? t.maxIntensity : 1f;
                light.intensity = Mathf.Clamp(value, 0, maxVal);
            }
        }
        else if (t.type == TargetType.ParticleSystem)
        {
            var ps = t.target.GetComponent<ParticleSystem>();
            if (ps)
            {
                var main = ps.main;
                Color orig = main.startColor.color;
                Color.RGBToHSV(orig, out float _, out float _, out float v);
                Color col = Color.HSVToRGB(t.hue, t.saturation, v);
                float maxVal = t.intensityOverride ? t.maxIntensity : 1f;
                col.a = Mathf.Clamp01(value / maxVal);
                main.startColor = new ParticleSystem.MinMaxGradient(col);
            }
        }
    }

    void SetObjectActiveEditorSafe(GameObject obj, bool value)
    {
#if UNITY_EDITOR
        if (!Application.isPlaying)
        {
            if (obj != null && obj.activeSelf != value)
                obj.SetActive(value);
            return;
        }
#endif
        if (obj != null && obj.activeSelf != value)
            obj.SetActive(value);
    }

    void OnValidate() => LateUpdate();
}
