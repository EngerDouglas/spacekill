using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace OrbitRush
{

/// <summary>Menu UI building blocks (Apex look): canvas, labels, text entries, left-aligned column screens, sliders.</summary>
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
        _canvas.sortingOrder = 50;                      // above HUD (10), wheel (12), scope (15) and scoreboard (20)

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

    private static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
    }

    /// <summary>A plain filled rectangle (no border, no glow).</summary>
    private Image Box(Transform parent, string name, Vector2 anchor, Vector2 pivot, Vector2 pos, Vector2 size, Color color)
    {
        var rt = NewRect(name, parent, anchor, pivot, pos, size);
        var img = rt.gameObject.AddComponent<Image>();
        img.color = color;
        img.raycastTarget = false;
        return img;
    }

    /// <summary>
    /// A text label. <paramref name="font"/> picks the Inter weight (light for big titles, semibold for names, regular otherwise).
    /// A faint shadow keeps it readable over the blurred world.
    /// </summary>
    private Text Label(Transform parent, string text, int size, Color color, TextAnchor align,
                       Vector2 anchor, Vector2 pivot, Vector2 pos, Vector2 boxSize, Font font = null)
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

    private static Button MakeClickable(Graphic target, System.Action onClick)
    {
        target.raycastTarget = true;
        var b = target.gameObject.AddComponent<Button>();
        b.targetGraphic = target;
        b.transition = Selectable.Transition.None;
        var nav = b.navigation; nav.mode = Navigation.Mode.None; b.navigation = nav;
        b.onClick.AddListener(() => onClick());
        return b;
    }

    // ── Text entries ──────────────────────────────────────────────────────

    /// <summary>
    /// One clickable text entry: a transparent hit area with a left accent bar and the label. Hover or keyboard focus turns the
    /// label cyan, nudges it right and grows the bar (see <see cref="MenuItemFx"/>). Position is the entry's left-middle point.
    /// </summary>
    private MenuItemFx MenuItem(Transform parent, string text, Vector2 pos, System.Action onClick, int size = 34, bool primary = false,
                                float width = 560f, Vector2? anchor = null)
    {
        Vector2 a = anchor ?? ML;
        var hit = NewRect("Item_" + text, parent, a, ML, pos, new Vector2(width, 64f));
        var area = hit.gameObject.AddComponent<Image>();
        area.color = new Color(0f, 0f, 0f, 0f);
        area.raycastTarget = true;

        var bar = Box(hit, "Bar", ML, ML, Vector2.zero, new Vector2(3f, 8f), new Color(Cyan.r, Cyan.g, Cyan.b, 0f));
        var label = Label(hit, text, size, primary ? White : Soft, TextAnchor.MiddleLeft, ML, ML, new Vector2(24f, 0f),
                          new Vector2(width - 24f, 64f), primary ? _fontSemi : _fontLight);

        var btn = hit.gameObject.AddComponent<Button>();
        btn.targetGraphic = area;
        btn.transition = Selectable.Transition.None;
        var nav = btn.navigation; nav.mode = Navigation.Mode.None; btn.navigation = nav;

        var fx = hit.gameObject.AddComponent<MenuItemFx>();
        fx.label = label; fx.bar = bar; fx.normal = primary ? White : Soft; fx.active = Cyan; fx.onClick = onClick;
        btn.onClick.AddListener(() => onClick());

        _pending.Add(fx);
        return fx;
    }

    /// <summary>
    /// A left-aligned screen: dark gradient on the left, big light title, a small line under it, a column of text entries and a
    /// hint line at the bottom. Used by the home screen and the pause menu (the world behind is blurred, so the text needs no panel).
    /// </summary>
    private Text BuildColumn(Transform parent, string title, string subtitle, string[] names, System.Action[] actions, string hint)
    {
        var shadeRt = NewRect("Shade_Left", parent, CC, CC, Vector2.zero, Vector2.zero);
        Stretch(shadeRt);
        var shade = shadeRt.gameObject.AddComponent<Image>();
        shade.sprite = LeftShade();
        shade.raycastTarget = false;

        Label(parent, title, 64, White, TextAnchor.MiddleLeft, TL, TL, new Vector2(120f, -96f), new Vector2(900f, 84f), _fontLight);
        var sub = Label(parent, subtitle, 16, Soft, TextAnchor.MiddleLeft, TL, TL, new Vector2(124f, -182f), new Vector2(700f, 22f));

        for (int i = 0; i < names.Length; i++)
            MenuItem(parent, names[i], new Vector2(96f, 70f - i * 72f), actions[i], i == 0 ? 40 : 34, i == 0);

        Label(parent, hint, 13, Soft, TextAnchor.MiddleLeft, BL, BL, new Vector2(124f, 48f), new Vector2(900f, 20f));
        return sub;
    }

    private void Toast(string message)
    {
        if (_toast == null) return;
        _toast.text = message;
        _toast.color = new Color(1f, 1f, 1f, 1f);
        _toastTimer = 2.2f;
    }

    // ── Slider: thin track, cyan fill, small round handle ─────────────────

    private Slider MakeSlider(Transform parent, Vector2 pos, Vector2 size, float min, float max, float value, UnityEngine.Events.UnityAction<float> onChange)
    {
        var rt = NewRect("Slider", parent, TL, TL, pos, size);
        var slider = rt.gameObject.AddComponent<Slider>();

        var back = Box(rt, "Background", CC, CC, Vector2.zero, new Vector2(size.x, 3f), Track);
        back.rectTransform.anchorMin = new Vector2(0f, 0.5f); back.rectTransform.anchorMax = new Vector2(1f, 0.5f);
        back.rectTransform.offsetMin = new Vector2(0f, -1.5f); back.rectTransform.offsetMax = new Vector2(0f, 1.5f);

        var fillArea = NewRect("Fill Area", rt, CC, CC, Vector2.zero, Vector2.zero);
        fillArea.anchorMin = new Vector2(0f, 0.5f); fillArea.anchorMax = new Vector2(1f, 0.5f);
        fillArea.offsetMin = new Vector2(0f, -1.5f); fillArea.offsetMax = new Vector2(0f, 1.5f);
        var fill = Box(fillArea, "Fill", CC, CC, Vector2.zero, Vector2.zero, Cyan);
        fill.rectTransform.anchorMin = new Vector2(0f, 0f); fill.rectTransform.anchorMax = new Vector2(0f, 1f);
        fill.rectTransform.offsetMin = Vector2.zero; fill.rectTransform.offsetMax = new Vector2(8f, 0f);

        var handleArea = NewRect("Handle Slide Area", rt, CC, CC, Vector2.zero, Vector2.zero);
        handleArea.anchorMin = Vector2.zero; handleArea.anchorMax = Vector2.one;
        handleArea.offsetMin = new Vector2(9f, 0f); handleArea.offsetMax = new Vector2(-9f, 0f);
        var handle = Box(handleArea, "Handle", CC, CC, Vector2.zero, new Vector2(18f, 18f), White);
        handle.sprite = Dot();
        handle.rectTransform.anchorMin = new Vector2(0f, 0.5f); handle.rectTransform.anchorMax = new Vector2(0f, 0.5f);
        handle.rectTransform.sizeDelta = new Vector2(18f, 18f);
        handle.raycastTarget = true;

        slider.fillRect = fill.rectTransform;
        slider.handleRect = handle.rectTransform;
        slider.targetGraphic = handle;
        slider.direction = Slider.Direction.LeftToRight;
        slider.minValue = min;
        slider.maxValue = max;
        slider.SetValueWithoutNotify(Mathf.Clamp(value, min, max));
        slider.onValueChanged.AddListener(onChange);
        return slider;
    }

    // ── Generated sprites ─────────────────────────────────────────────────

    private static Sprite _dot, _leftShade;

    private static Sprite Dot()
    {
        if (_dot == null) _dot = UiFactory.RadialSprite(64, d => Mathf.Clamp01((1f - d) * 32f), "MenuDot");
        return _dot;
    }

    /// <summary>Black fading to transparent from left to right: darkens the text side of the screen.</summary>
    private static Sprite LeftShade()
    {
        if (_leftShade != null) return _leftShade;
        const int w = 256;
        var tex = new Texture2D(w, 1, TextureFormat.RGBA32, false) { name = "MenuLeftShade", wrapMode = TextureWrapMode.Clamp };
        for (int x = 0; x < w; x++)
        {
            float t = x / (w - 1f);
            tex.SetPixel(x, 0, new Color(0f, 0f, 0f, 0.78f * Mathf.Pow(1f - t, 1.6f)));
        }
        tex.Apply();
        _leftShade = Sprite.Create(tex, new Rect(0, 0, w, 1), new Vector2(0.5f, 0.5f));
        return _leftShade;
    }
}
}
