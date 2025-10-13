using UnityEngine;
using UnityEngine.UI;

namespace CustomDancePlayer
{
    public class AvatarDancePlayerUtils : MonoBehaviour
    {
        [Header("Audio")]
        public AudioSource danceAudioSource;

        [Header("UI")]
        public Slider volumeSlider;

        void Start()
        {
            if (volumeSlider != null)
            {
                volumeSlider.minValue = 0f;
                volumeSlider.maxValue = 100f;
                volumeSlider.onValueChanged.AddListener(OnVolumeChanged);
            }

            if (danceAudioSource != null && volumeSlider != null)
                volumeSlider.value = danceAudioSource.volume * 100f;
        }

        void OnVolumeChanged(float value)
        {
            if (danceAudioSource != null)
                danceAudioSource.volume = Mathf.Clamp01(value / 100f);
        }
    }
}