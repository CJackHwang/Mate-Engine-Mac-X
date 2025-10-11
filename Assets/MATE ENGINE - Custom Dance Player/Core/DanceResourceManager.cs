using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace CustomDancePlayer
{
    public class DanceResourceManager : MonoBehaviour
    {
        [Serializable]
        public class DanceEntry
        {
            public string id;
            public bool isFromDisk;
            public AssetBundle bundle;
            public AnimationClip clip;
            public AudioClip audio;
        }

        private const string DANCE_FOLDER_NAME = "CustomDances";
        private AssetBundle _currentAssetBundle;

        public AudioClip CurrentAudioClip { get; private set; }
        public AnimationClip CurrentAnimationClip { get; private set; }
        public List<string> DanceFileList { get; private set; } = new List<string>();

        [Header("Dependency References")]
        public DanceAvatarHelper avatarHelper;

        private readonly List<DanceEntry> injectedDances = new List<DanceEntry>();

        public void RegisterDanceBundle(AssetBundle bundle, string id)
        {
            if (bundle == null) return;
            var clip = bundle.LoadAllAssets<AnimationClip>().FirstOrDefault();
            var audio = bundle.LoadAllAssets<AudioClip>().FirstOrDefault();
            if (clip == null && audio == null) return;
            injectedDances.Add(new DanceEntry { id = id, isFromDisk = false, bundle = bundle, clip = clip, audio = audio });
        }

        public int GetTotalDanceCount()
        {
            return injectedDances.Count + DanceFileList.Count;
        }

        public List<DanceEntry> GetAllDanceEntries()
        {
            var list = new List<DanceEntry>();
            list.AddRange(injectedDances);
            foreach (var f in DanceFileList)
                list.Add(new DanceEntry { id = Path.GetFileNameWithoutExtension(f), isFromDisk = true });
            return list;
        }

        public List<string> GetDisplayNames()
        {
            var names = new List<string>();
            foreach (var e in injectedDances) names.Add(e.id + " (Injected)");
            foreach (var f in DanceFileList) names.Add(Path.GetFileNameWithoutExtension(f) + " (Disk)");
            return names;
        }

        public bool PrepareByIndex(int index, DanceAvatarHelper helper)
        {
            UnloadCurrentResource();
            if (index < 0 || index >= GetTotalDanceCount()) return false;
            if (index < injectedDances.Count)
            {
                var e = injectedDances[index];
                CurrentAnimationClip = e.clip;
                CurrentAudioClip = e.audio;
                if (helper != null && helper.CurrentAudioSource != null)
                {
                    helper.CurrentAudioSource.clip = CurrentAudioClip;
                    helper.CurrentAudioSource.loop = false;
                }
                return CurrentAnimationClip != null || CurrentAudioClip != null;
            }
            int diskIndex = index - injectedDances.Count;
            return LoadDanceResource(DanceFileList[diskIndex]);
        }

        public void RefreshDanceFileList()
        {
            DanceFileList.Clear();
            string danceFolderPath = GetDanceFolderPath();
            if (!Directory.Exists(danceFolderPath))
            {
                Directory.CreateDirectory(danceFolderPath);
                return;
            }
            DanceFileList.AddRange(Directory.GetFiles(danceFolderPath, "*.unity3d").Select(Path.GetFileName));
        }

        public bool LoadDanceResource(string fileName)
        {
            if (!avatarHelper.IsAvatarAvailable() || string.IsNullOrEmpty(fileName) || !fileName.EndsWith(".unity3d")) return false;
            UnloadCurrentResource();
            string fullPath = Path.Combine(GetDanceFolderPath(), fileName);
            if (!File.Exists(fullPath)) return false;
            _currentAssetBundle = AssetBundle.LoadFromFile(fullPath);
            if (_currentAssetBundle == null) return false;
            var clips = _currentAssetBundle.LoadAllAssets<AnimationClip>();
            CurrentAnimationClip = clips.Length > 0 ? clips[0] : null;
            var audios = _currentAssetBundle.LoadAllAssets<AudioClip>();
            CurrentAudioClip = audios.Length > 0 ? audios[0] : null;
            if (avatarHelper.CurrentAudioSource != null)
            {
                avatarHelper.CurrentAudioSource.clip = CurrentAudioClip;
                avatarHelper.CurrentAudioSource.loop = false;
            }
            return CurrentAnimationClip != null || CurrentAudioClip != null;
        }

        public void UnloadCurrentResource()
        {
            if (avatarHelper.IsAvatarAvailable() && avatarHelper.CurrentAudioSource != null)
            {
                avatarHelper.CurrentAudioSource.Stop();
                avatarHelper.CurrentAudioSource.clip = null;
            }
            if (_currentAssetBundle != null)
            {
                _currentAssetBundle.Unload(true);
                _currentAssetBundle = null;
            }
            CurrentAnimationClip = null;
            CurrentAudioClip = null;
        }

        private string GetDanceFolderPath()
        {
            return Path.Combine(Application.streamingAssetsPath, DANCE_FOLDER_NAME);
        }

        public bool IsResourceLoaded()
        {
            return CurrentAnimationClip != null && CurrentAudioClip != null;
        }

        public bool UnregisterInjected(string id)
        {
            int i = injectedDances.FindIndex(e => e.id == id);
            if (i < 0) return false;
            try { injectedDances[i].bundle?.Unload(false); } catch { }
            injectedDances.RemoveAt(i);
            return true;
        }

        public void RefreshAllSources()
        {
            RefreshDanceFileList();
        }


    }
}
