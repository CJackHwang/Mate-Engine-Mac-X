using UnityEngine;
using System.Collections.Generic;
using System.Collections;

public class PetVoiceReactionHandler : MonoBehaviour
{
    [System.Serializable]
    public class VoiceRegion
    {
        public string name;
        public HumanBodyBones targetBone;
        public Vector3 offset = Vector3.zero;
        public Vector3 worldOffset = Vector3.zero;
        public float hoverRadius = 50f;
        public Color gizmoColor = new Color(1f, 0.5f, 0f, 0.25f);
        public List<AudioClip> voiceClips = new();
        public AnimationClip hoverAnimation;

        [Header("Hover Object Settings")]
        public bool enableHoverObject = false;
        public GameObject hoverObject;
        public bool bindHoverObjectToBone = false;

        [Header("Layered Sound Settings")]
        public bool enableLayeredSound = false;
        public List<AudioClip> layeredVoiceClips = new();

        [HideInInspector] public bool wasHovering = false;
        [HideInInspector] public Coroutine disableCoroutine;
    }

    public Animator avatarAnimator;
    public List<VoiceRegion> regions = new();
    public AudioSource voiceAudioSource;
    public AudioSource layeredAudioSource;

    public string idleStateName = "Idle";
    public string dragStateName = "isDragging";
    public string danceStateName = "isDancing";
    public string hoverTriggerParam = "HoverTrigger";

    public bool showDebugGizmos = true;

    private AnimatorOverrideController animatorOverrideController;
    private string hoverReactionClipName = "HoverReaction";
    private Camera cachedCamera;
    private WaitForSeconds wait4s;

    private void Start()
    {
        if (voiceAudioSource == null)
            voiceAudioSource = gameObject.AddComponent<AudioSource>();

        if (layeredAudioSource == null)
            layeredAudioSource = gameObject.AddComponent<AudioSource>();

        cachedCamera = Camera.main;
        wait4s = new WaitForSeconds(4f);

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

    private void BindHoverObjects()
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

    private void SetupAnimatorOverrideController()
    {
        if (avatarAnimator.runtimeAnimatorController == null) return;
        animatorOverrideController = new AnimatorOverrideController(avatarAnimator.runtimeAnimatorController);
        avatarAnimator.runtimeAnimatorController = animatorOverrideController;
    }

    private void Update()
    {
        if (cachedCamera == null || avatarAnimator == null) return;

        foreach (var region in regions)
        {
            Transform bone = avatarAnimator.GetBoneTransform(region.targetBone);
            if (bone == null) continue;

            Vector3 localOffset = bone.TransformVector(region.offset);
            Vector3 worldPoint = bone.position + localOffset + region.worldOffset;
            Vector2 screenPoint = cachedCamera.WorldToScreenPoint(worldPoint);

            float scaleFactor = bone.lossyScale.magnitude;
            float scaledRadius = region.hoverRadius * scaleFactor;
            Vector3 offsetWorld = worldPoint + cachedCamera.transform.right * scaledRadius;
            Vector2 screenOffset = cachedCamera.WorldToScreenPoint(offsetWorld);
            float screenRadius = Vector2.Distance(screenPoint, screenOffset);
            float cursorDist = Vector2.Distance(Input.mousePosition, screenPoint);

            bool hovering = cursorDist < screenRadius;

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
                    if (region.disableCoroutine != null)
                        StopCoroutine(region.disableCoroutine);
                    region.disableCoroutine = null;
                }
                else if (region.wasHovering && region.disableCoroutine == null)
                {
                    region.disableCoroutine = StartCoroutine(DisableHoverObject(region));
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

    private IEnumerator DisableHoverObject(VoiceRegion region)
    {
        yield return wait4s;
        if (!region.wasHovering && region.hoverObject != null)
        {
            region.hoverObject.SetActive(false);
        }
        region.disableCoroutine = null;
    }

    private bool IsInIdleState()
    {
        if (avatarAnimator == null) return false;
        if (avatarAnimator.GetBool(dragStateName)) return false;
        if (avatarAnimator.GetBool(danceStateName)) return false;

        AnimatorStateInfo info = avatarAnimator.GetCurrentAnimatorStateInfo(0);
        return info.IsName(idleStateName);
    }

    private void TriggerHoverReaction(VoiceRegion region, bool state)
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

    private void PlayRandomVoice(VoiceRegion region)
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
    private void OnDrawGizmos()
    {
        if (!showDebugGizmos || !Application.isPlaying || cachedCamera == null || avatarAnimator == null) return;

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
