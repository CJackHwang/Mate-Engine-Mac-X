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
        public TMP_Text authorText;

        [Header("UI Fallback")]
        public bool useFallbackFont = false;
        public Text playingNowFallbackText;
        public Text authorFallbackText;

        [Header("Sound")]
        public AudioSource audioSource;

        [Header("Misc Settings")]
        public string danceLayerName = "Dance Layer";
        public string danceStateName = "Custom Dance";
        public string placeholderClipName = "CUSTOM_DANCE";
        public string customDancingParam = "isCustomDancing";
        AnimationClip placeholderClipCached;
        Coroutine playRoutine;
        bool autoNextScheduled;

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
        string defaultAuthorText = "";
        const string unknownAuthorLabel = "Author: Unknown";

        float currentTotalSeconds = 0f;
        float playStartTime = 0f;
        bool isPlaying = false;

        HashSet<string> mmdBlendShapeNames = new HashSet<string>(new[]{
        "まばたき","ウィンク","ウィンク２","ウィンク右","笑い","なごみ","びっくり","ジト目","瞳小","キリッ","星目","はぁと","はちゅ目","はっ","ハイライト消し","怒るいい子！",
        "あ","い","う","え","お","えーん","ん","▲","口","ω口","はんっ！","にっこり","にやり","にやり２","べろっ","てへぺろ","口角上げ","口角下げ","口横広げ","真面目","上下","困る","怒り","照れ","涙","すぼめ"
        }, StringComparer.Ordinal);

        class DanceEntry
        {
            public string id;
            public string path;
            public string bundlePath;
            public AnimationClip clip;
            public AudioClip audio;
            public AssetBundle bundle;
            public bool fromME;
            public string extractedDir;
            public string author;
        }

        [Serializable]
        class DanceMeta
        {
            public string songName;
            public string songAuthor;
            public string mmdAuthor;
            public float songLength;
            public string placeholderClipName;
        }

        readonly List<DanceEntry> entries = new();
        readonly Dictionary<string, DanceEntry> byId = new(StringComparer.OrdinalIgnoreCase);
        DanceEntry loadedEntry = null;

        void Awake()
        {
            if (!useFallbackFont)
            {
                if (playingNowText != null) defaultPlayingNowText = playingNowText.text;
                if (authorText != null) defaultAuthorText = string.IsNullOrWhiteSpace(authorText.text) ? unknownAuthorLabel : authorText.text;
            }
            else
            {
                if (playingNowFallbackText != null) defaultPlayingNowText = playingNowFallbackText.text;
                if (authorFallbackText != null) defaultAuthorText = string.IsNullOrWhiteSpace(authorFallbackText.text) ? unknownAuthorLabel : authorFallbackText.text;
            }
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
            UpdateAuthorLabel(null);
            UpdateTimeLabels(0f, 0f);
        }

        void Update()
        {
            RefreshAnimatorIfChanged();

            bool dancingOn = animator != null && HasBool(customDancingParam) && animator.GetBool(customDancingParam);
            if (isPlaying && !dancingOn) StopAndUnload();

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
                else elapsed = Mathf.Clamp(Time.time - playStartTime, 0f, total);
            }

            if (progressSlider != null && total > 0f) progressSlider.value = Mathf.Clamp01(elapsed / total);
            else if (progressSlider != null) progressSlider.value = 0f;

            UpdateTimeLabels(elapsed, total);
            if (isPlaying && total > 0f)
            {
                bool audioEnded = audioSource != null && audioSource.clip != null && !audioSource.loop && !audioSource.isPlaying;
                bool timeReached = elapsed >= total - 0.05f;
                if (audioEnded || timeReached) TryAutoNext();
            }
        }

        bool IsMMDName(string n)
        {
            if (string.IsNullOrEmpty(n)) return false;
            if (mmdBlendShapeNames.Contains(n)) return true;
            if (n.StartsWith("mmd_", StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        void ResetMMDBlendShapes()
        {
            if (animator == null) return;
            var root = animator.gameObject;
            var renderers = root.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            for (int r = 0; r < renderers.Length; r++)
            {
                var smr = renderers[r];
                var mesh = smr.sharedMesh;
                if (mesh == null) continue;
                int count = mesh.blendShapeCount;
                for (int i = 0; i < count; i++)
                {
                    string bs = mesh.GetBlendShapeName(i);
                    if (IsMMDName(bs)) smr.SetBlendShapeWeight(i, 0f);
                }
            }
        }

        bool HasBool(string name)
        {
            var ps = animator.parameters;
            for (int i = 0; i < ps.Length; i++)
                if (ps[i].type == AnimatorControllerParameterType.Bool && ps[i].name == name)
                    return true;
            return false;
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
            UnloadEntry(loadedEntry);
            loadedEntry = null;

            foreach (var e in entries) { try { e.bundle?.Unload(true); } catch { } e.bundle = null; e.clip = null; e.audio = null; }
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

            var e = new DanceEntry
            {
                id = id,
                path = path,
                bundlePath = path,
                clip = null,
                audio = null,
                bundle = null,
                fromME = false,
                extractedDir = null,
                author = unknownAuthorLabel
            };
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

            string metaAuthor = unknownAuthorLabel;
            string metaPath = Path.Combine(dst, "dance_meta.json");
            if (File.Exists(metaPath))
            {
                try
                {
                    var json = File.ReadAllText(metaPath);
                    var meta = JsonUtility.FromJson<DanceMeta>(json);
                    string cand = null;
                    if (!string.IsNullOrWhiteSpace(meta.songAuthor)) cand = meta.songAuthor;
                    else if (!string.IsNullOrWhiteSpace(meta.mmdAuthor)) cand = meta.mmdAuthor;
                    if (!string.IsNullOrWhiteSpace(cand)) metaAuthor = "Author: " + cand;
                }
                catch { }
            }

            var e = new DanceEntry
            {
                id = id,
                path = mePath,
                bundlePath = bundlePath,
                clip = null,
                audio = null,
                bundle = null,
                fromME = true,
                extractedDir = dst,
                author = metaAuthor
            };
            entries.Add(e);
            byId[id] = e;
        }

        void BuildListUI()
        {
            if (contentObject == null || prefab == null) return;

            for (int i = contentObject.childCount - 1; i >= 0; i--)
                Destroy(contentObject.GetChild(i).gameObject);

            for (int i = 0; i < entries.Count; i++)
            {
                int idx = i;
                var e = entries[i];

                var go = Instantiate(prefab, contentObject);

                var titleTMP = FindChildByName<TMP_Text>(go.transform, "Title");
                var authorTMP = FindChildByName<TMP_Text>(go.transform, "Author");
                var titleFB = FindChildByName<Text>(go.transform, "TitleFallback");
                var authorFB = FindChildByName<Text>(go.transform, "AuthorFallback");

                if (useFallbackFont && (titleFB != null || authorFB != null))
                {
                    if (titleFB != null) { titleFB.text = e.id; titleFB.gameObject.SetActive(true); }
                    if (authorFB != null) { authorFB.text = string.IsNullOrWhiteSpace(e.author) ? unknownAuthorLabel : e.author; authorFB.gameObject.SetActive(true); }
                    if (titleTMP != null) titleTMP.gameObject.SetActive(false);
                    if (authorTMP != null) authorTMP.gameObject.SetActive(false);
                }
                else
                {
                    if (titleTMP != null) { titleTMP.text = e.id; titleTMP.gameObject.SetActive(true); }
                    if (authorTMP != null) { authorTMP.text = string.IsNullOrWhiteSpace(e.author) ? unknownAuthorLabel : e.author; authorTMP.gameObject.SetActive(true); }
                    if (titleFB != null) titleFB.gameObject.SetActive(false);
                    if (authorFB != null) authorFB.gameObject.SetActive(false);
                }

                Button btn = FindChildByName<Button>(go.transform, "Button");
                if (btn == null)
                {
                    var allButtons = go.GetComponentsInChildren<Button>(true);
                    if (allButtons != null && allButtons.Length > 0) btn = allButtons[0];
                }
                if (btn != null) btn.onClick.AddListener(() => { currentIndex = idx; PlayIndex(idx); });
            }
        }

        T FindChildByName<T>(Transform root, string name) where T : Component
        {
            if (root == null || string.IsNullOrEmpty(name)) return null;
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                if (t.name == name) return t.GetComponent<T>();
            return null;
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
            if (defaultController == null) defaultController = animator.runtimeAnimatorController;
            if (layerIndex < 0) layerIndex = animator.GetLayerIndex(danceLayerName);
            if (stateHash == 0) stateHash = Animator.StringToHash(danceStateName);
            if (placeholderClipCached == null) placeholderClipCached = FindPlaceholderClip(defaultController, placeholderClipName);
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

            if (playRoutine != null) StopCoroutine(playRoutine);
            playRoutine = StartCoroutine(PlayFlow(index));
            return true;
        }

        IEnumerator PlayFlow(int index)
        {
            var prev = loadedEntry;

            StopImmediateForRestart();
            ResetMMDBlendShapes();
            yield return null;

            var e = entries[index];

            if (e.bundle == null)
            {
                string bp = string.IsNullOrEmpty(e.bundlePath) ? e.path : e.bundlePath;
                e.bundle = AssetBundle.LoadFromFile(bp);
                if (e.bundle == null) yield break;
            }
            if (e.clip == null) e.clip = e.bundle.LoadAllAssets<AnimationClip>().FirstOrDefault();
            if (e.audio == null) e.audio = e.bundle.LoadAllAssets<AudioClip>().FirstOrDefault();

            if (overrideController != null) Destroy(overrideController);
            overrideController = new AnimatorOverrideController(defaultController);

            if (placeholderClipCached == null) placeholderClipCached = FindPlaceholderClip(defaultController, placeholderClipName);
            if (placeholderClipCached == null) yield break;

            overrideController[placeholderClipName] = e.clip != null ? e.clip : placeholderClipCached;
            animator.runtimeAnimatorController = overrideController;

            bool hasParam = false;
            for (int i = 0; i < animator.parameters.Length; i++)
                if (animator.parameters[i].name == customDancingParam && animator.parameters[i].type == AnimatorControllerParameterType.Bool)
                { hasParam = true; break; }
            if (hasParam) animator.SetBool(customDancingParam, true);

            animator.Play(stateHash, layerIndex, 0f);

            if (audioSource == null) EnsureAudioSource();
            if (audioSource != null)
            {
                audioSource.Stop();
                audioSource.clip = e.audio;
                audioSource.time = 0f;
                audioSource.loop = false;
                if (audioSource.clip != null) audioSource.Play();
            }

            currentTotalSeconds = e.audio != null ? e.audio.length : (e.clip != null ? e.clip.length : 0f);
            playStartTime = Time.time;
            isPlaying = true;

            currentIndex = index;
            loadedEntry = e;
            UpdatePlayingNowLabel(e.id);
            UpdateAuthorLabel(e.author);
            UpdateTimeLabels(0f, currentTotalSeconds);

            if (prev != null && prev != e) UnloadEntry(prev);
            playRoutine = null;
        }

        void StopAndUnload()
        {
            if (audioSource != null)
            {
                try { audioSource.Stop(); } catch { }
                try { if (audioSource.clip != null) audioSource.clip.UnloadAudioData(); } catch { }
                audioSource.clip = null;
            }

            if (animator != null)
            {
                if (overrideController != null && placeholderClipCached != null)
                    overrideController[placeholderClipName] = placeholderClipCached;

                for (int i = 0; i < animator.parameters.Length; i++)
                    if (animator.parameters[i].type == AnimatorControllerParameterType.Bool &&
                        animator.parameters[i].name == customDancingParam)
                        animator.SetBool(customDancingParam, false);
            }

            isPlaying = false;
            UpdatePlayingNowLabel(null);
            UpdateAuthorLabel(null);
            UpdateTimeLabels(0f, 0f);
            StartCoroutine(UnloadUnusedAssetsRoutine());
        }

        void StopImmediateForRestart()
        {
            ResetMMDBlendShapes();
            if (audioSource != null)
            {
                try { audioSource.Stop(); } catch { }
                try { if (audioSource.clip != null) audioSource.clip.UnloadAudioData(); } catch { }
                audioSource.clip = null;
            }
            if (animator != null)
            {
                if (overrideController != null && placeholderClipCached != null)
                    overrideController[placeholderClipName] = placeholderClipCached;
                for (int i = 0; i < animator.parameters.Length; i++)
                    if (animator.parameters[i].type == AnimatorControllerParameterType.Bool &&
                        animator.parameters[i].name == customDancingParam)
                        animator.SetBool(customDancingParam, false);
            }
            isPlaying = false;
        }

        public void StopPlay()
        {
            ResetMMDBlendShapes();
            if (!EnsureAnimatorReady())
            {
                isPlaying = false;
                UpdatePlayingNowLabel(null);
                UpdateAuthorLabel(null);
                UpdateTimeLabels(0f, 0f);
                return;
            }
            StopAndUnload();
        }

        void UnloadEntry(DanceEntry e)
        {
            if (e == null) return;
            try { if (e.bundle != null) e.bundle.Unload(true); } catch { }
            e.bundle = null;
            e.clip = null;
            e.audio = null;
        }

        void UpdatePlayingNowLabel(string nameOrNull)
        {
            if (useFallbackFont && playingNowFallbackText != null)
            {
                playingNowFallbackText.text = string.IsNullOrEmpty(nameOrNull) ? defaultPlayingNowText : nameOrNull;
                if (playingNowText != null) playingNowText.text = "";
                return;
            }
            if (playingNowText != null) playingNowText.text = string.IsNullOrEmpty(nameOrNull) ? defaultPlayingNowText : nameOrNull;
        }

        void UpdateAuthorLabel(string authorOrNull)
        {
            if (useFallbackFont && authorFallbackText != null)
            {
                if (string.IsNullOrWhiteSpace(authorOrNull))
                {
                    authorFallbackText.text = string.IsNullOrWhiteSpace(defaultAuthorText) ? unknownAuthorLabel : defaultAuthorText;
                }
                else authorFallbackText.text = authorOrNull;
                if (authorText != null) authorText.text = "";
                return;
            }

            if (authorText == null) return;
            if (string.IsNullOrWhiteSpace(authorOrNull))
            {
                authorText.text = string.IsNullOrWhiteSpace(defaultAuthorText) ? unknownAuthorLabel : defaultAuthorText;
            }
            else authorText.text = authorOrNull;
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
            UnloadEntry(loadedEntry);
            foreach (var e in entries) { try { e.bundle?.Unload(true); } catch { } }
            entries.Clear();
            byId.Clear();
        }

        IEnumerator UnloadUnusedAssetsRoutine()
        {
            yield return Resources.UnloadUnusedAssets();
        }

        void TryAutoNext()
        {
            if (autoNextScheduled) return;
            autoNextScheduled = true;
            StartCoroutine(AutoNextCo());
        }

        IEnumerator AutoNextCo()
        {
            yield return null;
            PlayNext();
            autoNextScheduled = false;
        }
    }
}
