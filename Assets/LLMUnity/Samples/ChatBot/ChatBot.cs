using UnityEngine;
using System.Collections.Generic;
using System.Threading.Tasks;
using LLMUnity;
using UnityEngine.UI;
using System.Collections;

namespace LLMUnitySamples
{
    public class ChatBot : MonoBehaviour
    {
        public Transform chatContainer;
        public Color playerColor = new Color32(81, 164, 81, 255);
        public Color aiColor = new Color32(29, 29, 73, 255);
        public Color fontColor = Color.white;
        public Font font;
        public int fontSize = 16;
        public int bubbleWidth = 600;
        public LLMCharacter llmCharacter;
        public float textPadding = 10f;
        public float bubbleSpacing = 10f;
        public Sprite sprite;
        // public Button stopButton;

        public Material playerMaterial;
        public Material aiMaterial;


        private InputBubble inputBubble;
        private List<Bubble> chatBubbles = new List<Bubble>();
        private bool blockInput = true;
        private BubbleUI playerUI, aiUI;
        private bool warmUpDone = false;
        private int lastBubbleOutsideFOV = -1;

        [Header("Input Settings")]
        public string inputPlaceholder = "Message me";


        [Header("Bubble Style")]
        [Range(0, 64)]
        public int cornerRadius = 16; // Used as border size for 9-sliced sprite
        public Sprite roundedSprite16;
        public Sprite roundedSprite32;
        public Sprite roundedSprite64;

        [Header("Streaming Audio")]
        public AudioSource streamAudioSource;


        public Color playerFontColor = Color.white;
        public Color aiFontColor = Color.white;

        public ScrollRect scrollRect; // Assign in Inspector


        void Start()
        {
            if (font == null) font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            playerUI = new BubbleUI
            {
                sprite = sprite,
                font = font,
                fontSize = fontSize,
                fontColor = playerFontColor,
                bubbleColor = playerColor,
                bottomPosition = 0,
                leftPosition = 0,
                textPadding = textPadding,
                bubbleOffset = bubbleSpacing,
                bubbleWidth = bubbleWidth,
                bubbleHeight = -1
            };

            aiUI = new BubbleUI
            {
                sprite = sprite,
                font = font,
                fontSize = fontSize,
                fontColor = aiFontColor,
                bubbleColor = aiColor,
                bottomPosition = 0,
                leftPosition = 1,
                textPadding = textPadding,
                bubbleOffset = bubbleSpacing,
                bubbleWidth = bubbleWidth,
                bubbleHeight = -1
            };


            inputBubble = new InputBubble(chatContainer, playerUI, "InputBubble", "Loading...", 4);
            inputBubble.AddSubmitListener(onInputFieldSubmit);
            inputBubble.AddValueChangedListener(onValueChanged);
            inputBubble.setInteractable(false);
            // stopButton.gameObject.SetActive(true);
            ShowLoadedMessages();
            _ = llmCharacter.Warmup(WarmUpCallback);

            // Choose rounded sprite based on radius
            if (cornerRadius <= 16)
                sprite = roundedSprite16;
            else if (cornerRadius <= 32)
                sprite = roundedSprite32;
            else
                sprite = roundedSprite64;

            playerUI.sprite = sprite;
            aiUI.sprite = sprite;

        }

        void OnDisable()
        {
            if (streamAudioSource != null && streamAudioSource.isPlaying)
            {
                streamAudioSource.Stop();
                streamAudioSource.volume = 1f; // reset for future use
            }
        }


        Bubble AddBubble(string message, bool isPlayerMessage)
        {
            Bubble bubble = new Bubble(chatContainer, isPlayerMessage ? playerUI : aiUI, isPlayerMessage ? "PlayerBubble" : "AIBubble", message);
            chatBubbles.Add(bubble);
            bubble.OnResize(UpdateBubblePositions);

            // Force-find the Image even in children
            var image = bubble.GetRectTransform().GetComponentInChildren<Image>(true);
            if (image != null)
            {
                image.material = isPlayerMessage ? playerMaterial : aiMaterial;
            }

            StartCoroutine(ScrollToBottomNextFrame());

            if (chatBubbles.Count > 50)
            {
                Bubble oldest = chatBubbles[0];
                oldest.Destroy();
                chatBubbles.RemoveAt(0);
            }

            return bubble;
        }

        void ShowLoadedMessages()
        {
            for (int i=1; i<llmCharacter.chat.Count; i++) AddBubble(llmCharacter.chat[i].content, i%2==1);
        }

        void onInputFieldSubmit(string newText)
        {
            inputBubble.ActivateInputField();
            if (blockInput || newText.Trim() == "" || Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift))
            {
                StartCoroutine(BlockInteraction());
                return;
            }
            blockInput = true;
            string message = inputBubble.GetText().Replace("\v", "\n");

            AddBubble(message, true);
            Bubble aiBubble = AddBubble("...", false);

            // Start playing audio if assigned
            if (streamAudioSource != null)
                streamAudioSource.Play();

            // Start chat and stop audio after it's done
            Task chatTask = llmCharacter.Chat(message, aiBubble.SetText, () =>
            {
                if (streamAudioSource != null && streamAudioSource.isPlaying)
                    StartCoroutine(FadeOutStreamAudio());
                AllowInput();
            });

            inputBubble.SetText("");
        }

        private IEnumerator FadeOutStreamAudio(float duration = 0.5f)
        {
            float startVolume = streamAudioSource.volume;

            while (streamAudioSource.volume > 0f)
            {
                streamAudioSource.volume -= startVolume * Time.deltaTime / duration;
                yield return null;
            }

            streamAudioSource.Stop();
            streamAudioSource.volume = startVolume; // reset for next time
        }



        public void WarmUpCallback()
        {
            warmUpDone = true;
            inputBubble.SetPlaceHolderText(inputPlaceholder);
            AllowInput();
        }

        public void AllowInput()
        {
            blockInput = false;
            inputBubble.ReActivateInputField();
        }

        public void CancelRequests()
        {
            llmCharacter.CancelRequests();
            AllowInput();
        }

        IEnumerator<string> BlockInteraction()
        {
            // prevent from change until next frame
            inputBubble.setInteractable(false);
            yield return null;
            inputBubble.setInteractable(true);
            // change the caret position to the end of the text
            inputBubble.MoveTextEnd();
        }

        void onValueChanged(string newText)
        {
            // Get rid of newline character added when we press enter
            if (Input.GetKey(KeyCode.Return))
            {
                if (inputBubble.GetText().Trim() == "")
                    inputBubble.SetText("");
            }
        }

        public void UpdateBubblePositions()
        {
            float y = inputBubble.GetSize().y + inputBubble.GetRectTransform().offsetMin.y + bubbleSpacing;
            float containerHeight = chatContainer.GetComponent<RectTransform>().rect.height;
            for (int i = chatBubbles.Count - 1; i >= 0; i--)
            {
                Bubble bubble = chatBubbles[i];
                RectTransform childRect = bubble.GetRectTransform();
                childRect.anchoredPosition = new Vector2(childRect.anchoredPosition.x, y);

                // last bubble outside the container
                if (y > containerHeight && lastBubbleOutsideFOV == -1)
                {
                    lastBubbleOutsideFOV = i;
                }
                y += bubble.GetSize().y + bubbleSpacing;
            }
        }

        void Update()
        {
            if (!inputBubble.inputFocused() && warmUpDone)
            {
                inputBubble.ActivateInputField();
                StartCoroutine(BlockInteraction());
            }

            if (lastBubbleOutsideFOV != -1)
            {
                // destroy bubbles outside the container
                for (int i = 0; i <= lastBubbleOutsideFOV; i++)
                {
                    chatBubbles[i].Destroy();
                }
                chatBubbles.RemoveRange(0, lastBubbleOutsideFOV + 1);
                lastBubbleOutsideFOV = -1;
            }

        }

        public void ExitGame()
        {
            Debug.Log("Exit button clicked");
            Application.Quit();
        }
        IEnumerator ScrollToBottomNextFrame()
        {
            yield return null;
            Canvas.ForceUpdateCanvases();
            if (scrollRect != null)
                scrollRect.verticalNormalizedPosition = 0f;
        }


        bool onValidateWarning = true;
        void OnValidate()
        {
            if (onValidateWarning && !llmCharacter.remote && llmCharacter.llm != null && llmCharacter.llm.model == "")
            {
                Debug.LogWarning($"Please select a model in the {llmCharacter.llm.gameObject.name} GameObject!");
                onValidateWarning = false;
            }
        }
    }
}
