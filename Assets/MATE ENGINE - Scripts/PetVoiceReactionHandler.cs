using UnityEngine;
using System.Collections.Generic;

public class PetVoiceReactionHandler : MonoBehaviour
{
    [System.Serializable]
    public class VoiceRegion
    {
        public string name;
        public HumanBodyBones targetBone;
        public Vector3 offset, worldOffset;
        public float hoverRadius = 50f;
        public Color gizmoColor = new(1f, 0.5f, 0f, 0.25f);
        public List<AudioClip> voiceClips = new();
        public AnimationClip hoverAnimation, faceAnimation;
        public bool enableHoverObject, bindHoverObjectToBone, enableLayeredSound;
        public GameObject hoverObject;
        [Range(0.1f, 10f)] public float despawnAfterSeconds = 5f;
        public List<AudioClip> layeredVoiceClips = new();
        [HideInInspector] public bool wasHovering;
        [HideInInspector] public Transform bone;
    }

    class HoverInstance { public GameObject obj; public float despawnTime; }

    public static bool GlobalHoverObjectsEnabled = true;
    public Animator avatarAnimator;
    public List<VoiceRegion> regions = new();
    public AudioSource voiceAudioSource, layeredAudioSource;
    public string idleStateName = "Idle", dragStateName = "isDragging", danceStateName = "isDancing";
    public string hoverTriggerParam = "HoverTrigger", hoverFaceTriggerParam = "HoverFaceTrigger";
    public bool showDebugGizmos = true;

    Camera cachedCamera;
    readonly Dictionary<VoiceRegion, List<HoverInstance>> pool = new();
    AnimatorOverrideController overrideController;
    RuntimeAnimatorController lastController;
    bool hasSetup;

    void Start() { if (!hasSetup) TrySetup(); }
    public void SetAnimator(Animator a) { avatarAnimator = a; hasSetup = false; }

    void TrySetup()
    {
        if (!avatarAnimator) return;
        if (!voiceAudioSource) voiceAudioSource = gameObject.AddComponent<AudioSource>();
        if (!layeredAudioSource) layeredAudioSource = gameObject.AddComponent<AudioSource>();
        cachedCamera = Camera.main;

        var baseCtrl = avatarAnimator.runtimeAnimatorController;
        if (!baseCtrl) return;

        if (overrideController == null || baseCtrl != lastController)
        {
            overrideController = baseCtrl is AnimatorOverrideController oc ? oc : new AnimatorOverrideController(baseCtrl);
            avatarAnimator.runtimeAnimatorController = overrideController;
            lastController = baseCtrl;
        }

        foreach (var region in regions)
        {
            region.bone = avatarAnimator.GetBoneTransform(region.targetBone);
            if (region.enableHoverObject && region.hoverObject)
            {
                var list = new List<HoverInstance>();
                for (int i = 0; i < 4; i++)
                {
                    var clone = Instantiate(region.hoverObject);
                    if (region.bindHoverObjectToBone && region.bone)
                    {
                        clone.transform.SetParent(region.bone, false);
                        clone.transform.localPosition = Vector3.zero;
                    }
                    clone.SetActive(false);
                    list.Add(new HoverInstance { obj = clone, despawnTime = -1f });
                }
                pool[region] = list;
            }
        }
        hasSetup = true;
    }

    void Update()
    {
        if (avatarAnimator && avatarAnimator.runtimeAnimatorController != lastController) hasSetup = false;
        if (!hasSetup) TrySetup();
        if (!cachedCamera || !avatarAnimator) return;

        Vector2 mouse = Input.mousePosition;
        foreach (var region in regions)
        {
            if (!region.bone) continue;
            Vector3 world = region.bone.position + region.bone.TransformVector(region.offset) + region.worldOffset;
            Vector2 screen = cachedCamera.WorldToScreenPoint(world);
            float scale = region.bone.lossyScale.magnitude;
            float radius = region.hoverRadius * scale;
            float screenRadius = Vector2.Distance(screen, cachedCamera.WorldToScreenPoint(world + cachedCamera.transform.right * radius));
            bool hovering = Vector2.Distance(mouse, screen) <= screenRadius;

            if (hovering && !region.wasHovering && !MenuActions.IsReactionBlocked() && IsInIdleState())
            {
                region.wasHovering = true;
                TriggerAnim(region, true);
                PlayRandomVoice(region);
                if (GlobalHoverObjectsEnabled && region.enableHoverObject && region.hoverObject && pool.TryGetValue(region, out var list))
                {
                    HoverInstance chosen = list.Find(h => !h.obj.activeSelf) ??
                        list.MinByOrDefault(h => h.despawnTime);
                    if (chosen != null)
                    {
                        if (!region.bindHoverObjectToBone) chosen.obj.transform.position = world;
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
            foreach (var h in pool[region])
                if (h.obj.activeSelf && Time.time >= h.despawnTime)
                {
                    h.obj.SetActive(false);
                    h.despawnTime = -1f;
                }
        }
    }

    void TriggerAnim(VoiceRegion region, bool state)
    {
        if (region.hoverAnimation && overrideController != null)
        {
            overrideController["HoverReaction"] = region.hoverAnimation;
            avatarAnimator.SetBool(hoverTriggerParam, state);
        }
        if (region.faceAnimation && overrideController != null)
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
            layeredAudioSource.PlayOneShot(region.layeredVoiceClips[Random.Range(0, region.layeredVoiceClips.Count)]);
    }

    bool IsInIdleState()
    {
        if (!avatarAnimator) return false;
        if (avatarAnimator.GetBool(dragStateName) || avatarAnimator.GetBool(danceStateName)) return false;
        return avatarAnimator.GetCurrentAnimatorStateInfo(0).IsName(idleStateName);
    }

#if UNITY_EDITOR
    void OnDrawGizmos()
    {
        if (!showDebugGizmos || !Application.isPlaying || !cachedCamera || !avatarAnimator) return;
        foreach (var region in regions)
        {
            if (!region.bone) continue;
            float scale = region.bone.lossyScale.magnitude;
            Vector3 center = region.bone.position + region.bone.TransformVector(region.offset) + region.worldOffset;
            Gizmos.color = region.gizmoColor;
            Gizmos.DrawWireSphere(center, region.hoverRadius * scale);
        }
    }
#endif
}

public static class ListExt
{
    public static T MinByOrDefault<T, TKey>(this List<T> list, System.Func<T, TKey> selector) where TKey : System.IComparable<TKey>
    {
        if (list == null || list.Count == 0) return default;
        int minIdx = 0;
        TKey min = selector(list[0]);
        for (int i = 1; i < list.Count; i++)
        {
            TKey val = selector(list[i]);
            if (val.CompareTo(min) < 0) { min = val; minIdx = i; }
        }
        return list[minIdx];
    }
}
