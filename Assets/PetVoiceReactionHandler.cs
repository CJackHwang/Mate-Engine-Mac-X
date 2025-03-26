using UnityEngine;
using UnityEngine.Animations;
using System.Collections.Generic;

public class PetVoiceReactionHandler : MonoBehaviour
{
    [System.Serializable]
    public class VoiceRegion
    {
        public string name;
        public List<GameObject> colliderObjects = new List<GameObject>();
        public List<AudioClip> voiceClips = new List<AudioClip>();
        public HumanBodyBones targetBone;
        public AnimationClip hoverAnimation;

        [HideInInspector] public bool wasHovering = false;
    }

    [Header("Main Camera")]
    public Camera mainCamera;

    [Header("Animator Source")]
    public Animator avatarAnimator;

    [Header("Voice Reaction Regions")]
    public List<VoiceRegion> regions = new List<VoiceRegion>();

    [Header("Voice Settings")]
    public AudioSource voiceAudioSource;

    [Header("Animator State Checks")]
    public string idleStateName = "Idle";
    public string dragStateName = "isDragging";
    public string danceStateName = "isDancing";
    public string hoverTriggerParam = "HoverTrigger";

    private AnimatorOverrideController animatorOverrideController;
    private string hoverReactionStateName = "HoverReaction";
    private string hoverReactionClipName = "HoverReaction";

    void Start()
    {
        if (mainCamera == null)
            mainCamera = Camera.main;

        if (voiceAudioSource == null)
            voiceAudioSource = gameObject.AddComponent<AudioSource>();

        if (avatarAnimator != null)
        {
            BindCollidersToBones();
            SetupAnimatorOverrideController();
        }
    }

    public void SetAnimator(Animator newAnimator)
    {
        avatarAnimator = newAnimator;
        BindCollidersToBones();
        SetupAnimatorOverrideController();
    }

    void BindCollidersToBones()
    {
        foreach (var region in regions)
        {
            Transform bone = avatarAnimator.GetBoneTransform(region.targetBone);
            if (bone == null) continue;

            foreach (var obj in region.colliderObjects)
            {
                if (obj != null)
                {
                    obj.transform.SetParent(bone, false);
                    obj.transform.localPosition = Vector3.zero;
                    obj.transform.localRotation = Quaternion.identity;
                }
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
        foreach (var region in regions)
        {
            bool hovering = false;

            foreach (var obj in region.colliderObjects)
            {
                if (obj == null) continue;
                Collider col = obj.GetComponent<Collider>();
                if (col == null) continue;

                Ray ray = mainCamera.ScreenPointToRay(Input.mousePosition);
                if (col.Raycast(ray, out RaycastHit hit, 100f))
                {
                    hovering = true;
                    break;
                }
            }

            if (hovering && !region.wasHovering && IsInIdleState())
            {
                PlayRandomVoice(region);
                TriggerHoverReaction(region, true);
                region.wasHovering = true;
            }
            else if (!hovering && region.wasHovering)
            {
                TriggerHoverReaction(region, false);
                region.wasHovering = false;
            }
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
        if (region.voiceClips.Count == 0) return;
        if (voiceAudioSource.isPlaying) return;

        AudioClip clip = region.voiceClips[Random.Range(0, region.voiceClips.Count)];
        voiceAudioSource.clip = clip;
        voiceAudioSource.Play();
    }
}
