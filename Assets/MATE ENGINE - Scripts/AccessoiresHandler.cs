using UnityEngine;
using System.Collections.Generic;

public class AccessoiresHandler : MonoBehaviour
{
    [System.Serializable]
    public class AccessoryRule
    {
        public string ruleName;
        public bool isEnabled = true;
        public HumanBodyBones targetBone;
        public GameObject linkedObject;
        [Range(0f, 1f)] public float smoothness = 0f;
        public bool steamExclusive = false;
        public Vector3 positionOffset = Vector3.zero;
    }
    public Animator animator;
    public List<AccessoryRule> rules = new List<AccessoryRule>();
    public bool featureEnabled = true;

    private class BoneTracking
    {
        public Transform bone;
        public GameObject obj;
        public Vector3 currentPosition;
        public Quaternion currentRotation;
        public bool lastActiveState;
    }

    private Dictionary<AccessoryRule, BoneTracking> trackingMap = new Dictionary<AccessoryRule, BoneTracking>();

    void Start()
    {
        if (animator == null) animator = GetComponent<Animator>();
        if (!SteamChecker.IsSteamVersionInitialized) SteamChecker.Initialize();

        foreach (var rule in rules)
        {
            if (rule.linkedObject == null) continue;

            Transform boneTransform = animator.GetBoneTransform(rule.targetBone);
            if (boneTransform == null) continue;

            BoneTracking tracking = new BoneTracking
            {
                bone = boneTransform,
                obj = rule.linkedObject,
                currentPosition = boneTransform.position,
                currentRotation = boneTransform.rotation,
                lastActiveState = false
            };

            trackingMap[rule] = tracking;
        }
    }

    public void LoadAccessoryStatesFromSave()
    {
        if (SaveLoadHandler.Instance == null) return;
        var save = SaveLoadHandler.Instance.data.accessoryStates;

        foreach (var rule in rules)
        {
            if (save.TryGetValue(rule.ruleName, out bool enabled))
            {
                rule.isEnabled = enabled;
            }
        }
    }

    public void SaveAccessoryStatesToSave()
    {
        if (SaveLoadHandler.Instance == null) return;
        var save = SaveLoadHandler.Instance.data.accessoryStates;

        foreach (var rule in rules)
        {
            if (!string.IsNullOrEmpty(rule.ruleName))
            {
                save[rule.ruleName] = rule.isEnabled;
            }
        }
    }

    public void ResetAccessoryStatesToDefault()
    {
        foreach (var rule in rules)
        {
            rule.isEnabled = false; 
        }
    }

    public void ClearAccessoryStatesFromSave()
    {
        if (SaveLoadHandler.Instance == null) return;
        foreach (var rule in rules)
        {
            SaveLoadHandler.Instance.data.accessoryStates.Remove(rule.ruleName);
        }
    }

    void Update()
    {
        foreach (var kvp in trackingMap)
        {
            AccessoryRule rule = kvp.Key;
            BoneTracking tracking = kvp.Value;

            bool shouldBeActive =
                featureEnabled &&
                rule.isEnabled &&
                (!rule.steamExclusive || SteamChecker.IsSteamVersion());

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
        if (!ActiveHandlers.Contains(this))
            ActiveHandlers.Add(this);
    }

    void OnDisable()
    {
        ActiveHandlers.Remove(this);
    }


}
public static class SteamChecker
{
    private static bool initialized = false;
    private static bool steamDetected = false;

    public static bool IsSteamVersionInitialized => initialized;

    public static void Initialize()
    {
        initialized = true;
        try
        {
            steamDetected = Steamworks.SteamAPI.Init() && Steamworks.SteamAPI.IsSteamRunning();
        }
        catch
        {
            steamDetected = false;
        }
    }

    public static bool IsSteamVersion()
    {
        return steamDetected;
    }


}