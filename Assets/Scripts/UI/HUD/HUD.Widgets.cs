using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace OrbitRush
{

/// <summary>Low-level HUD widget helpers: boxes, frames, labels, bars and generated sprites.</summary>
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

    Image Box(Transform parent, string name, Vector2 anchor, Vector2 pivot, Vector2 pos, Vector2 size, Color color, Color? border = null, float borderWidth = 2f)
    {
        var rt = NewRect(name, parent, anchor, pivot, pos, size);
        var img = rt.gameObject.AddComponent<Image>();
        img.color = color;
        img.raycastTarget = false;
        if (border.HasValue) AddFrame(rt, border.Value, borderWidth);
        return img;
    }

    /// <summary>
    /// Four thin edge images around a panel. (Unity's Outline component draws a tinted copy of the whole
    /// panel underneath it, which turned translucent navy panels bright pink/teal.)
    /// </summary>
    void AddFrame(RectTransform panel, Color color, float width)
    {
        Edge(panel, "Frame_Top",    new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 1f),   new Vector2(0, width), color);
        Edge(panel, "Frame_Bottom", new Vector2(0, 0), new Vector2(1, 0), new Vector2(0.5f, 0f),   new Vector2(0, width), color);
        Edge(panel, "Frame_Left",   new Vector2(0, 0), new Vector2(0, 1), new Vector2(0f, 0.5f),   new Vector2(width, 0), color);
        Edge(panel, "Frame_Right",  new Vector2(1, 0), new Vector2(1, 1), new Vector2(1f, 0.5f),   new Vector2(width, 0), color);
    }

    static void Edge(RectTransform parent, string name, Vector2 aMin, Vector2 aMax, Vector2 pivot, Vector2 size, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.layer = LayerMask.NameToLayer("UI");
        var rt = go.GetComponent<RectTransform>();
        rt.SetParent(parent, false);
        rt.anchorMin = aMin; rt.anchorMax = aMax; rt.pivot = pivot;
        rt.anchoredPosition = Vector2.zero; rt.sizeDelta = size;
        var img = go.AddComponent<Image>();
        img.color = color;
        img.raycastTarget = false;
    }

    static void SetFrameColor(Image panel, Color color)
    {
        foreach (Transform child in panel.transform)
            if (child.name.StartsWith("Frame_")) child.GetComponent<Image>().color = color;
    }

    Text Label(Transform parent, string text, int size, Color color, TextAnchor align, Vector2 anchor, Vector2 pivot,
               Vector2 pos, Vector2 boxSize, FontStyle style = FontStyle.Bold, Color? outline = null)
    {
        var rt = NewRect("Text", parent, anchor, pivot, pos, boxSize);
        var t = rt.gameObject.AddComponent<Text>();
        t.font = _font;
        t.text = text;
        t.fontSize = size;
        t.fontStyle = style;
        t.color = color;
        t.alignment = align;
        t.horizontalOverflow = HorizontalWrapMode.Overflow;
        t.verticalOverflow = VerticalWrapMode.Overflow;
        t.supportRichText = true;
        t.raycastTarget = false;
        if (outline.HasValue)
        {
            var o = rt.gameObject.AddComponent<Outline>();
            o.effectColor = outline.Value;
            o.effectDistance = new Vector2(3f, -3f);
        }
        else
        {
            var s = rt.gameObject.AddComponent<Shadow>();
            s.effectColor = new Color(0f, 0f, 0f, 0.65f);
            s.effectDistance = new Vector2(1.5f, -1.5f);
        }
        return t;
    }

    /// <summary>A flat bar: dark background, optional pale "damage trail" behind the fill, and the fill itself.</summary>
    Bar MakeBar(Transform parent, string name, Vector2 pos, Vector2 size, Color color, bool ghost)
    {
        var back = Box(parent, name, TL, TL, pos, size, new Color(0.05f, 0.05f, 0.14f, 0.92f), new Color(1f, 1f, 1f, 0.10f), 1f);
        var bar = new Bar();

        if (ghost)
        {
            var g = Box(back.transform, "Trail", ML, ML, Vector2.zero, size, new Color(1f, 0.92f, 0.7f, 0.65f));
            g.rectTransform.anchorMin = new Vector2(0, 0); g.rectTransform.anchorMax = new Vector2(1, 1);
            g.rectTransform.offsetMin = Vector2.zero; g.rectTransform.offsetMax = Vector2.zero;
            bar.ghost = g.rectTransform;
        }

        var fill = Box(back.transform, "Fill", ML, ML, Vector2.zero, size, color);
        fill.rectTransform.anchorMin = new Vector2(0, 0); fill.rectTransform.anchorMax = new Vector2(1, 1);
        fill.rectTransform.offsetMin = Vector2.zero; fill.rectTransform.offsetMax = Vector2.zero;
        bar.fill = fill.rectTransform;
        bar.fillImage = fill;
        return bar;
    }

    /// <summary>Thin dark dividers over a bar, giving it a segmented, sci-fi look.</summary>
    void AddSegments(Bar bar, int segments)
    {
        var parent = bar.fill.parent;
        float width = ((RectTransform)parent).sizeDelta.x;
        for (int i = 1; i < segments; i++)
        {
            var d = Box(parent, "Seg" + i, ML, ML, new Vector2(width * i / segments - 1f, 0f),
                        new Vector2(2f, ((RectTransform)parent).sizeDelta.y), new Color(0.02f, 0.02f, 0.08f, 0.9f));
            d.rectTransform.SetAsLastSibling();
        }
    }

    // ── Generated sprites (one-time) ──────────────────────────────────────

    static Sprite Circle()
    {
        if (_circle != null) return _circle;
        _circle = MakeRadialSprite(128, (d) => Mathf.Clamp01((1f - d) * 64f), "HudCircle");
        return _circle;
    }

    static Sprite Ring()
    {
        if (_ring != null) return _ring;
        const float thickness = 0.025f;
        _ring = MakeRadialSprite(256, (d) =>
        {
            float edge = Mathf.Abs(d - (1f - thickness));
            return Mathf.Clamp01(1f - edge / thickness);
        }, "HudRing");
        return _ring;
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

    static Sprite MakeRadialSprite(int size, System.Func<float, float> alpha, string name, bool clampToCircle = true)
        => UiFactory.RadialSprite(size, alpha, name, clampToCircle);
}
}
