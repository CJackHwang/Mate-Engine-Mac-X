using System;
using System.Collections;
using UnityEngine;

namespace CustomDancePlayer
{
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

        public void InitPlayer()
        {
            if (_settingsHandler == null) _settingsHandler = DanceSettingsHandler.Instance;
            if (_settingsHandler.data.autoPlayOnStart && resourceManager.GetTotalDanceCount() > 0 && _settingsHandler.data.currentPlayIndex >= 0)
            {
                PlayDanceByIndex(_settingsHandler.data.currentPlayIndex);
            }
        }

        public bool PlayDanceByIndex(int index)
        {
            if (!avatarHelper.IsAvatarAvailable()) return false;
            if (resourceManager.GetTotalDanceCount() == 0 || index < 0 || index >= resourceManager.GetTotalDanceCount()) return false;

            _settingsHandler.data.currentPlayIndex = index;

            if (!resourceManager.PrepareByIndex(index, avatarHelper))
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

            if (avatarHelper.CurrentAudioSource != null && resourceManager.CurrentAudioClip != null)
            {
                avatarHelper.CurrentAudioSource.Play();
                _settingsHandler.data.isPlaying = true;
                _settingsHandler.data.audioStartTime = Time.time;
                float extraDelay = Mathf.Clamp(_settingsHandler.data.animationStartDelay, 0f, 1f);
                _startAnimationCoroutine = StartCoroutine(
                    WaitForAudioThenStartAnimation(
                        avatarHelper.CurrentAnimator,
                        resourceManager.CurrentAnimationClip,
                        extraDelay
                    )
                );
            }
            else
            {
                ApplyAnimationImmediately(avatarHelper.CurrentAnimator, resourceManager.CurrentAnimationClip);
                _settingsHandler.data.isPlaying = true;
            }

            if (uiManager != null) uiManager.UpdateDropdownValue();
            DanceSettingsHandler.OnSettingChanged();
            return true;
        }

        private void ApplyAnimationImmediately(Animator animator, AnimationClip clip)
        {
            if (animator == null || clip == null) return;
            avatarHelper.SetupAnimation(clip);
        }

        public void PlayNext()
        {
            if (resourceManager.GetTotalDanceCount() == 0) return;

            int nextIndex = _settingsHandler.data.currentPlayIndex;

            switch (_settingsHandler.data.currentPlayMode)
            {
                case PlayMode.Sequence:
                    nextIndex = _settingsHandler.data.currentPlayIndex + 1;
                    if (nextIndex >= resourceManager.GetTotalDanceCount())
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
                        nextIndex = random.Next(0, resourceManager.GetTotalDanceCount());
                    } while (resourceManager.GetTotalDanceCount() > 1 && nextIndex == _settingsHandler.data.currentPlayIndex);
                    break;
            }

            PlayDanceByIndex(nextIndex);
        }

        public void PlayPrev()
        {
            if (resourceManager.GetTotalDanceCount() == 0) return;
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
            int idx = _settingsHandler.data.currentPlayIndex;
            var total = resourceManager.GetTotalDanceCount();
            if (total == 0 || idx < 0 || idx >= total) return "Not Playing";
            var entries = resourceManager.GetAllDanceEntries();
            if (idx < entries.Count && entries[idx] != null && !string.IsNullOrEmpty(entries[idx].id)) return entries[idx].id;
            return "Not Playing";
        }
    }
}
