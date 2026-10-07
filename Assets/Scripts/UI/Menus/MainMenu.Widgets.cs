using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace OrbitRush
{

/// <summary>Main-menu UI building blocks: canvas, boxes, buttons, labels.</summary>
public partial class MainMenu
{
    // ══════════════════════════════════════════════════════════════════════
    // UI building blocks
    // ══════════════════════════════════════════════════════════════════════

    private static Font LoadFont() => UiFactory.LoadFont();

    private void BuildCanvas()
    {
        var go = new GameObject("MainMenuCanvas");
        go.transform.SetParent(transform, false);
        _canvas = go.AddComponent<Canvas>();
        _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _canvas.sortingOrder = 50;                      // above HUD (10), scope (15) and scoreboard (20)

        var scaler = go.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;

        go.AddComponent<GraphicRaycaster>();
        _canvasRoot = go.transform;
    }

    static readonly Vector2 TL = new Vector2(0f, 1f), TR = new Vector2(1f, 1f), BL = new Vector2(0f, 0f),
                            BR = new Vector2(1f, 0f), TC = new Vector2(0.5f, 1f), CC = new Vector2(0.5f, 0.5f),
                            ML = new Vector2(0f, 0.5f), MR = new Vector2(1f, 0.5f), BC = new Vector2(0.5f, 0f);

    private static RectTransform NewRect(string name, Transform parent, Vector2 anchor, Vector2 pivot, Vector2 pos, Vector2 size)
        => UiFactory.NewRect(name, parent, anchor, pivot, pos, size);

    /// <summary>
    /// A plain filled rectangle — or, when a border colour is given, an angular neon panel
    /// (chamfered corners, inner glow, corner ticks; see NeonPanel).
    /// </summary>
    private Graphic Box(Transform parent, string name, Vector2 anchor, Vector2 pivot, Vector2 pos, Vector2 size,
                        Color color, Color? border = null, float borderWidth = 2f)
    {
        var rt = NewRect(name, parent, anchor, pivot, pos, size);
        if (border.HasValue)
            return MakeNeon(rt, size, color, border.Value, borderWidth);

        var img = rt.gameObject.AddComponent<Image>();
        img.color = color;
        img.raycastTarget = false;
        return img;
    }

    private static NeonPanel MakeNeon(RectTransform rt, Vector2 size, Color fill, Color border, float borderWidth)
    {
        var p = rt.gameObject.AddComponent<NeonPanel>();
        float small = Mathf.Min(size.x, size.y);
        bool tiny = small < 44f;

        p.border = border;
        p.borderThickness = tiny ? Mathf.Min(borderWidth, 2f) : Mathf.Max(2.5f, borderWidth);
        p.chamfer = Mathf.Clamp(small * 0.22f, 0f, 20f);
        p.cutTL = false; p.cutTR = true; p.cutBR = false; p.cutBL = true;

        // Vertical gradient from the given fill: a touch lighter on top, clearly darker at the bottom.
        Color top = Color.Lerp(fill, Color.white, 0.07f); top.a = fill.a;
        Color bottom = fill * 0.55f; bottom.a = fill.a;
        p.fillTop = top; p.fillBottom = bottom;

        p.cornerAccents = !tiny;
        p.glowThickness = tiny ? 0f : Mathf.Min(11f, small * 0.12f + 4f);
        return p;
    }

    private NeonPanel HexBox(Transform parent, string name, Vector2 anchor, Vector2 pivot, Vector2 pos, Vector2 size, Color accent)
    {
        var rt = NewRect(name, parent, anchor, pivot, pos, size);
        var p = rt.gameObject.AddComponent<NeonPanel>();
        p.shape = NeonPanel.Shape.Hexagon;
        p.border = accent;
        p.borderThickness = 2.5f;
        p.glowThickness = 10f;
        p.cornerAccents = false;
        p.fillTop = Color.Lerp(NavyDeep, accent, 0.34f);
        p.fillBottom = NavyDeep;
        return p;
    }

    /// <summary>Slanted title tab hanging on a panel's top-left edge (like "DRON GUARDIÁN" in the art).</summary>
    private NeonPanel Tab(Transform panel, string text, Color accent, float x = 24f, int fontSize = 18)
    {
        float w = text.Length * fontSize * 0.66f + 48f;
        var rt = NewRect("Tab_" + text, panel, TL, new Vector2(0f, 0.5f), new Vector2(x, 0f), new Vector2(w, 34f));
        var p = rt.gameObject.AddComponent<NeonPanel>();
        p.shape = NeonPanel.Shape.Parallelogram;
        p.slant = 13f;
        p.border = new Color(1f, 1f, 1f, 0.95f);
        p.borderThickness = 1.5f;
        p.glowThickness = 6f;
        p.glowAlpha = 0.35f;
        p.cornerAccents = false;
        Color top = Color.Lerp(accent, White, 0.18f); top.a = 1f;
        Color bottom = accent * 0.72f; bottom.a = 1f;
        p.fillTop = top; p.fillBottom = bottom;
        Label(rt, text, fontSize, White, TextAnchor.MiddleCenter, CC, CC, Vector2.zero, new Vector2(w, 34f), FontStyle.BoldAndItalic);
        return p;
    }

    private Text Label(Transform parent, string text, int size, Color color, TextAnchor align,
                       Vector2 anchor, Vector2 pivot, Vector2 pos, Vector2 boxSize,
                       FontStyle style = FontStyle.Bold, Color? outline = null)
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
        return t;
    }

    private static Button MakeClickable(Graphic target, System.Action onClick)
    {
        target.raycastTarget = true;
        var b = target.gameObject.AddComponent<Button>();
        b.targetGraphic = target;
        var cb = b.colors;
        cb.normalColor = new Color(0.88f, 0.88f, 0.94f, 1f);
        cb.highlightedColor = Color.white;
        cb.pressedColor = new Color(0.7f, 0.7f, 0.8f, 1f);
        cb.selectedColor = cb.normalColor;
        cb.fadeDuration = 0.06f;
        b.colors = cb;
        var nav = b.navigation; nav.mode = Navigation.Mode.None; b.navigation = nav;
        b.onClick.AddListener(() => onClick());
        return b;
    }

    /// <summary>Standard wide menu button used by the pause / settings panels.</summary>
    private Graphic MenuButton(Transform parent, string text, Vector2 pos, Color accent, System.Action onClick, float width = 520f, float height = 84f)
    {
        var bg = Box(parent, "Btn_" + text, CC, CC, pos, new Vector2(width, height), Color.Lerp(NavyDeep, accent, 0.28f), accent);
        Label(bg.transform, text, 32, White, TextAnchor.MiddleCenter, CC, CC, Vector2.zero, new Vector2(width, height));
        MakeClickable(bg, onClick);
        return bg;
    }

    /// <summary>Feature card: accent bar, icon badge, title and subtitle (the centre of the home screen).</summary>
    private Text Card(Transform parent, string title, string subtitle, Vector2 anchor, Vector2 pivot, Vector2 pos,
                      Color accent, string icon, System.Action onClick)
    {
        var bg = Box(parent, "Card_" + title, anchor, pivot, pos, new Vector2(540f, 114f), Navy, accent);
        var badge = HexBox(bg.transform, "Badge", ML, ML, new Vector2(22f, 0f), new Vector2(88f, 80f), accent);
        Label(badge.transform, icon, 44, accent, TextAnchor.MiddleCenter, CC, CC, Vector2.zero, new Vector2(88f, 80f));
        Label(bg.transform, title, 32, White, TextAnchor.UpperLeft, TL, TL, new Vector2(128f, -20f), new Vector2(400f, 40f), FontStyle.BoldAndItalic);
        var sub = Label(bg.transform, subtitle, 20, Muted, TextAnchor.UpperLeft, TL, TL, new Vector2(128f, -64f), new Vector2(400f, 30f), FontStyle.Normal);
        MakeClickable(bg, onClick);
        return sub;
    }

    private void NavItem(Transform parent, string label, string icon, float y, bool selected, System.Action onClick)
    {
        Color accent = selected ? Pink : Cyan;
        var bg = Box(parent, "Nav_" + label, ML, ML, new Vector2(0f, y), new Vector2(390f, 74f),
                     selected ? Color.Lerp(NavyDeep, Pink, 0.40f) : Navy,
                     selected ? Pink : new Color(0f, 0.72f, 0.95f, 0.55f));
        var hex = HexBox(bg.transform, "Icon", ML, ML, new Vector2(16f, 0f), new Vector2(54f, 48f), accent);
        Label(hex.transform, icon, 26, selected ? White : Cyan, TextAnchor.MiddleCenter, CC, CC, Vector2.zero, new Vector2(54f, 48f));
        Label(bg.transform, label, 26, White, TextAnchor.MiddleLeft, ML, ML, new Vector2(92f, 0f), new Vector2(290f, 74f), FontStyle.BoldAndItalic);
        MakeClickable(bg, onClick);
    }

    private void Toast(string message)
    {
        if (_toast == null) return;
        _toast.text = message;
        _toast.color = new Color(1f, 1f, 1f, 1f);
        _toastTimer = 2.2f;
    }
}
}
