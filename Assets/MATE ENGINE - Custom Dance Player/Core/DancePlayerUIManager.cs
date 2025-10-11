using System;
using System.Collections;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace CustomDancePlayer
{
    public class DancePlayerUIManager : MonoBehaviour
    {
        public Canvas TargetCanvas;
        public Text CurrentPlayText;
        public Slider ProgressSlider;
        public Dropdown DanceFileDropdown;
        public Button RefreshBtn;
        public Button PrevBtn;
        public Button PlayPauseBtn;
        public Button NextBtn;
        public Button StopBtn;
        public Button PlayModeBtn;
        public TMP_Text PlayModeText;
        public TMP_Text AvatarStatusText;
        public TMP_Text ToggleKeyText;
        public Button AdvancedToggleBtn;
        public TMP_Text AdvancedToggleBtnText;
        public GameObject MainPanelRoot;
        public GameObject SettingsPanelRoot;
        public ScrollRect SettingsScrollRect;
        public Slider VolumeSlider;
        public TMP_Text VolumeValueText;
        public Slider AnimationStartDelaySlider;
        public TMP_Text AnimationStartDelayValueText;
        public Toggle EnableUIPanelFollow;
        public Toggle EnableShadowFollow;
        public Toggle EnableWindowFollow;
        public Toggle EnableCameraDistanceKeep;
        public Toggle AutoPlayOnStartToggle;
        public Toggle HidePanelOnStartToggle;
        public Toggle EnableGlobalHotkey;

        [Header("Core Components")]
        public DancePlayerCore playerCore;
        public DanceAvatarHelper avatarHelper;
        public DanceResourceManager resourceManager;

        private DanceSettingsHandler _settingsHandler;
        private HipsFollower _hipsFollower;
        private DanceShadowFollower _shadowFollower;
        private DanceWindowFollower _danceWindowFollower;
        private DanceCameraDistKeeper _danceCameraDistKeeper;
        private GlobalHotkeyListener _globalHotkeyListener;

        private bool _isAdvancedOpen;
        private MenuActions _gameMenuActions;
        private MenuEntry _myUIMenuEntry;
        private bool _isMyUIAddedToMenuList;

        void Start()
        {
            _danceWindowFollower = FindFirstObjectByType<DanceWindowFollower>();
            _danceCameraDistKeeper = FindFirstObjectByType<DanceCameraDistKeeper>();
            _hipsFollower = FindFirstObjectByType<HipsFollower>();
            _shadowFollower = FindFirstObjectByType<DanceShadowFollower>();
            _globalHotkeyListener = FindFirstObjectByType<GlobalHotkeyListener>();

            _settingsHandler = DanceSettingsHandler.Instance;
            RefreshDropdown();
            playerCore.InitPlayer();
            UpdateToggleKeyText();
            InitUI();
            BindButtonEvents();

            if (_settingsHandler.data.hidePanelOnStart && TargetCanvas != null)
            {
                TargetCanvas.gameObject.SetActive(false);
            }

            if (_settingsHandler.data.autoPlayOnStart && _settingsHandler.data.currentPlayIndex >= 0)
            {
                StartCoroutine(TryAutoPlay());
            }

            _gameMenuActions = UnityEngine.Object.FindFirstObjectByType<MenuActions>();
            _myUIMenuEntry = new MenuEntry
            {
                menu = TargetCanvas.gameObject,
                blockMovement = true,
                blockHandTracking = false,
                blockReaction = false,
                blockChibiMode = false
            };
            AddMyUIToGameMenuList();
        }

        void Update()
        {
            UpdateUI();
            HandleKeyToggleUI();
        }

        private void InitUI()
        {
            CurrentPlayText.text = playerCore.GetCurrentPlayFileName();
            PlayModeText.text = GetPlayModeText();
            AvatarStatusText.text = "Avatar Status: Not Connected";
            UpdateDropdownValue();
        }

        public void SetPanelVisible(bool visible)
        {
            if (TargetCanvas == null) return;
            GameObject targetCanvasObject = TargetCanvas.gameObject;
            if (targetCanvasObject.activeSelf != visible)
            {
                targetCanvasObject.SetActive(visible);
                if (visible) AddMyUIToGameMenuList();
            }
        }

        private void HandleKeyToggleUI()
        {
            if (TargetCanvas == null) return;
            if (IsInTextInputState()) return;
            if (Input.GetKeyDown(_settingsHandler.data.toggleKey))
            {
                GameObject targetCanvasObject = TargetCanvas.gameObject;
                bool newVisibleState = !targetCanvasObject.activeSelf;
                targetCanvasObject.SetActive(newVisibleState);
                if (newVisibleState) AddMyUIToGameMenuList();
            }
        }

        private void BindButtonEvents()
        {
            PrevBtn.onClick.AddListener(playerCore.PlayPrev);
            PlayPauseBtn.onClick.AddListener(OnPlayPauseBtnClick);
            NextBtn.onClick.AddListener(playerCore.PlayNext);
            StopBtn.onClick.AddListener(playerCore.StopPlay);
            PlayModeBtn.onClick.AddListener(OnPlayModeBtnClick);
            RefreshBtn.onClick.AddListener(RefreshDropdown);

            if (DanceFileDropdown != null)
            {
                DanceFileDropdown.onValueChanged.AddListener(index =>
                {
                    _settingsHandler.data.currentPlayIndex = index;
                    DanceSettingsHandler.OnSettingChanged();
                });
            }

            if (VolumeSlider != null)
            {
                VolumeSlider.value = _settingsHandler.data.danceVolume;
                VolumeValueText.text = $"{Mathf.RoundToInt(_settingsHandler.data.danceVolume * 100)}%";
                VolumeSlider.onValueChanged.AddListener(value =>
                {
                    _settingsHandler.data.danceVolume = value;
                    avatarHelper.UpdateAudioVolume();
                    VolumeValueText.text = $"{Mathf.RoundToInt(value * 100)}%";
                    DanceSettingsHandler.OnSettingChanged();
                });
            }

            if (AdvancedToggleBtn != null)
            {
                AdvancedToggleBtn.onClick.AddListener(ToggleAdvancedPanel);
                AdvancedToggleBtnText.text = "Settings";
            }

            if (AnimationStartDelaySlider != null)
            {
                AnimationStartDelaySlider.value = _settingsHandler.data.animationStartDelay;
                AnimationStartDelayValueText.text = $"{_settingsHandler.data.animationStartDelay:0.000}s";
                AnimationStartDelaySlider.onValueChanged.AddListener(value =>
                {
                    _settingsHandler.data.animationStartDelay = value;
                    AnimationStartDelayValueText.text = $"{value:0.000}s";
                    DanceSettingsHandler.OnSettingChanged();
                });
            }

            if (AutoPlayOnStartToggle != null)
            {
                AutoPlayOnStartToggle.isOn = _settingsHandler.data.autoPlayOnStart;
                AutoPlayOnStartToggle.onValueChanged.AddListener(isOn =>
                {
                    _settingsHandler.data.autoPlayOnStart = isOn;
                    DanceSettingsHandler.OnSettingChanged();
                });
            }

            if (HidePanelOnStartToggle != null)
            {
                HidePanelOnStartToggle.isOn = _settingsHandler.data.hidePanelOnStart;
                HidePanelOnStartToggle.onValueChanged.AddListener(isOn =>
                {
                    _settingsHandler.data.hidePanelOnStart = isOn;
                    DanceSettingsHandler.OnSettingChanged();
                });
            }

            if (EnableUIPanelFollow != null && _hipsFollower != null)
            {
                EnableUIPanelFollow.isOn = _settingsHandler.data.enableDanceUIFollow;
                _hipsFollower.enabled = _settingsHandler.data.enableDanceUIFollow;
                EnableUIPanelFollow.onValueChanged.AddListener(isOn =>
                {
                    _settingsHandler.data.enableDanceUIFollow = isOn;
                    _hipsFollower.enabled = isOn;
                    DanceSettingsHandler.OnSettingChanged();
                });
            }

            if (EnableShadowFollow != null && _shadowFollower != null)
            {
                EnableShadowFollow.isOn = _settingsHandler.data.enableShadowFollow;
                _shadowFollower.enabled = _settingsHandler.data.enableShadowFollow;
                EnableShadowFollow.onValueChanged.AddListener(isOn =>
                {
                    _settingsHandler.data.enableShadowFollow = isOn;
                    _shadowFollower.enabled = isOn;
                    DanceSettingsHandler.OnSettingChanged();
                });
            }

            if (EnableWindowFollow != null && _danceWindowFollower != null)
            {
                EnableWindowFollow.isOn = _settingsHandler.data.enableWindowFollow;
                _danceWindowFollower.SetEnabled(_settingsHandler.data.enableWindowFollow);
                EnableWindowFollow.onValueChanged.AddListener(isOn =>
                {
                    _settingsHandler.data.enableWindowFollow = isOn;
                    _danceWindowFollower.SetEnabled(isOn);
                    DanceSettingsHandler.OnSettingChanged();
                });
            }

            if (EnableCameraDistanceKeep != null & _danceCameraDistKeeper != null)
            {
                EnableCameraDistanceKeep.isOn = _settingsHandler.data.enableCameraDistanceKeep;
                _danceCameraDistKeeper.enabled = _settingsHandler.data.enableCameraDistanceKeep;
                EnableCameraDistanceKeep.onValueChanged.AddListener(isOn =>
                {
                    _settingsHandler.data.enableCameraDistanceKeep = isOn;
                    _danceCameraDistKeeper.enabled = isOn;
                    DanceSettingsHandler.OnSettingChanged();
                });
            }

            if (EnableGlobalHotkey != null && _globalHotkeyListener != null)
            {
                EnableGlobalHotkey.isOn = _settingsHandler.data.enableGlobalHotkey;
                _globalHotkeyListener.enabled = _settingsHandler.data.enableGlobalHotkey;
                EnableGlobalHotkey.onValueChanged.AddListener(isOn =>
                {
                    _settingsHandler.data.enableGlobalHotkey = isOn;
                    _globalHotkeyListener.enabled = isOn;
                    DanceSettingsHandler.OnSettingChanged();
                });
            }
        }

        private void UpdateUI()
        {
            CurrentPlayText.text = playerCore.GetCurrentPlayFileName();
            AvatarStatusText.text = avatarHelper.IsAvatarAvailable() ? "Avatar Status: Connected" : "Avatar Status: Not Connected";
            PlayModeText.text = GetPlayModeText();

            bool isPlayerReady = avatarHelper.IsAvatarAvailable() && resourceManager.GetTotalDanceCount() > 0;
            PlayPauseBtn.interactable = isPlayerReady && !_settingsHandler.data.isPlaying;
            PrevBtn.interactable = isPlayerReady && _settingsHandler.data.isPlaying;
            NextBtn.interactable = isPlayerReady && _settingsHandler.data.isPlaying;
            StopBtn.interactable = isPlayerReady && _settingsHandler.data.isPlaying;
            DanceFileDropdown.interactable = isPlayerReady && !_settingsHandler.data.isPlaying;
            RefreshBtn.interactable = !_settingsHandler.data.isPlaying;

            if (_settingsHandler.data.isPlaying && resourceManager.CurrentAudioClip != null)
            {
                float elapsed = Time.time - _settingsHandler.data.audioStartTime;
                float total = resourceManager.CurrentAudioClip.length;
                ProgressSlider.value = Mathf.Clamp01(elapsed / total);
            }
            else
            {
                ProgressSlider.value = 0f;
            }
        }

        public IEnumerator TryAutoPlay()
        {
            yield return new WaitForSeconds(3f);
            float timeout = 10f;
            float elapsed = 0f;

            while (!avatarHelper.IsAvatarAvailable() && elapsed < timeout)
            {
                yield return null;
                elapsed += Time.deltaTime;
            }

            if (avatarHelper.IsAvatarAvailable() &&
                _settingsHandler.data.currentPlayIndex >= 0 &&
                DanceFileDropdown.options.Count > 0 &&
                _settingsHandler.data.currentPlayIndex < DanceFileDropdown.options.Count)
            {
                OnPlayPauseBtnClick();
            }
        }

        public void OnPlayPauseBtnClick()
        {
            if (!_settingsHandler.data.isPlaying && DanceFileDropdown.value >= 0)
            {
                playerCore.PlayDanceByIndex(DanceFileDropdown.value);
            }
        }

        private void OnPlayModeBtnClick()
        {
            _settingsHandler.data.currentPlayMode = (DancePlayerCore.PlayMode)(((int)_settingsHandler.data.currentPlayMode + 1) % Enum.GetValues(typeof(DancePlayerCore.PlayMode)).Length);
            DanceSettingsHandler.OnSettingChanged();
        }

        private string GetPlayModeText()
        {
            return _settingsHandler.data.currentPlayMode switch
            {
                DancePlayerCore.PlayMode.Sequence => "Sequence",
                DancePlayerCore.PlayMode.Loop => "Loop",
                DancePlayerCore.PlayMode.Random => "Random",
                _ => "Sequence"
            };
        }

        public void UpdateDropdownValue()
        {
            if (DanceFileDropdown == null || DanceFileDropdown.options.Count == 0) return;
            int targetIndex = _settingsHandler.data.currentPlayIndex;
            if (targetIndex < 0 || targetIndex >= DanceFileDropdown.options.Count)
            {
                targetIndex = 0;
                _settingsHandler.data.currentPlayIndex = targetIndex;
                DanceSettingsHandler.OnSettingChanged();
            }
            if (DanceFileDropdown.value != targetIndex)
            {
                DanceFileDropdown.value = targetIndex;
                DanceFileDropdown.captionText.text = DanceFileDropdown.options[targetIndex].text;
            }
        }

        public void RefreshDropdown()
        {
            DanceFileDropdown.ClearOptions();
            resourceManager.RefreshDanceFileList();
            var names = resourceManager.GetDisplayNames();
            if (names.Count == 0)
            {
                DanceFileDropdown.options.Add(new Dropdown.OptionData("No dances found"));
                _settingsHandler.data.currentPlayIndex = -1;
                DanceSettingsHandler.OnSettingChanged();
                return;
            }
            foreach (var n in names) DanceFileDropdown.options.Add(new Dropdown.OptionData(n));
            if (_settingsHandler.data.currentPlayIndex < 0 || _settingsHandler.data.currentPlayIndex >= names.Count)
            {
                _settingsHandler.data.currentPlayIndex = 0;
                DanceSettingsHandler.OnSettingChanged();
            }
            DanceFileDropdown.value = _settingsHandler.data.currentPlayIndex;
            DanceFileDropdown.captionText.text = names[_settingsHandler.data.currentPlayIndex];
        }

        public void UpdateToggleKeyText()
        {
            if (ToggleKeyText != null)
            {
                ToggleKeyText.text = $"Press {_settingsHandler.data.toggleKey} to hide UI";
            }
        }

        private bool IsInTextInputState()
        {
            if (EventSystem.current == null) return false;
            GameObject selectedObj = EventSystem.current.currentSelectedGameObject;
            return selectedObj != null && (selectedObj.GetComponent<InputField>() != null || selectedObj.GetComponent<TMP_InputField>() != null);
        }

        private void ToggleAdvancedPanel()
        {
            _isAdvancedOpen = !_isAdvancedOpen;
            MainPanelRoot.SetActive(!_isAdvancedOpen);
            SettingsPanelRoot.SetActive(_isAdvancedOpen);
            AdvancedToggleBtnText.text = _isAdvancedOpen ? "Back" : "Settings";
            if (_isAdvancedOpen && SettingsScrollRect != null) SettingsScrollRect.verticalNormalizedPosition = 1f;
        }

        public void AddMyUIToGameMenuList()
        {
            if (_gameMenuActions == null || _isMyUIAddedToMenuList || _myUIMenuEntry == null) return;
            bool isAlreadyInList = _gameMenuActions.menuEntries.Exists(entry => entry.menu == gameObject);
            if (!isAlreadyInList)
            {
                _gameMenuActions.menuEntries.Add(_myUIMenuEntry);
                _isMyUIAddedToMenuList = true;
            }
        }
    }
}
