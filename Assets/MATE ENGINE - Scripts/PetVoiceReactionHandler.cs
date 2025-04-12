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
        [Range(0.1f, 10f)] public float despawnAfterSeconds = 5f;

        [Header("Layered Sound Settings")]
        public bool enableLayeredSound = false;
        public List<AudioClip> layeredVoiceClips = new();

        [HideInInspector] public bool wasHovering = false;
    }

    public static bool GlobalHoverObjectsEnabled = true;

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


    private void Start()
    {
        if (voiceAudioSource == null)
            voiceAudioSource = gameObject.AddComponent<AudioSource>();
        if (layeredAudioSource == null)
            layeredAudioSource = gameObject.AddComponent<AudioSource>();

        cachedCamera = Camera.main;

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
        // No parenting or moving hoverObject itself
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

                if (PetVoiceReactionHandler.GlobalHoverObjectsEnabled && region.enableHoverObject && region.hoverObject != null)
                {
                    Vector3 spawnPos = region.bindHoverObjectToBone && bone != null ? bone.position : region.hoverObject.transform.position;
                    Quaternion spawnRot = region.hoverObject.transform.rotation;

                    GameObject clone = Instantiate(region.hoverObject);
                    clone.transform.position = spawnPos;
                    clone.transform.rotation = spawnRot;
                    clone.SetActive(true);

                    if (region.bindHoverObjectToBone && bone != null)
                        clone.transform.SetParent(bone, true);

                    StartCoroutine(AutoDestroy(clone, region.despawnAfterSeconds));
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

    private IEnumerator AutoDestroy(GameObject obj, float delay)
    {
        yield return new WaitForSeconds(delay);
        if (obj != null) Destroy(obj);
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
