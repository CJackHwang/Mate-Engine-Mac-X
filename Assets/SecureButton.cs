using UnityEngine;
using UnityEngine.EventSystems;
using TMPro;
using System.Collections;

public class SecureButton : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler
{
    public float holdDuration = 5f;

    private Coroutine holdRoutine;
    private TextMeshProUGUI buttonText;
    private string originalText;

    private void Awake()
    {
        buttonText = GetComponentInChildren<TextMeshProUGUI>();
        if (buttonText != null)
            originalText = buttonText.text;
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        if (holdRoutine != null) StopCoroutine(holdRoutine);
        holdRoutine = StartCoroutine(HoldTimer());
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        CancelHold();
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        CancelHold();
    }

    private void CancelHold()
    {
        if (holdRoutine != null)
        {
            StopCoroutine(holdRoutine);
            holdRoutine = null;
        }

        if (buttonText != null)
            buttonText.text = originalText;
    }

    private IEnumerator HoldTimer()
    {
        float time = holdDuration;

        while (time > 0f)
        {
            if (buttonText != null)
                buttonText.text = Mathf.CeilToInt(time).ToString();

            yield return null;
            time -= Time.unscaledDeltaTime;
        }

        if (buttonText != null)
            buttonText.text = originalText;

        BroadcastMessage("OnSecureClick", SendMessageOptions.DontRequireReceiver);
    }
}
