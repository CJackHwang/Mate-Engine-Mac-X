
using UnityEngine;
using System.Collections.Generic;

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
        public AnimationClip faceAnimation;
        public bool enableHoverObject = false;
        public GameObject hoverObject;
        public bool bindHoverObjectToBone = false;
        [Range(0.1f, 10f)] public float despawnAfterSeconds = 5f;
        public bool enableLayeredSound = false;
        public List<AudioClip> layeredVoiceClips = new();
        [HideInInspector] public bool wasHovering = false;
        [HideInInspector] public Transform bone;
    }

    private class HoverInstance { public GameObject obj; public float despawnTime; }

    public static bool GlobalHoverObjectsEnabled = true;
    public Animator avatarAnimator;
    public List<VoiceRegion> regions = new();
    public AudioSource voiceAudioSource;
    public AudioSource layeredAudioSource;
    public string idleStateName = "Idle";
    public string dragStateName = "isDragging";
    public string danceStateName = "isDancing";
    public string hoverTriggerParam = "HoverTrigger";
    public string hoverFaceTriggerParam = "HoverFaceTrigger";
    public bool showDebugGizmos = true;

    private Camera cachedCamera;
    private readonly Dictionary<VoiceRegion, List<HoverInstance>> pool = new();
    private AnimatorOverrideController overrideController;
    private bool hasSetup = false;

    void Start()
    {
        if (!hasSetup) TrySetup();
    }

    public void SetAnimator(Animator newAnimator)
    {
        avatarAnimator = newAnimator;
        hasSetup = false;
    }

    void TrySetup()
    {
        if (avatarAnimator == null) return;
        if (voiceAudioSource == null) voiceAudioSource = gameObject.AddComponent<AudioSource>();
        if (layeredAudioSource == null) layeredAudioSource = gameObject.AddComponent<AudioSource>();
        cachedCamera = Camera.main;

        var baseController = avatarAnimator.runtimeAnimatorController;
        if (baseController == null) return;

        if (baseController is AnimatorOverrideController oc)
        {
            overrideController = oc;
        }
        else
        {
            overrideController = new AnimatorOverrideController(baseController);
            avatarAnimator.runtimeAnimatorController = overrideController;
        }

        foreach (var region in regions)
        {
            region.bone = avatarAnimator.GetBoneTransform(region.targetBone);
            if (region.enableHoverObject && region.hoverObject != null)
            {
                pool[region] = new List<HoverInstance>();
                for (int i = 0; i < 4; i++)
                {
                    var clone = Instantiate(region.hoverObject);
                    if (region.bindHoverObjectToBone && region.bone != null)
                    {
                        clone.transform.SetParent(region.bone, false);
                        clone.transform.localPosition = Vector3.zero;
                    }
                    clone.SetActive(false);
                    pool[region].Add(new HoverInstance { obj = clone, despawnTime = -1f });
                }
            }
        }

        hasSetup = true;
    }

    void Update()
    {
        if (!hasSetup) TrySetup();
        if (cachedCamera == null || avatarAnimator == null) return;

        Vector2 mouse = Input.mousePosition;

        for (int r = 0; r < regions.Count; r++)
        {
            var region = regions[r];
            if (region.bone == null) continue;

            Vector3 world = region.bone.position + region.bone.TransformVector(region.offset) + region.worldOffset;
            Vector2 screen = cachedCamera.WorldToScreenPoint(world);
            float scale = region.bone.lossyScale.magnitude;
            float radius = region.hoverRadius * scale;
            Vector2 edge = cachedCamera.WorldToScreenPoint(world + cachedCamera.transform.right * radius);
            float screenRadius = Vector2.Distance(screen, edge);
            float dist = Vector2.Distance(mouse, screen);
            bool hovering = dist <= screenRadius;

            if (hovering && !region.wasHovering && IsInIdleState())
            {
                region.wasHovering = true;
                TriggerAnim(region, true);
                PlayRandomVoice(region);

                if (GlobalHoverObjectsEnabled && region.enableHoverObject && region.hoverObject != null)
                {
                    var list = pool[region];
                    HoverInstance chosen = null;

                    for (int i = 0; i < list.Count; i++)
                    {
                        if (!list[i].obj.activeSelf)
                        {
                            chosen = list[i];
                            break;
                        }
                    }

                    if (chosen == null)
                    {
                        float oldest = float.MaxValue;
                        for (int i = 0; i < list.Count; i++)
                        {
                            if (list[i].despawnTime < oldest)
                            {
                                oldest = list[i].despawnTime;
                                chosen = list[i];
                            }
                        }
                    }

                    if (chosen != null)
                    {
                        if (!region.bindHoverObjectToBone)
                            chosen.obj.transform.position = world;
                        chosen.obj.SetActive(false);
                        chosen.obj.SetActive(true);
                        chosen.despawnTime = Time.time + region.despawnAfterSeconds;
                    }
                }
            }
            else if (!hovering && region.wasHovering)
            {
                region.wasHovering = false;
                TriggerAnim(region, false);
            }
        }

        foreach (var region in regions)
        {
            if (!region.enableHoverObject || !pool.ContainsKey(region)) continue;
            var list = pool[region];
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i].obj.activeSelf && Time.time >= list[i].despawnTime)
                {
                    list[i].obj.SetActive(false);
                    list[i].despawnTime = -1f;
                }
            }
        }
    }

    void TriggerAnim(VoiceRegion region, bool state)
    {
        if (region.hoverAnimation != null && overrideController != null)
        {
            overrideController["HoverReaction"] = region.hoverAnimation;
            avatarAnimator.SetBool(hoverTriggerParam, state);
        }
        if (region.faceAnimation != null && overrideController != null)
        {
            overrideController["HoverFace"] = region.faceAnimation;
            avatarAnimator.SetBool(hoverFaceTriggerParam, state);
        }
    }

    void PlayRandomVoice(VoiceRegion region)
    {
        if (region.voiceClips.Count > 0 && !voiceAudioSource.isPlaying)
        {
            voiceAudioSource.clip = region.voiceClips[Random.Range(0, region.voiceClips.Count)];
            voiceAudioSource.Play();
        }

        if (region.enableLayeredSound && region.layeredVoiceClips.Count > 0)
        {
            layeredAudioSource.PlayOneShot(region.layeredVoiceClips[Random.Range(0, region.layeredVoiceClips.Count)]);
        }
    }

    bool IsInIdleState()
    {
        if (avatarAnimator == null) return false;
        if (avatarAnimator.GetBool(dragStateName)) return false;
        if (avatarAnimator.GetBool(danceStateName)) return false;
        return avatarAnimator.GetCurrentAnimatorStateInfo(0).IsName(idleStateName);
    }

#if UNITY_EDITOR
    void OnDrawGizmos()
    {
        if (!showDebugGizmos || !Application.isPlaying || cachedCamera == null || avatarAnimator == null) return;
        foreach (var region in regions)
        {
            if (region.bone == null) continue;
            float scale = region.bone.lossyScale.magnitude;
            Vector3 center = region.bone.position + region.bone.TransformVector(region.offset) + region.worldOffset;
            Gizmos.color = region.gizmoColor;
            Gizmos.DrawWireSphere(center, region.hoverRadius * scale);
        }
    }
#endif
}