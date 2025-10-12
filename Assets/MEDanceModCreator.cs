using System;
using UnityEngine;

public class MEDanceModCreator : MonoBehaviour
{
    public RuntimeAnimatorController controller;
    public AnimationClip danceClip;
    public AudioClip song;
    public string songName;
    public string songAuthor;
    public string mmdAuthor;
    public string placeholderClipName = "CUSTOM_DANCE";

    [Serializable]
    public class DanceMeta
    {
        public string songName;
        public string songAuthor;
        public string mmdAuthor;
        public float songLength;
        public string placeholderClipName;
    }

    public AnimationClip GetDanceClip()
    {
        if (danceClip != null) return danceClip;
        if (controller == null) return null;
        var clips = new AnimatorOverrideController(controller).animationClips;
        for (int i = 0; i < clips.Length; i++)
            if (clips[i] != null && clips[i].name == placeholderClipName)
                return clips[i];
        return clips.Length > 0 ? clips[0] : null;
    }

    public AudioClip GetAudioClip()
    {
        return song;
    }

    public AnimatorOverrideController BuildOverrideForExport()
    {
        if (controller == null) return null;
        var aoc = controller as AnimatorOverrideController;
        if (aoc == null) aoc = new AnimatorOverrideController(controller);
        var clip = GetDanceClip();
        if (clip != null)
        {
            bool assigned = false;
            var baseClips = aoc.animationClips;
            for (int i = 0; i < baseClips.Length; i++)
                if (baseClips[i] != null && baseClips[i].name == placeholderClipName)
                {
                    aoc[placeholderClipName] = clip;
                    assigned = true;
                    break;
                }
            if (!assigned && baseClips.Length > 0) aoc[baseClips[0].name] = clip;
        }
        return aoc;
    }

    public DanceMeta GetMetadata()
    {
        return new DanceMeta
        {
            songName = string.IsNullOrWhiteSpace(songName) && song != null ? song.name : songName,
            songAuthor = songAuthor,
            mmdAuthor = mmdAuthor,
            songLength = song != null ? song.length : 0f,
            placeholderClipName = placeholderClipName
        };
    }

    void OnValidate()
    {
        if (string.IsNullOrWhiteSpace(songName) && song != null) songName = song.name;
        if (danceClip == null && controller is AnimatorOverrideController oc)
        {
            try
            {
                var baseClips = oc.animationClips;
                for (int i = 0; i < baseClips.Length; i++)
                    if (baseClips[i] != null && baseClips[i].name == placeholderClipName)
                    {
                        var mapped = oc[placeholderClipName];
                        if (mapped != null) danceClip = mapped;
                        break;
                    }
            }
            catch { }
        }
    }
}
