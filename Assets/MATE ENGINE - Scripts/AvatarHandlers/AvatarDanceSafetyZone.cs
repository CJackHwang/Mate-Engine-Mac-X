using UnityEngine;
using UnityEngine.UI;
using Kirurobo;
using System.Linq;
using CustomDancePlayer;

public class AvatarDanceSafetyZone : MonoBehaviour
{
    public Camera cam;
    public UniWindowController uniWindow;
    public string dancingParam = "isCustomDancing";
    public float marginPx = 24f;
    public float softZoneLeftPx = 220f;
    public float softZoneRightPx = 220f;
    public float panSpeedPxPerSec = 1800f;
    public bool moveWindowAlong = true;
    public bool enableSafety = false;

    [Header("Activate Toggle")]
    public Toggle activateToggle;

    Animator animator;
    Transform hip;
    int dancingHash;
    AvatarDanceHandler danceHandler;
    Transform lastModelRoot;
    bool wasActive;
    Vector3 camPosBeforeDance;
    Quaternion camRotBeforeDance;
    float refetchCooldown;

    void OnEnable()
    {
        dancingHash = Animator.StringToHash(dancingParam);
        danceHandler = FindFirstObjectByType<AvatarDanceHandler>();
        if (uniWindow == null) uniWindow = UniWindowController.current;
        if (activateToggle != null)
        {
#if UNITY_2019_1_OR_NEWER
            activateToggle.SetIsOnWithoutNotify(enableSafety);
#else
            activateToggle.isOn = enableSafety;
#endif
            activateToggle.onValueChanged.AddListener(OnActivateToggleChanged);
        }
    }

    void OnDisable()
    {
        if (activateToggle != null) activateToggle.onValueChanged.RemoveListener(OnActivateToggleChanged);
        if (wasActive) RestoreCamera();
        wasActive = false;
    }

    void Update()
    {
        bool active = IsActive();
        if (active && !wasActive)
        {
            if (cam == null) return;
            camPosBeforeDance = cam.transform.position;
            camRotBeforeDance = cam.transform.rotation;
            FetchActiveAvatar(true);
        }
        if (!active && wasActive) RestoreCamera();
        wasActive = active;

        if (active)
        {
            refetchCooldown -= Time.unscaledDeltaTime;
            if (refetchCooldown <= 0f) { FetchActiveAvatar(false); refetchCooldown = 0.5f; }
        }
    }

    void LateUpdate()
    {
        if (!wasActive) return;
        if (cam == null || hip == null || uniWindow == null) return;
#if !UNITY_EDITOR
        if (Screen.fullScreen) return;
#endif
        if (uniWindow.shouldFitMonitor || uniWindow.isZoomed) return;

        Rect r = new Rect(0f, 0f, Screen.width, Screen.height);
        r.xMin += marginPx;
        r.xMax -= marginPx;

        Vector3 sp = cam.WorldToScreenPoint(hip.position);
        float leftEdge = r.xMin + softZoneLeftPx;
        float rightEdge = r.xMax - softZoneRightPx;

        float over = 0f;
        if (sp.x < leftEdge) over = sp.x - leftEdge;
        else if (sp.x > rightEdge) over = sp.x - rightEdge;
        if (Mathf.Abs(over) < 0.5f) return;

        float stepPx = Mathf.Clamp(over, -panSpeedPxPerSec * Time.unscaledDeltaTime, panSpeedPxPerSec * Time.unscaledDeltaTime);
        float depth = Mathf.Max(0.01f, sp.z);

        Vector3 a = cam.ScreenToWorldPoint(new Vector3(sp.x, sp.y, depth));
        Vector3 b = cam.ScreenToWorldPoint(new Vector3(sp.x - stepPx, sp.y, depth));
        float dx = b.x - a.x;

        cam.transform.position -= new Vector3(dx, 0f, 0f);

        if (moveWindowAlong && !uniWindow.shouldFitMonitor && !uniWindow.isZoomed)
        {
            var wp = uniWindow.windowPosition;
            wp.x += stepPx;
            uniWindow.windowPosition = wp;
        }
    }

    void RestoreCamera()
    {
        if (cam == null) return;
        cam.transform.position = camPosBeforeDance;
        cam.transform.rotation = camRotBeforeDance;
    }

    void OnActivateToggleChanged(bool on)
    {
        enableSafety = on;
        if (!on && wasActive)
        {
            RestoreCamera();
            wasActive = false;
        }
    }

    public void SetSafetyEnabled(bool on)
    {
        enableSafety = on;
        if (activateToggle != null)
        {
#if UNITY_2019_1_OR_NEWER
            activateToggle.SetIsOnWithoutNotify(on);
#else
            activateToggle.isOn = on;
#endif
        }
        if (!on && wasActive)
        {
            RestoreCamera();
            wasActive = false;
        }
    }

    bool IsActive()
    {
        if (!enableSafety) return false;
        if (cam == null) return false;
        if (danceHandler == null) danceHandler = FindFirstObjectByType<AvatarDanceHandler>();
        bool byPlayer = danceHandler != null && danceHandler.IsPlaying;
        bool byParam = false;
        if (animator != null)
        {
            var ps = animator.parameters;
            for (int i = 0; i < ps.Length; i++)
                if (ps[i].type == AnimatorControllerParameterType.Bool && ps[i].nameHash == dancingHash)
                { byParam = animator.GetBool(dancingHash); break; }
        }
        return byPlayer || byParam;
    }

    void FetchActiveAvatar(bool force)
    {
        Transform curRoot = ResolveCurrentModelRoot();
        bool modelChanged = curRoot != lastModelRoot;
        if (!force && !wasActive && !modelChanged) return;
        lastModelRoot = curRoot;
        animator = ResolveAnimator(curRoot);
        hip = ResolveHip(animator);
    }

    Transform ResolveCurrentModelRoot()
    {
        var loader = FindFirstObjectByType<VRMLoader>();
        if (loader != null)
        {
            var current = loader.GetCurrentModel();
            if (current != null) return current.transform;
        }
        var model = GameObject.Find("Model");
        if (model != null) return model.transform;
        return null;
    }

    Animator ResolveAnimator(Transform root)
    {
        if (root != null)
        {
            var found = root.GetComponentsInChildren<Animator>(true).FirstOrDefault(a => a && a.gameObject.activeInHierarchy);
            if (found != null) return found;
        }
        var any = GameObject.FindObjectsByType<Animator>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        return any.FirstOrDefault(a => a && a.isActiveAndEnabled);
    }

    Transform ResolveHip(Animator a)
    {
        if (a != null && a.isHuman)
        {
            var h = a.GetBoneTransform(HumanBodyBones.Hips);
            if (h != null) return h;
        }
        return a != null ? a.transform : transform;
    }
}
