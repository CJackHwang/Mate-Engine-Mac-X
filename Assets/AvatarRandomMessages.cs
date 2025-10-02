using UnityEngine;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.InteropServices;

[Serializable]
public class AvatarMessage
{
    [TextArea(1, 3)]
    public string text = "Hello!";
    public string state = "Idle";
    public bool onActive = false;
}

public class AvatarRandomMessages : MonoBehaviour
{
    [Header("Enable Random Messages")]
    public bool enableRandomMessages = true;

    [Header("Random Delay Settings (Seconds)")]
    [Range(5, 60)] public int minDelay = 10;
    [Range(5, 60)] public int maxDelay = 60;

    [Header("Message Lifetime (Seconds)")]
    [Range(5, 20)] public int despawnTime = 10;

    [Header("Random Steps for OnActive Messages (0–100%)")]
    [Range(0, 100)] public int onActiveChance = 100;

    [Header("Random Message Pool")]
    public List<AvatarMessage> messages = new List<AvatarMessage>();

    [Header("Chat Bubble UI")]
    public Transform chatContainer;
    public Sprite bubbleSprite;
    public Color bubbleColor = new Color32(120, 120, 255, 255);
    public Color fontColor = Color.white;
    public Font font;
    public int fontSize = 16;
    public int bubbleWidth = 600;
    public float textPadding = 10f;
    public float bubbleSpacing = 10f;

    [Header("Fake Stream Settings")]
    [Range(5, 100)] public int streamSpeed = 35;
    public AudioSource streamAudioSource;

    [Header("Allowed Animator States")]
    public bool useAllowedStatesWhitelist = false;
    public string[] allowedStates = { "Idle" };

    [Header("Block when these objects are active")]
    public List<GameObject> blockObjects = new List<GameObject>();

    [Header("Live Status (Inspector)")]
    [SerializeField] private string inspectorEvent;

    private LLMUnitySamples.Bubble activeBubble;
    private Coroutine streamCoroutine;
    private Coroutine despawnCoroutine;
    private bool isBubbleActive = false;

    private Animator avatarAnimator;
    private string lastAnimatorStateName = "";

    void Start()
    {
        avatarAnimator = GetComponent<Animator>();
        if (enableRandomMessages)
            StartCoroutine(RandomMessageLoop());
    }

    void Update()
    {
        if (isBubbleActive)
        {
            if (IsBlockedByObjects())
            {
                inspectorEvent = "Bubble removed (blocked by GameObject)";
                RemoveBubble();
            }
            else if (useAllowedStatesWhitelist && !IsInAllowedState())
            {
                inspectorEvent = "Bubble removed (state not allowed)";
                RemoveBubble();
            }
        }

        // OnActive-Trigger mit Zufalls-Chance
        if (avatarAnimator != null)
        {
            var current = avatarAnimator.GetCurrentAnimatorStateInfo(0);
            string currentStateName = GetCurrentStateName(current);

            if (currentStateName != lastAnimatorStateName)
            {
                List<AvatarMessage> candidates = messages.FindAll(m =>
                    m.onActive &&
                    !string.IsNullOrEmpty(m.state) &&
                    current.IsName(m.state)
                );

                if (candidates.Count > 0)
                {
                    // Prüfe Chance
                    if (UnityEngine.Random.Range(0, 100) < onActiveChance)
                    {
                        AvatarMessage chosen = candidates[UnityEngine.Random.Range(0, candidates.Count)];
                        inspectorEvent = $"Triggered OnActive message ({chosen.state})";
                        ShowSpecificMessage(chosen);
                    }
                    else
                    {
                        inspectorEvent = $"OnActive skipped due to chance ({onActiveChance}%)";
                    }
                }

                lastAnimatorStateName = currentStateName;
            }
        }
    }

    IEnumerator RandomMessageLoop()
    {
        while (enableRandomMessages)
        {
            if (!isBubbleActive && messages.Count > 0)
            {
                float wait = UnityEngine.Random.Range(minDelay, maxDelay + 1);
                yield return new WaitForSeconds(wait);

                if (IsBlockedByObjects())
                {
                    inspectorEvent = "Message blocked by GameObject";
                    yield return new WaitForSeconds(1f);
                    continue;
                }

                if (useAllowedStatesWhitelist && !IsInAllowedState())
                {
                    inspectorEvent = "Message blocked by state";
                    yield return new WaitForSeconds(1f);
                    continue;
                }

                ShowRandomMessage();
            }
            else
            {
                yield return null;
            }
        }
    }

    void ShowRandomMessage()
    {
        List<AvatarMessage> idlePool = messages.FindAll(m => !m.onActive);
        if (idlePool.Count == 0) return;

        AvatarMessage msg = idlePool[UnityEngine.Random.Range(0, idlePool.Count)];
        ShowSpecificMessage(msg);
    }

    void ShowSpecificMessage(AvatarMessage msg)
    {
        if (chatContainer == null || msg == null) return;

        RemoveBubble(); // alte Bubble + Timer killen

        var ui = new LLMUnitySamples.BubbleUI
        {
            sprite = bubbleSprite,
            font = font,
            fontSize = fontSize,
            fontColor = fontColor,
            bubbleColor = bubbleColor,
            bottomPosition = 0,
            leftPosition = 1,
            textPadding = textPadding,
            bubbleOffset = bubbleSpacing,
            bubbleWidth = bubbleWidth,
            bubbleHeight = -1
        };

        activeBubble = new LLMUnitySamples.Bubble(chatContainer, ui, "RandomBubble", "");
        isBubbleActive = true;

        if (streamAudioSource != null)
        {
            streamAudioSource.Stop();
            streamAudioSource.Play();
        }

        if (streamCoroutine != null) StopCoroutine(streamCoroutine);
        streamCoroutine = StartCoroutine(FakeStreamText(msg.text));

        if (despawnCoroutine != null) StopCoroutine(despawnCoroutine);
        despawnCoroutine = StartCoroutine(DespawnAfterDelay());
    }

    IEnumerator FakeStreamText(string fullText)
    {
        if (activeBubble == null) yield break;
        activeBubble.SetText("");
        int length = 0;
        float delay = 1f / Mathf.Max(streamSpeed, 1);

        while (length < fullText.Length)
        {
            length++;
            activeBubble.SetText(fullText.Substring(0, length));
            yield return new WaitForSeconds(delay);
            if (activeBubble == null) yield break;
        }
        activeBubble.SetText(fullText);
        if (streamAudioSource != null && streamAudioSource.isPlaying)
            streamAudioSource.Stop();
        streamCoroutine = null;
    }

    IEnumerator DespawnAfterDelay()
    {
        yield return new WaitForSeconds(despawnTime);
        RemoveBubble();
    }

    void RemoveBubble()
    {
        if (streamCoroutine != null)
        {
            StopCoroutine(streamCoroutine);
            streamCoroutine = null;
        }
        if (despawnCoroutine != null)
        {
            StopCoroutine(despawnCoroutine);
            despawnCoroutine = null;
        }
        if (activeBubble != null)
        {
            activeBubble.Destroy();
            activeBubble = null;
        }
        if (streamAudioSource != null && streamAudioSource.isPlaying)
            streamAudioSource.Stop();
        isBubbleActive = false;
    }

    bool IsInAllowedState()
    {
        if (avatarAnimator == null || allowedStates == null || allowedStates.Length == 0)
            return true;
        var current = avatarAnimator.GetCurrentAnimatorStateInfo(0);
        foreach (var s in allowedStates)
            if (!string.IsNullOrEmpty(s) && current.IsName(s)) return true;
        return false;
    }

    bool IsBlockedByObjects()
    {
        if (blockObjects == null || blockObjects.Count == 0) return false;
        foreach (var go in blockObjects)
        {
            if (go != null && go.activeInHierarchy) return true;
        }
        return false;
    }

    string GetCurrentStateName(AnimatorStateInfo stateInfo)
    {
        if (avatarAnimator == null) return "";
        var clips = avatarAnimator.GetCurrentAnimatorClipInfo(0);
        if (clips.Length > 0 && clips[0].clip != null)
            return clips[0].clip.name;
        return stateInfo.IsName("") ? "" : stateInfo.shortNameHash.ToString();
    }

#if UNITY_EDITOR
    [UnityEditor.CustomEditor(typeof(AvatarRandomMessages))]
    public class AvatarRandomMessagesEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            AvatarRandomMessages script = (AvatarRandomMessages)target;
            if (GUILayout.Button("Trigger Random Now (Debug)"))
            {
                script.ShowRandomMessage();
            }
        }
    }
#endif
}
