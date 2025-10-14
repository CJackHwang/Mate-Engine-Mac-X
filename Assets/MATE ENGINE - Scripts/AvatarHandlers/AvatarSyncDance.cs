using System;
using System.IO;
using System.Reflection;
using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TMPro;

namespace CustomDancePlayer
{
    public class AvatarSyncDance : MonoBehaviour
    {
        public AvatarDanceHandler handler;
        public Transform contentRoot;
        public AudioSource audioSource;
        public float pollInterval = 0.1f;
        public bool enforceMuteOnSecondary = true;
        public string syncFileName = "dance_sync.json";
        public float startLeadSeconds = 0.75f;

        int instanceIndex;
        bool isMain;
        string syncPath;
        int currentVersion;
        int lastVersion = -1;
        string lastKey = "";
        bool lastPlaying;

        int scheduledEvent = -1;
        Coroutine resumeAnimatorCo;

        Animator cachedAnimator;
        float cachedAnimatorSpeed = 1f;
        bool animatorFrozen;

        double plannedStartUtc;

        [Serializable]
        class SyncData
        {
            public string key;
            public double time;
            public bool playing;
            public int version;
            public double writeUtc;
            public bool hasStart;
            public double startUtc;
            public int ev;
        }

        void Awake()
        {
            DetectInstanceIndex();
            ResolveRefs();
            syncPath = GetSyncPath();
            try { Directory.CreateDirectory(Path.GetDirectoryName(syncPath)); } catch { }
        }

        void OnEnable()
        {
            StartCoroutine(MainLoop());
        }

        void OnDisable()
        {
            StopAllCoroutines();
            UnfreezeAnimator();
            resumeAnimatorCo = null;
        }

        IEnumerator MainLoop()
        {
            var wait = new WaitForSecondsRealtime(pollInterval);
            while (true)
            {
                ResolveRefs();
                if (isMain) TickMain();
                else TickSecondary();
                yield return wait;
            }
        }

        void TickMain()
        {
            if (handler == null) return;

            var key = GetCurrentKey();
            var playing = handler.IsPlaying;
            var t = handler.GetPlaybackTime();

            bool changed = key != lastKey || playing != lastPlaying;
            if (changed) currentVersion++;

            bool justStarted = playing && (!lastPlaying || key != lastKey);

            double startUtc = 0;
            int ev = currentVersion;

            if (justStarted)
            {
                startUtc = NowUtc() + Mathf.Max(0.05f, startLeadSeconds);
                ev = currentVersion;
                ScheduleStart(key, startUtc);
            }

            var data = new SyncData
            {
                key = key,
                time = t,
                playing = playing,
                version = currentVersion,
                writeUtc = NowUtc(),
                hasStart = justStarted || scheduledEvent == currentVersion,
                startUtc = scheduledEvent == currentVersion ? GetPlannedStartUtcFromState(startUtc) : 0,
                ev = scheduledEvent == currentVersion ? currentVersion : ev
            };
            WriteData(data);

            lastKey = key;
            lastPlaying = playing;
        }

        void TickSecondary()
        {
            var data = ReadData();
            if (data == null) return;

            if (data.hasStart && data.ev != scheduledEvent)
            {
                ScheduleStart(data.key, data.startUtc);
            }

            bool sameState = data.version == lastVersion && data.key == lastKey && data.playing == lastPlaying;

            if (sameState)
            {
                if (data.playing) AlignPredicted(data.time, data.writeUtc);
                return;
            }

            lastVersion = data.version;
            lastKey = data.key;
            lastPlaying = data.playing;

            if (enforceMuteOnSecondary) EnsureMuted();

            if (string.IsNullOrEmpty(data.key)) return;

            if (!data.playing)
            {
                TryStop();
                UnmuteLocal();
                return;
            }

            if (data.hasStart && NowUtc() + 0.0005 < data.startUtc) return;

            bool ok = TryPlayByKey(data.key);
            if (ok) AlignPredicted(data.time, data.writeUtc);
        }

        void ScheduleStart(string key, double startUtc)
        {
            if (string.IsNullOrEmpty(key)) return;

            if (!IsCurrentKey(key))
            {
                if (!TryPlayByKey(key)) return;
            }

            if (audioSource == null) audioSource = FindAudio(handler);
            if (audioSource == null || audioSource.clip == null) return;

            double dspNow = AudioSettings.dspTime;
            double delay = Math.Max(0.05, startUtc - NowUtc());
            double dspTarget = dspNow + delay;

            try { audioSource.Stop(); } catch { }
            audioSource.time = 0f;
            audioSource.PlayScheduled(dspTarget);

            CacheAnimator();
            FreezeAnimator();
            if (resumeAnimatorCo != null) StopCoroutine(resumeAnimatorCo);
            resumeAnimatorCo = StartCoroutine(ResumeAnimatorAt(startUtc));

            plannedStartUtc = startUtc;
            scheduledEvent = currentVersion;
        }

        IEnumerator ResumeAnimatorAt(double utc)
        {
            while (NowUtc() + 0.0005 < utc) yield return null;
            UnfreezeAnimator();
            resumeAnimatorCo = null;
        }

        void AlignPredicted(double mainTime, double mainWriteUtc)
        {
            if (handler == null) return;
            double age = NowUtc() - mainWriteUtc;
            double target = Math.Max(0, mainTime + age);
            AlignTime(target);
        }

        void AlignTime(double target)
        {
            if (handler == null) return;
            bool done = TrySeekOnHandler((float)target);
            if (done) return;
            if (audioSource == null) audioSource = FindAudio(handler);
            if (audioSource != null && audioSource.clip != null)
            {
                float dt = Mathf.Abs(audioSource.time - (float)target);
                if (dt > 0.02f) audioSource.time = Mathf.Clamp((float)target, 0f, audioSource.clip.length - 0.01f);
            }
        }

        bool TryPlayByKey(string key)
        {
            if (handler == null) return false;

            var m = FindMethod(handler, new[] { "PlayByTitle", "PlaySongByTitle", "SelectAndPlayByTitle", "PlayByName" }, new[] { typeof(string) });
            if (m != null) { m.Invoke(handler, new object[] { key }); return true; }

            if (contentRoot == null) contentRoot = GetContentRoot(handler);
            if (contentRoot == null) return false;

            for (int i = 0; i < contentRoot.childCount; i++)
            {
                var item = contentRoot.GetChild(i);
                var title = Normalize(ExtractTitle(item));
                if (title == Normalize(key))
                {
                    var btn = item.GetComponentInChildren<Button>(true);
                    if (btn != null) { btn.onClick.Invoke(); return true; }
                    var evsys = EventSystem.current;
                    if (evsys != null) ExecuteEvents.Execute<IPointerClickHandler>(item.gameObject, new PointerEventData(evsys), ExecuteEvents.pointerClickHandler);
                    return true;
                }
            }
            return false;
        }

        bool IsCurrentKey(string key)
        {
            return Normalize(GetCurrentKey()) == Normalize(key);
        }

        void TryStop()
        {
            if (handler == null) return;
            var m0 = FindMethod(handler, new[] { "StopAndUnload" }, Type.EmptyTypes);
            if (m0 != null) { m0.Invoke(handler, null); return; }

            var m = FindMethod(handler, new[] { "Stop", "StopDance", "ForceStop" }, Type.EmptyTypes);
            if (m != null) { m.Invoke(handler, null); return; }

            if (audioSource == null) audioSource = FindAudio(handler);
            if (audioSource != null) audioSource.Stop();
        }

        void EnsureMuted()
        {
            if (audioSource == null) audioSource = FindAudio(handler);
            if (audioSource != null)
            {
                audioSource.mute = true;
                audioSource.volume = 0f;
            }
        }

        void UnmuteLocal()
        {
            if (audioSource == null) audioSource = FindAudio(handler);
            if (audioSource != null)
            {
                audioSource.mute = false;
                audioSource.volume = 1f;
            }
        }

        string GetCurrentKey()
        {
            var playingNow = FindByExactName<TMP_Text>(handler?.transform, "PlayingNow");
            if (playingNow != null && !string.IsNullOrEmpty(playingNow.text)) return playingNow.text;
            var playingNowFB = FindByExactName<Text>(handler?.transform, "Playing Now Fallback");
            if (playingNowFB != null && !string.IsNullOrEmpty(playingNowFB.text)) return playingNowFB.text;

            var clip = handler != null ? handler.GetCurrentClip() : null;
            if (clip != null && !string.IsNullOrEmpty(clip.name)) return clip.name;

            if (contentRoot == null) contentRoot = GetContentRoot(handler);
            if (contentRoot != null)
            {
                for (int i = 0; i < contentRoot.childCount; i++)
                {
                    var t = contentRoot.GetChild(i);
                    if (t.gameObject.activeInHierarchy) return ExtractTitle(t);
                }
            }
            return "";
        }

        AudioSource FindAudio(AvatarDanceHandler h)
        {
            if (h == null) return null;
            var a = h.GetComponentInChildren<AudioSource>(true);
            if (a != null) return a;
            return FindFirstObjectByType<AudioSource>();
        }

        Transform GetContentRoot(AvatarDanceHandler h)
        {
            if (h == null) return null;
            var f = h.GetType().GetField("contentObject", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (f != null) return f.GetValue(h) as Transform;
            var p = h.GetType().GetProperty("contentObject", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (p != null) return p.GetValue(h) as Transform;
            return null;
        }

        bool TrySeekOnHandler(float t)
        {
            var m = FindMethod(handler, new[] { "Seek", "SetTime", "SetPlaybackTime", "JumpTo", "SeekTo" }, new[] { typeof(float) });
            if (m == null) return false;
            m.Invoke(handler, new object[] { t });
            return true;
        }

        MethodInfo FindMethod(object obj, string[] names, Type[] args)
        {
            if (obj == null) return null;
            var flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
            foreach (var n in names)
            {
                var mi = obj.GetType().GetMethod(n, flags, null, args, null);
                if (mi != null) return mi;
            }
            return null;
        }

        string ExtractTitle(Transform item)
        {
            var tf = FindByExactName<Text>(item, "TitleFallback");
            if (tf != null && !string.IsNullOrEmpty(tf.text)) return tf.text;
            var tt = FindByExactName<TMP_Text>(item, "Title");
            if (tt != null && !string.IsNullOrEmpty(tt.text)) return tt.text;
            var tf2 = FindByNameContains<Text>(item, "titlefallback");
            if (tf2 != null && !string.IsNullOrEmpty(tf2.text)) return tf2.text;
            var tt2 = FindByNameContains<TMP_Text>(item, "title");
            if (tt2 != null && !string.IsNullOrEmpty(tt2.text)) return tt2.text;
            var anyTmp = item.GetComponentInChildren<TMP_Text>(true);
            if (anyTmp != null && !string.IsNullOrEmpty(anyTmp.text)) return anyTmp.text;
            var anyTxt = item.GetComponentInChildren<Text>(true);
            if (anyTxt != null && !string.IsNullOrEmpty(anyTxt.text)) return anyTxt.text;
            return "";
        }

        T FindByExactName<T>(Transform root, string name) where T : Component
        {
            if (root == null) return null;
            var arr = root.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < arr.Length; i++)
                if (arr[i].name == name) return arr[i].GetComponent<T>();
            return null;
        }

        T FindByNameContains<T>(Transform root, string partLower) where T : Component
        {
            if (root == null) return null;
            var arr = root.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < arr.Length; i++)
                if (arr[i].name.ToLowerInvariant().Contains(partLower)) return arr[i].GetComponent<T>();
            return null;
        }

        string Normalize(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            s = s.Trim();
            s = s.Replace("\u200B", "").Replace("\u200C", "").Replace("\u200D", "").Replace("\uFEFF", "");
            return s.ToLowerInvariant();
        }

        double NowUtc()
        {
            var epoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            return (DateTime.UtcNow - epoch).TotalSeconds;
        }

        double GetPlannedStartUtcFromState(double fallback) { return plannedStartUtc != 0 ? plannedStartUtc : fallback; }

        void DetectInstanceIndex()
        {
            instanceIndex = 0;
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i].Equals("--instance", StringComparison.OrdinalIgnoreCase))
                    int.TryParse(args[i + 1], out instanceIndex);
            instanceIndex = Mathf.Max(0, instanceIndex);
            isMain = instanceIndex == 0;
        }

        string GetSyncPath()
        {
            var root = Path.Combine(Application.persistentDataPath, "Sync");
            return Path.Combine(root, syncFileName);
        }

        void WriteData(SyncData data)
        {
            try
            {
                string dir = Path.GetDirectoryName(syncPath);
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                string tmp = syncPath + ".tmp";
                File.WriteAllText(tmp, JsonUtility.ToJson(data));
                if (File.Exists(syncPath))
                {
                    string bak = syncPath + ".bak";
                    try { File.Copy(syncPath, bak, true); } catch { }
                    try { File.Replace(tmp, syncPath, bak); }
                    catch { try { File.Copy(tmp, syncPath, true); File.Delete(tmp); } catch { } }
                }
                else
                {
                    try { File.Move(tmp, syncPath); } catch { try { File.Copy(tmp, syncPath, true); File.Delete(tmp); } catch { } }
                }
            }
            catch { }
        }

        SyncData ReadData()
        {
            for (int attempt = 0; attempt < 3; attempt++)
            {
                try
                {
                    if (!File.Exists(syncPath)) return null;
                    string json = File.ReadAllText(syncPath);
                    if (string.IsNullOrWhiteSpace(json)) { System.Threading.Thread.Sleep(5); continue; }
                    var obj = JsonUtility.FromJson<SyncData>(json);
                    if (obj != null) return obj;
                }
                catch { System.Threading.Thread.Sleep(5); }
            }
            return null;
        }

        void ResolveRefs()
        {
            if (handler == null) handler = FindFirstObjectByType<AvatarDanceHandler>();
            if (contentRoot == null && handler != null) contentRoot = GetContentRoot(handler);
            if (audioSource == null && handler != null) audioSource = FindAudio(handler);
        }

        void CacheAnimator()
        {
            if (cachedAnimator != null) return;
            if (handler == null) return;
            var fi = handler.GetType().GetField("animator", BindingFlags.NonPublic | BindingFlags.Instance);
            if (fi != null) cachedAnimator = fi.GetValue(handler) as Animator;
            if (cachedAnimator == null) cachedAnimator = FindFirstObjectByType<Animator>();
        }

        void FreezeAnimator()
        {
            if (animatorFrozen) return;
            if (cachedAnimator == null) return;
            cachedAnimatorSpeed = cachedAnimator.speed;
            cachedAnimator.speed = 0f;
            animatorFrozen = true;
        }

        void UnfreezeAnimator()
        {
            if (!animatorFrozen) return;
            if (cachedAnimator != null) cachedAnimator.speed = cachedAnimatorSpeed;
            animatorFrozen = false;
        }
    }
}
