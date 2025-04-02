using UnityEngine;
using VRM;

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

    private VRMBlendShapeProxy proxy;

    private void Awake()
    {
        proxy = GetComponent<VRMBlendShapeProxy>();
    }

    private void LateUpdate()
    {
        if (proxy == null || proxy.BlendShapeAvatar == null)
            return;

        proxy.ImmediatelySetValue(BlendShapeKey.CreateFromPreset(BlendShapePreset.Blink), Blink);
        proxy.ImmediatelySetValue(BlendShapeKey.CreateFromPreset(BlendShapePreset.Blink_L), Blink_L);
        proxy.ImmediatelySetValue(BlendShapeKey.CreateFromPreset(BlendShapePreset.Blink_R), Blink_R);
        proxy.ImmediatelySetValue(BlendShapeKey.CreateFromPreset(BlendShapePreset.LookUp), LookUp);
        proxy.ImmediatelySetValue(BlendShapeKey.CreateFromPreset(BlendShapePreset.LookDown), LookDown);
        proxy.ImmediatelySetValue(BlendShapeKey.CreateFromPreset(BlendShapePreset.LookLeft), LookLeft);
        proxy.ImmediatelySetValue(BlendShapeKey.CreateFromPreset(BlendShapePreset.LookRight), LookRight);
        proxy.ImmediatelySetValue(BlendShapeKey.CreateFromPreset(BlendShapePreset.Neutral), Neutral);
        proxy.ImmediatelySetValue(BlendShapeKey.CreateFromPreset(BlendShapePreset.A), A);
        proxy.ImmediatelySetValue(BlendShapeKey.CreateFromPreset(BlendShapePreset.I), I);
        proxy.ImmediatelySetValue(BlendShapeKey.CreateFromPreset(BlendShapePreset.U), U);
        proxy.ImmediatelySetValue(BlendShapeKey.CreateFromPreset(BlendShapePreset.E), E);
        proxy.ImmediatelySetValue(BlendShapeKey.CreateFromPreset(BlendShapePreset.O), O);
        proxy.ImmediatelySetValue(BlendShapeKey.CreateFromPreset(BlendShapePreset.Joy), Joy);
        proxy.ImmediatelySetValue(BlendShapeKey.CreateFromPreset(BlendShapePreset.Angry), Angry);
        proxy.ImmediatelySetValue(BlendShapeKey.CreateFromPreset(BlendShapePreset.Sorrow), Sorrow);
        proxy.ImmediatelySetValue(BlendShapeKey.CreateFromPreset(BlendShapePreset.Fun), Fun);

        proxy.Apply();
    }
}
