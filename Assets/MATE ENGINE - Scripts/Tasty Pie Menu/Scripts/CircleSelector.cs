using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;
using UnityEngine.Assertions;

namespace Xamin
{
    public class CircleSelector : MonoBehaviour
    {
        [Range(2, 10)] private int buttonCount;
        private int startButCount;

        [Header("Customization")] public Color AccentColor = Color.red;
        public Color DisabledColor = Color.gray, BackgroundColor = Color.white;
        [Space(10)] public bool UseSeparators = true;
        [SerializeField] private GameObject separatorPrefab;

        [Header("Animations")]
        [Range(0.0001f, 1)]
        public float LerpAmount = .145f;

        public AnimationType OpenAnimation, CloseAnimation;
        public float Size = 1f;
        private Image _cursor, _background;
        private float _desiredFill;
        float radius = 120f;

        [Header("Interaction")] public List<GameObject> Buttons = new List<GameObject>();
        public ButtonSource buttonSource;
        private List<Xamin.Button> buttonsInstances = new List<Xamin.Button>();
        private Vector2 _menuCenter;
        public bool RaiseOnSelection;

        private GameObject _selectedSegment;
        private bool _previousUseSeparators;

        public bool flip = true;

        [HideInInspector]
        public GameObject SelectedSegment
        {
            get { return _selectedSegment; }
            set
            {
                if (value == null) return;
                if (value == SelectedSegment) return;
                _selectedSegment = value;
            }
        }

        public bool selectOnlyOnHover;
        public float pieThickness = 85;
        public bool snap, tiltTowardsMouse;
        public float tiltAmount = 15;
        private bool opened;

        public enum ControlType
        {
            mouseAndTouch,
            gamepad,
            customVector
        }

        public enum ButtonSource
        {
            prefabs,
            scene
        }

        [Header("Controls")] public string activationButton = "Fire1";
        public ControlType controlType;
        public string gamepadAxisX, gamepadAxisY;
        public Vector2 CustomInputVector;

        public enum AnimationType
        {
            zoomIn,
            zoomOut
        }

        private Dictionary<GameObject, Button> instancedButtons;

        private AvatarAnimatorReceiver animatorReceiver;

        void Start()
        {
            instancedButtons = new Dictionary<GameObject, Button>();
            transform.localScale = Vector3.zero;
            Assert.IsNotNull(transform.Find("Cursor"));
            _cursor = transform.Find("Cursor").GetComponent<Image>();
            Assert.IsNotNull(transform.Find("Background"));
            _background = transform.Find("Background").GetComponent<Image>();

            EnsureAnimatorReceiver();

            BuildButtons();
        }
        public bool Open()
        {
            RefreshAllButtonColorsDelayed();
            EnsureAnimatorReceiver();
            BuildButtons();

            if (buttonsInstances == null || buttonsInstances.Count == 0)
            {
                opened = false;
                transform.localScale = Vector3.zero;
                return false; 
            }
            _menuCenter = new Vector2((float)Screen.width / 2f, (float)Screen.height / 2f);
            opened = true;
            transform.localScale = (OpenAnimation == AnimationType.zoomIn) ? Vector3.zero : Vector3.one * 10;
            return true;
        }

        public bool Open(Vector2 origin)
        {
            RefreshAllButtonColorsDelayed();
            bool openedSuccessfully = Open();
            if (!openedSuccessfully) return false;
            _menuCenter = origin;
            Vector2 relativeCenter = new Vector2(_menuCenter.x - Screen.width / 2f, _menuCenter.y - Screen.height / 2f);
            transform.localPosition = relativeCenter;
            return true;
        }


        public void Close()
        {
            opened = false;
        }

        public Xamin.Button GetButtonWithId(string id)
        {
            foreach (var btn in buttonsInstances)
            {
                if (btn.id == id)
                    return btn;
            }
            return null;
        }

        public float zRotation = 180;
        public bool rotateButtons = false;

        void ChangeSeparatorsState()
        {
            if (!transform.Find("Separators"))
            {
                Debug.LogError("Can't find Separators");
                return;
            }
            transform.Find("Separators").gameObject.SetActive(UseSeparators);
        }

        void Update()
        {
            if (opened)
            {
                transform.localScale = Vector3.Lerp(transform.localScale, new Vector3(Size, Size, Size), .2f);
                _background.color = BackgroundColor;
                if (UseSeparators != _previousUseSeparators)
                    ChangeSeparatorsState();
                if (transform.localScale.x >= Size - .2f)
                {
                    buttonCount = buttonsInstances.Count;
                    if (startButCount != buttonCount && buttonSource == ButtonSource.prefabs)
                    {
                        Start();
                        return;
                    }

                    _cursor.fillAmount = Mathf.Lerp(_cursor.fillAmount, _desiredFill, .2f);
                    Vector3 screenBounds = Camera.main.WorldToScreenPoint(transform.position);
                    Vector2 vector = (UnityEngine.Input.mousePosition - screenBounds);

                    if (tiltTowardsMouse)
                    {
                        float x = vector.x / screenBounds.x, y = vector.y / screenBounds.y;
                        transform.localRotation = Quaternion.Slerp(transform.localRotation,
                            Quaternion.Euler((Vector3)(new Vector2(y, -x) * -tiltAmount) +
                                             Vector3.forward * zRotation), LerpAmount);
                    }
                    else
                    {
                        transform.localRotation = Quaternion.Euler(Vector3.forward * zRotation);
                    }

                    float mouseRotation = zRotation + 57.29578f *
                        (controlType == ControlType.mouseAndTouch
                            ? Mathf.Atan2(vector.x, vector.y)
                            : controlType == ControlType.gamepad
                                ? Mathf.Atan2(Input.GetAxis(gamepadAxisX), Input.GetAxis(gamepadAxisY))
                                : Mathf.Atan2(CustomInputVector.x, CustomInputVector.y));

                    if (mouseRotation < 0f)
                        mouseRotation += 360f;
                    float cursorRotation = -(mouseRotation - _cursor.fillAmount * 360f / 2f) + zRotation;

                    float mouseDistanceFromCenter = Vector2.Distance(Camera.main.WorldToScreenPoint(transform.position), Input.mousePosition);
                    if (selectOnlyOnHover && controlType == ControlType.mouseAndTouch &&
                        mouseDistanceFromCenter > pieThickness ||
                        (selectOnlyOnHover && controlType == ControlType.gamepad &&
                         (Mathf.Abs(Input.GetAxisRaw(gamepadAxisX) + Mathf.Abs(Input.GetAxisRaw(gamepadAxisY)))) !=
                         0) ||
                        !selectOnlyOnHover)
                    {
                        _cursor.enabled = true;

                        float difference = float.MaxValue;
                        GameObject nearest = null;
                        for (int i = 0; i < buttonCount; i++)
                        {
                            var btn = buttonsInstances[i];
                            GameObject b = btn.gameObject;
                            b.transform.localScale = Vector3.one;
                            float rotation = System.Convert.ToSingle(b.name);
                            if (Mathf.Abs(rotation - mouseRotation) < difference)
                            {
                                nearest = b;
                                difference = Mathf.Abs(rotation - mouseRotation);
                            }

                            if (rotateButtons)
                                b.transform.localEulerAngles = new Vector3(0, 0, -zRotation);
                        }

                        SelectedSegment = nearest;

                        if (snap && SelectedSegment != null)
                            cursorRotation = -(System.Convert.ToSingle(SelectedSegment.name) -
                                               _cursor.fillAmount * 360f / 2f);
                        _cursor.transform.localRotation = Quaternion.Slerp(_cursor.transform.localRotation,
                            Quaternion.Euler(0, 0, cursorRotation), LerpAmount);

                        if (SelectedSegment != null && instancedButtons.ContainsKey(SelectedSegment))
                            instancedButtons[SelectedSegment].SetColor(
                                Color.Lerp(instancedButtons[SelectedSegment].currentColor, BackgroundColor, LerpAmount));

                        for (int i = 0; i < buttonCount; i++)
                        {
                            Button b = buttonsInstances[i];
                            if (b.gameObject != SelectedSegment)
                            {
                                if (b.unlocked)
                                    b.SetColor(Color.Lerp(b.currentColor,
                                        b.useCustomColor ? b.customColor : AccentColor, LerpAmount));
                                else
                                    b.SetColor(Color.Lerp(b.currentColor,
                                        DisabledColor, LerpAmount));
                            }
                        }

                        try
                        {
                            if (SelectedSegment != null && instancedButtons.ContainsKey(SelectedSegment) && instancedButtons[SelectedSegment].unlocked)
                            {
                                _cursor.color = Color.Lerp(_cursor.color,
                                    instancedButtons[SelectedSegment].useCustomColor
                                        ? instancedButtons[SelectedSegment].customColor
                                        : AccentColor, LerpAmount);
                            }
                            else
                                _cursor.color = Color.Lerp(_cursor.color, DisabledColor, LerpAmount);
                        }
                        catch
                        {
                        }
                    }
                    else if (_cursor.enabled && SelectedSegment != null)
                    {
                        _cursor.enabled = false;
                        if (instancedButtons.ContainsKey(SelectedSegment) && instancedButtons[SelectedSegment].unlocked)
                            instancedButtons[SelectedSegment].SetColor(instancedButtons[SelectedSegment].useCustomColor
                                ? instancedButtons[SelectedSegment].customColor
                                : AccentColor);
                        else if (instancedButtons.ContainsKey(SelectedSegment))
                            instancedButtons[SelectedSegment].SetColor(DisabledColor);

                        for (int i = 0; i < buttonCount; i++)
                        {
                            Button b = buttonsInstances[i];
                            if (b.gameObject != SelectedSegment)
                            {
                                if (b.unlocked)
                                    b.SetColor(buttonsInstances[SelectedSegment != null ? buttonsInstances.IndexOf(instancedButtons[SelectedSegment]) : 0].useCustomColor
                                        ? buttonsInstances[SelectedSegment != null ? buttonsInstances.IndexOf(instancedButtons[SelectedSegment]) : 0].customColor
                                        : AccentColor);
                                else
                                    b.SetColor(DisabledColor);
                            }
                        }
                    }

                    if (_cursor.isActiveAndEnabled)
                        CheckForInput();
                    else if (Input.GetButtonUp(activationButton))
                        Close();
                }
                _previousUseSeparators = UseSeparators;
            }
            else
            {
                transform.localScale = Vector3.Lerp(transform.localScale,
                    (CloseAnimation == AnimationType.zoomIn) ? Vector3.zero : Vector3.one * 10, .2f);
                _cursor.color = Color.Lerp(_cursor.color, Color.clear, LerpAmount / 3f);
                _background.color = Color.Lerp(_background.color, Color.clear, LerpAmount / 3f);
            }
        }

        public void RefreshAllButtonColors()
        {
            if (buttonsInstances == null || buttonsInstances.Count == 0) return;
            for (int i = 0; i < buttonCount; i++)
            {
                var btn = buttonsInstances[i];
                if (btn == null) continue;
                if (btn.unlocked)
                    btn.SetColor(Color.Lerp(btn.currentColor, btn.useCustomColor ? btn.customColor : AccentColor, LerpAmount));
                else
                    btn.SetColor(Color.Lerp(btn.currentColor, DisabledColor, LerpAmount));
            }
        }

        public void RefreshAllButtonColorsDelayed()
        {
            StartCoroutine(DoRefreshAllButtonColorsDelayed());
        }

        private System.Collections.IEnumerator DoRefreshAllButtonColorsDelayed()
        {
            yield return null; 
            RefreshAllButtonColors();
        }


        void CheckForInput()
        {
            if (SelectedSegment == null || instancedButtons == null || !instancedButtons.ContainsKey(SelectedSegment))
                return;

            var btn = instancedButtons[SelectedSegment];
            if (Input.GetButton(activationButton))
            {
                _cursor.rectTransform.localPosition = Vector3.Lerp(_cursor.rectTransform.localPosition,
                    new Vector3(0, 0, RaiseOnSelection ? -10 : 0), LerpAmount);
                if (btn.unlocked)
                {
                    SelectedSegment.transform.localScale = new Vector2(.8f, .8f);
                }
            }
            else
            {
                _cursor.rectTransform.localPosition = Vector3.Lerp(_cursor.rectTransform.localPosition,
                    Vector3.zero, LerpAmount);
            }

            if (Input.GetButtonUp(activationButton))
            {
                if (btn.unlocked)
                {
                    btn.ExecuteAction();
                    var audio = FindFirstObjectByType<MenuAudioHandler>();
                    if (audio != null)
                        audio.PlayButtonSound();
                }
                Close();
            }
        }


        void EnsureAnimatorReceiver()
        {
            animatorReceiver = null;
            var receivers = FindObjectsOfType<AvatarAnimatorReceiver>();
            foreach (var recv in receivers)
            {
                if (recv != null
                    && recv.isActiveAndEnabled
                    && recv.gameObject.activeInHierarchy
                    && recv.avatarAnimator != null
                    && recv.avatarAnimator.isActiveAndEnabled
                    && recv.avatarAnimator.gameObject.activeInHierarchy)
                {
                    animatorReceiver = recv;
                    break;
                }
            }
        }

        void BuildButtons()
        {
            foreach (Transform child in transform.Find("Buttons"))
                Destroy(child.gameObject);
            foreach (Transform sep in transform.Find("Separators"))
                Destroy(sep.gameObject);
            buttonsInstances.Clear();
            instancedButtons = new Dictionary<GameObject, Button>();

            int visibleCount = 0;
            List<GameObject> visibleButtonObjects = new List<GameObject>();
            for (int i = 0; i < Buttons.Count; i++)
            {
                GameObject buttonObj;
                if (buttonSource == ButtonSource.prefabs)
                    buttonObj = Instantiate(Buttons[i], Vector2.zero, transform.rotation) as GameObject;
                else
                    buttonObj = Buttons[i];

                Xamin.Button btn = buttonObj.GetComponent<Xamin.Button>();
                if (ShouldHideButton(btn))
                {
                    Destroy(buttonObj);
                    continue;
                }

                visibleButtonObjects.Add(buttonObj);
                visibleCount++;
            }

            buttonCount = visibleCount;
            if (buttonCount > 0 && buttonCount < 11)
            {
                startButCount = buttonCount;
                _desiredFill = 1f / (float)buttonCount;
                float fillRadius = _desiredFill * 360f;
                float previousRotation = 0;

                for (int i = 0; i < visibleCount; i++)
                {
                    GameObject buttonObj = visibleButtonObjects[i];
                    Xamin.Button btn = buttonObj.GetComponent<Xamin.Button>();

                    buttonObj.transform.SetParent(transform.Find("Buttons"));
                    float bRot = previousRotation + fillRadius / 2;
                    previousRotation = bRot + fillRadius / 2;
                    GameObject separator =
                        Instantiate(separatorPrefab, Vector3.zero, Quaternion.identity) as GameObject;
                    separator.transform.SetParent(transform.Find("Separators"));
                    separator.transform.localScale = Vector3.one;
                    separator.transform.localPosition = Vector3.zero;
                    separator.transform.localRotation = Quaternion.Euler(0, 0, previousRotation);

                    buttonObj.transform.localPosition = new Vector2(radius * Mathf.Cos((bRot - 90) * Mathf.Deg2Rad),
                        -radius * Mathf.Sin((bRot - 90) * Mathf.Deg2Rad));
                    buttonObj.transform.localScale = Vector3.one;
                    if (bRot > 360)
                        bRot -= 360;
                    buttonObj.name = bRot.ToString();
                    if (btn)
                    {
                        instancedButtons[buttonObj] = btn;
                        if (btn.useCustomColor)
                            btn.SetColor(btn.customColor);
                        else
                            btn.SetColor(btn.useCustomColor ? btn.customColor : AccentColor);
                        buttonsInstances.Add(btn);
                    }
                    else
                        buttonObj.GetComponent<Image>().color = DisabledColor;
                }
            }
            if (buttonsInstances.Count != 0)
            {
                SelectedSegment = buttonsInstances[buttonsInstances.Count - 1].gameObject;
            }
            else
            {
                SelectedSegment = null;
                opened = false;
                transform.localScale = Vector3.zero;
            }
        }

        bool ShouldHideButton(Xamin.Button btn)
        {
            if (animatorReceiver == null || animatorReceiver.avatarAnimator == null)
                return false;

            var animator = animatorReceiver.avatarAnimator;
            if (btn.hideIfAnimatorBool != null && btn.hideIfAnimatorBool.Length > 0)
            {
                foreach (var param in btn.hideIfAnimatorBool)
                {
                    if (!string.IsNullOrEmpty(param))
                    {
                        foreach (var animatorParam in animator.parameters)
                        {
                            if (animatorParam.type == AnimatorControllerParameterType.Bool && animatorParam.name == param)
                            {
                                if (animator.GetBool(param))
                                    return true;
                            }
                        }
                    }
                }
            }

            if (btn.hideIfStateName != null && btn.hideIfStateName.Length > 0)
            {
                AnimatorStateInfo stateInfo = animator.GetCurrentAnimatorStateInfo(0);
                foreach (var state in btn.hideIfStateName)
                {
                    if (!string.IsNullOrEmpty(state) && stateInfo.IsName(state))
                        return true;
                }
            }
            if (btn != null && btn.id == "clothes")
            {
                GameObject avatarGO = animatorReceiver != null && animatorReceiver.avatarAnimator != null
                    ? animatorReceiver.avatarAnimator.gameObject
                    : null;

                bool hasClothes = false;
                if (avatarGO != null)
                {
                    var clothes = avatarGO.GetComponent<MEClothes>();
                    if (clothes == null)
                        clothes = avatarGO.GetComponentInChildren<MEClothes>(true);
                    hasClothes = (clothes != null);
                }
                if (!hasClothes)
                    return true;
            }
            return false;
        }

    }
}