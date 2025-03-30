using UnityEngine;
using UnityEngine.Animations;
using System.Collections.Generic;
using System.Collections;

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

        [Header("Hover Object Settings (Per Region)")]
        public bool enableHoverObject = false;
        public GameObject hoverObject;
        public bool bindHoverObjectToBone = false;

        [Header("Layered Sound Settings (Per Region)")] // Replace Later
        public bool enableLayeredSound = false;
        public List<AudioClip> layeredVoiceClips = new List<AudioClip>();

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
    public AudioSource layeredAudioSource; // New secondary audio source for layered sounds

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

        if (layeredAudioSource == null)
            layeredAudioSource = gameObject.AddComponent<AudioSource>();

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

                    if (!region.wasHovering && IsInIdleState())
                    {
                        PlayRandomVoice(region);
                        TriggerHoverReaction(region, true);
                    }

                    break;
                }
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

        // Play layered audio if enabled
        if (region.enableLayeredSound && region.layeredVoiceClips.Count > 0)
        {
            AudioClip layeredClip = region.layeredVoiceClips[Random.Range(0, region.layeredVoiceClips.Count)];
            layeredAudioSource.PlayOneShot(layeredClip);
        }
    }
}
