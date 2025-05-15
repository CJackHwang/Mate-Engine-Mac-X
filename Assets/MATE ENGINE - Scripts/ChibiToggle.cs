using UnityEngine;
using System.Collections;
using System.Collections.Generic;

[RequireComponent(typeof(Animator))]
public class ChibiToggle : MonoBehaviour
{
    [Header("Chibi Scale Settings")]
    public Vector3 chibiArmatureScale = new Vector3(0.3f, 0.3f, 0.3f);
    public Vector3 chibiHeadScale = new Vector3(2.7f, 2.7f, 2.7f);
    public Vector3 chibiUpperLegScale = new Vector3(0.6f, 0.6f, 0.6f);

    [Header("Gizmo Interaction")]
    public float screenInteractionRadius = 30f;
    public float holdDuration = 2f;
    public bool showDebugGizmos = true;
    public Color gizmoColor = new Color(1f, 0.5f, 0.7f, 0.25f);

    [Header("Sound Effects")]
    public AudioSource audioSource;
    public List<AudioClip> chibiEnterSounds = new List<AudioClip>();
    public List<AudioClip> chibiExitSounds = new List<AudioClip>();

    [Header("Particle Effect")]
    public GameObject particleEffectObject;
    public float particleDuration = 4f;

    private Animator anim;
    private Transform armatureRoot, head, leftFoot, rightFoot;
    private Transform leftUpperLeg, rightUpperLeg;

    private bool isChibi = false;
    private float holdTimer = 0f;
    private Camera mainCam;

    private Vector3 originalArmaturePosition;

    void Start()
    {
        anim = GetComponent<Animator>();
        anim.applyRootMotion = false;
        mainCam = Camera.main;

        Transform hips = anim.GetBoneTransform(HumanBodyBones.Hips);
        head = anim.GetBoneTransform(HumanBodyBones.Head);
        leftFoot = anim.GetBoneTransform(HumanBodyBones.LeftFoot);
        rightFoot = anim.GetBoneTransform(HumanBodyBones.RightFoot);
        leftUpperLeg = anim.GetBoneTransform(HumanBodyBones.LeftUpperLeg);
        rightUpperLeg = anim.GetBoneTransform(HumanBodyBones.RightUpperLeg);

        if (hips != null)
        {
            armatureRoot = hips;
            while (armatureRoot.parent != null && armatureRoot.parent != transform)
            {
                armatureRoot = armatureRoot.parent;
            }

            originalArmaturePosition = armatureRoot.localPosition;
        }
    }

    void Update()
    {
        if (!armatureRoot || !head || !leftFoot || !rightFoot || mainCam == null || MenuActions.IsChibiModeBlocked())
            return;

        Vector2 mousePos = Input.mousePosition;

        Vector2 leftFootScreen = mainCam.WorldToScreenPoint(leftFoot.position);
        Vector2 rightFootScreen = mainCam.WorldToScreenPoint(rightFoot.position);

        bool hoveringLeft = Vector2.Distance(mousePos, leftFootScreen) <= screenInteractionRadius;
        bool hoveringRight = Vector2.Distance(mousePos, rightFootScreen) <= screenInteractionRadius;

        if (hoveringLeft || hoveringRight)
        {
            holdTimer += Time.deltaTime;
            if (holdTimer >= holdDuration)
            {
                ToggleChibiMode();
                holdTimer = 0f;
            }
        }
        else
        {
            holdTimer = 0f;
        }
    }

    void ToggleChibiMode()
    {
        if (!armatureRoot || !head || !leftFoot || !rightFoot) return;

        bool becomingChibi = !isChibi;

        float originalFootY = Mathf.Min(leftFoot.position.y, rightFoot.position.y);

        armatureRoot.localScale = becomingChibi ? chibiArmatureScale : Vector3.one;
        head.localScale = becomingChibi ? chibiHeadScale : Vector3.one;
        if (leftUpperLeg) leftUpperLeg.localScale = becomingChibi ? chibiUpperLegScale : Vector3.one;
        if (rightUpperLeg) rightUpperLeg.localScale = becomingChibi ? chibiUpperLegScale : Vector3.one;

        isChibi = becomingChibi;
        PlayRandomSound(becomingChibi);
        TriggerParticles();
        StartCoroutine(AdjustFeetToGround(originalFootY));
    }

    private IEnumerator AdjustFeetToGround(float originalFootY)
    {
        yield return null; 

        float newFootY = Mathf.Min(leftFoot.position.y, rightFoot.position.y);
        float offsetY = originalFootY - newFootY;
        transform.position += new Vector3(0f, offsetY, 0f);
    }


    void PlayRandomSound(bool enteringChibi)
    {
        if (!audioSource) return;

        List<AudioClip> sourceList = enteringChibi ? chibiEnterSounds : chibiExitSounds;
        if (sourceList.Count == 0) return;

        AudioClip clip = sourceList[Random.Range(0, sourceList.Count)];
        audioSource.PlayOneShot(clip);
    }

    void TriggerParticles()
    {
        if (!particleEffectObject) return;

        StopAllCoroutines();
        StartCoroutine(TemporaryParticleCoroutine());
    }

    IEnumerator TemporaryParticleCoroutine()
    {
        particleEffectObject.SetActive(true);
        yield return new WaitForSeconds(particleDuration);
        particleEffectObject.SetActive(false);
    }

    void OnDrawGizmos()
    {
        if (!showDebugGizmos || !Application.isPlaying || !mainCam) return;

        Gizmos.color = gizmoColor;
        DrawScreenRadiusSphere(leftFoot, screenInteractionRadius);
        DrawScreenRadiusSphere(rightFoot, screenInteractionRadius);
    }

    private void DrawScreenRadiusSphere(Transform bone, float screenRadius)
    {
        if (bone == null) return;

        Vector3 screenPos = mainCam.WorldToScreenPoint(bone.position);
        if (screenPos.z < 0f) return;

        Vector3 offsetScreen = screenPos + new Vector3(screenRadius, 0f, 0f);
        Vector3 offsetWorld = mainCam.ScreenToWorldPoint(offsetScreen);
        float radiusWorld = Vector3.Distance(bone.position, offsetWorld);

        Gizmos.DrawWireSphere(bone.position, radiusWorld);
    }
}
