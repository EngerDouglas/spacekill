using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace OrbitRush
{

/// <summary>Low-level HUD widget helpers: rects, labels, bars, rings and generated sprites.</summary>
public partial class HUD
{
    // ══════════════════════════════════════════════════════════════════════
    // HELPERS
    // ══════════════════════════════════════════════════════════════════════

    static readonly Vector2 TL = new Vector2(0f, 1f), TR = new Vector2(1f, 1f), BL = new Vector2(0f, 0f),
                            BR = new Vector2(1f, 0f), TC = new Vector2(0.5f, 1f), CC = new Vector2(0.5f, 0.5f),
                            ML = new Vector2(0f, 0.5f), MR = new Vector2(1f, 0.5f), BC = new Vector2(0.5f, 0f);

    static Font LoadFont() => UiFactory.LoadFont();

    static RectTransform NewRect(string name, Transform parent, Vector2 anchor, Vector2 pivot, Vector2 pos, Vector2 size)
        => UiFactory.NewRect(name, parent, anchor, pivot, pos, size);

    Image Box(Transform parent, string name, Vector2 anchor, Vector2 pivot, Vector2 pos, Vector2 size, Color color)
    {
        var rt = NewRect(name, parent, anchor, pivot, pos, size);
        var img = rt.gameObject.AddComponent<Image>();
        img.color = color;
        img.raycastTarget = false;
        return img;
    }

    /// <summary>
    /// A text label. <paramref name="font"/> picks the Inter weight (light for big numbers, semibold for names, regular otherwise).
    /// A faint shadow keeps it readable over bright scenery without adding any panel.
    /// </summary>
    Text Txt(Transform parent, string text, int size, Color color, TextAnchor align, Vector2 anchor, Vector2 pivot,
             Vector2 pos, Vector2 boxSize, Font font = null)
    {
        var rt = NewRect("Text", parent, anchor, pivot, pos, boxSize);
        var t = rt.gameObject.AddComponent<Text>();
        t.font = font != null ? font : _font;
        t.text = text;
        t.fontSize = size;
        t.fontStyle = FontStyle.Normal;
        t.color = color;
        t.alignment = align;
        t.horizontalOverflow = HorizontalWrapMode.Overflow;
        t.verticalOverflow = VerticalWrapMode.Overflow;
        t.supportRichText = true;
        t.raycastTarget = false;
        var s = rt.gameObject.AddComponent<Shadow>();
        s.effectColor = new Color(0f, 0f, 0f, 0.45f);
        s.effectDistance = new Vector2(1f, -1f);
        return t;
    }

    /// <summary>
    /// A thin rounded bar: translucent track, a gradient fill (<paramref name="from"/> → <paramref name="to"/>, optional pale
    /// "damage trail" behind it) and three dark tick marks at 25 / 50 / 75 %. Position is the bar's top-left corner.
    /// </summary>
    Bar ApexBar(Transform parent, string name, Vector2 anchor, Vector2 pivot, Vector2 pos, float width, float height,
                Color from, Color to, bool ghost, bool ticks = true)
    {
        var track = NewRect(name, parent, anchor, pivot, pos, new Vector2(width, height));
        var tImg = track.gameObject.AddComponent<Image>();
        tImg.sprite = Pill(); tImg.type = Image.Type.Sliced; tImg.pixelsPerUnitMultiplier = 32f / height;
        tImg.color = Track; tImg.raycastTarget = false;

        var bar = new Bar();
        if (ghost) bar.ghost = PillFill(track, "Trail", Pill(), new Color(1f, 0.92f, 0.7f, 0.65f), height);
        var fill = PillFill(track, "Fill", GradientPill(from, to), Color.white, height);
        bar.fill = fill; bar.fillImage = fill.GetComponent<Image>();

        if (ticks)
            for (int i = 1; i <= 3; i++)
            {
                var tick = Box(track, "Tick" + i, new Vector2(i * 0.25f, 0.5f), CC, Vector2.zero, new Vector2(2f, height), Ink);
                tick.rectTransform.SetAsLastSibling();
            }
        return bar;
    }

    static RectTransform PillFill(RectTransform track, string name, Sprite sprite, Color color, float height)
    {
        var rt = NewRect(name, track, CC, CC, Vector2.zero, Vector2.zero);
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
        var img = rt.gameObject.AddComponent<Image>();
        img.sprite = sprite; img.type = Image.Type.Sliced; img.pixelsPerUnitMultiplier = 32f / height;
        img.color = color; img.raycastTarget = false;
        return rt;
    }

    // ── Generated sprites (one-time) ──────────────────────────────────────

    static Sprite Circle()
    {
        if (_circle != null) return _circle;
        _circle = MakeRadialSprite(128, (d) => Mathf.Clamp01((1f - d) * 64f), "HudCircle");
        return _circle;
    }

    /// <summary>A circle outline; <paramref name="thickness"/> is a fraction of the radius (0.02 ≈ hairline, 0.07 ≈ ability ring).</summary>
    static Sprite Ring(float thickness)
    {
        string key = thickness.ToString("F3");
        if (_rings.TryGetValue(key, out var cached) && cached != null) return cached;
        var sprite = MakeRadialSprite(256, (d) =>
        {
            float edge = Mathf.Abs(d - (1f - thickness * 0.5f));
            return Mathf.Clamp01((thickness * 0.5f - edge) / 0.012f + 0.5f);
        }, "HudRing" + key);
        _rings[key] = sprite;
        return sprite;
    }

    static Sprite VignetteSprite()
    {
        if (_vignetteSprite != null) return _vignetteSprite;
        // Transparent in the middle, increasingly opaque toward the screen edges.
        _vignetteSprite = MakeRadialSprite(256, (d) =>
        {
            float a = Mathf.InverseLerp(0.55f, 1.15f, d);
            return a * a;
        }, "HudVignette", clampToCircle: false);
        return _vignetteSprite;
    }

    /// <summary>Small upward-pointing triangle (the player marker on the radar).</summary>
    static Sprite Triangle()
    {
        if (_triangle != null) return _triangle;
        const int n = 64;
        var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { name = "HudTriangle", wrapMode = TextureWrapMode.Clamp };
        var px = new Color32[n * n];
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                // Apex at the top centre, base along the bottom; one pixel of antialiasing on the slanted edges
                float v = (y + 0.5f) / n;                       // 0 bottom → 1 top
                float half = (1f - v) * 0.5f;                   // half-width at this height (0.5 at the base)
                float d = Mathf.Abs((x + 0.5f) / n - 0.5f);
                px[y * n + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01((half - d) * n + 0.5f) * 255f));
            }
        tex.SetPixels32(px); tex.Apply();
        _triangle = Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f));
        return _triangle;
    }

    /// <summary>Rounded 9-sliced pill (white), tinted by the Image colour.</summary>
    static Sprite Pill() => MakePill(null, null);

    /// <summary>The same pill with a baked left→right colour gradient (9-sliced, so the gradient stretches with the bar).</summary>
    static Sprite GradientPill(Color from, Color to)
    {
        string key = ColorUtility.ToHtmlStringRGB(from) + ColorUtility.ToHtmlStringRGB(to);
        if (_gradPills.TryGetValue(key, out var cached) && cached != null) return cached;
        var s = MakePill(from, to);
        _gradPills[key] = s;
        return s;
    }

    static Sprite MakePill(Color? from, Color? to)
    {
        if (!from.HasValue && _pill != null) return _pill;
        const int w = 64, h = 32;
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { name = "HudPill", wrapMode = TextureWrapMode.Clamp };
        var px = new Color32[w * h];
        float r = h * 0.5f;
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float cx = Mathf.Clamp(x + 0.5f, r, w - r), cy = h * 0.5f;
                float d = Mathf.Sqrt((x + 0.5f - cx) * (x + 0.5f - cx) + (y + 0.5f - cy) * (y + 0.5f - cy));
                byte a = (byte)(Mathf.Clamp01(r - d + 0.5f) * 255f);
                Color c = from.HasValue ? Color.Lerp(from.Value, to.Value, Mathf.Clamp01((x + 0.5f - r) / (w - 2f * r))) : Color.white;
                px[y * w + x] = new Color32((byte)(c.r * 255f), (byte)(c.g * 255f), (byte)(c.b * 255f), a);
            }
        tex.SetPixels32(px); tex.Apply();
        var sprite = Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, new Vector4(16, 0, 16, 0));
        if (!from.HasValue) _pill = sprite;
        return sprite;
    }

    static Sprite MakeRadialSprite(int size, System.Func<float, float> alpha, string name, bool clampToCircle = true)
        => UiFactory.RadialSprite(size, alpha, name, clampToCircle);
}
}
