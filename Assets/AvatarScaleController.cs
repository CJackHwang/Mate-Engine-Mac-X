using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using System.Collections.Generic;

public class AvatarScaleController : MonoBehaviour
{
    [SerializeField] private Slider avatarSizeSlider;
    [SerializeField] private Canvas targetCanvas;
    [SerializeField] private float scrollSensitivity = 0.1f;
    [SerializeField] private float smoothFactor = 0.1f; // 0 = instant, 1 = very slow smoothing

    private GraphicRaycaster raycaster;
    private PointerEventData pointerEventData;
    private EventSystem eventSystem;

    private float minSize = 0.1f;
    private float maxSize = 2.0f;
    private float targetSize = 1.0f;

    private readonly List<RaycastResult> raycastResults = new();

    void Start()
    {
        if (avatarSizeSlider != null)
        {
            minSize = avatarSizeSlider.minValue;
            maxSize = avatarSizeSlider.maxValue;
            targetSize = avatarSizeSlider.value;
            avatarSizeSlider.onValueChanged.AddListener(OnSliderChanged);

        }

        raycaster = targetCanvas?.GetComponent<GraphicRaycaster>();
        eventSystem = EventSystem.current;
        pointerEventData = new PointerEventData(eventSystem);
    }

    private void OnSliderChanged(float newValue)
    {
        targetSize = newValue;
    }

    public void SyncWithSlider()
    {
        if (avatarSizeSlider != null)
            targetSize = avatarSizeSlider.value;
    }


    void Update()
    {
        if (avatarSizeSlider == null || raycaster == null || eventSystem == null) return;

        pointerEventData.position = Input.mousePosition;
        raycastResults.Clear();
        raycaster.Raycast(pointerEventData, raycastResults);
        if (raycastResults.Count == 0) return;

        float scroll = Input.mouseScrollDelta.y;
        if (scroll != 0f)
        {
            targetSize = Mathf.Clamp(targetSize + scroll * scrollSensitivity, minSize, maxSize);
        }

        float current = avatarSizeSlider.value;
        float smoothed = Mathf.Lerp(current, targetSize, 1f - Mathf.Pow(1f - smoothFactor, Time.deltaTime * 60f));

        if (Mathf.Abs(smoothed - current) > 0.0001f)
        {
            avatarSizeSlider.SetValueWithoutNotify(smoothed);
            SaveLoadHandler.Instance.data.avatarSize = smoothed;
            avatarSizeSlider.value = smoothed;

            SaveLoadHandler.Instance.SaveToDisk();
            SaveLoadHandler.ApplyAllSettingsToAllAvatars();
        }
    }
}
