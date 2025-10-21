using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEngine.Localization.Settings;

public class UiTooltip : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerMoveHandler
{
    public RectTransform container;

    public string localizationTable = "Languages (UI)";
    public string locKey = "";
    [TextArea(1, 6)] public string tooltipText = "";

    public Material bubbleMaterial;
    public Sprite bubbleSprite;
    public Color bubbleColor = new Color32(29, 29, 73, 255);
    public Color fontColor = Color.white;
    public Font font;
    public int fontSize = 16;
    public int bubbleWidth = 600;
    public float textPadding = 10f;
    public float bubbleSpacing = 10f;

    public Vector2 mouseOffset = Vector2.zero;

    [Range(0f, 5f)] public float ShowTooltextIn = 0f;

    [Header("Hover Zone")]
    public float expandLeft = 0f;
    public float expandRight = 0f;
    public float expandTop = 0f;
    public float expandBottom = 0f;
    public bool drawHoverGizmos = true;

    static LLMUnitySamples.Bubble activeBubble;
    static UiTooltip owner;

    Canvas rootCanvas;
    RectTransform containerRT;
    RectTransform selfRT;
    bool hovering;
    Vector2 lastScreenPos;
    float hoverStartTime = -1f;

    bool IsShown => activeBubble != null && owner == this;

    void Awake()
    {
        if (container == null)
        {
            var c = GetComponentInParent<Canvas>();
            if (c != null) container = c.transform as RectTransform;
        }
        containerRT = container;
        rootCanvas = containerRT != null ? containerRT.GetComponentInParent<Canvas>() : null;
        selfRT = transform as RectTransform;
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        lastScreenPos = eventData.position;
        hoverStartTime = Time.unscaledTime;
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        hovering = false;
        hoverStartTime = -1f;
        HideTooltip();
    }

    public void OnPointerMove(PointerEventData eventData)
    {
        lastScreenPos = eventData.position;
        if (hovering && IsShown) Reposition(lastScreenPos);
    }

    void Update()
    {
        if (selfRT == null || rootCanvas == null) return;

        lastScreenPos = Input.mousePosition;
        bool inside = IsInsideExpandedZone(lastScreenPos);

        if (inside && !hovering)
        {
            hovering = true;
            hoverStartTime = Time.unscaledTime;
            if (owner != null && owner != this) owner.HideTooltip();
            if (ShowTooltextIn <= 0f)
            {
                ShowTooltip();
                Reposition(lastScreenPos);
            }
        }
        else if (!inside && hovering)
        {
            hovering = false;
            hoverStartTime = -1f;
            HideTooltip();
        }

        if (hovering && !IsShown && hoverStartTime >= 0f && Time.unscaledTime - hoverStartTime >= ShowTooltextIn)
        {
            ShowTooltip();
            Reposition(lastScreenPos);
        }

        if (hovering && IsShown) Reposition(lastScreenPos);
    }

    void OnRectTransformDimensionsChange()
    {
        if (hovering && IsShown) Reposition(lastScreenPos);
    }

    void ShowTooltip()
    {
        if (containerRT == null) return;
        if (owner != null && owner != this) owner.HideTooltip();
        HideTooltip();

        var ui = new LLMUnitySamples.BubbleUI
        {
            sprite = bubbleSprite,
            font = font,
            fontSize = fontSize,
            fontColor = fontColor,
            bubbleColor = bubbleColor,
            bottomPosition = 0,
            leftPosition = 0,
            textPadding = textPadding,
            bubbleOffset = bubbleSpacing,
            bubbleWidth = bubbleWidth,
            bubbleHeight = -1
        };

        activeBubble = new LLMUnitySamples.Bubble(containerRT, ui, "UiTooltip", "");
        var img = activeBubble.GetOuterRectTransform().GetComponent<Image>();
        if (img != null)
        {
            if (bubbleMaterial != null) img.material = bubbleMaterial;
            img.type = Image.Type.Sliced;
            img.pixelsPerUnitMultiplier = 2f;
        }

        activeBubble.SetText(ResolveText());

        var bubbleRT = activeBubble.GetRectTransform();
        var outerRT = activeBubble.GetOuterRectTransform();
        outerRT.offsetMin = new Vector2(-25f, outerRT.offsetMin.y);

        var bubbleCanvas = bubbleRT.GetComponent<Canvas>();
        var imageCanvas = outerRT.GetComponent<Canvas>();
        if (bubbleCanvas == null) bubbleCanvas = bubbleRT.gameObject.AddComponent<Canvas>();
        if (imageCanvas == null) imageCanvas = outerRT.gameObject.AddComponent<Canvas>();
        bubbleCanvas.overrideSorting = true; bubbleCanvas.sortingOrder = 3;
        imageCanvas.overrideSorting = true; imageCanvas.sortingOrder = 2;

        var le1 = bubbleRT.GetComponent<LayoutElement>();
        if (le1 == null) le1 = bubbleRT.gameObject.AddComponent<LayoutElement>();
        le1.ignoreLayout = true;
        var le2 = outerRT.GetComponent<LayoutElement>();
        if (le2 == null) le2 = outerRT.gameObject.AddComponent<LayoutElement>();
        le2.ignoreLayout = true;

        bubbleRT.anchorMin = new Vector2(0.5f, 0.5f);
        bubbleRT.anchorMax = new Vector2(0.5f, 0.5f);
        bubbleRT.pivot = new Vector2(0.5f, 0.5f);

        var lp = bubbleRT.localPosition; lp.z = 0f; bubbleRT.localPosition = lp;
        var lp2 = outerRT.localPosition; lp2.z = 0f; outerRT.localPosition = lp2;

        owner = this;
    }

    void OnDisable()
    {
        hovering = false;
        hoverStartTime = -1f;
        HideTooltip();
    }

    void OnDestroy()
    {
        hovering = false;
        hoverStartTime = -1f;
        HideTooltip();
    }

    void HideTooltip()
    {
        if (owner != this) return;
        if (activeBubble != null) { activeBubble.Destroy(); activeBubble = null; }
        owner = null;
        hovering = false;
    }

    string ResolveText()
    {
        if (!string.IsNullOrEmpty(locKey))
        {
            try
            {
                string localized = LocalizationSettings.StringDatabase.GetLocalizedString(localizationTable, locKey);
                if (!string.IsNullOrEmpty(localized)) return localized;
            }
            catch { }
        }
        return tooltipText;
    }

    void Reposition(Vector2 screenPos)
    {
        if (owner != this || activeBubble == null || containerRT == null || rootCanvas == null) return;

        var bubbleRT = activeBubble.GetRectTransform();
        var outerRT = activeBubble.GetOuterRectTransform();
        var canvasRT = rootCanvas.transform as RectTransform;
        var cam = rootCanvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : rootCanvas.worldCamera;

        RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRT, screenPos, cam, out Vector2 local);
        Vector2 target = local + mouseOffset;

        bubbleRT.anchoredPosition = target;
        var lp = bubbleRT.localPosition; lp.z = 0f; bubbleRT.localPosition = lp;

        var lp2 = outerRT.localPosition; lp2.z = 0f; outerRT.localPosition = lp2;
    }

    bool IsInsideExpandedZone(Vector2 screenPos)
    {
        if (selfRT == null) return false;
        var cam = rootCanvas != null && rootCanvas.renderMode != RenderMode.ScreenSpaceOverlay ? rootCanvas.worldCamera : null;

        Vector3[] wc = new Vector3[4];
        selfRT.GetWorldCorners(wc);
        Vector2 s0 = RectTransformUtility.WorldToScreenPoint(cam, wc[0]);
        Vector2 s1 = RectTransformUtility.WorldToScreenPoint(cam, wc[1]);
        Vector2 s2 = RectTransformUtility.WorldToScreenPoint(cam, wc[2]);
        Vector2 s3 = RectTransformUtility.WorldToScreenPoint(cam, wc[3]);

        float minX = Mathf.Min(s0.x, s1.x, s2.x, s3.x) - expandLeft;
        float maxX = Mathf.Max(s0.x, s1.x, s2.x, s3.x) + expandRight;
        float minY = Mathf.Min(s0.y, s1.y, s2.y, s3.y) - expandBottom;
        float maxY = Mathf.Max(s0.y, s1.y, s2.y, s3.y) + expandTop;

        return screenPos.x >= minX && screenPos.x <= maxX && screenPos.y >= minY && screenPos.y <= maxY;
    }

    void OnDrawGizmosSelected()
    {
        if (!drawHoverGizmos) return;
        if (selfRT == null) selfRT = transform as RectTransform;

        Canvas c = rootCanvas != null ? rootCanvas : GetComponentInParent<Canvas>();
        var cam = c != null && c.renderMode != RenderMode.ScreenSpaceOverlay ? c.worldCamera : null;

        Vector3[] wc = new Vector3[4];
        selfRT.GetWorldCorners(wc);
        Vector2 s0 = RectTransformUtility.WorldToScreenPoint(cam, wc[0]);
        Vector2 s1 = RectTransformUtility.WorldToScreenPoint(cam, wc[1]);
        Vector2 s2 = RectTransformUtility.WorldToScreenPoint(cam, wc[2]);
        Vector2 s3 = RectTransformUtility.WorldToScreenPoint(cam, wc[3]);

        Vector2 e0 = new Vector2(Mathf.Min(s0.x, s1.x, s2.x, s3.x) - expandLeft, Mathf.Min(s0.y, s1.y, s2.y, s3.y) - expandBottom);
        Vector2 e2 = new Vector2(Mathf.Max(s0.x, s1.x, s2.x, s3.x) + expandRight, Mathf.Max(s0.y, s1.y, s2.y, s3.y) + expandTop);
        Vector2 e1 = new Vector2(e0.x, e2.y);
        Vector2 e3 = new Vector2(e2.x, e0.y);

        RectTransform rt = selfRT;
        RectTransformUtility.ScreenPointToWorldPointInRectangle(rt, e0, cam, out Vector3 w0);
        RectTransformUtility.ScreenPointToWorldPointInRectangle(rt, e1, cam, out Vector3 w1);
        RectTransformUtility.ScreenPointToWorldPointInRectangle(rt, e2, cam, out Vector3 w2);
        RectTransformUtility.ScreenPointToWorldPointInRectangle(rt, e3, cam, out Vector3 w3);

        Gizmos.color = new Color(0f, 1f, 0f, 0.7f);
        Gizmos.DrawLine(w0, w1);
        Gizmos.DrawLine(w1, w2);
        Gizmos.DrawLine(w2, w3);
        Gizmos.DrawLine(w3, w0);
    }
}
