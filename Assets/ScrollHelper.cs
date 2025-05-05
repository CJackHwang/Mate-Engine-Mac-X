using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

[RequireComponent(typeof(ScrollRect))]
public class ScrollHelper : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    [Header("Smoothness (0 = instant, 1 = ultra slow)")]
    [Range(0f, 1f)]
    public float smoothFactor = 0.1f;

    private ScrollRect scrollRect;
    private bool isPointerOver = false;
    private float scrollVelocity = 0f;

    void Awake()
    {
        scrollRect = GetComponent<ScrollRect>();
    }

    void Update()
    {
        if (isPointerOver)
        {
            float input = Input.GetAxis("Mouse ScrollWheel");
            if (Mathf.Abs(input) > 0.001f)
            {
                // Capture input and convert to scroll velocity
                scrollVelocity += input * scrollRect.scrollSensitivity * 0.01f;
            }
        }

        if (Mathf.Abs(scrollVelocity) > 0.0001f)
        {
            // Apply velocity to ScrollRect
            scrollRect.verticalNormalizedPosition = Mathf.Clamp01(
                scrollRect.verticalNormalizedPosition + scrollVelocity
            );

            // Decay velocity smoothly
            scrollVelocity = Mathf.Lerp(scrollVelocity, 0f, 1f - Mathf.Pow(1f - smoothFactor, Time.deltaTime * 60f));
        }
    }

    public void OnPointerEnter(PointerEventData eventData) => isPointerOver = true;
    public void OnPointerExit(PointerEventData eventData) => isPointerOver = false;
}
