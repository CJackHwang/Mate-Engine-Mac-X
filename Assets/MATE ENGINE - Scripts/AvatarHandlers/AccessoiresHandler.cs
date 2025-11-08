using UnityEngine;
using System.Collections;
using System.Collections.Generic;

public class AccessoiresHandler : MonoBehaviour
{
    [System.Serializable]
    public class AccessoryRule
    {
        public string ruleName;
        public bool isEnabled = false;
        public HumanBodyBones targetBone;
        public GameObject linkedObject;
        [Range(0f, 1f)] public float smoothness = 0f;
        public bool steamExclusive = false;
        public Vector3 positionOffset = Vector3.zero;
    }

    public int steamAppId = 0;
    public int ttlDays = 14;
    public float retrySeconds = 5f;
    public float maxWaitSeconds = 180f;

    public Animator animator;
    public List<AccessoryRule> rules = new List<AccessoryRule>();
    public bool featureEnabled = true;

    class BoneTracking
    {
        public Transform bone;
        public GameObject obj;
        public Vector3 currentPosition;
        public Quaternion currentRotation;
        public bool lastActiveState;
    }

    Dictionary<AccessoryRule, BoneTracking> trackingMap = new Dictionary<AccessoryRule, BoneTracking>();

    void Start()
    {
        if (animator == null) animator = GetComponent<Animator>();
        if (!SteamDRM.Initialized) SteamDRM.Initialize(steamAppId, ttlDays);

        foreach (var rule in rules)
        {
            if (rule.linkedObject == null) continue;
            Transform boneTransform = animator.GetBoneTransform(rule.targetBone);
            if (boneTransform == null) continue;

            var tracking = new BoneTracking
            {
                bone = boneTransform,
                obj = rule.linkedObject,
                currentPosition = boneTransform.position,
                currentRotation = boneTransform.rotation,
                lastActiveState = false
            };
            trackingMap[rule] = tracking;
        }

        StartCoroutine(ReinitLoop());
    }

    void Update()
    {
        foreach (var kvp in trackingMap)
        {
            var rule = kvp.Key;
            var tracking = kvp.Value;

            bool shouldBeActive =
                featureEnabled &&
                rule.isEnabled &&
                (!rule.steamExclusive || SteamDRM.IsEntitled);

            if (tracking.obj != null && tracking.lastActiveState != shouldBeActive)
            {
                tracking.obj.SetActive(shouldBeActive);
                tracking.lastActiveState = shouldBeActive;
            }

            if (shouldBeActive && tracking.obj != null)
            {
                Vector3 targetPos = tracking.bone.TransformPoint(rule.positionOffset);
                Quaternion targetRot = tracking.bone.rotation;

                tracking.currentPosition = Vector3.Lerp(tracking.currentPosition, targetPos, 1f - rule.smoothness);
                tracking.currentRotation = Quaternion.Slerp(tracking.currentRotation, targetRot, 1f - rule.smoothness);

                tracking.obj.transform.position = tracking.currentPosition;
                tracking.obj.transform.rotation = tracking.currentRotation;
            }
        }
    }

    public static readonly List<AccessoiresHandler> ActiveHandlers = new List<AccessoiresHandler>();

    void OnEnable()
    {
        if (!ActiveHandlers.Contains(this)) ActiveHandlers.Add(this);
    }

    void OnDisable()
    {
        ActiveHandlers.Remove(this);
    }

    IEnumerator ReinitLoop()
    {
        float t = 0f;
        while (!SteamDRM.IsEntitled && t < maxWaitSeconds)
        {
            SteamDRM.TryInitLive(steamAppId, ttlDays);
            yield return new WaitForSeconds(retrySeconds);
            t += retrySeconds;
        }
    }
}
