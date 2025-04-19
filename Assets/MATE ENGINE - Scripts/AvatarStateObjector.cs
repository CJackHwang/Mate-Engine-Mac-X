using UnityEngine;
using System;
using System.Collections.Generic;

public class AvatarStateObjector : MonoBehaviour
{
    [Header("Avatar State Objector Rules")]
    public List<ObjectorRule> objectorRules = new List<ObjectorRule>();

    [Serializable]
    public class ObjectorRule
    {
        public string stateName;
        public GameObject targetObject;
        [Range(0f, 1f)] public float spawnAnimationSpeed = 0.1f; // 0 = instant, 1 = 4s
        [NonSerialized] public Vector3 originalScale;
        [NonSerialized] public float currentLerp;
        [NonSerialized] public bool wasActive;
    }

    private Animator cachedAnimator;
    private AvatarAnimatorController cachedAvatar;

    void Start()
    {
        cachedAvatar = FindObjectOfType<AvatarAnimatorController>();
        if (cachedAvatar != null)
            cachedAnimator = cachedAvatar.GetComponent<Animator>();

        for (int i = 0; i < objectorRules.Count; i++)
        {
            var rule = objectorRules[i];
            if (rule.targetObject != null)
            {
                rule.originalScale = rule.targetObject.transform.localScale;
                rule.targetObject.SetActive(false);
                rule.targetObject.transform.localScale = Vector3.zero;
                rule.wasActive = false;
                rule.currentLerp = 0f;
            }
        }
    }

    void Update()
    {
        if (cachedAnimator == null)
        {
            if (cachedAvatar == null)
                cachedAvatar = FindObjectOfType<AvatarAnimatorController>();

            if (cachedAvatar != null)
                cachedAnimator = cachedAvatar.GetComponent<Animator>();

            if (cachedAnimator == null) return;
        }

        for (int i = 0; i < objectorRules.Count; i++)
        {
            var rule = objectorRules[i];
            if (rule.targetObject == null) continue;

            bool shouldBeActive = false;

            if (cachedAnimator.HasParameter(rule.stateName, AnimatorControllerParameterType.Bool))
                shouldBeActive = cachedAnimator.GetBool(rule.stateName);
            else
            {
                var stateInfo = cachedAnimator.GetCurrentAnimatorStateInfo(0);
                if (!cachedAnimator.IsInTransition(0) && stateInfo.IsName(rule.stateName))
                    shouldBeActive = true;
            }

            // Animate transition
            float target = shouldBeActive ? 1f : 0f;
            float speed = Mathf.Lerp(10f, 0.25f, rule.spawnAnimationSpeed); // inverse of time
            rule.currentLerp = Mathf.MoveTowards(rule.currentLerp, target, Time.unscaledDeltaTime * speed);

            if (!rule.wasActive && rule.currentLerp > 0f)
            {
                rule.targetObject.SetActive(true);
                rule.wasActive = true;
            }

            rule.targetObject.transform.localScale = Vector3.Lerp(Vector3.zero, rule.originalScale, rule.currentLerp);

            if (rule.wasActive && rule.currentLerp <= 0f)
            {
                rule.targetObject.SetActive(false);
                rule.wasActive = false;
            }
        }
    }
}

public static class AnimatorExtensions
{
    public static bool HasParameter(this Animator animator, string name, AnimatorControllerParameterType type)
    {
        if (animator == null) return false;
        var parameters = animator.parameters;
        for (int i = 0; i < parameters.Length; i++)
        {
            if (parameters[i].name == name && parameters[i].type == type)
                return true;
        }
        return false;
    }
}