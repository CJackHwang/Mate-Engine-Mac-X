using System.Collections;
using System.Reflection;
using UnityEngine;

public class AvatarRebindHandler : MonoBehaviour
{
    public bool rebindOnEnable = true;
    public bool rebindOnStart = false;
    public int waitFrames = 1;
    public bool setCullingAlwaysAnimate = true;
    public bool softControllerNudge = true;
    public bool hardControllerNudge = false;
    public bool rebindUniversalBlendshapes = true;

    void OnEnable()
    {
        if (rebindOnEnable) StartCoroutine(RebindRoutine());
    }

    void Start()
    {
        if (rebindOnStart) StartCoroutine(RebindRoutine());
    }

    public void RebindNow()
    {
        StartCoroutine(RebindRoutine());
    }

    public static void RebindTree(GameObject root, bool setCulling = true, bool softNudge = true, bool hardNudge = false)
    {
        if (root == null) return;
        var anims = root.GetComponentsInChildren<Animator>(true);
        for (int i = 0; i < anims.Length; i++)
        {
            var a = anims[i];
            if (a == null) continue;
            if (setCulling) a.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            if (hardNudge)
            {
                var ctrl = a.runtimeAnimatorController;
                a.runtimeAnimatorController = null;
                a.Update(0f);
                a.runtimeAnimatorController = ctrl;
            }
            else if (softNudge)
            {
                var ctrl = a.runtimeAnimatorController;
                a.runtimeAnimatorController = ctrl;
            }
            a.Rebind();
            a.Update(0f);
        }
    }

    IEnumerator RebindRoutine()
    {
        for (int i = 0; i < Mathf.Max(0, waitFrames); i++) yield return null;
        RebindTree(gameObject, setCullingAlwaysAnimate, softControllerNudge, hardControllerNudge);
        if (rebindUniversalBlendshapes) TryRebindUniversalBlendshapes();
    }

    void TryRebindUniversalBlendshapes()
    {
        var t = System.Type.GetType("UniversalBlendshapes");
        if (t == null) return;
        var m = t.GetMethod("RebindAllIn", BindingFlags.Public | BindingFlags.Static);
        if (m != null) m.Invoke(null, new object[] { gameObject });
    }
}
