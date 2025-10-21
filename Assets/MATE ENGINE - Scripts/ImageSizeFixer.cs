using UnityEngine;

[ExecuteAlways]
public class ImageSizeFixer : MonoBehaviour
{
    public RectTransform startElement;
    public RectTransform endElement;
    public float topOffset = 10f;
    public float bottomOffset = 10f;
    public bool liveInEditor = true;

    RectTransform self;
    RectTransform parentRect;
    Canvas rootCanvas;

    void OnEnable()
    {
        self = GetComponent<RectTransform>();
        parentRect = self != null ? self.parent as RectTransform : null;
        rootCanvas = self != null ? self.GetComponentInParent<Canvas>() : null;
        if (Application.isPlaying) enabled = false;
        if (!Application.isPlaying && liveInEditor) ApplyNow();
    }

    void OnValidate()
    {
        if (!Application.isPlaying && liveInEditor) ApplyNow();
    }

    void Update()
    {
        if (!Application.isPlaying && liveInEditor) ApplyNow();
    }

    [ContextMenu("Apply Now")]
    public void ApplyNow()
    {
        if (self == null || parentRect == null || startElement == null || endElement == null) return;

        Vector3[] s = new Vector3[4];
        Vector3[] e = new Vector3[4];
        startElement.GetWorldCorners(s);
        endElement.GetWorldCorners(e);

        Vector2 sTopL = WorldToParentLocal(s[1]);
        Vector2 sTopR = WorldToParentLocal(s[2]);
        Vector2 eBotL = WorldToParentLocal(e[0]);
        Vector2 eBotR = WorldToParentLocal(e[3]);

        float topLocalY = Mathf.Max(sTopL.y, sTopR.y) + topOffset;
        float bottomLocalY = Mathf.Min(eBotL.y, eBotR.y) - bottomOffset;

        if (bottomLocalY > topLocalY) { float t = topLocalY; topLocalY = bottomLocalY; bottomLocalY = t; }

        float height = Mathf.Max(0f, topLocalY - bottomLocalY);
        float centerY = (topLocalY + bottomLocalY) * 0.5f;

        float anchorY = Mathf.Lerp(parentRect.rect.yMin, parentRect.rect.yMax, (self.anchorMin.y + self.anchorMax.y) * 0.5f);

        self.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, height);
        self.anchoredPosition = new Vector2(self.anchoredPosition.x, centerY - anchorY);
    }

    Vector2 WorldToParentLocal(Vector3 world)
    {
        Camera cam = rootCanvas != null && rootCanvas.renderMode != RenderMode.ScreenSpaceOverlay ? rootCanvas.worldCamera : null;
        RectTransformUtility.ScreenPointToLocalPointInRectangle(parentRect, RectTransformUtility.WorldToScreenPoint(cam, world), cam, out Vector2 local);
        return local;
    }
}
