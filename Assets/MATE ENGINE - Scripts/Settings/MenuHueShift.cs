using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;
using Xamin;
using LLMUnitySamples;

[ExecuteAlways]
public class MenuHueShift : MonoBehaviour
{
    [Range(0f, 1f)] public float hueShift = 0f;
    [Range(0f, 1f)] public float saturation = 0.5f;
    public List<ParticleSystem> particleSystems = new List<ParticleSystem>();

    private readonly List<Graphic> graphics = new List<Graphic>();
    private readonly Dictionary<Graphic, Color> originalColors = new Dictionary<Graphic, Color>();

    private readonly List<Selectable> selectables = new List<Selectable>();
    private readonly Dictionary<Selectable, ColorBlock> originalColorBlocks = new Dictionary<Selectable, ColorBlock>();

    private readonly Dictionary<ParticleSystem, Color> originalStartColors = new Dictionary<ParticleSystem, Color>();

    private float lastHue = -1f;
    private float lastSat = -1f;

    private CircleSelector[] circleSelectors;
    private Dictionary<CircleSelector, (Color accent, Color disabled, Color background)> originalCircleColors = new();

    public ChatBot chatBot;
    private Color originalAiColor;


    private bool initialized = false;

    void Start()
    {
        if (!Application.isPlaying) return;
        Initialize();
    }

    void Update()
    {
        if (!Application.isPlaying || !enabled || !gameObject.activeInHierarchy) return;

        if (!initialized)
        {
            Initialize();
            return;
        }

        if (Mathf.Abs(lastHue - hueShift) < 0.001f && Mathf.Abs(lastSat - saturation) < 0.001f) return;

        ApplyHueShift();
        lastHue = hueShift;
        lastSat = saturation;
    }

    private void Initialize()
    {
        graphics.Clear();
        originalColors.Clear();
        selectables.Clear();
        originalColorBlocks.Clear();
        originalStartColors.Clear();

        var allGraphics = GameObject.FindObjectsOfType<Graphic>(true);
        for (int i = 0; i < allGraphics.Length; i++)
        {
            var g = allGraphics[i];
            if (g is TMPro.TextMeshProUGUI || g == null) continue;
            if (!originalColors.ContainsKey(g))
            {
                originalColors[g] = g.color;
                graphics.Add(g);
            }
        }

        var allSelectables = GameObject.FindObjectsOfType<Selectable>(true);
        for (int i = 0; i < allSelectables.Length; i++)
        {
            var s = allSelectables[i];
            if (s == null) continue;

            if (!originalColorBlocks.ContainsKey(s))
            {
                originalColorBlocks[s] = s.colors;
                selectables.Add(s);
            }

            var tg = s.targetGraphic;
            if (tg != null && !originalColors.ContainsKey(tg))
            {
                originalColors[tg] = tg.color;
                graphics.Add(tg);
            }

            var childGraphics = s.GetComponentsInChildren<Graphic>(true);
            for (int j = 0; j < childGraphics.Length; j++)
            {
                var cg = childGraphics[j];
                if (cg is TMPro.TextMeshProUGUI || cg == null) continue;
                if (!originalColors.ContainsKey(cg))
                {
                    originalColors[cg] = cg.color;
                    graphics.Add(cg);
                }
            }
        }

        for (int i = 0; i < particleSystems.Count; i++)
        {
            var ps = particleSystems[i];
            if (ps == null || originalStartColors.ContainsKey(ps)) continue;
            originalStartColors[ps] = ps.main.startColor.color;
        }

        initialized = true;
        if (chatBot != null)
            originalAiColor = chatBot.aiColor;


        circleSelectors = GameObject.FindObjectsOfType<CircleSelector>(true);
        foreach (var cs in circleSelectors)
        {
            if (cs == null || originalCircleColors.ContainsKey(cs)) continue;
            originalCircleColors[cs] = (cs.AccentColor, cs.DisabledColor, cs.BackgroundColor);
        }

    }
    public void ApplyHueShift()
    {
        for (int i = 0; i < graphics.Count; i++)
        {
            var g = graphics[i];
            if (g == null || !originalColors.TryGetValue(g, out var original)) continue;
            g.color = AdjustColor(original);
        }

        for (int i = 0; i < selectables.Count; i++)
        {
            var s = selectables[i];
            if (s == null || !originalColorBlocks.TryGetValue(s, out var original)) continue;

            var mod = original;
            mod.normalColor = AdjustColor(original.normalColor);
            mod.highlightedColor = AdjustColor(original.highlightedColor);
            mod.pressedColor = AdjustColor(original.pressedColor);
            mod.selectedColor = AdjustColor(original.selectedColor);
            mod.disabledColor = AdjustColor(original.disabledColor);
            s.colors = mod;
        }

        foreach (var kvp in originalStartColors)
        {
            var ps = kvp.Key;
            if (ps == null) continue;

            var main = ps.main;
            var gradient = main.startColor;
            gradient.color = AdjustColor(kvp.Value);
            main.startColor = gradient;
        }
        foreach (var kvp in originalCircleColors)
        {
            var cs = kvp.Key;
            if (cs == null) continue;

            cs.AccentColor = AdjustColor(kvp.Value.accent);
            cs.DisabledColor = AdjustColor(kvp.Value.disabled);
            cs.BackgroundColor = AdjustColor(kvp.Value.background);
        }

        if (chatBot != null)
        {
            Color newAiColor = AdjustColor(originalAiColor);
            chatBot.aiColor = newAiColor;

            var aiUIField = chatBot.GetType().GetField("aiUI", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (aiUIField != null)
            {
                if (aiUIField != null)
                {
                    var aiUI = (BubbleUI)aiUIField.GetValue(chatBot);
                    aiUI.bubbleColor = newAiColor;
                    aiUIField.SetValue(chatBot, aiUI); 
                }

            }

            foreach (Transform child in chatBot.chatContainer)
            {
                if (child.name.Contains("AIBubble"))
                {
                    var image = child.GetComponentInChildren<Image>(true);
                    if (image != null) image.color = newAiColor;
                }
            }
        }



    }

    private Color AdjustColor(Color original)
    {
        Color.RGBToHSV(original, out float h, out float s, out float v);
        h = (h + hueShift) % 1f;
        float adjustedS = Mathf.Clamp01(s * (saturation * 2f));
        var result = Color.HSVToRGB(h, adjustedS, v);
        result.a = original.a;
        return result;
    }

    private void LateUpdate()
    {
        if (!Application.isPlaying || !enabled || !gameObject.activeInHierarchy)
            return;

        var allGraphics = GameObject.FindObjectsOfType<Graphic>(true);
        foreach (var g in allGraphics)
        {
            if (g is TMPro.TextMeshProUGUI || g == null) continue;
            if (!originalColors.ContainsKey(g))
            {
                originalColors[g] = g.color;
                graphics.Add(g);
                g.color = AdjustColor(g.color); 
            }
        }

        var allSelectables = GameObject.FindObjectsOfType<Selectable>(true);
        foreach (var s in allSelectables)
        {
            if (s == null) continue;
            if (!originalColorBlocks.ContainsKey(s))
            {
                originalColorBlocks[s] = s.colors;
                selectables.Add(s);
                var tg = s.targetGraphic;
                if (tg != null && !originalColors.ContainsKey(tg))
                {
                    originalColors[tg] = tg.color;
                    graphics.Add(tg);
                    tg.color = AdjustColor(tg.color);
                }
            }
        }
    }

    public void RefreshNewGraphicsAndSelectables(Transform parent = null)
    {
        var newGraphics = parent == null
            ? GameObject.FindObjectsOfType<Graphic>(true)
            : parent.GetComponentsInChildren<Graphic>(true);

        foreach (var g in newGraphics)
        {
            if (g is TMPro.TextMeshProUGUI || g == null) continue;
            if (!originalColors.ContainsKey(g))
            {
                originalColors[g] = g.color;
                graphics.Add(g);
                g.color = AdjustColor(g.color);
            }
        }

        var newSelectables = parent == null
            ? GameObject.FindObjectsOfType<Selectable>(true)
            : parent.GetComponentsInChildren<Selectable>(true);

        foreach (var s in newSelectables)
        {
            if (s == null) continue;
            if (!originalColorBlocks.ContainsKey(s))
            {
                originalColorBlocks[s] = s.colors;
                selectables.Add(s);
                var tg = s.targetGraphic;
                if (tg != null && !originalColors.ContainsKey(tg))
                {
                    originalColors[tg] = tg.color;
                    graphics.Add(tg);
                    tg.color = AdjustColor(tg.color);
                }
            }
        }
    }


}
