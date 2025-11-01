using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

[Serializable]
public class FoodEntry
{
    public string id;
    public GameObject obj;
    [Range(0.01f, 2f)] public float spawnDuration = 0.2f;
    [Range(0.01f, 2f)] public float despawnDuration = 0.15f;
    public List<AudioClip> spawnClips = new();
    public List<AudioClip> spawnLayerClips = new();
    public List<AudioClip> despawnClips = new();
    public List<AudioClip> interactClips = new();
    public Vector3 worldOffset = Vector3.zero;
    public bool followMouse = true;
    [Range(-3f, 3f)] public float minPitch = 0.95f;
    [Range(-3f, 3f)] public float maxPitch = 1.05f;
}

public class AvatarFoodController : MonoBehaviour
{
    [Header("Feature")]
    public bool featureEnabled = false;

    [Header("Disable When Feature Off")]
    public List<GameObject> disableWhenOff = new();


    public List<FoodEntry> foods = new();
    public AudioSource spawnDespawnAudio;
    public AudioSource spawnMainAudio;
    public AudioSource spawnLayerAudio;
    public AudioSource interactionAudio;

    [Header("Interaction Radius")]
    public bool useWorldRadius = true;
    [Range(0f, 2f)] public float interactRadiusWorld = 0.18f;
    [Range(10f, 1000f)] public float interactRadiusPx = 90f;
    [Range(0f, 100f)] public float bigScreenRadiusScale = 100f;

    public float interactCooldown = 0.35f;
    public bool showHeadGizmo = true;
    public Color headGizmoColor = new Color(0.2f, 0.8f, 1f, 0.35f);

    [Header("Head Center")]
    public Vector3 headLocalOffset = Vector3.zero;
    public Vector3 headWorldOffset = Vector3.zero;

    public bool enableSway = true;
    public bool swayUseLocalRotation = true;
    public float mouseSensitivity = 0.6f;
    public bool invertHorizontal = false;
    public bool invertVertical = false;
    public float horizontalVelocityToLean = 0.25f;
    public float verticalVelocityToPitch = 0.15f;
    public float maxLeanZ = 25f;
    public float maxLeanX = 12f;
    public float springFrequency = 2.6f;
    public float dampingRatio = 0.35f;
    public float swayBlendSpeed = 8f;

    public float avatarProbeInterval = 0.25f;

    Camera cam;
    Animator animator;
    Transform head;
    FoodEntry activeEntry;
    string activeIdNorm;
    Coroutine scaleRoutine;
    float depthZ = 1f;
    float nextInteractAt;
    bool wasInside;

    Vector2 prevMousePos;
    Vector2 filteredDelta;
    float leanZ;
    float leanZVel;
    float leanX;
    float leanXVel;
    float swayWeight;
    Quaternion baseLocalRot;
    Quaternion baseWorldRot;

    VRMLoader loader;
    GameObject currentAvatarRoot;
    float probeTimer;

    void Awake()
    {
        featureEnabled = SaveLoadHandler.Instance ? SaveLoadHandler.Instance.data.enableFeedSystem : false;
        if (!featureEnabled) DeactivateAll();
        TrySetup();
        DeactivateAll();
        prevMousePos = Input.mousePosition;
        ProbeAvatarNow();
    }

    void TrySetup()
    {
        if (!cam) cam = Camera.main ? Camera.main : FindFirstObjectByType<Camera>();
        if (!spawnDespawnAudio) spawnDespawnAudio = gameObject.AddComponent<AudioSource>();
        if (!spawnMainAudio) spawnMainAudio = gameObject.AddComponent<AudioSource>();
        if (!spawnLayerAudio) spawnLayerAudio = gameObject.AddComponent<AudioSource>();
        if (!interactionAudio) interactionAudio = gameObject.AddComponent<AudioSource>();
        if (!loader) loader = FindFirstObjectByType<VRMLoader>();
        UpdateDepthFromHead();
    }

    void UpdateDepthFromHead()
    {
        if (cam && head)
        {
            var p = cam.WorldToScreenPoint(GetHeadCenterWorld());
            depthZ = p.z <= 0f ? 1f : p.z;
        }
    }

    void LateUpdate()
    {
        probeTimer -= Time.unscaledDeltaTime;
        if (probeTimer <= 0f) { ProbeAvatarNow(); probeTimer = Mathf.Max(0.05f, avatarProbeInterval); }
        if (!featureEnabled)
        {
            if (activeEntry != null) DeactivateAll();
            return;
        }
        if (activeEntry != null && activeEntry.obj && activeEntry.followMouse)
        {
            var mp = Input.mousePosition;
            mp.z = depthZ;
            var wp = cam.ScreenToWorldPoint(mp) + activeEntry.worldOffset;
            activeEntry.obj.transform.position = wp;
        }
        UpdateSway();
        ApplySway();
        HeadInteractCheck();
    }

    void UpdateSway()
    {
        if (!enableSway || activeEntry == null || activeEntry.obj == null) { swayWeight = Mathf.MoveTowards(swayWeight, 0f, swayBlendSpeed * Time.deltaTime); return; }
        Vector2 m = Input.mousePosition;
        Vector2 md = (m - prevMousePos) * mouseSensitivity;
        prevMousePos = m;
        float dt = Time.deltaTime;
        filteredDelta = Vector2.Lerp(filteredDelta, md, 1f - Mathf.Exp(-12f * dt));
        float signH = invertHorizontal ? 1f : -1f;
        float signV = invertVertical ? -1f : 1f;
        float targetLeanZ = Mathf.Clamp(signH * filteredDelta.x * horizontalVelocityToLean, -maxLeanZ, maxLeanZ);
        float targetLeanX = Mathf.Clamp(signV * filteredDelta.y * verticalVelocityToPitch, -maxLeanX, maxLeanX);
        Spring(ref leanZ, ref leanZVel, targetLeanZ, springFrequency, dampingRatio, dt);
        Spring(ref leanX, ref leanXVel, targetLeanX, springFrequency, dampingRatio, dt);
        swayWeight = Mathf.MoveTowards(swayWeight, 1f, swayBlendSpeed * dt);
    }

    void ApplySway()
    {
        if (swayWeight <= 0f || activeEntry == null || activeEntry.obj == null) return;
        float x = leanX * swayWeight;
        float z = leanZ * swayWeight;
        if (swayUseLocalRotation) activeEntry.obj.transform.localRotation = baseLocalRot * Quaternion.Euler(x, 0f, z);
        else
        {
            Transform space = transform;
            Quaternion add = Quaternion.AngleAxis(x, space.right) * Quaternion.AngleAxis(z, space.forward);
            activeEntry.obj.transform.rotation = add * baseWorldRot;
        }
    }

    void HeadInteractCheck()
    {
        if (!cam || !head) return;
        Vector2 mouse = Input.mousePosition;
        Vector3 centerWorld = GetHeadCenterWorld();
        Vector2 centerScreen = cam.WorldToScreenPoint(centerWorld);
        float rPx = ComputeScreenRadiusPx(centerWorld);
        bool inside = (mouse - centerScreen).sqrMagnitude <= rPx * rPx;
        if (inside && !wasInside && Time.time >= nextInteractAt)
        {
            PlayRandom(activeEntry != null ? activeEntry.interactClips : null, interactionAudio, activeEntry);
            nextInteractAt = Time.time + interactCooldown;
        }
        wasInside = inside;
    }

    Vector3 GetHeadCenterWorld()
    {
        if (!head) return transform.position;
        return head.position + head.TransformVector(headLocalOffset) + headWorldOffset;
    }

    float ComputeScreenRadiusPx(Vector3 centerWorld)
    {
        if (!cam || !head) return interactRadiusPx;
        if (!useWorldRadius) return interactRadiusPx;
        float scale = head.lossyScale.magnitude;
        float radiusScale = (animator != null && animator.GetBool("isBigScreen")) ? (bigScreenRadiusScale * 0.01f) : 1f;
        float worldR = Mathf.Max(0f, interactRadiusWorld) * Mathf.Max(0.0001f, scale) * radiusScale;
        Vector2 center = cam.WorldToScreenPoint(centerWorld);
        Vector2 edge = cam.WorldToScreenPoint(centerWorld + cam.transform.right * worldR);
        return (center - edge).magnitude;
    }

    public void ToggleById(string id)
    {
        if (!featureEnabled) return;
        if (string.IsNullOrEmpty(id)) return;
        if (HasActive()) { DespawnActive(); return; }
        string n = NormalizeId(id);
        var entry = PickRandomById(n);
        if (entry == null) return;
        Spawn(entry, n);
    }

    public void ToggleByIndex(int index)
    {
        if (!featureEnabled) return;
        if (index < 0 || index >= foods.Count) return;
        if (HasActive()) { DespawnActive(); return; }
        var e = foods[index];
        Spawn(e, NormalizeId(e != null ? e.id : null));
    }

    public void SpawnByIndex(int index)
    {
        if (!featureEnabled) return;
        if (index < 0 || index >= foods.Count) return;
        var e = foods[index];
        Spawn(e, NormalizeId(e != null ? e.id : null));
    }

    public void SpawnById(string id)
    {
        if (!featureEnabled) return;
        string n = NormalizeId(id);
        var entry = PickRandomById(n);
        if (entry == null) return;
        Spawn(entry, n);
    }

    public void DespawnActive()
    {
        if (activeEntry == null || activeEntry.obj == null) return;
        if (scaleRoutine != null) StopCoroutine(scaleRoutine);
        PlayRandom(activeEntry.despawnClips, spawnDespawnAudio, activeEntry);
        var t = activeEntry.obj.transform;
        var from = t.localScale;
        scaleRoutine = StartCoroutine(ScaleTo(t, from, Vector3.zero, activeEntry.despawnDuration, () =>
        {
            ResetPose(activeEntry);
            activeEntry.obj.SetActive(false);
            activeEntry = null;
            activeIdNorm = null;
            ResetSwayState();
        }));
    }

    public void PlayInteract()
    {
        if (!featureEnabled) return;
        PlayRandom(activeEntry != null ? activeEntry.interactClips : null, interactionAudio, activeEntry);
        nextInteractAt = Time.time + interactCooldown;
    }

    public bool HasActive() => activeEntry != null && activeEntry.obj && activeEntry.obj.activeSelf;

    void Spawn(FoodEntry entry, string idNorm)
    {
        DeactivateAll();
        if (entry == null || entry.obj == null) return;
        ResetPose(entry);
        activeEntry = entry;
        activeIdNorm = idNorm;
        activeEntry.obj.SetActive(true);
        activeEntry.obj.transform.localScale = Vector3.zero;
        baseLocalRot = Quaternion.identity;
        baseWorldRot = Quaternion.identity;
        ResetSwayState();
        UpdateDepthFromHead();
        MoveOnceToCursor();
        PlaySpawnSounds(activeEntry);
        if (scaleRoutine != null) StopCoroutine(scaleRoutine);
        scaleRoutine = StartCoroutine(ScaleTo(activeEntry.obj.transform, Vector3.zero, Vector3.one, activeEntry.spawnDuration, null));
        wasInside = false;
    }

    void PlaySpawnSounds(FoodEntry e)
    {
        var mainSrc = spawnMainAudio ? spawnMainAudio : spawnDespawnAudio;
        var layerSrc = spawnLayerAudio ? spawnLayerAudio : spawnDespawnAudio;
        PlayRandom(e.spawnClips, mainSrc, e);
        PlayRandom(e.spawnLayerClips, layerSrc, e);
    }

    void MoveOnceToCursor()
    {
        if (activeEntry == null || cam == null) return;
        var mp = Input.mousePosition;
        mp.z = depthZ;
        var wp = cam.ScreenToWorldPoint(mp) + activeEntry.worldOffset;
        activeEntry.obj.transform.position = wp;
    }

    void DeactivateAll()
    {
        for (int i = 0; i < foods.Count; i++)
            if (foods[i].obj) { ResetPose(foods[i]); foods[i].obj.SetActive(false); }
        activeEntry = null;
        activeIdNorm = null;
        ResetSwayState();
    }

    void ResetPose(FoodEntry e)
    {
        if (e == null || e.obj == null) return;
        var tr = e.obj.transform;
        tr.localRotation = Quaternion.identity;
        tr.rotation = Quaternion.identity;
    }

    void ResetSwayState()
    {
        leanX = 0f; leanZ = 0f; leanXVel = 0f; leanZVel = 0f; filteredDelta = Vector2.zero; swayWeight = 0f;
    }

    IEnumerator ScaleTo(Transform t, Vector3 from, Vector3 to, float dur, Action onDone)
    {
        if (dur <= 0f)
        {
            t.localScale = to;
            onDone?.Invoke();
            yield break;
        }
        float e = 0f;
        t.localScale = from;
        while (e < dur)
        {
            e += Time.unscaledDeltaTime;
            float k = Mathf.Clamp01(e / dur);
            t.localScale = Vector3.LerpUnclamped(from, to, k);
            yield return null;
        }
        t.localScale = to;
        onDone?.Invoke();
    }

    void PlayRandom(List<AudioClip> clips, AudioSource src, FoodEntry entry)
    {
        if (src == null || clips == null || clips.Count == 0) return;
        float lo = entry != null ? entry.minPitch : 1f;
        float hi = entry != null ? entry.maxPitch : 1f;
        if (hi < lo) { var tmp = lo; lo = hi; hi = tmp; }
        src.pitch = UnityEngine.Random.Range(lo, hi);
        int i = UnityEngine.Random.Range(0, clips.Count);
        src.PlayOneShot(clips[i]);
    }

    static void Spring(ref float x, ref float v, float xt, float f, float z, float dt)
    {
        float w = Mathf.Max(0.01f, f) * 2f * Mathf.PI;
        float a = w * w * (xt - x) - 2f * z * w * v;
        v += a * dt;
        x += v * dt;
    }

    FoodEntry PickRandomById(string idNorm)
    {
        var list = foods.Where(f => f != null && f.obj != null && string.Equals(NormalizeId(f.id), idNorm, StringComparison.OrdinalIgnoreCase)).ToList();
        if (list.Count == 0) return null;
        int i = UnityEngine.Random.Range(0, list.Count);
        return list[i];
    }

    string NormalizeId(string s)
    {
        return string.IsNullOrEmpty(s) ? "" : s.Trim();
    }

    void ProbeAvatarNow()
    {
        if (!loader) loader = FindFirstObjectByType<VRMLoader>();
        GameObject root = null;
        if (loader) root = loader.GetCurrentModel() != null ? loader.GetCurrentModel() : loader.mainModel;
        if (!root)
        {
            var any = GameObject.FindObjectsByType<Animator>(FindObjectsInactive.Include, FindObjectsSortMode.None).FirstOrDefault(a => a && a.isActiveAndEnabled);
            root = any ? any.gameObject : null;
        }
        if (root != currentAvatarRoot)
        {
            currentAvatarRoot = root;
            Animator found = root ? root.GetComponentInChildren<Animator>(true) : null;
            if (found != animator)
            {
                animator = found;
                head = animator ? animator.GetBoneTransform(HumanBodyBones.Head) : null;
                UpdateDepthFromHead();
            }
        }
    }

#if UNITY_EDITOR
    void OnDrawGizmos()
    {
        if (!showHeadGizmo) return;
        if (!cam) cam = Camera.main ? Camera.main : FindFirstObjectByType<Camera>();
        if (!animator) ProbeAvatarNow();
        if (!head && animator) head = animator.GetBoneTransform(HumanBodyBones.Head);
        if (!head) return;
        Vector3 center = GetHeadCenterWorld();
        float scale = head.lossyScale.magnitude;
        float radiusScale = (animator != null && animator.GetBool("isBigScreen")) ? (bigScreenRadiusScale * 0.01f) : 1f;
        float worldR = useWorldRadius ? interactRadiusWorld * scale * radiusScale : HandleUtilityApproximateWorldRadius(interactRadiusPx);
        Gizmos.color = headGizmoColor;
        Gizmos.DrawWireSphere(center, Mathf.Max(0.0001f, worldR));
    }

    float HandleUtilityApproximateWorldRadius(float radiusPx)
    {
        if (!cam || !head) return 0.2f;
        Vector3 a = GetHeadCenterWorld();
        Vector3 b = a + cam.transform.right * 0.1f;
        Vector2 sa = cam.WorldToScreenPoint(a);
        Vector2 sb = cam.WorldToScreenPoint(b);
        float pxPerWorld = (sb - sa).magnitude / 0.1f;
        if (pxPerWorld <= 0.0001f) return 0.2f;
        return radiusPx / pxPerWorld;
    }
#endif

    public void SetFeatureEnabled(bool on)
    {
        featureEnabled = on;
        foreach (var g in disableWhenOff)
            if (g) g.SetActive(on);
        if (!featureEnabled) DeactivateAll();
    }


    public void EnableFeature()
    {
        SetFeatureEnabled(true);
    }

    public void DisableFeature()
    {
        SetFeatureEnabled(false);
    }

    public void ToggleFeature()
    {
        SetFeatureEnabled(!featureEnabled);
    }
}
