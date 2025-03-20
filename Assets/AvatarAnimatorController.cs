using UnityEngine;
using NAudio.CoreAudioApi;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Collections;

public class AvatarAnimatorController : MonoBehaviour
{
    [Header("General Settings")]
    public Animator animator;
    public bool enableAudioDetection = true;
    public float SOUND_THRESHOLD = 0.01f;
    public List<string> ignoredApps = new List<string> { "discord", "mateengine", "mateenginex", "chrome", "audiodg", "explorer" }; // No ".exe"

    [Header("Idle Animation Settings")]
    public int totalIdleAnimations = 10; // We may delete that integer. We will not need it.
    public float IDLE_SWITCH_TIME = 12f;
    public float IDLE_TRANSITION_TIME = 3f;

    [Header("Drag & Dance Settings")]
    public bool enableDragging = true;
    public bool enableDancing = true;

    public bool isDragging = false;
    public bool isSitting = false;
    public bool isDancing = false;

    private MMDevice defaultDevice;
    private float lastSoundCheckTime = 0f;
    private const float SOUND_CHECK_INTERVAL = 0.25f;

    private float idleTimer = 0f;
    private int idleState = 0;

    void Start()
    {
        if (animator == null)
            animator = GetComponent<Animator>();

        Application.runInBackground = true;

        try
        {
            var enumerator = new MMDeviceEnumerator();
            defaultDevice = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
        }
        catch (System.Exception) { }

        StartCoroutine(CheckSoundContinuously());
    }

    private IEnumerator CheckSoundContinuously()
    {
        while (true)
        {
            if (enableAudioDetection)
                CheckForSound();
            yield return null;
        }
    }

    void CheckForSound()
    {
        if (defaultDevice == null) return;

        bool isValidSoundPlaying = IsValidAppPlaying(); // ✅ Fully ignores bad apps

        if (!isDragging)
        {
            if (isValidSoundPlaying && enableDancing)
            {
                if (!isDancing)
                {
                    isDancing = true;
                    animator.SetBool("isDancing", true);
                }
            }
            else if (!isValidSoundPlaying) // ✅ Stops only if no valid app is playing
            {
                if (isDancing)
                {
                    isDancing = false;
                    animator.SetBool("isDancing", false);
                }
            }
        }
    }

    bool IsValidAppPlaying()
    {
        if (Time.time - lastSoundCheckTime < SOUND_CHECK_INTERVAL)
        {
            return isDancing; // ✅ If dancing, keep dancing until we properly check
        }

        lastSoundCheckTime = Time.time;
        bool validAppPlaying = false;

        try
        {
            var sessions = defaultDevice.AudioSessionManager.Sessions;
            for (int i = 0; i < sessions.Count; i++)
            {
                var session = sessions[i];

                if (session.AudioMeterInformation.MasterPeakValue > SOUND_THRESHOLD)
                {
                    try
                    {
                        int processId = (int)session.GetProcessID;
                        if (processId != 0)
                        {
                            Process process = Process.GetProcessById(processId);
                            string processName = process.ProcessName.ToLower(); // No ".exe"

                            if (!ignoredApps.Contains(processName) && !IsSubprocessIgnored(processName))
                            {
                                validAppPlaying = true; // ✅ A valid app is playing sound
                            }
                        }
                    }
                    catch (System.Exception)
                    {
                        continue;
                    }
                }
            }
        }
        catch (System.Exception) { }

        return validAppPlaying; // ✅ True = keep dancing, False = stop dancing
    }

    bool IsSubprocessIgnored(string processName)
    {
        foreach (string ignoredApp in ignoredApps)
        {
            if (processName.StartsWith(ignoredApp)) // ✅ Ensures sub-processes are caught
            {
                return true;
            }
        }
        return false;
    }

    void Update()
    {
        if (enableDragging && Input.GetMouseButtonDown(0))
        {
            isDragging = true;
            animator.SetBool("isDragging", true);
            animator.SetBool("isDancing", false);
            isDancing = false;
        }

        if (enableDragging && Input.GetMouseButtonUp(0))
        {
            isDragging = false;
            animator.SetBool("isDragging", false);
            animator.SetBool("isDancing", isDancing);
        }

        // Handle idle animation cycling in sequence every X seconds
        idleTimer += Time.deltaTime;
        if (idleTimer > IDLE_SWITCH_TIME)
        {
            if (idleState + 1 >= totalIdleAnimations)
            {
                idleState = 0; // Jump to first motion without transition
                animator.SetFloat("IdleIndex", idleState); // Instantly set first motion
            }
            else
            {
                idleState++; // Move to next motion normally
                StartCoroutine(SmoothIdleTransition(idleState));
            }
            idleTimer = 0f; // Reset timer
        }
    }

    private IEnumerator SmoothIdleTransition(int newIdleState)
    {
        float elapsedTime = 0f;
        float startValue = animator.GetFloat("IdleIndex");

        while (elapsedTime < IDLE_TRANSITION_TIME)
        {
            elapsedTime += Time.deltaTime;
            float t = elapsedTime / IDLE_TRANSITION_TIME;
            animator.SetFloat("IdleIndex", Mathf.Lerp(startValue, newIdleState, t));
            yield return null;
        }

        animator.SetFloat("IdleIndex", newIdleState); // Ensure exact final value
    }

    public void SetSitting(bool sitting)
    {
        isSitting = sitting;
        animator.SetBool("isSitting", sitting);
    }

    void OnDestroy()
    {
        if (defaultDevice != null) defaultDevice.Dispose();
    }
}
