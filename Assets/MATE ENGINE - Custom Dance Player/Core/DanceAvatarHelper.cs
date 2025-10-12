using System;
using System.Linq;
using UnityEngine;

namespace CustomDancePlayer
{
    public class DanceAvatarHelper : MonoBehaviour
    {
        private const string MODEL_PARENT_NAME = "Model"; private const string CUSTOM_DANCE_AUDIO_NAME = "CustomDanceAudio"; private const string BODY_NAME = "Body";
        private static readonly string[] MMDBlendshapeKeywords = { "まばたき", "あ", "い", "う", "え", "お" };

        public Mesh DummyBlendshapeMesh;
        public RuntimeAnimatorController CustomDanceAvatarController;
        public DancePlayerCore playerCore;

        public GameObject CurrentAvatar { get; private set; }
        public Animator CurrentAnimator { get; private set; }
        public Transform CurrentAvatarHips { get; private set; }
        public AudioSource CurrentAudioSource { get; private set; }
        public SkinnedMeshRenderer TargetSMR { get; private set; }
        public AnimatorOverrideController CurrentOverrideController { get; private set; }
        public RuntimeAnimatorController DefaultAnimatorController { get; private set; }

        private GameObject _modelParent;
        private DanceSettingsHandler _settingsHandler;
        private Transform _originalBodyTransform;
        private Transform _dummyBodyTransform;
        private string _oldBodyName;

        void Start()
        {
            _modelParent = GameObject.Find(MODEL_PARENT_NAME);
            SetupAudioSource();
            _settingsHandler = DanceSettingsHandler.Instance;
            CheckAndUpdateCurrentAvatar();
            if (CurrentAnimator != null) DefaultAnimatorController = CurrentAnimator.runtimeAnimatorController;
            SetupMMDBlendshapeSMR();
            UpdateAudioVolume();
        }

        void Update()
        {
            CheckAndUpdateCurrentAvatar();
        }

        void OnDestroy()
        {
            ClearCurrentAvatar();
            CurrentAvatar = null;
            CurrentAvatarHips = null;
            CurrentAnimator = null;
            CurrentAudioSource = null;
        }

        private void SetupAudioSource()
        {
            GameObject soundFX = GameObject.Find("SoundFX");
            if (soundFX == null) return;
            Transform audioTrans = soundFX.transform.Find(CUSTOM_DANCE_AUDIO_NAME);
            GameObject audioObj = audioTrans != null ? audioTrans.gameObject : new GameObject(CUSTOM_DANCE_AUDIO_NAME);
            if (audioTrans == null) audioObj.transform.SetParent(soundFX.transform, false);
            CurrentAudioSource = audioObj.GetComponent<AudioSource>();
            if (CurrentAudioSource == null) CurrentAudioSource = audioObj.AddComponent<AudioSource>();
        }

        public void UpdateAudioVolume()
        {
            if (CurrentAudioSource != null) CurrentAudioSource.volume = _settingsHandler.data.danceVolume;
        }

        private void CheckAndUpdateCurrentAvatar()
        {
            if (_modelParent == null)
            {
                ClearCurrentAvatar();
                return;
            }
            GameObject newAvatar = null;
            foreach (Transform child in _modelParent.transform)
            {
                if (child.gameObject.activeSelf && child.GetComponent<Animator>() != null)
                {
                    newAvatar = child.gameObject;
                    break;
                }
            }
            if (newAvatar != CurrentAvatar) UpdateAvatarComponents(newAvatar);
        }

        private void UpdateAvatarComponents(GameObject newAvatar)
        {
            ClearCurrentAvatar();
            if (newAvatar == null) return;
            CurrentAvatar = newAvatar;
            CurrentAnimator = newAvatar.GetComponentInChildren<Animator>();
            if (CurrentAnimator == null)
            {
                CurrentAvatar = null;
                CurrentAvatarHips = null;
                return;
            }
            CurrentAvatarHips = CurrentAnimator.GetBoneTransform(HumanBodyBones.Hips);
            SetupMMDBlendshapeSMR();
            DefaultAnimatorController = CurrentAnimator.runtimeAnimatorController;
            if (playerCore != null) playerCore.StopPlay();
            SMRHandler.SetUpdateWhenOffscreen(CurrentAvatar, true);
            var proxy = CurrentAvatar.AddComponent<DancePlayerAvatarProxy>();
            proxy.playerCore = playerCore;
        }

        private void SetupMMDBlendshapeSMR()
        {
            if (CurrentAvatar == null) return;
            SkinnedMeshRenderer[] smrs = CurrentAvatar.GetComponentsInChildren<SkinnedMeshRenderer>();
            TargetSMR = smrs.FirstOrDefault(smr => smr.sharedMesh != null && smr.sharedMesh.blendShapeCount > 0 &&
                !smr.sharedMesh.GetBlendShapeName(0).ToLower().Contains("dummy") &&
                MMDBlendshapeKeywords.All(keyword => Enumerable.Range(0, smr.sharedMesh.blendShapeCount).Any(i => smr.sharedMesh.GetBlendShapeName(i) == keyword)));
            if (TargetSMR != null && TargetSMR.transform.parent != CurrentAvatar.transform)
            {
                TargetSMR.transform.SetParent(CurrentAvatar.transform, false);
                TargetSMR.gameObject.name = BODY_NAME;
            }
        }

        private void ClearCurrentAvatar()
        {
            if (CurrentAnimator != null && DefaultAnimatorController != null)
            {
                CurrentAnimator.runtimeAnimatorController = DefaultAnimatorController;
                if (CurrentAnimator.HasParameterOfType("isDancing", AnimatorControllerParameterType.Bool)) CurrentAnimator.SetBool("isDancing", false);
                if (CurrentAnimator.HasParameterOfType("isCustomDancing", AnimatorControllerParameterType.Bool)) CurrentAnimator.SetBool("isCustomDancing", false);
            }
        }

        public bool IsAvatarAvailable()
        {
            return CurrentAvatar != null && CurrentAnimator != null;
        }

        public void SetupDummyForDance()
        {
            if (TargetSMR != null) return;
            Transform existingBody = CurrentAvatar.transform.Find(BODY_NAME);
            if (existingBody != null)
            {
                SkinnedMeshRenderer smr = existingBody.GetComponent<SkinnedMeshRenderer>();
                if (smr != null && smr.sharedMesh != null && smr.sharedMesh.blendShapeCount > 0 &&
                    Enumerable.Range(0, smr.sharedMesh.blendShapeCount).Any(i => smr.sharedMesh.GetBlendShapeName(i).ToLower().Contains("dummy"))) return;
                _originalBodyTransform = existingBody;
                _oldBodyName = BODY_NAME + $"_Old_{UnityEngine.Random.Range(0, 10000)}";
                existingBody.name = _oldBodyName;
            }
            GameObject dummyObj = new GameObject(BODY_NAME);
            dummyObj.transform.SetParent(CurrentAvatar.transform, false);
            var dummySmr = dummyObj.AddComponent<SkinnedMeshRenderer>();
            dummySmr.sharedMesh = DummyBlendshapeMesh;
            dummySmr.updateWhenOffscreen = true;
            _dummyBodyTransform = dummyObj.transform;
            if (!CurrentAvatar.TryGetComponent<DummyToUniversalSync>(out var dummySync)) dummySync = CurrentAvatar.AddComponent<DummyToUniversalSync>();
            dummySync.dummySmr = dummySmr;
            dummySync.enabled = true;
        }

        public void RestoreOriginalBody()
        {
            if (TargetSMR != null) return;
            if (_dummyBodyTransform != null)
            {
                Destroy(_dummyBodyTransform.gameObject);
                _dummyBodyTransform = null;
            }
            if (_originalBodyTransform != null)
            {
                _originalBodyTransform.name = BODY_NAME;
                _originalBodyTransform = null;
                _oldBodyName = null;
            }
            if (CurrentAvatar.TryGetComponent<DummyToUniversalSync>(out var sync)) sync.enabled = false;
        }

        public void SetupAnimation(AnimationClip clip)
        {
            if (CurrentAnimator == null || clip == null) return;

            if (DefaultAnimatorController != null) CurrentAnimator.runtimeAnimatorController = DefaultAnimatorController;

            if (CurrentOverrideController == null) CurrentOverrideController = new AnimatorOverrideController(DefaultAnimatorController);

            var originals = CurrentOverrideController.animationClips;
            var placeholder = originals.FirstOrDefault(c => c != null && c.name == "CUSTOM_DANCE");
            if (placeholder == null)
            {
                Debug.LogError("[DanceAvatarHelper] Missing placeholder clip 'CUSTOM_DANCE' in state 'Custom Dance' on 'Dance Layer'.");
                return;
            }
            CurrentOverrideController["CUSTOM_DANCE"] = clip;
            CurrentAnimator.runtimeAnimatorController = CurrentOverrideController;

            if (CurrentAnimator.HasParameterOfType("isCustomDancing", AnimatorControllerParameterType.Bool))
                CurrentAnimator.SetBool("isCustomDancing", true);

            int danceLayer = CurrentAnimator.GetLayerIndex("Dance Layer");
            if (danceLayer >= 0) CurrentAnimator.CrossFadeInFixedTime("Custom Dance", 0.1f, danceLayer);

            SMRHandler.SetUpdateWhenOffscreen(CurrentAvatar, true);
        }

        public void StopCustomDance()
        {
            if (CurrentAnimator == null) return;
            if (CurrentAnimator.HasParameterOfType("isCustomDancing", AnimatorControllerParameterType.Bool))
                CurrentAnimator.SetBool("isCustomDancing", false);
            if (TargetSMR == null) RestoreOriginalBody();
            if (DefaultAnimatorController != null) CurrentAnimator.runtimeAnimatorController = DefaultAnimatorController;
            if (CurrentOverrideController != null)
            {
                Destroy(CurrentOverrideController);
                CurrentOverrideController = null;
            }
        }
    }
}

public static class AnimatorDanceExtensions
{
    public static bool HasParameterOfType(this Animator self, string name, AnimatorControllerParameterType type)
    {
        foreach (var param in self.parameters)
            if (param.type == type && param.name == name)
                return true;
        return false;
    }
}
