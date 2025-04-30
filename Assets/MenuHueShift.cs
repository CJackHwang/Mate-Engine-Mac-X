using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;

[ExecuteAlways]
public class MenuHueShift : MonoBehaviour
{
    [Range(0f, 1f)] public float hueShift = 0f;
    [Range(0f, 1f)] public float saturation = 0.5f;
    public List<ParticleSystem> particleSystems = new List<ParticleSystem>();

    private List<Graphic> graphics = new List<Graphic>();
    private Dictionary<Graphic, Color> originalColors = new Dictionary<Graphic, Color>();

    private List<Selectable> selectables = new List<Selectable>();
    private Dictionary<Selectable, ColorBlock> originalColorBlocks = new Dictionary<Selectable, ColorBlock>();

    private Dictionary<ParticleSystem, Color> originalStartColors = new Dictionary<ParticleSystem, Color>();

    private float lastHue = -1f;
    private float lastSat = -1f;

    void Start()
    {
        if (!Application.isPlaying) return;

        graphics.Clear();
        originalColors.Clear();
        selectables.Clear();
        originalColorBlocks.Clear();
        originalStartColors.Clear();

        Graphic[] allGraphics = GameObject.FindObjectsOfType<Graphic>(true);
        foreach (var g in allGraphics)
        {
            if (g is TMPro.TextMeshProUGUI) continue;

            if (!originalColors.ContainsKey(g))
            {
                originalColors[g] = g.color;
                graphics.Add(g);
            }
        }

        Selectable[] allSelectables = GameObject.FindObjectsOfType<Selectable>(true);
        foreach (var s in allSelectables)
        {
            if (!originalColorBlocks.ContainsKey(s))
            {
                originalColorBlocks[s] = s.colors;
                selectables.Add(s);
            }

            if (s.targetGraphic != null && !originalColors.ContainsKey(s.targetGraphic))
            {
                originalColors[s.targetGraphic] = s.targetGraphic.color;
                graphics.Add(s.targetGraphic);
            }

            Graphic[] childGraphics = s.GetComponentsInChildren<Graphic>(true);
            foreach (var cg in childGraphics)
            {
                if (cg is TMPro.TextMeshProUGUI) continue;
                if (!originalColors.ContainsKey(cg))
                {
                    originalColors[cg] = cg.color;
                    graphics.Add(cg);
                }
            }
        }

        foreach (var ps in particleSystems)
        {
            if (ps == null || originalStartColors.ContainsKey(ps)) continue;
            originalStartColors[ps] = ps.main.startColor.color;
        }
    }

    void Update()
    {
        if (!Application.isPlaying) return;
        if (Mathf.Abs(lastHue - hueShift) < 0.001f && Mathf.Abs(lastSat - saturation) < 0.001f) return;

        foreach (var g in graphics)
        {
            if (g == null || !originalColors.ContainsKey(g)) continue;
            g.color = AdjustColor(originalColors[g]);
        }

        foreach (var s in selectables)
        {
            if (s == null || !originalColorBlocks.ContainsKey(s)) continue;

            ColorBlock original = originalColorBlocks[s];
            ColorBlock mod = original;

            mod.normalColor = AdjustColor(original.normalColor);
            mod.highlightedColor = AdjustColor(original.highlightedColor);
            mod.pressedColor = AdjustColor(original.pressedColor);
            mod.selectedColor = AdjustColor(original.selectedColor);
            mod.disabledColor = AdjustColor(original.disabledColor);

            s.colors = mod;
        }

        foreach (var ps in particleSystems)
        {
            if (ps == null || !originalStartColors.ContainsKey(ps)) continue;

            var main = ps.main;
            Color newColor = AdjustColor(originalStartColors[ps]);
            ParticleSystem.MinMaxGradient gradient = main.startColor;
            gradient.color = newColor;
            main.startColor = gradient;
        }

        lastHue = hueShift;
        lastSat = saturation;
    }

    private Color AdjustColor(Color original)
    {
        Color.RGBToHSV(original, out float h, out float s, out float v);
        h = (h + hueShift) % 1f;

        // Scale saturation around 0.5 = original
        float adjustedS = Mathf.Clamp01(s * (saturation * 2f));

        Color result = Color.HSVToRGB(h, adjustedS, v);
        result.a = original.a;
        return result;
    }
}
