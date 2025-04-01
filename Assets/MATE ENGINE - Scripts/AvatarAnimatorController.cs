using UnityEngine;
using NAudio.CoreAudioApi;
using System.Collections.Generic;
using System.Linq;
using System.Diagnostics;
using System.Collections;

public class AvatarAnimatorController : MonoBehaviour
{
    [Header("General Settings")]
    public Animator animator;
    public bool enableAudioDetection = true;
    public float SOUND_THRESHOLD = 0.02f;
    public List<string> allowedApps = new List<string> { "spotify", "firefox", "chrome", "opera" };

    [Header("Idle Animation Settings")]
    public int totalIdleAnimations = 10;
    public float IDLE_SWITCH_TIME = 12f;
    public float IDLE_TRANSITION_TIME = 3f;

    [Header("Drag & Dance Settings")]
    public bool enableDragging = true;
    public bool enableDancing = true;

    [Header("Debug Logging")]
    public bool enableDebugLogging = false;

    public bool isDragging = false;
    public bool isDancing = false;
    public bool isIdle = false;

    private MMDevice defaultDevice;
    private float lastSoundCheckTime = 0f;
    private const float SOUND_CHECK_INTERVAL = 0.25f;

    private float idleTimer = 0f;
    private int idleState = 0;

    private Coroutine soundCheckCoroutine;
    private MMDeviceEnumerator enumerator;

    private static readonly int isIdleParam = Animator.StringToHash("isIdle");

    void OnEnable()
    {
        if (animator == null)
            animator = GetComponent<Animator>();

        Application.runInBackground = true;

        enumerator = new MMDeviceEnumerator();
        UpdateDefaultDevice();

        if (soundCheckCoroutine == null)
            soundCheckCoroutine = StartCoroutine(CheckSoundContinuously());
    }

    void OnDisable()
    {
        if (soundCheckCoroutine != null)
        {
            StopCoroutine(soundCheckCoroutine);
            soundCheckCoroutine = null;
        }

        defaultDevice?.Dispose();
        defaultDevice = null;

        enumerator?.Dispose();
        enumerator = null;
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
                        Log("Reinitialized default audio device.");
                    }
                    catch (System.Exception ex)
                    {
                        LogError("Failed to reinitialize audio device: " + ex.Message);
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
            defaultDevice?.Dispose();
            defaultDevice = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
        }
        catch (System.Exception ex)
        {
            LogError("Failed to get default audio device: " + ex.Message);
            defaultDevice = null;
        }
    }

    void CheckForSound()
    {
        if (defaultDevice == null) return;

        bool isValidSoundPlaying = IsValidAppPlaying();

        if (!isDragging)
        {
            if (isValidSoundPlaying && enableDancing && !isDancing)
            {
                isDancing = true;
                animator.SetBool("isDancing", true);
                Log("Started dancing.");
            }
            else if (!isValidSoundPlaying && isDancing)
            {
                // Check if we're no longer fully in dancing state or transitioning out
                AnimatorStateInfo stateInfo = animator.GetCurrentAnimatorStateInfo(0);
                bool isInDanceState = stateInfo.IsName("Dancing"); // <-- match exact name of your dance state
                bool isTransitioning = animator.IsInTransition(0);

                if (!isInDanceState && !isTransitioning)
                {
                    isDancing = false;
                    animator.SetBool("isDancing", false);
                    Log("Stopped dancing (fully exited state).");
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
                if (peak <= SOUND_THRESHOLD) continue;

                int processId = (int)session.GetProcessID;
                if (processId == 0) continue;

                Process process = null;
                try
                {
                    process = Process.GetProcessById(processId);
                }
                catch
                {
                    continue;
                }

                string processName = process.ProcessName.ToLowerInvariant();
                if (allowedApps.Any(allowed => processName.StartsWith(allowed)))
                    return true;

            }
        }
        catch (System.Exception ex)
        {
            LogError("Error checking audio sessions: " + ex.Message);
        }

        return false;
    }

    void Update()
    {
        // Settings menu override: block all interactive states while open
        if (AvatarSettingsMenu.IsMenuOpen)
        {
            if (isDragging)
            {
                isDragging = false;
                animator.SetBool("isDragging", false);
            }

            if (isDancing)
            {
                isDancing = false;
                animator.SetBool("isDancing", false);
            }

            return;
        }

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
            idleTimer = 0f;
            int nextState = (idleState + 1) % totalIdleAnimations;
            if (nextState == 0)
            {
                animator.SetFloat("IdleIndex", 0);
            }
            else
            {
                StartCoroutine(SmoothIdleTransition(nextState));
            }
            idleState = nextState;
        }

        UpdateIdleStatus();
    }

    private void UpdateIdleStatus()
    {
        if (animator == null) return;

        AnimatorStateInfo state = animator.GetCurrentAnimatorStateInfo(0);
        bool inIdle = state.IsName("Idle");

        if (isIdle != inIdle)
        {
            isIdle = inIdle;
            animator.SetBool(isIdleParam, isIdle);
            Log($"Animator state is now: {(isIdle ? "Idle" : "Not Idle")}");
        }
    }

    private IEnumerator SmoothIdleTransition(int newIdleState)
    {
        float elapsedTime = 0f;
        float startValue = animator.GetFloat("IdleIndex");

        while (elapsedTime < IDLE_TRANSITION_TIME)
        {
            elapsedTime += Time.deltaTime;
            animator.SetFloat("IdleIndex", Mathf.Lerp(startValue, newIdleState, elapsedTime / IDLE_TRANSITION_TIME));
            yield return null;
        }

        animator.SetFloat("IdleIndex", newIdleState);
    }

    public bool IsInIdleState() => isIdle;

    void OnDestroy() => CleanupAudioResources();
    void OnApplicationQuit() => CleanupAudioResources();

    void CleanupAudioResources()
    {
        if (soundCheckCoroutine != null)
        {
            StopCoroutine(soundCheckCoroutine);
            soundCheckCoroutine = null;
        }

        defaultDevice?.Dispose();
        defaultDevice = null;

        enumerator?.Dispose();
        enumerator = null;
    }

    private void Log(string message)
    {
        if (!enableDebugLogging) return;
        UnityEngine.Debug.Log("[AvatarAnimatorController] " + message);
    }

    private void LogError(string message)
    {
        if (!enableDebugLogging) return;
        UnityEngine.Debug.LogError("[AvatarAnimatorController] " + message);
    }
}
