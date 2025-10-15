using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TMPro;

namespace CustomDancePlayer
{
    public class AvatarDanceSync : MonoBehaviour
    {
        [Serializable]
        class BusCmd
        {
            public int v;
            public string cmd;
            public string sid;
            public string title;
            public int index;
            public double atUtc;
            public double writeUtc;
        }

        class PreArmHook : MonoBehaviour, IPointerDownHandler
        {
            public AvatarDanceSync owner;
            public string intent;
            public string sid;
            public int index;
            public string title;
            public void OnPointerDown(PointerEventData e)
            {
                var btn = GetComponent<Button>();
                owner?.BeginLeaderAction(intent, sid, index, title, btn);
            }
        }

        public AvatarDanceHandler handler;
        public string fileName = "avatar_dance_play_bus.json";
        public float pollInterval = 0.05f;
        public double leadSeconds = 1.5;

        string path;
        int lastSeenV = -1;
        Coroutine scheduledCo;
        Mutex leaderMutex;
        bool isLeader;

        Button mainPlayBtn;
        Button stopBtn;
        Button nextBtn;
        Button prevBtn;
        Transform contentRoot;
        IList entriesList;
        FieldInfo entriesFi;
        FieldInfo entryStableIdFi;

        readonly HashSet<Button> wiredButtons = new HashSet<Button>();
        readonly List<Button> tempDisabled = new List<Button>();

        bool guardActive;
        double guardUntilUtc;
        AudioSource audioSource;
        Animator animator;
        float animatorPrevSpeed = 1f;

        AvatarDancePlayerUtils utils;
        Slider volumeSlider;
        float storedSliderValue = -1f;
        float storedAudioVolume = -1f;
        bool followerMuted;

        void Awake()
        {
            if (handler == null) handler = GetComponent<AvatarDanceHandler>();
            var dir = Path.Combine(Application.persistentDataPath, "Sync");
            try { Directory.CreateDirectory(dir); } catch { }
            path = Path.Combine(dir, fileName);
            TryAcquireLeader();
            ResolveRefs();
        }

        void OnEnable()
        {
            StartCoroutine(Poll());
            StartCoroutine(WireLoop());
        }

        void OnDisable()
        {
            if (scheduledCo != null) { StopCoroutine(scheduledCo); scheduledCo = null; }
            StopAllCoroutines();
            ReleaseLeader();
            UnfreezeAnimator();
            guardActive = false;
            ReenableAll();
        }

        void Update()
        {
            if (!guardActive) return;
            if (UtcNow() < guardUntilUtc) EnforceHold();
            else guardActive = false;
        }

        IEnumerator Poll()
        {
            var wait = new WaitForSecondsRealtime(pollInterval);
            while (true)
            {
                if (!isLeader)
                {
                    var d = Read();
                    if (d != null && d.v > lastSeenV)
                    {
                        lastSeenV = d.v;
                        if (scheduledCo != null) { StopCoroutine(scheduledCo); scheduledCo = null; }
                        if (d.cmd == "PlayCurrentOrFirst") { MuteFollower(); ScheduleRemote(() => TryPlayCurrentOrFirst(), d.atUtc); }
                        else if (d.cmd == "PlayByStableId") { MuteFollower(); ScheduleRemote(() => TryPlayByStableIdOrFallback(d.sid, d.index, d.title), d.atUtc); }
                        else if (d.cmd == "StopPlay") { ScheduleRemote(() => { TryStopPlay(); UnmuteFollower(); }, d.atUtc); }
                        else if (d.cmd == "PlayNext") { MuteFollower(); ScheduleRemote(() => TryPlayNext(), d.atUtc); }
                        else if (d.cmd == "PlayPrev") { MuteFollower(); ScheduleRemote(() => TryPlayPrev(), d.atUtc); }
                    }
                }
                yield return wait;
            }
        }

        IEnumerator WireLoop()
        {
            var wait = new WaitForSecondsRealtime(0.35f);
            while (true)
            {
                ResolveRefs();
                WireMainControls();
                WireListButtons();
                yield return wait;
            }
        }

        void ResolveRefs()
        {
            if (handler == null) return;

            if (mainPlayBtn == null)
            {
                var fi = handler.GetType().GetField("playButton", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                mainPlayBtn = fi != null ? fi.GetValue(handler) as Button : null;
            }
            if (stopBtn == null)
            {
                var fi = handler.GetType().GetField("stopButton", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                stopBtn = fi != null ? fi.GetValue(handler) as Button : null;
            }
            if (nextBtn == null)
            {
                var fi = handler.GetType().GetField("nextButton", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                nextBtn = fi != null ? fi.GetValue(handler) as Button : null;
            }
            if (prevBtn == null)
            {
                var fi = handler.GetType().GetField("prevButton", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                prevBtn = fi != null ? fi.GetValue(handler) as Button : null;
            }
            if (contentRoot == null)
            {
                var fi = handler.GetType().GetField("contentObject", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                contentRoot = fi != null ? fi.GetValue(handler) as Transform : null;
            }
            if (entriesFi == null)
                entriesFi = handler.GetType().GetField("entries", BindingFlags.Instance | BindingFlags.NonPublic);
            entriesList = entriesFi != null ? entriesFi.GetValue(handler) as IList : null;

            if (audioSource == null) audioSource = handler.GetComponentInChildren<AudioSource>(true);
            if (animator == null)
            {
                var fiA = handler.GetType().GetField("animator", BindingFlags.NonPublic | BindingFlags.Instance);
                animator = fiA != null ? fiA.GetValue(handler) as Animator : null;
                if (animator == null) animator = FindFirstObjectByType<Animator>();
            }

            if (utils == null) utils = handler.GetComponentInChildren<AvatarDancePlayerUtils>(true);
            if (utils != null)
            {
                if (utils.danceAudioSource != null) audioSource = utils.danceAudioSource;
                if (volumeSlider == null) volumeSlider = utils.volumeSlider;
            }
            if (volumeSlider == null) volumeSlider = handler.GetComponentInChildren<Slider>(true);
        }

        void WireMainControls()
        {
            if (mainPlayBtn != null && !wiredButtons.Contains(mainPlayBtn))
            {
                AddPreArm(mainPlayBtn, "play", null, -1, null);
                wiredButtons.Add(mainPlayBtn);
            }
            if (stopBtn != null && !wiredButtons.Contains(stopBtn))
            {
                AddPreArm(stopBtn, "stop", null, -1, null);
                wiredButtons.Add(stopBtn);
            }
            if (nextBtn != null && !wiredButtons.Contains(nextBtn))
            {
                AddPreArm(nextBtn, "next", null, -1, null);
                wiredButtons.Add(nextBtn);
            }
            if (prevBtn != null && !wiredButtons.Contains(prevBtn))
            {
                AddPreArm(prevBtn, "prev", null, -1, null);
                wiredButtons.Add(prevBtn);
            }
        }

        void WireListButtons()
        {
            if (contentRoot == null || entriesList == null) return;

            int n = Mathf.Min(contentRoot.childCount, entriesList.Count);
            for (int i = 0; i < n; i++)
            {
                var tr = contentRoot.GetChild(i);
                var btn = tr.GetComponentInChildren<Button>(true);
                if (btn == null) continue;
                if (wiredButtons.Contains(btn)) continue;

                if (entryStableIdFi == null)
                    entryStableIdFi = entriesList[i].GetType().GetField("stableId", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

                string sid = entryStableIdFi != null ? (entryStableIdFi.GetValue(entriesList[i]) as string) : null;
                string title = ExtractTitle(tr);

                AddPreArm(btn, "playByItem", sid, i, title);
                wiredButtons.Add(btn);
            }
        }

        void AddPreArm(Button b, string intent, string sid, int index, string title)
        {
            var hook = b.gameObject.GetComponent<PreArmHook>();
            if (hook == null) hook = b.gameObject.AddComponent<PreArmHook>();
            hook.owner = this;
            hook.intent = intent;
            hook.sid = sid;
            hook.index = index;
            hook.title = title;
        }

        public void BeginLeaderAction(string intent, string sid, int idx, string title, Button srcBtn)
        {
            if (!isLeader) return;

            double at = intent == "stop" ? UtcNow() : UtcNow() + leadSeconds;

            if (intent != "stop")
            {
                guardActive = true;
                guardUntilUtc = at;
                EnforceHold();
            }

            if (srcBtn != null)
            {
                srcBtn.interactable = false;
                tempDisabled.Add(srcBtn);
            }

            if (intent == "play")
            {
                ScheduleLocal(() => TryPlayCurrentOrFirst(), at);
                Broadcast("PlayCurrentOrFirst", null, -1, null, at);
            }
            else if (intent == "playByItem")
            {
                ScheduleLocal(() => TryPlayByStableIdOrFallback(sid, idx, title), at);
                Broadcast("PlayByStableId", sid, idx, title, at);
            }
            else if (intent == "next")
            {
                ScheduleLocal(() => TryPlayNext(), at);
                Broadcast("PlayNext", null, -1, null, at);
            }
            else if (intent == "prev")
            {
                ScheduleLocal(() => TryPlayPrev(), at);
                Broadcast("PlayPrev", null, -1, null, at);
            }
            else if (intent == "stop")
            {
                ScheduleLocal(() => { TryStopPlay(); }, at);
                Broadcast("StopPlay", null, -1, null, at);
            }
        }

        void EnforceHold()
        {
            if (handler != null)
            {
                var stopMi = handler.GetType().GetMethod("StopPlay", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (stopMi != null) stopMi.Invoke(handler, null);
            }
            if (audioSource != null)
            {
                try { audioSource.Stop(); } catch { }
                audioSource.time = 0f;
            }
            FreezeAnimator();
        }

        void FreezeAnimator()
        {
            if (animator == null) return;
            animatorPrevSpeed = animator.speed;
            animator.speed = 0f;
        }

        void UnfreezeAnimator()
        {
            if (animator == null) return;
            animator.speed = animatorPrevSpeed;
        }

        void ReenableAll()
        {
            for (int i = 0; i < tempDisabled.Count; i++)
                if (tempDisabled[i] != null) tempDisabled[i].interactable = true;
            tempDisabled.Clear();
        }

        void MuteFollower()
        {
            if (isLeader) return;
            ResolveRefs();
            if (!followerMuted)
            {
                if (volumeSlider != null)
                {
                    storedSliderValue = volumeSlider.value;
                    volumeSlider.value = 0f;
                }
                if (audioSource != null)
                {
                    storedAudioVolume = audioSource.volume;
                    audioSource.volume = 0f;
                }
                followerMuted = true;
            }
        }

        void UnmuteFollower()
        {
            if (isLeader) return;
            ResolveRefs();
            if (followerMuted)
            {
                if (volumeSlider != null && storedSliderValue >= 0f) volumeSlider.value = storedSliderValue;
                if (audioSource != null && storedAudioVolume >= 0f) audioSource.volume = storedAudioVolume;
                followerMuted = false;
            }
        }

        void ScheduleLocal(Action act, double atUtc)
        {
            double wait = Math.Max(0.0, atUtc - UtcNow());
            if (scheduledCo != null) StopCoroutine(scheduledCo);
            scheduledCo = StartCoroutine(Co(wait, act));
        }

        void ScheduleRemote(Action act, double atUtc)
        {
            double wait = Math.Max(0.0, atUtc - UtcNow());
            if (scheduledCo != null) StopCoroutine(scheduledCo);
            scheduledCo = StartCoroutine(Co(wait, act));
        }

        IEnumerator Co(double wait, Action act)
        {
            if (wait > 0) yield return new WaitForSecondsRealtime((float)wait);
            act();
            UnfreezeAnimator();
            guardActive = false;
            ReenableAll();
            scheduledCo = null;
        }

        void TryPlayCurrentOrFirst()
        {
            if (handler == null) return;
            var mi = handler.GetType().GetMethod("TryPlayCurrentOrFirst", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (mi != null) mi.Invoke(handler, null);
        }

        void TryStopPlay()
        {
            if (handler == null) return;
            var mi = handler.GetType().GetMethod("StopPlay", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (mi != null) mi.Invoke(handler, null);
        }

        void TryPlayNext()
        {
            if (handler == null) return;
            var mi = handler.GetType().GetMethod("PlayNext", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (mi != null) mi.Invoke(handler, null);
        }

        void TryPlayPrev()
        {
            if (handler == null) return;
            var mi = handler.GetType().GetMethod("PlayPrev", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (mi != null) mi.Invoke(handler, null);
        }

        void TryPlayByStableIdOrFallback(string sid, int idx, string title)
        {
            if (handler == null)
            {
                TryPlayCurrentOrFirst();
                return;
            }

            if (!string.IsNullOrEmpty(sid))
            {
                var mSid = handler.GetType().GetMethod("PlayByStableId", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, new[] { typeof(string) }, null);
                if (mSid != null)
                {
                    var ok = mSid.Invoke(handler, new object[] { sid });
                    if (ok is bool b && b) return;
                }
            }

            if (idx >= 0)
            {
                var mIdx = handler.GetType().GetMethod("PlayIndex", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, new[] { typeof(int) }, null);
                if (mIdx != null) { mIdx.Invoke(handler, new object[] { idx }); return; }
            }

            if (!string.IsNullOrEmpty(title))
            {
                var mTitle = FindByTitleMethod(handler);
                if (mTitle != null) { mTitle.Invoke(handler, new object[] { title }); return; }
            }

            TryPlayCurrentOrFirst();
        }

        MethodInfo FindByTitleMethod(object target)
        {
            var t = target.GetType();
            var cands = new[] { "PlayByTitle", "PlaySongByTitle", "SelectAndPlayByTitle", "PlayByName" };
            for (int i = 0; i < cands.Length; i++)
            {
                var mi = t.GetMethod(cands[i], BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, new[] { typeof(string) }, null);
                if (mi != null) return mi;
            }
            return null;
        }

        string ExtractTitle(Transform item)
        {
            var tt = item.GetComponentInChildren<TMP_Text>(true);
            if (tt != null && !string.IsNullOrEmpty(tt.text)) return tt.text.Trim();
            var tx = item.GetComponentInChildren<Text>(true);
            if (tx != null && !string.IsNullOrEmpty(tx.text)) return tx.text.Trim();
            return null;
        }

        void Broadcast(string cmd, string sid, int index, string title, double atUtc)
        {
            var d0 = Read();
            int v = d0 != null ? d0.v + 1 : 0;
            var d = new BusCmd { v = v, cmd = cmd, sid = sid, index = index, title = title, atUtc = atUtc, writeUtc = UtcNow() };
            SafeWrite(d);
        }

        BusCmd Read()
        {
            try
            {
                if (!File.Exists(path)) return null;
                var s = File.ReadAllText(path);
                if (string.IsNullOrWhiteSpace(s)) return null;
                return JsonUtility.FromJson<BusCmd>(s);
            }
            catch { return null; }
        }

        void SafeWrite(BusCmd d)
        {
            try
            {
                string tmp = path + ".tmp";
                File.WriteAllText(tmp, JsonUtility.ToJson(d));
                if (File.Exists(path)) File.Delete(path);
                File.Move(tmp, path);
            }
            catch { }
        }

        void TryAcquireLeader()
        {
            ReleaseLeader();
            try
            {
                bool createdNew;
                leaderMutex = new Mutex(false, "MateEngine.AvatarDanceSync.Leader", out createdNew);
                isLeader = leaderMutex.WaitOne(0);
            }
            catch { isLeader = GetInstanceIndex() == 0; }
        }

        int GetInstanceIndex()
        {
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
                if (string.Equals(args[i], "--instance", StringComparison.OrdinalIgnoreCase) && int.TryParse(args[i + 1], out int v))
                    return Math.Max(0, v);
            return 0;
        }

        void ReleaseLeader()
        {
            if (leaderMutex != null)
            {
                try { if (isLeader) leaderMutex.ReleaseMutex(); } catch { }
                leaderMutex.Dispose();
                leaderMutex = null;
            }
            isLeader = false;
        }

        static double UtcNow()
        {
            var e = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            return (DateTime.UtcNow - e).TotalSeconds;
        }
    }
}