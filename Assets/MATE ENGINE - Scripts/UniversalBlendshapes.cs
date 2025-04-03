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
        public float holdUntil; // New: hold the value at least until this time.
    }

    private Dictionary<BlendShapePreset, BlendState> states;

    private void Awake()
    {
        proxy = GetComponent<VRMBlendShapeProxy>();
        states = new Dictionary<BlendShapePreset, BlendState>();

        foreach (BlendShapePreset preset in System.Enum.GetValues(typeof(BlendShapePreset)))
        {
            if (!states.ContainsKey(preset))
                states[preset] = new BlendState();
        }
    }

    private void LateUpdate()
    {
        if (proxy == null || proxy.BlendShapeAvatar == null) return;

        float now = Time.time;
        float dt = Time.deltaTime;

        // Update each blendshape state.
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

        // Immediately apply all the blendshape values.
        foreach (var kv in states)
        {
            proxy.ImmediatelySetValue(kv.Key, kv.Value.value);
        }

        proxy.Apply();
    }

    private void UpdateState(BlendShapePreset preset, float input, float now, float dt)
    {
        var state = states[preset];

        // Determine if this frame has an "active" update.
        // We consider any change as active.
        bool valueChanged = !Mathf.Approximately(input, state.lastInput);

        // Additionally, if the input is nonzero we assume it’s actively driven.
        bool activelyDriven = !Mathf.Approximately(input, 0f);

        if (valueChanged || activelyDriven)
        {
            state.lastInput = input;
            state.lastUpdateTime = now;
            state.value = input;
            // Lock the value for at least minHoldTime seconds.
            state.holdUntil = now + minHoldTime;
        }
        else
        {
            // If we're still in the hold period, keep the current value.
            if (now < state.holdUntil)
            {
                state.value = input;
            }
            else
            {
                // Once hold period expires, check safe timeout.
                float idleTime = now - state.lastUpdateTime;
                if (idleTime > safeTimeout)
                {
                    state.value = 0f;
                }
                else
                {
                    // Smooth fade back to 0.
                    state.value = Mathf.MoveTowards(state.value, 0f, fadeSpeed * dt);
                }
            }
        }
    }
}
