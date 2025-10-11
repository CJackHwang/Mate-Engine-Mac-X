using System;
using System.Collections;
using UnityEngine;

namespace CustomDancePlayer
{ // Manages dance playback logic and state updates

    public class DancePlayerCore : MonoBehaviour
    {

        [Header("Dependency References")]
        public DanceAvatarHelper avatarHelper;
        public DanceResourceManager resourceManager;
        public DancePlayerUIManager uiManager;

        public enum PlayMode { Sequence, Loop, Random }


        private DanceSettingsHandler _settingsHandler;
        private Coroutine _startAnimationCoroutine;
        public bool IsPlaying
        {
            get => _settingsHandler.data.isPlaying;
            set
            {
                _settingsHandler.data.isPlaying = value;
                DanceSettingsHandler.OnSettingChanged();
            }
        }

        void Start()
        {

            _settingsHandler = DanceSettingsHandler.Instance;
        }

        // Initializes player with playlist
        public void InitPlayer()
        {
            if (_settingsHandler  == null)
            {
                _settingsHandler = DanceSettingsHandler.Instance;
            }
            if (_settingsHandler.data.autoPlayOnStart && resourceManager.DanceFileList.Count > 0 && _settingsHandler.data.currentPlayIndex >= 0)
            {
                PlayDanceByIndex(_settingsHandler.data.currentPlayIndex);
            }
        }

        public bool PlayDanceByIndex(int index)
        {
            if (resourceManager.DanceFileList.Count == 0 || index < 0 || index >= resourceManager.DanceFileList.Count || !avatarHelper.IsAvatarAvailable())
            {
                return false;
            }

            _settingsHandler.data.currentPlayIndex = index;
            string targetFileName = resourceManager.DanceFileList[index];

            if (!resourceManager.LoadDanceResource(targetFileName))
            {
                _settingsHandler.data.isPlaying = false;
                DanceSettingsHandler.OnSettingChanged();
                return false;
            }

            if (_startAnimationCoroutine != null)
            {
                StopCoroutine(_startAnimationCoroutine);
                _startAnimationCoroutine = null;
            }

            if (avatarHelper.CurrentOverrideController != null)
            {
                Destroy(avatarHelper.CurrentOverrideController);

            }

            if (avatarHelper.TargetSMR != null)
            {
                var proxy = avatarHelper.CurrentAnimator.GetComponent<UniversalBlendshapes>();
                if (proxy != null) proxy.enabled = false;
            }
            else
            {
                avatarHelper.SetupDummyForDance();
            }

            avatarHelper.CurrentAudioSource.Play();
            _settingsHandler.data.isPlaying = true;
            _settingsHandler.data.audioStartTime = Time.time;

            // Due to DSF buffer causing audio startup delay, must wait for audio to actually start playing before starting animation
            float extraDelay = Mathf.Clamp(_settingsHandler.data.animationStartDelay, 0f, 1f);
            _startAnimationCoroutine = StartCoroutine(
                WaitForAudioThenStartAnimation(
                    avatarHelper.CurrentAnimator,
                    resourceManager.CurrentAnimationClip,
                    extraDelay
                )
            );
            if (uiManager != null)
            {
                uiManager.UpdateDropdownValue(); 
            }

            DanceSettingsHandler.OnSettingChanged();
            return true;
        }

        // Applies animation immediately
        private void ApplyAnimationImmediately(Animator animator, AnimationClip clip)
        {
            if (animator == null || clip == null) return;

            avatarHelper.SetupAnimation(clip);
        }


        public void PlayNext()
        {
            if (resourceManager.DanceFileList.Count == 0) return;

            int nextIndex = _settingsHandler.data.currentPlayIndex;

            switch (_settingsHandler.data.currentPlayMode)
            {
                case PlayMode.Sequence:
                    nextIndex = _settingsHandler.data.currentPlayIndex + 1;
                    if (nextIndex >= resourceManager.DanceFileList.Count)
                    {
                        StopPlay();
                        return;
                    }
                    break;
                case PlayMode.Loop:
                    nextIndex = _settingsHandler.data.currentPlayIndex;
                    break;
                case PlayMode.Random:
                    System.Random random = new System.Random();
                    do
                    {
                        nextIndex = random.Next(0, resourceManager.DanceFileList.Count);
                    } while (resourceManager.DanceFileList.Count > 1 && nextIndex == _settingsHandler.data.currentPlayIndex);
                    break;
            }

            PlayDanceByIndex(nextIndex);
        }

        public void PlayPrev()
        {
            if (resourceManager.DanceFileList.Count == 0) return;
            PlayDanceByIndex(Mathf.Max(0, _settingsHandler.data.currentPlayIndex - 1));
        }

        public void StopPlay()
        {
            if (!avatarHelper.IsAvatarAvailable()) return;

            if (_startAnimationCoroutine != null)
            {
                StopCoroutine(_startAnimationCoroutine);
                _startAnimationCoroutine = null;
            }

            avatarHelper.CurrentAudioSource.Stop();
            avatarHelper.CurrentAnimator.SetBool("isDancing", false);

            if (avatarHelper.DefaultAnimatorController != null)
            {
                avatarHelper.CurrentAnimator.runtimeAnimatorController = avatarHelper.DefaultAnimatorController;
            }

            if (avatarHelper.TargetSMR != null)
            {
                var proxy = avatarHelper.CurrentAnimator.GetComponent<UniversalBlendshapes>();
                if (proxy != null) proxy.enabled = true;
            }
            else
            {
                avatarHelper.RestoreOriginalBody();
            }

            resourceManager.UnloadCurrentResource();
            _settingsHandler.data.isPlaying = false;
            DanceSettingsHandler.OnSettingChanged();
        }

        private IEnumerator WaitForAudioThenStartAnimation(Animator animator, AnimationClip clip, float extraDelay)
        {
            if (avatarHelper.CurrentAudioSource == null)
            {
                ApplyAnimationImmediately(animator, clip);
                yield break;
            }

            float startTime = Time.time;
            float maxWaitTime = 1f;

            while (Time.time - startTime < maxWaitTime)
            {
                if (avatarHelper.CurrentAudioSource.time > 0.001f)
                {
                    if (extraDelay > 0.001f)
                        yield return new WaitForSeconds(extraDelay);


                    ApplyAnimationImmediately(animator, clip);
                    yield break;
                }

                yield return null; 
            }

            ApplyAnimationImmediately(animator, clip);
        }

        public string GetCurrentPlayFileName()
        {
            if (resourceManager.DanceFileList.Count == 0 || _settingsHandler.data.currentPlayIndex < 0 || _settingsHandler.data.currentPlayIndex >= resourceManager.DanceFileList.Count)
            {
                return "Not Playing";
            }
            string fileName = resourceManager.DanceFileList[_settingsHandler.data.currentPlayIndex];
            return fileName.EndsWith(".unity3d", StringComparison.OrdinalIgnoreCase) ? fileName.Substring(0, fileName.Length - ".unity3d".Length) : fileName;
        }
    }
}