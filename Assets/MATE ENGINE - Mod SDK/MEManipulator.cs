using UnityEngine;
using System;
using System.Collections.Generic;
using System.Reflection;

[ExecuteInEditMode]
public class MEReplacer : MonoBehaviour
{
    [Serializable]
    public class ReplacementEntry
    {
        public GameObject sourceObject;
    }

    public List<ReplacementEntry> replacements = new();

    private MEReceiver receiver;
    private GameObject lastPatchedVRMModel;
    private GameObject lastPatchedCustomVRM;

    void OnEnable()
    {
        receiver = FindReceiver();
    }

    void Update()
    {
        if (!Application.isPlaying) return;
        if (receiver == null) receiver = FindReceiver();
        if (receiver == null) return;

        bool needsPatch = false;

        if (receiver.VRMModel && receiver.VRMModel != lastPatchedVRMModel)
        {
            lastPatchedVRMModel = receiver.VRMModel;
            needsPatch = true;
        }

        if (receiver.CustomVRM && receiver.CustomVRM != lastPatchedCustomVRM)
        {
            lastPatchedCustomVRM = receiver.CustomVRM;
            needsPatch = true;
        }

        if (needsPatch)
        {
            ApplyAllReplacements();
        }
    }

    MEReceiver FindReceiver()
    {
        var all = GameObject.FindObjectsOfType<MEReceiver>(true);
        foreach (var r in all)
            if (r.VRMModel != null || r.CustomVRM != null)
                return r;
        return null;
    }

    void ApplyAllReplacements()
    {
        foreach (var entry in replacements)
        {
            if (!entry.sourceObject) continue;

            var overrideAnimator = entry.sourceObject.GetComponent<Animator>();
            if (overrideAnimator != null && overrideAnimator.runtimeAnimatorController != null)
            {
                var targets = new[] { receiver.VRMModel, receiver.CustomVRM };
                foreach (var target in targets)
                {
                    var anim = target?.GetComponent<Animator>();
                    if (anim != null)
                        anim.runtimeAnimatorController = overrideAnimator.runtimeAnimatorController;
                }
            }

            var overrideComponents = entry.sourceObject.GetComponents<MonoBehaviour>();
            foreach (var overrideComp in overrideComponents)
            {
                if (overrideComp == null) continue;
                Type t = overrideComp.GetType();

                if (receiver.VRMModel != null)
                    CopyFieldsAndProperties(receiver.VRMModel, t, overrideComp);

                if (receiver.CustomVRM != null)
                    CopyFieldsAndProperties(receiver.CustomVRM, t, overrideComp);

                if (overrideComp is Behaviour b)
                    b.enabled = false;
            }

            entry.sourceObject.SetActive(false);
        }
    }

    void CopyFieldsAndProperties(GameObject targetRoot, Type type, MonoBehaviour source)
    {
        var target = targetRoot.GetComponent(type);
        if (!target)
            target = targetRoot.AddComponent(type);

        var fields = type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        foreach (var f in fields)
        {
            if (f.IsNotSerialized || f.Name == "enabled") continue;
            try
            {
                object value = f.GetValue(source);
                if (IsEmpty(value)) continue;
                f.SetValue(target, value);
            }
            catch { }
        }

        var properties = type.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        foreach (var p in properties)
        {
            if (!p.CanWrite || !p.CanRead || p.Name == "name" || p.Name == "tag" || p.Name == "enabled") continue;
            try
            {
                object value = p.GetValue(source, null);
                if (IsEmpty(value)) continue;
                p.SetValue(target, value, null);
            }
            catch { }
        }
    }

    bool IsEmpty(object value)
    {
        if (value == null) return true;

        Type type = value.GetType();

        if (type == typeof(string)) return string.IsNullOrWhiteSpace((string)value);
        if (type.IsValueType)
        {
            object defaultValue = Activator.CreateInstance(type);
            return value.Equals(defaultValue);
        }

        if (value is UnityEngine.Object unityObj)
            return unityObj == null;

        return false;
    }
}
