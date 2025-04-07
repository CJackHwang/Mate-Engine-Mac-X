using UnityEngine;
using VRM;
using System.Collections.Generic;

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

    [Tooltip("How fast blendshapes fade back to 0 when not updated.")]
    public float fadeSpeed = 5f;

    [Tooltip("Maximum seconds a blendshape can stay untouched before being force-reset.")]
    public float safeTimeout = 2f;

    [Tooltip("Minimum time to hold a blendshape value after an update (prevents fast re-trigger issues).")]
    public float minHoldTime = 0.1f;

    private VRMBlendShapeProxy proxy;

    private class BlendState
    {
        public float value;
        public float lastInput;
        public float lastUpdateTime;
        public float holdUntil;
    }

    private Dictionary<BlendShapePreset, BlendState> states = new();
    private Dictionary<BlendShapePreset, BlendShapeKey> keys = new();
    private List<KeyValuePair<BlendShapeKey, float>> reusableKeyValueList = new();

    private void Awake()
    {
        proxy = GetComponent<VRMBlendShapeProxy>();
        foreach (BlendShapePreset preset in System.Enum.GetValues(typeof(BlendShapePreset)))
        {
            if (!states.ContainsKey(preset))
                states[preset] = new BlendState();

            if (!keys.ContainsKey(preset))
                keys[preset] = BlendShapeKey.CreateFromPreset(preset);
        }
    }

    private void LateUpdate()
    {
        if (proxy == null || proxy.BlendShapeAvatar == null) return;

        float now = Time.time;
        float dt = Time.deltaTime;

        UpdateState(BlendShapePreset.Blink, Blink, now, dt);
        UpdateState(BlendShapePreset.Blink_L, Blink_L, now, dt);
        UpdateState(BlendShapePreset.Blink_R, Blink_R, now, dt);
        UpdateState(BlendShapePreset.LookUp, LookUp, now, dt);
        UpdateState(BlendShapePreset.LookDown, LookDown, now, dt);
        UpdateState(BlendShapePreset.LookLeft, LookLeft, now, dt);
        UpdateState(BlendShapePreset.LookRight, LookRight, now, dt);
        UpdateState(BlendShapePreset.Neutral, Neutral, now, dt);
        UpdateState(BlendShapePreset.A, A, now, dt);
        UpdateState(BlendShapePreset.I, I, now, dt);
        UpdateState(BlendShapePreset.U, U, now, dt);
        UpdateState(BlendShapePreset.E, E, now, dt);
        UpdateState(BlendShapePreset.O, O, now, dt);
        UpdateState(BlendShapePreset.Joy, Joy, now, dt);
        UpdateState(BlendShapePreset.Angry, Angry, now, dt);
        UpdateState(BlendShapePreset.Sorrow, Sorrow, now, dt);
        UpdateState(BlendShapePreset.Fun, Fun, now, dt);

        reusableKeyValueList.Clear();
        foreach (var kv in states)
        {
            reusableKeyValueList.Add(new KeyValuePair<BlendShapeKey, float>(keys[kv.Key], kv.Value.value));
        }

        proxy.SetValues(reusableKeyValueList);
        proxy.Apply();
    }

    private void UpdateState(BlendShapePreset preset, float input, float now, float dt)
    {
        var state = states[preset];

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
