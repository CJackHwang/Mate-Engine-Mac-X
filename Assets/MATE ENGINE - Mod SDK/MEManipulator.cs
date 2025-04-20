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
        public GameObject sourceObject; // Holds the component with the override values
    }

    public List<ReplacementEntry> replacements = new();
    private MEReceiver receiver;

    void Awake()
    {
        if (Application.isPlaying)
        {
            receiver = FindReceiver();
            if (receiver && receiver.VRMModel && receiver.CustomVRM)
                ApplyAllReplacements();
        }
    }

    MEReceiver FindReceiver()
    {
        var all = GameObject.FindObjectsOfType<MEReceiver>(true);
        foreach (var r in all)
            if (r.VRMModel && r.CustomVRM)
                return r;
        return null;
    }

    void ApplyAllReplacements()
    {
        foreach (var entry in replacements)
        {
            if (!entry.sourceObject) continue;
            var overrideComponents = entry.sourceObject.GetComponents<MonoBehaviour>();
            foreach (var overrideComp in overrideComponents)
            {
                if (overrideComp == null) continue;
                Type t = overrideComp.GetType();
                CopyFieldsTo(receiver.VRMModel, t, overrideComp);
                CopyFieldsTo(receiver.CustomVRM, t, overrideComp);
            }
        }
    }

    void CopyFieldsTo(GameObject targetRoot, Type type, MonoBehaviour source)
    {
        var target = targetRoot.GetComponent(type);
        if (!target) return;

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
