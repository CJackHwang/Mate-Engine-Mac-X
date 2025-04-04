using UnityEngine;

public class SafeAnimatorController : MonoBehaviour
{
    public Animator animator;
    private bool isTransitioning = false;
    private float transitionCooldown = 0.2f; // minimum time between transitions
    private float transitionTimer = 0f;

    void Update()
    {
        if (isTransitioning)
        {
            transitionTimer -= Time.deltaTime;
            if (transitionTimer <= 0f)
            {
                isTransitioning = false;
            }
        }
    }

    public void PlaySafe(string stateName, int layer = 0)
    {
        if (isTransitioning) return;

        AnimatorStateInfo currentState = animator.GetCurrentAnimatorStateInfo(layer);
        if (!currentState.IsName(stateName))
        {
            animator.CrossFadeInFixedTime(stateName, 0.1f, layer);
            isTransitioning = true;
            transitionTimer = transitionCooldown;
        }
    }
}
