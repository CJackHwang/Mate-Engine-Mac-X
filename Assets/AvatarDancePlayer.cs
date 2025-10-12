using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace CustomDancePlayer
{
    public class AvatarDanceHandler : MonoBehaviour
    {
        [Header("UI")]
        public Button playButton;
        public Button stopButton;
        public Button prevButton;
        public Button nextButton;
        public Slider progressSlider;
        public TMP_Text playingNowText;
        public TMP_Text playTimeText;
        public TMP_Text maxPlayTimeText;

        [Header("Sound")]
        public AudioSource audioSource;

        [Header("Misc Settings")]
        public string danceLayerName = "Dance Layer";
        public string danceStateName = "Custom Dance";
        public string placeholderClipName = "CUSTOM_DANCE";
        public string customDancingParam = "isCustomDancing";

        [Header("Prefab Settings")]
        public Transform contentObject;
        public GameObject prefab;
        public Button songPlayButton;

        [Header("Folders")]
        public string streamingSubfolder = "CustomDances";
        public string modsFolderName = "Mods";

        Animator animator;
        Animator lastAnimator;
        RuntimeAnimatorController defaultController;
        AnimatorOverrideController overrideController;
        int layerIndex = -1;
        int stateHash = 0;
        int currentIndex = -1;

        string defaultPlayingNowText = "";
        string defaultPlayTimeText = "";
        string defaultMaxPlayTimeText = "";

        float currentTotalSeconds = 0f;
        float playStartTime = 0f;
        bool isPlaying = false;

        class DanceEntry
        {
            public string id;
            public string path;
            public AnimationClip clip;
            public AudioClip audio;
            public AssetBundle bundle;
            public bool fromME;
            public string extractedDir;
        }

        readonly List<DanceEntry> entries = new();
        readonly Dictionary<string, DanceEntry> byId = new(StringComparer.OrdinalIgnoreCase);

        void Awake()
        {
            if (playingNowText != null) defaultPlayingNowText = playingNowText.text;
            if (playTimeText != null) defaultPlayTimeText = playTimeText.text;
            if (maxPlayTimeText != null) defaultMaxPlayTimeText = maxPlayTimeText.text;
            BindUI();
        }

        IEnumerator Start()
        {
            if (audioSource == null) EnsureAudioSource();
            yield return null;
            FindAvatarSmart();
            LoadAllSources();
            BuildListUI();
            if (entries.Count > 0 && currentIndex < 0) currentIndex = 0;
            UpdatePlayingNowLabel(null);
            UpdateTimeLabels(0f, 0f);
        }

        void Update()
        {
            RefreshAnimatorIfChanged();

            float total = currentTotalSeconds;
            float elapsed = 0f;

            if (isPlaying)
            {
                if (audioSource != null && audioSource.clip != null)
                {
                    total = audioSource.clip.length;
                    elapsed = audioSource.time;
                    currentTotalSeconds = total;
                }
                else
                {
                    elapsed = Mathf.Clamp(Time.time - playStartTime, 0f, total);
                }
            }

            if (progressSlider != null && total > 0f) progressSlider.value = Mathf.Clamp01(elapsed / total);
            else if (progressSlider != null) progressSlider.value = 0f;

            UpdateTimeLabels(elapsed, total);
        }

        public void RescanMods()
        {
            LoadAllSources();
            BuildListUI();
        }

        void BindUI()
        {
            if (playButton != null) playButton.onClick.AddListener(() => TryPlayCurrentOrFirst());
            if (stopButton != null) stopButton.onClick.AddListener(StopPlay);
            if (prevButton != null) prevButton.onClick.AddListener(PlayPrev);
            if (nextButton != null) nextButton.onClick.AddListener(PlayNext);
            if (songPlayButton != null) songPlayButton.onClick.AddListener(() => TryPlayCurrentOrFirst());
        }

        void TryPlayCurrentOrFirst()
        {
            int idx = currentIndex < 0 ? 0 : currentIndex;
            PlayIndex(idx);
        }

        void EnsureAudioSource()
        {
            var soundFX = GameObject.Find("SoundFX");
            if (soundFX == null) soundFX = new GameObject("SoundFX");
            var t = soundFX.transform.Find("CustomDanceAudio");
            GameObject go = t ? t.gameObject : new GameObject("CustomDanceAudio");
            if (!t) go.transform.SetParent(soundFX.transform, false);
            audioSource = go.GetComponent<AudioSource>();
            if (audioSource == null) audioSource = go.AddComponent<AudioSource>();
            audioSource.playOnAwake = false;
            audioSource.loop = false;
            audioSource.spatialBlend = 0f;
            audioSource.volume = 0.25f;
        }

        void FindAvatarSmart()
        {
            Animator found = null;
            var loader = FindFirstObjectByType<VRMLoader>();
            if (loader != null)
            {
                var current = loader.GetCurrentModel();
                if (current != null) found = current.GetComponentsInChildren<Animator>(true).FirstOrDefault(a => a && a.gameObject.activeInHierarchy);
            }
            if (found == null)
            {
                var modelParent = GameObject.Find("Model");
                if (modelParent != null) found = modelParent.GetComponentsInChildren<Animator>(true).FirstOrDefault(a => a && a.gameObject.activeInHierarchy);
            }
            if (found == null)
            {
                var all = GameObject.FindObjectsByType<Animator>(FindObjectsInactive.Include, FindObjectsSortMode.None);
                found = all.FirstOrDefault(a => a && a.isActiveAndEnabled);
            }
            if (found != animator)
            {
                animator = found;
                lastAnimator = animator;
                defaultController = animator != null ? animator.runtimeAnimatorController : null;
                layerIndex = animator != null ? animator.GetLayerIndex(danceLayerName) : -1;
                stateHash = Animator.StringToHash(danceStateName);
                overrideController = null;
            }
        }

        void RefreshAnimatorIfChanged()
        {
            if (animator == null || lastAnimator == null || animator != lastAnimator || animator.runtimeAnimatorController != defaultController)
            {
                FindAvatarSmart();
            }
        }

        void LoadAllSources()
        {
            foreach (var e in entries) { try { e.bundle?.Unload(true); } catch { } }
            entries.Clear();
            byId.Clear();

            var files = new List<string>();

            string streamDir = Path.Combine(Application.streamingAssetsPath, streamingSubfolder);
            if (Directory.Exists(streamDir)) files.AddRange(Directory.GetFiles(streamDir, "*", SearchOption.AllDirectories));

            string modsDir = Path.Combine(Application.persistentDataPath, modsFolderName);
            Directory.CreateDirectory(modsDir);
            files.AddRange(Directory.GetFiles(modsDir, "*", SearchOption.AllDirectories));

            for (int i = 0; i < files.Count; i++)
            {
                string f = files[i];
                string ext = Path.GetExtension(f);
                if (string.IsNullOrEmpty(ext)) continue;

                if (ext.Equals(".unity3d", StringComparison.OrdinalIgnoreCase))
                    TryAddUnity3D(f);
                else if (ext.Equals(".me", StringComparison.OrdinalIgnoreCase))
                    TryAddME(f);
            }

            entries.Sort((a, b) => string.Compare(a.id, b.id, StringComparison.OrdinalIgnoreCase));
        }

        void TryAddUnity3D(string path)
        {
            string id = Path.GetFileNameWithoutExtension(path);
            if (byId.ContainsKey(id)) return;

            var bundle = AssetBundle.LoadFromFile(path);
            if (bundle == null) return;

            var clip = bundle.LoadAllAssets<AnimationClip>().FirstOrDefault();
            var audio = bundle.LoadAllAssets<AudioClip>().FirstOrDefault();

            if (clip == null && audio == null)
            {
                try { bundle.Unload(true); } catch { }
                return;
            }

            var e = new DanceEntry { id = id, path = path, clip = clip, audio = audio, bundle = bundle, fromME = false, extractedDir = null };
            entries.Add(e);
            byId[id] = e;
        }

        void TryAddME(string mePath)
        {
            string id = Path.GetFileNameWithoutExtension(mePath);
            if (byId.ContainsKey(id)) return;

            string cacheRoot = Path.Combine(Application.temporaryCachePath, "ME_Cache");
            Directory.CreateDirectory(cacheRoot);
            string dst = Path.Combine(cacheRoot, id);

            bool needExtract = true;
            try
            {
                if (Directory.Exists(dst))
                {
                    DateTime srcTime = File.GetLastWriteTimeUtc(mePath);
                    DateTime dstTime = Directory.GetLastWriteTimeUtc(dst);
                    if (dstTime >= srcTime) needExtract = false;
                    else Directory.Delete(dst, true);
                }
                if (needExtract)
                {
                    ZipFile.ExtractToDirectory(mePath, dst);
                    Directory.SetLastWriteTimeUtc(dst, File.GetLastWriteTimeUtc(mePath));
                }
            }
            catch { return; }

            string bundlePath = Directory.GetFiles(dst, "*.bundle", SearchOption.AllDirectories).FirstOrDefault();
            if (string.IsNullOrEmpty(bundlePath) || !File.Exists(bundlePath)) return;

            var bundle = AssetBundle.LoadFromFile(bundlePath);
            if (bundle == null) return;

            var clip = bundle.LoadAllAssets<AnimationClip>().FirstOrDefault();
            var audio = bundle.LoadAllAssets<AudioClip>().FirstOrDefault();

            if (clip == null && audio == null)
            {
                try { bundle.Unload(true); } catch { }
                return;
            }

            var e = new DanceEntry { id = id, path = mePath, clip = clip, audio = audio, bundle = bundle, fromME = true, extractedDir = dst };
            entries.Add(e);
            byId[id] = e;
        }

        void BuildListUI()
        {
            if (contentObject == null || prefab == null) return;
            for (int i = contentObject.childCount - 1; i >= 0; i--) Destroy(contentObject.GetChild(i).gameObject);
            for (int i = 0; i < entries.Count; i++)
            {
                int idx = i;
                var go = Instantiate(prefab, contentObject);
                var txt = go.GetComponentInChildren<TextMeshProUGUI>(true);
                if (txt != null) txt.text = entries[i].id;
                Button btn = null;
                var allButtons = go.GetComponentsInChildren<Button>(true);
                if (allButtons != null && allButtons.Length > 0) btn = allButtons[0];
                if (btn != null) btn.onClick.AddListener(() => { currentIndex = idx; PlayIndex(idx); });
            }
        }

        void PlayPrev()
        {
            if (entries.Count == 0) return;
            currentIndex = currentIndex <= 0 ? entries.Count - 1 : currentIndex - 1;
            PlayIndex(currentIndex);
        }

        void PlayNext()
        {
            if (entries.Count == 0) return;
            currentIndex = (currentIndex + 1) % entries.Count;
            PlayIndex(currentIndex);
        }

        bool EnsureAnimatorReady()
        {
            RefreshAnimatorIfChanged();
            if (animator == null) return false;
            if (defaultController == null) defaultController = animator.runtimeAnimatorController;
            if (layerIndex < 0) layerIndex = animator.GetLayerIndex(danceLayerName);
            if (stateHash == 0) stateHash = Animator.StringToHash(danceStateName);
            return true;
        }

        AnimationClip FindPlaceholderClip(RuntimeAnimatorController ctrl, string name)
        {
            if (ctrl == null) return null;
            var aocProbe = new AnimatorOverrideController(ctrl);
            var clips = aocProbe.animationClips;
            return clips.FirstOrDefault(c => c != null && c.name == name);
        }

        public bool PlayIndex(int index)
        {
            if (entries.Count == 0) return false;
            if (index < 0 || index >= entries.Count) return false;
            if (!EnsureAnimatorReady()) return false;

            var e = entries[index];

            if (overrideController != null) Destroy(overrideController);
            overrideController = new AnimatorOverrideController(defaultController);

            var placeholder = FindPlaceholderClip(defaultController, placeholderClipName);
            if (placeholder == null) return false;

            if (e.clip != null) overrideController[placeholderClipName] = e.clip;
            animator.runtimeAnimatorController = overrideController;

            if (layerIndex >= 0) animator.CrossFadeInFixedTime(stateHash, 0.1f, layerIndex);

            bool hasParam = false;
            foreach (var p in animator.parameters)
            {
                if (p.name == customDancingParam && p.type == AnimatorControllerParameterType.Bool)
                {
                    hasParam = true;
                    break;
                }
            }
            if (hasParam) animator.SetBool(customDancingParam, true);

            if (audioSource == null) EnsureAudioSource();
            if (audioSource != null)
            {
                audioSource.Stop();
                audioSource.clip = e.audio;
                audioSource.loop = false;
                if (audioSource.clip != null) audioSource.Play();
            }

            currentTotalSeconds = e.audio != null ? e.audio.length : (e.clip != null ? e.clip.length : 0f);
            playStartTime = Time.time;
            isPlaying = true;

            currentIndex = index;
            UpdatePlayingNowLabel(e.id);
            UpdateTimeLabels(0f, currentTotalSeconds);
            return true;
        }

        public void StopPlay()
        {
            if (!EnsureAnimatorReady())
            {
                isPlaying = false;
                UpdatePlayingNowLabel(null);
                UpdateTimeLabels(0f, 0f);
                return;
            }

            if (audioSource != null) audioSource.Stop();
            foreach (var p in animator.parameters)
                if (p.name == customDancingParam && p.type == AnimatorControllerParameterType.Bool)
                    animator.SetBool(customDancingParam, false);

            isPlaying = false;
            UpdatePlayingNowLabel(null);
            UpdateTimeLabels(0f, 0f);
        }

        void UpdatePlayingNowLabel(string nameOrNull)
        {
            if (playingNowText == null) return;
            playingNowText.text = string.IsNullOrEmpty(nameOrNull) ? defaultPlayingNowText : nameOrNull;
        }

        void UpdateTimeLabels(float elapsed, float total)
        {
            if (playTimeText != null) playTimeText.text = total <= 0f ? defaultPlayTimeText : FormatTime(elapsed);
            if (maxPlayTimeText != null) maxPlayTimeText.text = total <= 0f ? defaultMaxPlayTimeText : FormatTime(total);
        }

        string FormatTime(float seconds)
        {
            int s = Mathf.FloorToInt(seconds + 0.0001f);
            int m = s / 60;
            int r = s % 60;
            return m.ToString("00") + ":" + r.ToString("00");
        }

        void OnDestroy()
        {
            foreach (var e in entries) { try { e.bundle?.Unload(true); } catch { } }
            entries.Clear();
            byId.Clear();
        }
    }
}
