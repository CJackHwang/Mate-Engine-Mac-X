using UnityEngine;
using VRM;
using UniVRM10;
using System.Collections.Generic;
using System;
using System.Reflection;

[DisallowMultipleComponent]
public class UniversalBlendshapes : MonoBehaviour
{
    [Range(0f, 1f)] public float Blink;
    [Range(0f, 1f)] public float Blink_L;
    [Range(0f, 1f)] public float Blink_R;
    [Range(0f, 1f)] public float LookUp;
    [Range(0f, 1f)] public float LookDown;
    [Range(0f, 1f)] public float LookLeft;
    [Range(0f, 1f)] public float LookRight;
    [Range(0f, 1f)] public float Neutral;
    [Range(0f, 1f)] public float A;
    [Range(0f, 1f)] public float I;
    [Range(0f, 1f)] public float U;
    [Range(0f, 1f)] public float E;
    [Range(0f, 1f)] public float O;
    [Range(0f, 1f)] public float Joy;
    [Range(0f, 1f)] public float Angry;
    [Range(0f, 1f)] public float Sorrow;
    [Range(0f, 1f)] public float Fun;

    public float fadeSpeed = 5f;
    public float safeTimeout = 2f;
    public float minHoldTime = 0.1f;

    private VRMBlendShapeProxy proxy0;
    private Vrm10Instance vrm1;
    private Vrm10RuntimeExpression expr1;

    private class BlendState
    {
        public float value;
        public float lastInput;
        public float lastUpdateTime;
        public float holdUntil;
    }

    private readonly Dictionary<string, BlendState> states = new();
    private readonly List<KeyValuePair<BlendShapeKey, float>> reusableList = new();

    private static readonly string[] keys = new[]
    {
        "Blink", "Blink_L", "Blink_R",
        "LookUp", "LookDown", "LookLeft", "LookRight",
        "Neutral",
        "A", "I", "U", "E", "O",
        "Joy", "Angry", "Sorrow", "Fun"
    };

    private static readonly Dictionary<string, string> vrm10KeyMap = new()
    {
        { "A", "aa" }, { "I", "ih" }, { "U", "ou" }, { "E", "ee" }, { "O", "oh" },
        { "Joy", "happy" }, { "Angry", "angry" }, { "Sorrow", "sad" }, { "Fun", "relaxed" },
        { "Blink", "blink" }, { "Blink_L", "blinkLeft" }, { "Blink_R", "blinkRight" },
        { "LookUp", "lookUp" }, { "LookDown", "lookDown" }, { "LookLeft", "lookLeft" }, { "LookRight", "lookRight" },
        { "Neutral", "neutral" }
    };

    private readonly Dictionary<string, FieldInfo> fieldMap = new();
    private readonly Dictionary<string, ExpressionKey> vrm1ExpressionKeyMap = new();

    private void Awake()
    {
        proxy0 = GetComponent<VRMBlendShapeProxy>();
        vrm1 = GetComponentInChildren<Vrm10Instance>(true);
        expr1 = vrm1 != null ? vrm1.Runtime?.Expression : null;

        foreach (var key in keys)
        {
            states[key] = new BlendState();
            fieldMap[key] = typeof(UniversalBlendshapes).GetField(key, BindingFlags.Instance | BindingFlags.Public);
        }

        if (expr1 == null)
        {
            Debug.LogWarning("[UniversalBlendshapes] No Vrm10RuntimeExpression found.");
        }
        else
        {
            vrm1ExpressionKeyMap.Clear();
            foreach (var k in expr1.ExpressionKeys)
            {
                vrm1ExpressionKeyMap[k.Name] = k;
            }

            Debug.Log("[UniversalBlendshapes] Expression keys loaded: " + string.Join(", ", vrm1ExpressionKeyMap.Keys));
        }
    }

    private void LateUpdate()
    {
        float now = Time.time;
        float dt = Time.deltaTime;

        foreach (var key in keys)
        {
            float value = (float)fieldMap[key].GetValue(this);
            UpdateState(key, value, now, dt);
        }

        if (proxy0 != null)
        {
            reusableList.Clear();
            foreach (var kv in states)
            {
                if (Enum.TryParse(kv.Key, out BlendShapePreset preset))
                {
                    reusableList.Add(new KeyValuePair<BlendShapeKey, float>(
                        BlendShapeKey.CreateFromPreset(preset), kv.Value.value
                    ));
                }
            }
            proxy0.SetValues(reusableList);
            proxy0.Apply();
        }
        else if (expr1 != null)
        {
            foreach (var kv in states)
            {
                string keyName = vrm10KeyMap.TryGetValue(kv.Key, out var mapped) ? mapped : kv.Key;
                if (vrm1ExpressionKeyMap.TryGetValue(keyName, out var exprKey))
                {
                    expr1.SetWeight(exprKey, kv.Value.value);
                }
            }
        }
    }

    private void UpdateState(string key, float input, float now, float dt)
    {
        if (!states.TryGetValue(key, out var state)) return;

        bool valueChanged = !Mathf.Approximately(input, state.lastInput);
        bool activelyDriven = !Mathf.Approximately(input, 0f);

        if (valueChanged || activelyDriven)
        {
            state.lastInput = input;
            state.lastUpdateTime = now;
            state.value = input;
            state.holdUntil = now + minHoldTime;
        }
        else
        {
            if (now < state.holdUntil)
            {
                state.value = input;
            }
            else
            {
                float idleTime = now - state.lastUpdateTime;
                if (idleTime > safeTimeout)
                {
                    state.value = 0f;
                }
                else
                {
                    state.value = Mathf.MoveTowards(state.value, 0f, fadeSpeed * dt);
                }
            }
        }
    }
}
