using UnityEngine;

public class AvatarDragSoundHandler : MonoBehaviour
{
    [Header("Drag Sound Settings")]
    public AudioSource dragStartSound;
    public AudioSource dragStopSound;

    [Header("Pitch Randomizer Settings")]
    [Range(0, 100)] public float maxHighPitchPercent = 10f;
    [Range(0, 100)] public float maxLowPitchPercent = 10f;

    private bool wasDragging = false;
    private AvatarAnimatorController avatarController;

    void Start()
    {
        avatarController = GetComponent<AvatarAnimatorController>();
        if (avatarController == null)
        {
            Debug.LogError("AvatarAnimatorController script not found on this GameObject.");
        }
    }

    void Update()
    {
        if (avatarController == null) return;

        // Detect drag start
        if (avatarController.isDragging && !wasDragging)
        {
            PlayDragStartSound();
            wasDragging = true;
        }
        // Detect drag stop
        else if (!avatarController.isDragging && wasDragging)
        {
            PlayDragStopSound();
            wasDragging = false;
        }
    }

    void PlayDragStartSound()
    {
        if (dragStartSound != null)
        {
            RandomizePitch(dragStartSound);
            dragStartSound.Play();
        }
    }

    void PlayDragStopSound()
    {
        if (dragStopSound != null)
        {
            RandomizePitch(dragStopSound);
            dragStopSound.Play();
        }
    }

    void RandomizePitch(AudioSource audioSource)
    {
        float pitchRange = (maxHighPitchPercent + maxLowPitchPercent) / 100f;
        float randomPitch = 1f + Random.Range(-maxLowPitchPercent / 100f, maxHighPitchPercent / 100f);
        audioSource.pitch = Mathf.Clamp(randomPitch, 1f - pitchRange, 1f + pitchRange);
    }
}