using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace OrbitRush
{

/// <summary>HUD construction: builds every panel, bar, slot, crosshair and vignette in code.</summary>
public partial class HUD
{
    // ══════════════════════════════════════════════════════════════════════
    // BUILD
    // ══════════════════════════════════════════════════════════════════════

    void BuildHUD()
    {
        var canvas = GetComponent<Canvas>();
        if (canvas == null) canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 10;

        var cs = gameObject.GetComponent<CanvasScaler>() ?? gameObject.AddComponent<CanvasScaler>();
        cs.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        cs.referenceResolution = new Vector2(1920, 1080);
        cs.matchWidthOrHeight = 0.5f;

        if (gameObject.GetComponent<GraphicRaycaster>() == null)
            gameObject.AddComponent<GraphicRaycaster>();

        BuildVignette();       // first = drawn behind everything else
        BuildPlanetAndRadar();
        BuildTopCenter();
        BuildTopRight();
        BuildVitals();
        BuildWeaponCard();
        BuildSlots();
        BuildBanner();
        BuildCrosshair();
    }


    // ══════════════════════════════════════════════════════════════════════
    // ART PANELS (Resources/HudArt/*.png — the supplied PlanetaryWar HUD pack with its numbers erased;
    // the live values are drawn on top as text). Falls back to the code-built neon panels if the art is missing.
    // ══════════════════════════════════════════════════════════════════════

    const float ArtScale = 0.7f;           // PNG pixels -> canvas units
    private bool _art;
    private float _radarRadius = RadarRadius;

    static Sprite ArtSprite(string name) => Resources.Load<Sprite>(ResourcePaths.HudArtFolder + name);
    static Vector2 At(float x, float y) => new Vector2(x * ArtScale, -y * ArtScale);

    RectTransform ArtPanel(string name, Sprite sprite, Vector2 anchor, Vector2 pivot, Vector2 pos)
    {
        var rt = NewRect(name, transform, anchor, pivot, pos, sprite.rect.size * ArtScale);
        var img = rt.gameObject.AddComponent<Image>();
        img.sprite = sprite; img.raycastTarget = false;
        return rt;
    }

    /// <summary>Text placed in PNG pixel coordinates (top-left of the panel = 0,0).</summary>
    Text ArtLabel(RectTransform panel, string text, int size, Color color, TextAnchor align, float x, float y, float w, float h,
                  FontStyle style = FontStyle.BoldAndItalic, int minSize = 0)
    {
        Vector2 pivot = align == TextAnchor.MiddleLeft ? ML : align == TextAnchor.MiddleRight ? MR : CC;
        float px = align == TextAnchor.MiddleLeft ? x : align == TextAnchor.MiddleRight ? x + w : x + w * 0.5f;
        var rt = NewRect("Text", panel, TL, pivot, At(px, y + h * 0.5f), new Vector2(w, h) * ArtScale);
        var t = rt.gameObject.AddComponent<Text>();
        t.font = _font; t.text = text; t.fontSize = size; t.fontStyle = style; t.color = color; t.alignment = align;
        t.horizontalOverflow = HorizontalWrapMode.Overflow; t.verticalOverflow = VerticalWrapMode.Overflow;
        t.supportRichText = true; t.raycastTarget = false;
        if (minSize > 0)      // shrink to fit the box instead of spilling over the art
        {
            t.resizeTextForBestFit = true; t.resizeTextMinSize = minSize; t.resizeTextMaxSize = size;
            t.horizontalOverflow = HorizontalWrapMode.Wrap; t.verticalOverflow = VerticalWrapMode.Truncate;
        }
        var sh = rt.gameObject.AddComponent<Shadow>();
        sh.effectColor = new Color(0f, 0f, 0f, 0.7f); sh.effectDistance = new Vector2(1.5f, -1.5f);
        return t;
    }

    /// <summary>Rounded bar placed in PNG pixel coordinates: a dark track plus a fill (and optionally a damage trail).</summary>
    Bar ArtBar(RectTransform panel, string name, float x, float y, float w, float h, Color color, bool ghost)
    {
        var size = new Vector2(w, h) * ArtScale;
        var track = NewRect(name, panel, TL, TL, At(x, y), size);
        return MakePillBar(track, size.y, color, ghost, 0.95f);
    }

    Bar MakePillBar(RectTransform track, float height, Color color, bool ghost, float trackAlpha)
    {
        var tImg = track.gameObject.AddComponent<Image>();
        tImg.sprite = Pill(); tImg.type = Image.Type.Sliced; tImg.pixelsPerUnitMultiplier = 32f / height;
        tImg.color = new Color(0.03f, 0.03f, 0.10f, trackAlpha); tImg.raycastTarget = false;
        var bar = new Bar();
        if (ghost) bar.ghost = PillFill(track, "Trail", new Color(1f, 0.92f, 0.7f, 0.65f), height);
        var fill = PillFill(track, "Fill", color, height);
        bar.fill = fill; bar.fillImage = fill.GetComponent<Image>();
        return bar;
    }

    static RectTransform PillFill(RectTransform track, string name, Color color, float height)
    {
        var rt = NewRect(name, track, CC, CC, Vector2.zero, Vector2.zero);
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
        var img = rt.gameObject.AddComponent<Image>();
        img.sprite = Pill(); img.type = Image.Type.Sliced; img.pixelsPerUnitMultiplier = 32f / height;
        img.color = color; img.raycastTarget = false;
        return rt;
    }

    static Sprite _pill;
    static Sprite Pill()
    {
        if (_pill != null) return _pill;
        const int w = 64, h = 32;
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { name = "HudPill", wrapMode = TextureWrapMode.Clamp };
        var px = new Color32[w * h];
        float r = h * 0.5f;
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float cx = Mathf.Clamp(x + 0.5f, r, w - r), cy = h * 0.5f;
                float d = Mathf.Sqrt((x + 0.5f - cx) * (x + 0.5f - cx) + (y + 0.5f - cy) * (y + 0.5f - cy));
                px[y * w + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(r - d + 0.5f) * 255f));
            }
        tex.SetPixels32(px); tex.Apply();
        _pill = Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, new Vector4(16, 0, 16, 0));
        return _pill;
    }

    void BuildPlanetAndRadar()
    {
        var loc = ArtSprite("Ubicacion"); var rad = ArtSprite("Radar");
        if (loc == null || rad == null) return;
        _art = true;

        var p = ArtPanel("PlanetPanel", loc, TL, TL, new Vector2(30, -30));
        _planetName = ArtLabel(p, "-", 34, White, TextAnchor.MiddleLeft, 58, 52, 410, 62, FontStyle.BoldAndItalic, 16);
        _gravityText = ArtLabel(p, "", 18, Muted, TextAnchor.MiddleLeft, 78, 114, 360, 36, FontStyle.Bold);

        var r = ArtPanel("RadarFrame", rad, TL, TL, new Vector2(30, -30 - loc.rect.height * ArtScale - 10));
        // Radar disc centre / radius measured on the art (PNG px): centre (173,197), outer ring radius 128
        var disc = NewRect("Radar", r, TL, CC, At(173, 197), Vector2.one * 256f * ArtScale);
        _radar = disc;
        _radarRadius = 128f * ArtScale;
    }

    void BuildTopCenter()
    {
        var spr = ArtSprite("Tiempo");
        if (spr == null) return;

        var p = ArtPanel("TimerChip", spr, TC, TC, new Vector2(0, -26));
        _timerText = ArtLabel(p, "10:00", 58, White, TextAnchor.MiddleCenter, 120, 52, 280, 92);
        _modeText = Label(transform, "", 17, Muted, TextAnchor.UpperCenter, TC, TC, new Vector2(0, -26 - spr.rect.height * ArtScale - 6), new Vector2(420, 24), FontStyle.Bold);

        _teamGroup = NewRect("Teams", transform, TC, TC, Vector2.zero, Vector2.zero).gameObject;
        var pink = Neon(_teamGroup.transform, "PinkScore", TC, TC, new Vector2(-285, -34), new Vector2(170, 62), Pink, true, false, false, true, 16f);
        _pinkScore = Label(pink.transform, "0", 42, Pink, TextAnchor.MiddleCenter, CC, CC, new Vector2(0, -6), new Vector2(170, 62), FontStyle.BoldAndItalic);
        Tab(pink.transform, "ROSA", Pink, 14f, 13);
        var cyan = Neon(_teamGroup.transform, "CyanScore", TC, TC, new Vector2(285, -34), new Vector2(170, 62), Cyan, false, true, true, false, 16f);
        _cyanScore = Label(cyan.transform, "0", 42, Cyan, TextAnchor.MiddleCenter, CC, CC, new Vector2(0, -6), new Vector2(170, 62), FontStyle.BoldAndItalic);
        Tab(cyan.transform, "CIAN", Cyan, 14f, 13);
        _teamGroup.SetActive(false);
    }

    void BuildTopRight()
    {
        var spr = ArtSprite("Combate");
        if (spr == null) return;

        var p = ArtPanel("KillsPanel", spr, TR, TR, new Vector2(-30, -30));
        _killsText = ArtLabel(p, "0", 48, Pink, TextAnchor.MiddleLeft, 140, 92, 120, 70);
        _pveText = ArtLabel(p, "0", 48, Cyan, TextAnchor.MiddleLeft, 440, 92, 120, 70);
    }

    void BuildVitals()
    {
        var spr = ArtSprite("Vitales");
        if (spr == null) return;

        var p = ArtPanel("Vitals", spr, BL, BL, new Vector2(30, 30));
        _healthText = ArtLabel(p, "100", 52, White, TextAnchor.MiddleCenter, 40, 88, 134, 72);

        // The art's three bars (PNG px): shield / energy / jetpack
        _shield  = ArtBar(p, "Shield",  348, 63,  429, 21, Cyan,   false);
        _stamina = ArtBar(p, "Energy",  348, 103, 429, 21, Yellow, false);
        _jet     = ArtBar(p, "Jetpack", 348, 143, 429, 21, Pink,   false);
        _shieldText = ArtLabel(p, "", 12, Cyan, TextAnchor.MiddleLeft, 320, 63, 10, 10, FontStyle.Bold);   // unused in art mode

        // Health, dash, dodge and katana aren't in the art: a matching "ESTADO" panel stacked above it
        float w = spr.rect.width * ArtScale;
        float top = 30 + spr.rect.height * ArtScale + 12;
        var panel = Neon(transform, "StatusPanel", BL, BL, new Vector2(30, top), new Vector2(w, 86), Cyan, false, true, false, true, 14f);
        Tab(panel.transform, "ESTADO", Cyan, 24f, 14);

        Label(panel.transform, "VIDA", 12, Muted, TextAnchor.MiddleLeft, TL, TL, new Vector2(26, -22), new Vector2(40, 16), FontStyle.Bold);
        var healthTrack = NewRect("VIDA", panel.transform, TL, TL, new Vector2(70, -24), new Vector2(w - 70 - 26, 12));
        _health = MakePillBar(healthTrack, 12f, Green, true, 0.92f);

        float gap = 14f, colW = (w - 52f - 2f * gap) / 3f;
        string[] names = { "DASH", "ESQUIVA [C]", "KATANA [V]" };
        Color[] cols = { Pink, Cyan, Pink };
        var bars = new Bar[3];
        for (int i = 0; i < 3; i++)
        {
            float x = 26f + i * (colW + gap);
            Label(panel.transform, names[i], 12, Muted, TextAnchor.MiddleLeft, TL, TL, new Vector2(x, -44), new Vector2(colW, 16), FontStyle.Bold);
            var track = NewRect(names[i], panel.transform, TL, TL, new Vector2(x, -62), new Vector2(colW, 8));
            bars[i] = MakePillBar(track, 8f, cols[i], false, 0.92f);
        }
        _dash = bars[0]; _dodge = bars[1]; _katana = bars[2];
    }

    /// <summary>A labelled slim bar for the strip above the vitals panel (x, y from the strip's bottom-left, canvas units).</summary>
    Bar ExtraBar(RectTransform strip, string label, float x, float y, float width, Color color, bool ghost)
    {
        float h = ghost ? 12f : 8f;
        var track = NewRect(label, strip, BL, BL, new Vector2(x, y), new Vector2(width, h));
        var bar = MakePillBar(track, h, color, ghost, 0.92f);
        Label(strip, label, 12, Muted, TextAnchor.LowerLeft, BL, BL, new Vector2(x + 2f, y + h + 1f), new Vector2(width, 16), FontStyle.Bold);
        return bar;
    }

    void BuildWeaponCard()
    {
        var spr = ArtSprite("Armas");
        if (spr == null) return;

        var p = ArtPanel("WeaponCard", spr, BR, BR, new Vector2(-30, 30));
        _weaponName = ArtLabel(p, "SIN ARMA", 30, White, TextAnchor.MiddleLeft, 78, 62, 400, 56);
        _ammoCurrent = ArtLabel(p, "0", 64, White, TextAnchor.MiddleRight, 470, 56, 150, 76);
        _ammoMax = ArtLabel(p, "/ 0", 28, Muted, TextAnchor.MiddleLeft, 626, 70, 110, 56);

        // Reload / heat bars under the weapon name row
        _reloadGroup = NewRect("Reload", p, TL, TL, At(78, 124), new Vector2(620, 30) * ArtScale).gameObject;
        _reloadLabel = Label(_reloadGroup.transform, "RECARGANDO", 13, Yellow, TextAnchor.UpperLeft, TL, TL, Vector2.zero, new Vector2(160, 14), FontStyle.Bold);
        _reload = SlimBar(_reloadGroup.transform, new Vector2(0, -16), 620 * ArtScale, Yellow);
        _reloadGroup.SetActive(false);

        _heatGroup = NewRect("Heat", p, TL, TL, At(78, 124), new Vector2(620, 30) * ArtScale).gameObject;
        Label(_heatGroup.transform, "CALOR", 13, new Color(1f, 0.5f, 0.1f), TextAnchor.UpperLeft, TL, TL, Vector2.zero, new Vector2(120, 14), FontStyle.Bold);
        _heat = SlimBar(_heatGroup.transform, new Vector2(0, -16), 620 * ArtScale, new Color(1f, 0.45f, 0.05f));
        _heatGroup.SetActive(false);

        // Grenade names sit in the art's two chips, to the right of the [Q] / [F] icons
        _grenadeQ = ArtLabel(p, "", 17, White, TextAnchor.MiddleLeft, 172, 178, 225, 40, FontStyle.Bold, 11);
        _grenadeF = ArtLabel(p, "", 15, White, TextAnchor.MiddleLeft, 566, 178, 185, 40, FontStyle.Bold, 10);
    }

    Bar SlimBar(Transform parent, Vector2 pos, float width, Color color)
    {
        var track = NewRect("Bar", parent, TL, TL, pos, new Vector2(width, 8));
        return MakePillBar(track, 8f, color, false, 0.92f);
    }

    // ── Neon frame helpers ────────────────────────────────────────────────

    /// <summary>Angular neon panel (chamfered corners, glow, corner ticks) — see NeonPanel.</summary>
    NeonPanel Neon(Transform parent, string name, Vector2 anchor, Vector2 pivot, Vector2 pos, Vector2 size, Color accentColor,
                   bool cutTL = false, bool cutTR = true, bool cutBR = false, bool cutBL = true, float chamfer = 16f)
    {
        var rt = NewRect(name, parent, anchor, pivot, pos, size);
        var p = rt.gameObject.AddComponent<NeonPanel>();
        p.border = accentColor;
        p.cutTL = cutTL; p.cutTR = cutTR; p.cutBR = cutBR; p.cutBL = cutBL;
        p.chamfer = chamfer;
        return p;
    }

    /// <summary>Slanted title tab hanging on the panel's top-left edge (like "DRON GUARDIÁN" in the art).</summary>
    NeonPanel Tab(Transform panel, string text, Color accentColor, float x = 24f, int fontSize = 16)
    {
        float w = text.Length * fontSize * 0.66f + 46f;
        var rt = NewRect("Tab_" + text, panel, TL, new Vector2(0f, 0.5f), new Vector2(x, 0f), new Vector2(w, 30f));
        var p = rt.gameObject.AddComponent<NeonPanel>();
        p.shape = NeonPanel.Shape.Parallelogram;
        p.slant = 12f;
        p.border = new Color(1f, 1f, 1f, 0.95f);
        p.borderThickness = 1.5f;
        p.glowThickness = 6f;
        p.glowAlpha = 0.35f;
        p.cornerAccents = false;
        Color top = Color.Lerp(accentColor, White, 0.18f); top.a = 1f;
        Color bottom = accentColor * 0.72f; bottom.a = 1f;
        p.fillTop = top; p.fillBottom = bottom;
        Label(rt, text, fontSize, White, TextAnchor.MiddleCenter, CC, CC, Vector2.zero, new Vector2(w, 30f), FontStyle.BoldAndItalic);
        return p;
    }

    /// <summary>Hexagon badge (key hints, icons).</summary>
    NeonPanel HexBadge(Transform parent, string text, Color accentColor, Vector2 anchor, Vector2 pivot, Vector2 pos, Vector2 size, int fontSize = 20)
    {
        var rt = NewRect("Hex_" + text, parent, anchor, pivot, pos, size);
        var p = rt.gameObject.AddComponent<NeonPanel>();
        p.shape = NeonPanel.Shape.Hexagon;
        p.border = accentColor;
        p.borderThickness = 2f;
        p.glowThickness = 8f;
        p.cornerAccents = false;
        p.fillTop = Color.Lerp(NavyDeep, accentColor, 0.30f);
        p.fillBottom = NavyDeep;
        Label(rt, text, fontSize, White, TextAnchor.MiddleCenter, CC, CC, Vector2.zero, size, FontStyle.BoldAndItalic);
        return p;
    }

    // ── Top-left: planet + radar ──────────────────────────────────────────

    // ── Top-centre: timer, mode, team scores ──────────────────────────────

    // ── Top-right: kills ──────────────────────────────────────────────────

    // ── Bottom-left: vitals ───────────────────────────────────────────────

    void MiniBar(Transform parent, string label, float y, Color color, out Bar bar)
    {
        Label(parent, label, 13, Muted, TextAnchor.MiddleLeft, TL, TL, new Vector2(26, y), new Vector2(120, 18), FontStyle.Bold);
        bar = MakeBar(parent, label, new Vector2(170, y - 5), new Vector2(374, 9), color, ghost: false);
    }

    // ── Bottom-right: weapon ──────────────────────────────────────────────

    // ── Bottom-centre: weapon slots ───────────────────────────────────────

    void BuildSlots()
    {
        _slotsRoot = NewRect("Slots", transform, BC, BC, new Vector2(0, 30), new Vector2(560, 64));
    }

    void BuildSlotBoxes(int count)
    {
        foreach (Transform child in _slotsRoot) Destroy(child.gameObject);

        _slotBg = new NeonPanel[count]; _slotKey = new Text[count]; _slotName = new Text[count]; _slotState = new int[count];
        const float w = 160f, gap = 10f;
        float total = count * w + (count - 1) * gap;
        for (int i = 0; i < count; i++)
        {
            float x = -total / 2f + w / 2f + i * (w + gap);
            var bg = Neon(_slotsRoot, "Slot" + i, CC, CC, new Vector2(x, 0), new Vector2(w, 58), Cyan, false, true, false, true, 12f);
            bg.glowThickness = 8f;
            _slotBg[i] = bg;
            _slotState[i] = -1;

            var badge = HexBadge(bg.transform, (i + 1).ToString(), Cyan, ML, ML, new Vector2(8, 0), new Vector2(40, 38), 18);
            _slotKey[i] = badge.GetComponentInChildren<Text>();
            _slotName[i] = Label(bg.transform, "", 16, White, TextAnchor.MiddleLeft, ML, ML, new Vector2(56, 0), new Vector2(98, 58), FontStyle.Bold);
        }
        _builtSlotCount = count;
    }

    // ── Event banner ──────────────────────────────────────────────────────

    void BuildBanner()
    {
        var panel = Neon(transform, "EventBanner", new Vector2(0.5f, 0.74f), CC, Vector2.zero, new Vector2(820, 90), Pink, true, true, true, true, 24f);
        panel.borderThickness = 3f;
        panel.glowThickness = 14f;
        Tab(panel.transform, "ALERTA", Pink);
        _bannerText = Label(panel.transform, "", 34, White, TextAnchor.MiddleCenter, CC, CC, new Vector2(0, -4), new Vector2(780, 70), FontStyle.BoldAndItalic, outline: Pink);
        _banner = panel.gameObject;
        _banner.SetActive(false);
    }

    // ── Crosshair + hit marker ────────────────────────────────────────────
    // Marks exactly where WeaponBase.GetAimDirection() raycasts from — a camera's forward vector passes
    // through screen centre, so the centre of this reticle is always where shots are aimed.

    void BuildCrosshair()
    {
        var root = NewRect("Crosshair", transform, CC, CC, Vector2.zero, Vector2.zero);
        _crosshairRoot = root.gameObject;

        _centerDot = Box(root, "Dot", CC, CC, Vector2.zero, new Vector2(4, 4), White);

        // Four ticks: up, down, left, right
        _ticks = new RectTransform[4]; _tickImages = new Image[4];
        Vector2[] sizes = { new Vector2(3, 12), new Vector2(3, 12), new Vector2(12, 3), new Vector2(12, 3) };
        for (int i = 0; i < 4; i++)
        {
            var img = Box(root, "Tick" + i, CC, CC, Vector2.zero, sizes[i], White);
            img.gameObject.AddComponent<Shadow>().effectColor = new Color(0, 0, 0, 0.7f);
            _ticks[i] = img.rectTransform; _tickImages[i] = img;
        }

        // Hit marker: four diagonal ticks around the centre
        _hitMarker = NewRect("HitMarker", root, CC, CC, Vector2.zero, Vector2.zero);
        _hitImages = new Image[4];
        for (int i = 0; i < 4; i++)
        {
            var img = Box(_hitMarker, "Hit" + i, CC, CC, Vector2.zero, new Vector2(3, 14), White);
            float ang = 45f + i * 90f;
            img.rectTransform.localRotation = Quaternion.Euler(0, 0, ang);
            img.rectTransform.anchoredPosition = Quaternion.Euler(0, 0, ang) * new Vector2(0, 17);
            _hitImages[i] = img;
        }
        _hitMarker.gameObject.SetActive(false);
    }

    // ── Damage vignette ───────────────────────────────────────────────────

    void BuildVignette()
    {
        var rt = NewRect("DamageVignette", transform, CC, CC, Vector2.zero, Vector2.zero);
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
        _vignette = rt.gameObject.AddComponent<Image>();
        _vignette.sprite = VignetteSprite();
        _vignette.color = new Color(1f, 0.05f, 0.1f, 0f);
        _vignette.raycastTarget = false;
    }
}
}
