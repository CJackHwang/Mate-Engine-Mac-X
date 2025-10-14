using System.Collections.Generic;
using UnityEngine;

[ExecuteAlways]
public class ThemeManager : MonoBehaviour
{
    public static ThemeManager Instance;

    [Range(0f, 1f)] public float hue = 0f;
    [Range(0f, 2f)] public float saturation = 1f;
    public List<Material> materials = new List<Material>();
    public List<ParticleSystem> particleSystems = new List<ParticleSystem>();
    public bool revertOnExitPlayMode = true;

    readonly Dictionary<Material, Color> baseColor = new Dictionary<Material, Color>();
    readonly Dictionary<Material, Color> baseMainColor = new Dictionary<Material, Color>();
    readonly Dictionary<Material, Color> baseOverlayColor = new Dictionary<Material, Color>();
    readonly Dictionary<ParticleSystem, Color> baseParticleColor = new Dictionary<ParticleSystem, Color>();

    float lastHue = -1f;
    float lastSat = -1f;
    bool runtimeActive;

    void OnEnable()
    {
        Instance = this;
        if (Application.isPlaying)
        {
            CaptureBaseProps();
            runtimeActive = true;
            Apply();
        }
    }

    void OnDisable()
    {
        if (runtimeActive && revertOnExitPlayMode) RestoreAll();
        if (Instance == this) Instance = null;
        runtimeActive = false;
        lastHue = -1f;
        lastSat = -1f;
    }

    void OnValidate()
    {
        if (Application.isPlaying) Apply();
    }

    public void SetHue(float value)
    {
        hue = Mathf.Repeat(value, 1f);
        if (Application.isPlaying) Apply();
    }

    public void SetSaturation(float value)
    {
        saturation = Mathf.Clamp(value, 0f, 2f);
        if (Application.isPlaying) Apply();
    }

    public void RegisterMaterial(Material mat)
    {
        if (mat == null) return;
        if (!materials.Contains(mat)) materials.Add(mat);
        if (Application.isPlaying)
        {
            CacheMaterial(mat);
            ApplyTo(mat);
        }
    }

    public void UnregisterMaterial(Material mat)
    {
        if (mat == null) return;
        materials.Remove(mat);
        baseColor.Remove(mat);
        baseMainColor.Remove(mat);
        baseOverlayColor.Remove(mat);
    }

    public void RegisterParticle(ParticleSystem ps)
    {
        if (ps == null) return;
        if (!particleSystems.Contains(ps)) particleSystems.Add(ps);
        if (Application.isPlaying)
        {
            CacheParticle(ps);
            ApplyTo(ps);
        }
    }

    public void UnregisterParticle(ParticleSystem ps)
    {
        if (ps == null) return;
        particleSystems.Remove(ps);
        baseParticleColor.Remove(ps);
    }

    public void CaptureBaseProps()
    {
        baseColor.Clear();
        baseMainColor.Clear();
        baseOverlayColor.Clear();
        baseParticleColor.Clear();
        for (int i = 0; i < materials.Count; i++) CacheMaterial(materials[i]);
        for (int i = 0; i < particleSystems.Count; i++) CacheParticle(particleSystems[i]);
    }

    void CacheMaterial(Material m)
    {
        if (m == null) return;
        if (m.HasProperty("_Color") && !baseColor.ContainsKey(m)) baseColor[m] = m.GetColor("_Color");
        if (m.HasProperty("_MainColor") && !baseMainColor.ContainsKey(m)) baseMainColor[m] = m.GetColor("_MainColor");
        if (m.HasProperty("_OverlayColor") && !baseOverlayColor.ContainsKey(m)) baseOverlayColor[m] = m.GetColor("_OverlayColor");
    }

    void CacheParticle(ParticleSystem ps)
    {
        if (ps == null) return;
        if (!baseParticleColor.ContainsKey(ps)) baseParticleColor[ps] = ps.main.startColor.color;
    }

    public void Apply()
    {
        if (!Application.isPlaying) return;
        if (Mathf.Approximately(lastHue, hue) && Mathf.Approximately(lastSat, saturation)) return;

        for (int i = 0; i < materials.Count; i++) ApplyTo(materials[i]);
        for (int i = 0; i < particleSystems.Count; i++) ApplyTo(particleSystems[i]);

        lastHue = hue;
        lastSat = saturation;
    }

    void ApplyTo(Material m)
    {
        if (m == null) return;

        if (m.HasProperty("_Color") && baseColor.TryGetValue(m, out var c0))
            m.SetColor("_Color", Adjust(c0));

        if (m.HasProperty("_MainColor") && baseMainColor.TryGetValue(m, out var mc0))
            m.SetColor("_MainColor", Adjust(mc0));

        if (m.HasProperty("_OverlayColor") && baseOverlayColor.TryGetValue(m, out var oc0))
            m.SetColor("_OverlayColor", Adjust(oc0));
    }

    void ApplyTo(ParticleSystem ps)
    {
        if (ps == null) return;
        if (!baseParticleColor.TryGetValue(ps, out var c0)) return;
        var main = ps.main;
        var mmg = main.startColor;
        mmg.color = Adjust(c0);
        main.startColor = mmg;
    }

    Color Adjust(Color src)
    {
        Color.RGBToHSV(src, out float h, out float s, out float v);
        h = (h + hue) % 1f;
        s = Mathf.Clamp01(s * saturation);
        var c = Color.HSVToRGB(h, s, v);
        c.a = src.a;
        return c;
    }

    public void RestoreAll()
    {
        for (int i = 0; i < materials.Count; i++)
        {
            var m = materials[i];
            if (m == null) continue;
            if (m.HasProperty("_Color") && baseColor.TryGetValue(m, out var c0)) m.SetColor("_Color", c0);
            if (m.HasProperty("_MainColor") && baseMainColor.TryGetValue(m, out var mc0)) m.SetColor("_MainColor", mc0);
            if (m.HasProperty("_OverlayColor") && baseOverlayColor.TryGetValue(m, out var oc0)) m.SetColor("_OverlayColor", oc0);
        }
        for (int i = 0; i < particleSystems.Count; i++)
        {
            var ps = particleSystems[i];
            if (ps == null) continue;
            if (!baseParticleColor.TryGetValue(ps, out var c0)) continue;
            var main = ps.main;
            var mmg = main.startColor;
            mmg.color = c0;
            main.startColor = mmg;
        }
    }
}
