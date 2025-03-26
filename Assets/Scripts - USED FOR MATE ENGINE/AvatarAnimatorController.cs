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
    public float SOUND_THRESHOLD = 0.02f; // Slightly raised from 0.01
    public List<string> ignoredApps = new List<string> { "discord", "mateengine", "mateenginex", "chrome", "audiodg", "explorer" }; // No ".exe"

    [Header("Idle Animation Settings")]
    public int totalIdleAnimations = 10;
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

    private Coroutine soundCheckCoroutine;
    private MMDeviceEnumerator enumerator;


    void Start()
    {
        if (animator == null)
            animator = GetComponent<Animator>();

        Application.runInBackground = true;

        enumerator = new MMDeviceEnumerator();
        UpdateDefaultDevice();

        soundCheckCoroutine = StartCoroutine(CheckSoundContinuously());
    }


    private IEnumerator CheckSoundContinuously()
    {
        while (true)
        {
            if (enableAudioDetection)
            {
                UpdateDefaultDevice();

                if (defaultDevice == null)
                {
                    try
                    {
                        UpdateDefaultDevice();
                        UnityEngine.Debug.Log("Reinitialized default audio device.");
                    }
                    catch (System.Exception ex)
                    {
                        UnityEngine.Debug.LogError("Failed to reinitialize audio device: " + ex.Message);
                    }
                }

                CheckForSound();
            }

            yield return new WaitForSeconds(SOUND_CHECK_INTERVAL);
        }
    }

    void UpdateDefaultDevice()
    {
        try
        {
            if (defaultDevice != null)
            {
                defaultDevice.Dispose();
                defaultDevice = null;
            }

            defaultDevice = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
        }
        catch (System.Exception ex)
        {
            UnityEngine.Debug.LogError("Failed to get default audio device: " + ex.Message);
            defaultDevice = null;
        }
    }


    void CheckForSound()
    {
        if (defaultDevice == null) return;

        bool isValidSoundPlaying = IsValidAppPlaying();

        if (!isDragging)
        {
            if (isValidSoundPlaying && enableDancing)
            {
                if (!isDancing)
                {
                    isDancing = true;
                    animator.SetBool("isDancing", true);
                    UnityEngine.Debug.Log("🎵 Dancing started.");
                }
            }
            else if (!isValidSoundPlaying)
            {
                if (isDancing)
                {
                    isDancing = false;
                    animator.SetBool("isDancing", false);
                    UnityEngine.Debug.Log("🛑 Dancing stopped.");
                }
            }
        }
    }

    bool IsValidAppPlaying()
    {
        if (Time.time - lastSoundCheckTime < SOUND_CHECK_INTERVAL)
            return isDancing;

        lastSoundCheckTime = Time.time;

        try
        {
            var sessions = defaultDevice.AudioSessionManager.Sessions;

            for (int i = 0; i < sessions.Count; i++)
            {
                var session = sessions[i];

                float peak = session.AudioMeterInformation.MasterPeakValue;
                if (peak > SOUND_THRESHOLD)
                {
                    int processId = (int)session.GetProcessID;

                    if (processId == 0)
                    {
                        UnityEngine.Debug.Log("🔍 Skipping session with no valid process.");
                        continue;
                    }

                    Process process = null;
                    try
                    {
                        process = Process.GetProcessById(processId);
                    }
                    catch
                    {
                        UnityEngine.Debug.Log("⚠️ Could not get process for ID: " + processId);
                        continue;
                    }

                    string processName = process.ProcessName.ToLowerInvariant();
                    UnityEngine.Debug.Log($"🎧 Audio from: {processName} | Peak: {peak}");

                    if (ignoredApps.Any(ignored => processName.StartsWith(ignored)))
                    {
                        UnityEngine.Debug.Log($"🚫 Ignored audio source: {processName}");
                        continue;
                    }

                    // ✅ Passed all checks
                    UnityEngine.Debug.Log($"✅ Valid audio source: {processName}");
                    return true;
                }
            }
        }
        catch (System.Exception ex)
        {
            UnityEngine.Debug.LogError("❌ Error checking audio sessions: " + ex.Message);
        }

        return false;
    }


    bool IsSubprocessIgnored(string processName)
    {
        foreach (string ignoredApp in ignoredApps)
        {
            if (processName.StartsWith(ignoredApp))
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

        idleTimer += Time.deltaTime;
        if (idleTimer > IDLE_SWITCH_TIME)
        {
            if (idleState + 1 >= totalIdleAnimations)
            {
                idleState = 0;
                animator.SetFloat("IdleIndex", idleState);
            }
            else
            {
                idleState++;
                StartCoroutine(SmoothIdleTransition(idleState));
            }
            idleTimer = 0f;
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

        animator.SetFloat("IdleIndex", newIdleState);
    }

    public void SetSitting(bool sitting)
    {
        isSitting = sitting;
        animator.SetBool("isSitting", sitting);
    }

    void OnDestroy()
    {
        CleanupAudioResources();
    }

    void OnApplicationQuit()
    {
        CleanupAudioResources();
    }

    void CleanupAudioResources()
    {
        if (soundCheckCoroutine != null)
        {
            StopCoroutine(soundCheckCoroutine);
            soundCheckCoroutine = null;
        }

        if (defaultDevice != null)
        {
            defaultDevice.Dispose();
            defaultDevice = null;
        }

        if (enumerator != null)
        {
            enumerator.Dispose();
            enumerator = null;
        }
    }

}