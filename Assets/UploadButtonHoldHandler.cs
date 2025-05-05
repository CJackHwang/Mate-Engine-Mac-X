using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.EventSystems;
using System.Collections;

public class UploadButtonHoldHandler : MonoBehaviour, IPointerDownHandler, IPointerUpHandler
{
    [HideInInspector] public AvatarLibraryMenu.AvatarEntry entry;

    [Header("UI References")]
    public Slider progressSlider;
    public TMP_Text labelText;

    [Header("Audio")]
    public AudioSource audioSource;
    public AudioClip tickSound;
    public AudioClip completeSound;

    private Coroutine holdRoutine;
    private bool isHolding = false;

    public void OnPointerDown(PointerEventData eventData)
    {
        if (holdRoutine == null)
        {
            isHolding = true;
            holdRoutine = StartCoroutine(HoldToUpload());
        }
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        isHolding = false;
    }

    private IEnumerator HoldToUpload()
    {
        float duration = 5f;
        float timeHeld = 0f;
        int lastSecond = -1;
        float pitch = 1f;
        bool completed = false;

        if (labelText != null) labelText.text = "5";
        GetComponent<Button>().interactable = false;

        while (isHolding && timeHeld < duration)
        {
            timeHeld += Time.deltaTime;
            int currentSecond = Mathf.CeilToInt(duration - timeHeld);

            if (currentSecond != lastSecond)
            {
                lastSecond = currentSecond;
                if (labelText != null) labelText.text = currentSecond.ToString();

                if (audioSource != null && tickSound != null)
                {
                    audioSource.pitch = pitch;
                    audioSource.PlayOneShot(tickSound);
                    pitch += 0.1f;
                }
            }

            yield return null;
        }

        if (timeHeld >= duration && isHolding)
        {
            completed = true;
            if (labelText != null) labelText.text = "0";

            if (audioSource != null && completeSound != null)
            {
                audioSource.pitch = 1f;
                audioSource.PlayOneShot(completeSound);
            }

            yield return new WaitForSeconds(0.5f);

            if (SteamWorkshopHandler.Instance != null)
                SteamWorkshopHandler.Instance.UploadToWorkshop(entry, progressSlider);

            if (labelText != null) labelText.text = "Uploaded";
            yield return new WaitForSeconds(1.5f);
        }

        if (!completed && labelText != null)
            labelText.text = "Upload";

        GetComponent<Button>().interactable = true;
        holdRoutine = null;
    }
}
