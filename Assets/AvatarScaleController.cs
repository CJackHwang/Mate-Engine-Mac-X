using UnityEngine;
using UnityEngine.UI;
using Kirurobo;  // for UniWindowController

public class AvatarScaleController : MonoBehaviour
{
    [Header("UI")]
    [SerializeField] private Slider avatarSizeSlider;

    [Header("Scroll Settings")]
    [SerializeField] private float scrollSensitivity = 0.1f;
    [SerializeField] private float smoothFactor = 0.1f; // 0 = instant, 1 = very slow

    private float minSize;
    private float maxSize;
    private float targetSize;

    void Start()
    {
        if (avatarSizeSlider == null) return;

        // initialize range & target
        minSize = avatarSizeSlider.minValue;
        maxSize = avatarSizeSlider.maxValue;
        targetSize = avatarSizeSlider.value;

        // keep targetSize in sync if slider is dragged manually
        avatarSizeSlider.onValueChanged.AddListener(v => targetSize = v);
    }

    /// <summary>
    /// (Optional) call this from elsewhere to force-sync targetSize to the slider
    /// </summary>
    public void SyncWithSlider()
    {
        if (avatarSizeSlider != null)
            targetSize = avatarSizeSlider.value;
    }

    void Update()
    {
        if (avatarSizeSlider == null)
            return;

        // --- gate scrolling on Kirurobo's per-pixel hit test ---
        // when isClickThrough==true, mouse is over transparent pixels → we block scaling
        if (UniWindowController.current.isClickThrough)
            return;
        // -------------------------------------------------------

        // read scroll delta
        float scroll = Input.mouseScrollDelta.y;
        if (scroll != 0f)
        {
            targetSize = Mathf.Clamp(
                targetSize + scroll * scrollSensitivity,
                minSize, maxSize
            );
        }

        // smooth the slider toward targetSize
        float current = avatarSizeSlider.value;
        float smoothed = Mathf.Lerp(
            current,
            targetSize,
            1f - Mathf.Pow(1f - smoothFactor, Time.deltaTime * 60f)
        );

        if (Mathf.Abs(smoothed - current) > 0.0001f)
        {
            // update without triggering onValueChanged again
            avatarSizeSlider.SetValueWithoutNotify(smoothed);
            avatarSizeSlider.value = smoothed;

            // persist & apply
            SaveLoadHandler.Instance.data.avatarSize = smoothed;
            SaveLoadHandler.Instance.SaveToDisk();
            SaveLoadHandler.ApplyAllSettingsToAllAvatars();
        }
    }
}
