using UnityEngine;
using UnityEngine.Animations;
using System.Collections.Generic;
using System.Collections;
using System.Linq;

public class PetVoiceReactionHandler : MonoBehaviour
{
    [System.Serializable]
    public class VoiceRegion
    {
        public string name;
        public HumanBodyBones targetBone;

        [Tooltip("Local offset from bone")]
        public Vector3 offset = Vector3.zero;

        [Tooltip("World offset (added after local offset)")]
        public Vector3 worldOffset = Vector3.zero;

        [Tooltip("Base hover radius (will scale with model)")]
        public float hoverRadius = 50f;

        public Color gizmoColor = new Color(1f, 0.5f, 0f, 0.25f);
        public List<AudioClip> voiceClips = new List<AudioClip>();
        public AnimationClip hoverAnimation;

        [Header("Hover Object Settings")]
        public bool enableHoverObject = false;
        public GameObject hoverObject;
        public bool bindHoverObjectToBone = false;

        [Header("Layered Sound Settings")]
        public bool enableLayeredSound = false;
        public List<AudioClip> layeredVoiceClips = new List<AudioClip>();

        [HideInInspector] public bool wasHovering = false;
    }

    [Header("Animator Source")]
    public Animator avatarAnimator;

    [Header("Voice Reaction Regions")]
    public List<VoiceRegion> regions = new List<VoiceRegion>();

    [Header("Voice Settings")]
    public AudioSource voiceAudioSource;
    public AudioSource layeredAudioSource;

    [Header("Animator State Checks")]
    public string idleStateName = "Idle";
    public string dragStateName = "isDragging";
    public string danceStateName = "isDancing";
    public string hoverTriggerParam = "HoverTrigger";

    [Header("Debug Settings")]
    public bool showDebugGizmos = true;

    private AnimatorOverrideController animatorOverrideController;
    private string hoverReactionClipName = "HoverReaction";

    void Start()
    {
        if (voiceAudioSource == null)
            voiceAudioSource = gameObject.AddComponent<AudioSource>();

        if (layeredAudioSource == null)
            layeredAudioSource = gameObject.AddComponent<AudioSource>();

        if (avatarAnimator != null)
        {
            BindHoverObjects();
            SetupAnimatorOverrideController();
        }
    }

    public void SetAnimator(Animator newAnimator)
    {
        avatarAnimator = newAnimator;
        BindHoverObjects();
        SetupAnimatorOverrideController();
    }

    void BindHoverObjects()
    {
        foreach (var region in regions)
        {
            Transform bone = avatarAnimator.GetBoneTransform(region.targetBone);
            if (bone == null) continue;

            if (region.enableHoverObject && region.bindHoverObjectToBone && region.hoverObject != null)
            {
                region.hoverObject.transform.SetParent(bone, false);
                region.hoverObject.transform.localPosition = Vector3.zero;
                region.hoverObject.transform.localRotation = Quaternion.identity;
            }
        }
    }

    void SetupAnimatorOverrideController()
    {
        if (avatarAnimator.runtimeAnimatorController == null) return;

        animatorOverrideController = new AnimatorOverrideController(avatarAnimator.runtimeAnimatorController);
        avatarAnimator.runtimeAnimatorController = animatorOverrideController;
    }

    void Update()
    {
        if (Camera.main == null || avatarAnimator == null) return;

        foreach (var region in regions)
        {
            Transform bone = avatarAnimator.GetBoneTransform(region.targetBone);
            if (bone == null) continue;

            Vector3 worldPoint = bone.position + bone.TransformVector(region.offset) + region.worldOffset;
            Vector2 screenPoint = Camera.main.WorldToScreenPoint(worldPoint);

            float scaleFactor = bone.lossyScale.magnitude;
            float scaledWorldRadius = region.hoverRadius * scaleFactor;

            Vector3 worldOffsetPos = worldPoint + Camera.main.transform.right * scaledWorldRadius;
            Vector2 screenOffsetPos = Camera.main.WorldToScreenPoint(worldOffsetPos);
            float screenRadius = Vector2.Distance(screenPoint, screenOffsetPos);

            float distance = Vector2.Distance(Input.mousePosition, screenPoint);
            bool hovering = distance < screenRadius;

            if (hovering && !region.wasHovering && IsInIdleState())
            {
                PlayRandomVoice(region);
                TriggerHoverReaction(region, true);
            }

            if (region.enableHoverObject && region.hoverObject != null)
            {
                if (hovering)
                {
                    region.hoverObject.SetActive(true);
                    StopCoroutine(DisableHoverObject(region));
                }
                else if (region.wasHovering)
                {
                    StartCoroutine(DisableHoverObject(region));
                }
            }

            if (hovering && !region.wasHovering)
            {
                region.wasHovering = true;
            }
            else if (!hovering && region.wasHovering)
            {
                TriggerHoverReaction(region, false);
                region.wasHovering = false;
            }
        }
    }

    IEnumerator DisableHoverObject(VoiceRegion region)
    {
        yield return new WaitForSeconds(4f);
        if (!region.wasHovering && region.hoverObject != null)
        {
            region.hoverObject.SetActive(false);
        }
    }

    bool IsInIdleState()
    {
        if (avatarAnimator == null) return false;
        if (avatarAnimator.GetBool(dragStateName)) return false;
        if (avatarAnimator.GetBool(danceStateName)) return false;

        AnimatorStateInfo stateInfo = avatarAnimator.GetCurrentAnimatorStateInfo(0);
        return stateInfo.IsName(idleStateName);
    }

    void TriggerHoverReaction(VoiceRegion region, bool state)
    {
        if (region.hoverAnimation == null || animatorOverrideController == null) return;

        var overrides = new List<KeyValuePair<AnimationClip, AnimationClip>>();
        animatorOverrideController.GetOverrides(overrides);

        for (int i = 0; i < overrides.Count; i++)
        {
            if (overrides[i].Key != null && overrides[i].Key.name == hoverReactionClipName)
            {
                overrides[i] = new KeyValuePair<AnimationClip, AnimationClip>(overrides[i].Key, region.hoverAnimation);
                break;
            }
        }

        animatorOverrideController.ApplyOverrides(overrides);
        avatarAnimator.SetBool(hoverTriggerParam, state);
    }

    void PlayRandomVoice(VoiceRegion region)
    {
        if (region.voiceClips.Count > 0 && !voiceAudioSource.isPlaying)
        {
            AudioClip clip = region.voiceClips[Random.Range(0, region.voiceClips.Count)];
            voiceAudioSource.clip = clip;
            voiceAudioSource.Play();
        }

        if (region.enableLayeredSound && region.layeredVoiceClips.Count > 0)
        {
            AudioClip layeredClip = region.layeredVoiceClips[Random.Range(0, region.layeredVoiceClips.Count)];
            layeredAudioSource.PlayOneShot(layeredClip);
        }
    }

#if UNITY_EDITOR
    void OnDrawGizmos()
    {
        if (!showDebugGizmos || !Application.isPlaying || Camera.main == null || avatarAnimator == null) return;

        foreach (var region in regions)
        {
            Transform bone = avatarAnimator.GetBoneTransform(region.targetBone);
            if (bone == null) continue;

            float scaleFactor = bone.lossyScale.magnitude;
            float scaledRadius = region.hoverRadius * scaleFactor;

            Vector3 worldPoint = bone.position + bone.TransformVector(region.offset) + region.worldOffset;

            Gizmos.color = region.gizmoColor;
            Gizmos.DrawWireSphere(worldPoint, scaledRadius);
        }
    }
#endif
}
