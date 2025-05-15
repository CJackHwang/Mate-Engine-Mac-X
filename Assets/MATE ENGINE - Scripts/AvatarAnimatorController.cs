using UnityEngine;
using NAudio.CoreAudioApi;
using System.Collections.Generic;
using System.Diagnostics;
using System.Collections;

public class AvatarAnimatorController : MonoBehaviour
{
    public Animator animator; public float SOUND_THRESHOLD = 0.02f;
    public List<string> allowedApps = new(); public int totalIdleAnimations = 10;
    public float IDLE_SWITCH_TIME = 12f, IDLE_TRANSITION_TIME = 3f;
    public int DANCE_CLIP_COUNT = 5; public bool enableDancing = true;

    private static readonly int danceIndexParam = Animator.StringToHash("DanceIndex"), isIdleParam = Animator.StringToHash("isIdle");

    public bool isDragging = false, isDancing = false, isIdle = false;
    private MMDevice defaultDevice; private MMDeviceEnumerator enumerator;
    private float lastSoundCheckTime = 0f, idleTimer = 0f;
    private const float SOUND_CHECK_INTERVAL = 2f;
    private int idleState = 0;
    private Coroutine soundCheckCoroutine, idleTransitionCoroutine;

    private float dragLockTimer = 0f;
    private bool mouseHeld = false;


    void OnEnable()
    {
        if (animator == null) animator = GetComponent<Animator>();
        Application.runInBackground = true;

        enumerator = new MMDeviceEnumerator();
        defaultDevice = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia); // << Add this
        soundCheckCoroutine = StartCoroutine(CheckSoundContinuously());
    }

    void OnDisable() => CleanupAudioResources();
    void OnDestroy() => CleanupAudioResources();
    void OnApplicationQuit() => CleanupAudioResources();

    private IEnumerator CheckSoundContinuously()
    {
        WaitForSeconds wait = new WaitForSeconds(SOUND_CHECK_INTERVAL);
        while (true)
        {
            CheckForSound();
            yield return wait;
        }
    }

    void CheckForSound()
    {
        if (MenuActions.IsMovementBlocked() || !enableDancing)
        {
            if (isDancing)
            {
                isDancing = false;
                animator.SetBool("isDancing", false);
            }
            return;
        }

        if (defaultDevice == null) return;
        bool isValidSoundPlaying = IsValidAppPlaying();

        if (!isDragging)
        {
            if (isValidSoundPlaying && !isDancing)
                StartDancing();
            else if (!isValidSoundPlaying && isDancing)
            {
                var stateInfo = animator.GetCurrentAnimatorStateInfo(0);
                if (!stateInfo.IsName("Dancing") && !animator.IsInTransition(0))
                {
                    isDancing = false;
                    animator.SetBool("isDancing", false);
                }
            }
        }
    }

    private void StartDancing()
    {
        isDancing = true;
        animator.SetBool("isDancing", true);
        animator.SetFloat(danceIndexParam, Random.Range(0, DANCE_CLIP_COUNT));
    }

    bool IsValidAppPlaying()
    {
        if (Time.time - lastSoundCheckTime < SOUND_CHECK_INTERVAL)
            return isDancing;
        lastSoundCheckTime = Time.time;

        try
        {
            // Fully refresh the defaultDevice every time to update the session list
            defaultDevice?.Dispose();
            defaultDevice = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);

            var sessions = defaultDevice.AudioSessionManager.Sessions;
            int sessionCount = sessions.Count;

            for (int i = 0; i < sessionCount; i++)
            {
                var session = sessions[i];

                float peak = session.AudioMeterInformation.MasterPeakValue;
                if (peak <= SOUND_THRESHOLD) continue;

                int pid = (int)session.GetProcessID;
                if (pid == 0) continue;

                try
                {
                    var process = Process.GetProcessById(pid);
                    string pname = process?.ProcessName;
                    if (string.IsNullOrEmpty(pname)) continue;

                    for (int j = 0; j < allowedApps.Count; j++)
                    {
                        if (pname.StartsWith(allowedApps[j], System.StringComparison.OrdinalIgnoreCase))
                            return true;
                    }
                }
                catch { continue; }
            }
        }
        catch
        {
            defaultDevice?.Dispose();
            defaultDevice = null;
        }

        return false;
    }


    void Update()
    {
        if (MenuActions.IsMovementBlocked() || TutorialMenu.IsActive)
        {
            if (isDragging) { isDragging = false; animator.SetBool("isDragging", false); }
            if (isDancing) { isDancing = false; animator.SetBool("isDancing", false); }
            return;
        }

        // On press
        if (Input.GetMouseButtonDown(0))
        {
            isDragging = true;
            mouseHeld = true;
            dragLockTimer = 0.30f;

            animator.SetBool("isDragging", true);
            animator.SetBool("isDancing", false);
            isDancing = false;
        }

        // On release
        if (Input.GetMouseButtonUp(0))
        {
            mouseHeld = false;
        }

        // During lock
        if (dragLockTimer > 0f)
        {
            dragLockTimer -= Time.deltaTime;
            animator.SetBool("isDragging", true); // force drag to stay on
        }
        else
        {
            // After 0.25s, allow clean exit if mouse was released
            if (!mouseHeld && isDragging)
            {
                isDragging = false;
                animator.SetBool("isDragging", false);
                animator.SetBool("isDancing", isDancing);
            }
        }


        idleTimer += Time.deltaTime;
        if (idleTimer > IDLE_SWITCH_TIME)
        {
            idleTimer = 0f;
            int nextState = (idleState + 1) % totalIdleAnimations;
            if (nextState == 0)
                animator.SetFloat("IdleIndex", 0);
            else
            {
                if (idleTransitionCoroutine != null)
                    StopCoroutine(idleTransitionCoroutine);
                idleTransitionCoroutine = StartCoroutine(SmoothIdleTransition(nextState));
            }
            idleState = nextState;
        }

        UpdateIdleStatus();
    }

    private void UpdateIdleStatus()
    {
        var state = animator.GetCurrentAnimatorStateInfo(0);
        bool inIdle = state.IsName("Idle");
        if (isIdle != inIdle)
        {
            isIdle = inIdle;
            animator.SetBool(isIdleParam, isIdle);
        }
    }

    private IEnumerator SmoothIdleTransition(int newIdleState)
    {
        float elapsed = 0f;
        float start = animator.GetFloat("IdleIndex");
        while (elapsed < IDLE_TRANSITION_TIME)
        {
            elapsed += Time.deltaTime;
            animator.SetFloat("IdleIndex", Mathf.Lerp(start, newIdleState, elapsed / IDLE_TRANSITION_TIME));
            yield return null;
        }
        animator.SetFloat("IdleIndex", newIdleState);
    }

    public bool IsInIdleState() => isIdle;

    private void CleanupAudioResources()
    {
        if (soundCheckCoroutine != null) { StopCoroutine(soundCheckCoroutine); soundCheckCoroutine = null; }
        if (idleTransitionCoroutine != null) { StopCoroutine(idleTransitionCoroutine); idleTransitionCoroutine = null; }
        defaultDevice?.Dispose(); defaultDevice = null;
        enumerator?.Dispose(); enumerator = null;
    }
}