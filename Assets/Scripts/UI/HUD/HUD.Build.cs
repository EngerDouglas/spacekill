using UnityEngine;
using UnityEngine.UI;

namespace OrbitRush
{

/// <summary>HUD construction: builds every widget in code (Apex layout, see the class summary in HUD.cs).</summary>
public partial class HUD
{
    // ══════════════════════════════════════════════════════════════════════
    // BUILD
    // ══════════════════════════════════════════════════════════════════════

    const float Margin = 56f;

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
        BuildRadarAndPlanet();
        BuildTopCenter();
        BuildTopRight();
        BuildCoins();
        BuildVitals();
        BuildEnergy();
        BuildWeapon();
        BuildBanner();
        BuildCrosshair();
        BuildDamageArcs();
        BuildShieldWarning();

        // The weapon wheel builds itself under this canvas, drawn on top of everything above.
        if (GetComponent<WeaponWheel>() == null) gameObject.AddComponent<WeaponWheel>();
        if (GetComponent<ShopPanel>() == null) gameObject.AddComponent<ShopPanel>();     // vending machine (E)
    }

    // ── Top-left: radar + planet ──────────────────────────────────────────

    void BuildRadarAndPlanet()
    {
        _radar = NewRect("Radar", transform, TL, TL, new Vector2(Margin, -Margin), new Vector2(200f, 200f));
        _radarRadius = RadarRadius;

        var back = _radar.gameObject.AddComponent<Image>();
        back.sprite = Circle(); back.color = new Color(Ink.r, Ink.g, Ink.b, 0.40f); back.raycastTarget = false;

        var outer = Box(_radar, "Ring", CC, CC, Vector2.zero, new Vector2(200f, 200f), new Color(1f, 1f, 1f, 0.40f));
        outer.sprite = Ring(0.015f);
        var inner = Box(_radar, "RingInner", CC, CC, Vector2.zero, new Vector2(100f, 100f), new Color(1f, 1f, 1f, 0.13f));
        inner.sprite = Ring(0.03f);
        var me = Box(_radar, "Player", CC, CC, Vector2.zero, new Vector2(14f, 14f), White);
        me.sprite = Triangle();

        _planetName  = Txt(transform, "-", 15, White, TextAnchor.UpperLeft, TL, TL, new Vector2(Margin, -272f), new Vector2(440f, 20f), _fontSemi);
        _gravityText = Txt(transform, "", 13, Soft, TextAnchor.UpperLeft, TL, TL, new Vector2(Margin, -296f), new Vector2(440f, 18f));
    }

    // ── Top-centre: timer, mode, team scores ──────────────────────────────

    void BuildTopCenter()
    {
        _timerText = Txt(transform, "10:00", 36, White, TextAnchor.UpperCenter, TC, TC, new Vector2(0f, -30f), new Vector2(300f, 48f), _fontLight);
        _modeText  = Txt(transform, "", 12, Soft, TextAnchor.UpperCenter, TC, TC, new Vector2(0f, -84f), new Vector2(500f, 16f), _fontSemi);
        _teamText  = Txt(transform, "", 13, White, TextAnchor.UpperCenter, TC, TC, new Vector2(0f, -106f), new Vector2(500f, 18f), _fontSemi);
        _teamText.gameObject.SetActive(false);
    }

    // ── Top-right: kills + kill feed ──────────────────────────────────────

    void BuildTopRight()
    {
        _statsText = Txt(transform, "", 15, White, TextAnchor.MiddleRight, TR, TR, new Vector2(-Margin, -52f), new Vector2(480f, 22f));
        _feedTexts = new Text[FeedLines];
        for (int i = 0; i < FeedLines; i++)
            _feedTexts[i] = Txt(transform, "", 13, Soft, TextAnchor.MiddleRight, TR, TR, new Vector2(-Margin, -90f - i * 22f), new Vector2(480f, 20f));
    }

    // ── Bottom-centre: shield, health, dash / dodge ───────────────────────

    void BuildVitals()
    {
        var vit = NewRect("Vitales", transform, BC, BC, new Vector2(0f, 110f), new Vector2(342f, 56f));

        // Shield: six segments, each filled from the left
        _shieldFill = new RectTransform[6];
        _shieldFillImg = new Image[6];
        for (int i = 0; i < 6; i++)
        {
            var seg = Box(vit, "Seg" + i, TL, TL, new Vector2(i * 58f, 0f), new Vector2(52f, 5f), Track);
            var fill = Box(seg.transform, "Fill", ML, ML, Vector2.zero, Vector2.zero, Cyan);
            fill.rectTransform.anchorMin = Vector2.zero; fill.rectTransform.anchorMax = Vector2.one;
            fill.rectTransform.offsetMin = Vector2.zero; fill.rectTransform.offsetMax = Vector2.zero;
            _shieldFill[i] = fill.rectTransform; _shieldFillImg[i] = fill;
        }

        // Health: thin white bar with a damage trail
        _health = ApexBar(vit, "Vida", TL, TL, new Vector2(0f, -15f), 342f, 8f, White, White, true, false);

        _vidaText   = Txt(vit, "VIDA  100", 13, White, TextAnchor.MiddleLeft, TL, TL, new Vector2(0f, -33f), new Vector2(171f, 16f), _fontSemi);
        _escudoText = Txt(vit, "ESCUDO  100%", 13, Cyan, TextAnchor.MiddleRight, TR, TR, new Vector2(0f, -33f), new Vector2(171f, 16f), _fontSemi);

        // Dash and dodge: a ring that refills as the ability recharges
        var ab = NewRect("Habilidades", transform, BC, BL, new Vector2(230f, 80f), new Vector2(120f, 66f));
        _dashRing  = AbilityRing(ab, 0f,  "ALT", "DASH",    out _dashKey);
        _dodgeRing = AbilityRing(ab, 64f, "C",   "ESQUIVA", out _dodgeKey);
    }

    Image AbilityRing(RectTransform parent, float x, string key, string label, out Text keyText)
    {
        var holder = NewRect(label, parent, TL, TL, new Vector2(x, 0f), new Vector2(54f, 66f));

        var track = Box(holder, "Track", TL, TL, new Vector2(5f, 0f), new Vector2(44f, 44f), new Color(1f, 1f, 1f, 0.20f));
        track.sprite = Ring(0.07f);

        var fill = Box(holder, "Fill", TL, TL, new Vector2(5f, 0f), new Vector2(44f, 44f), Cyan);
        fill.sprite = Ring(0.07f);
        fill.type = Image.Type.Filled;
        fill.fillMethod = Image.FillMethod.Radial360;
        fill.fillOrigin = (int)Image.Origin360.Top;
        fill.fillClockwise = true;
        fill.fillAmount = 1f;

        keyText = Txt(holder, key, 13, Cyan, TextAnchor.MiddleCenter, TL, TL, new Vector2(5f, 0f), new Vector2(44f, 44f), _fontSemi);
        Txt(holder, label, 10, Soft, TextAnchor.UpperCenter, TL, TL, new Vector2(-10f, -52f), new Vector2(74f, 14f), _fontSemi);
        return fill;
    }

    // ── Bottom-left: energy + jetpack ─────────────────────────────────────

    void BuildEnergy()
    {
        var en = NewRect("Energia", transform, BL, BL, new Vector2(Margin, 72f), new Vector2(260f, 72f));
        _energy  = EnergyRow(en, 0f,   "ENERGÍA", "Zap",    new Color32(0xFF, 0xB0, 0x20, 255), new Color32(0xFF, 0xE0, 0x8A, 255), Amber, out _energyValue);
        // The game has no jetpack yet: this row is a placeholder that stays empty.
        _jetpack = EnergyRow(en, -46f, "JETPACK", "Rocket", new Color32(0x22, 0xA9, 0xDD, 255), new Color32(0x8D, 0xEB, 0xFF, 255), Cyan, out _jetpackValue);
    }

    Bar EnergyRow(RectTransform parent, float y, string label, string iconName, Color from, Color to, Color valueColor, out Text value)
    {
        var icon = Box(parent, "Icon", TL, TL, new Vector2(0f, y), new Vector2(15f, 15f), White);
        icon.sprite = Resources.Load<Sprite>(ResourcePaths.HudArtFolder + "Icons/" + iconName);
        icon.preserveAspect = true;
        if (icon.sprite == null) icon.enabled = false;

        Txt(parent, label, 12, new Color(1f, 1f, 1f, 0.85f), TextAnchor.MiddleLeft, TL, TL, new Vector2(23f, y + 1f), new Vector2(150f, 15f), _fontSemi);
        value = Txt(parent, "0", 15, valueColor, TextAnchor.MiddleRight, TL, TL, new Vector2(180f, y + 2f), new Vector2(80f, 17f), _fontSemi);
        return ApexBar(parent, label, TL, TL, new Vector2(0f, y - 24f), 260f, 6f, from, to, false);
    }

    // ── Bottom-right: weapon, ammo, grenades ──────────────────────────────

    void BuildWeapon()
    {
        _grenadeF = Txt(transform, "", 14, White, TextAnchor.MiddleRight, BR, BR, new Vector2(-Margin, 56f), new Vector2(240f, 20f), _fontSemi);
        _grenadeQ = Txt(transform, "", 14, White, TextAnchor.MiddleRight, BR, BR, new Vector2(-Margin - 250f, 56f), new Vector2(240f, 20f), _fontSemi);

        // Bullet pips sit right above the grenades; the reload label and the plasma heat bar share the strip above the pips
        BuildPips();

        _reloadGroup = NewRect("Reload", transform, BR, BR, new Vector2(-Margin, 108f), new Vector2(300f, 14f)).gameObject;
        _reloadCg = _reloadGroup.AddComponent<CanvasGroup>();
        _reloadCg.alpha = 0f;
        _reloadLabel = Txt(_reloadGroup.transform, "RECARGANDO", 10, Amber, TextAnchor.UpperRight, TR, TR, Vector2.zero, new Vector2(300f, 12f), _fontSemi);

        _heatGroup = NewRect("Heat", transform, BR, BR, new Vector2(-Margin, 108f), new Vector2(300f, 24f)).gameObject;
        Txt(_heatGroup.transform, "CALOR", 10, new Color(1f, 0.55f, 0.15f), TextAnchor.UpperRight, TR, TR, Vector2.zero, new Vector2(300f, 12f), _fontSemi);
        _heat = ApexBar(_heatGroup.transform, "Bar", TR, TR, new Vector2(0f, -15f), 300f, 4f,
                        new Color32(0xFF, 0x7A, 0x1A, 255), new Color32(0xFF, 0xB2, 0x4A, 255), false, false);
        _heatGroup.SetActive(false);

        _ammoText   = Txt(transform, "", 92, White, TextAnchor.MiddleRight, BR, BR, AmmoBasePos, new Vector2(480f, 110f), _fontLight);
        _weaponName = Txt(transform, "SIN ARMA", 14, new Color(1f, 1f, 1f, 0.70f), TextAnchor.MiddleRight, BR, BR, NameBasePos, new Vector2(480f, 20f), _fontSemi);
    }

    // ── Event banner ──────────────────────────────────────────────────────

    void BuildBanner()
    {
        var b = NewRect("EventBanner", transform, new Vector2(0.5f, 0.74f), CC, Vector2.zero, new Vector2(900f, 70f));
        _bannerText = Txt(b, "", 32, White, TextAnchor.MiddleCenter, CC, CC, new Vector2(0f, 6f), new Vector2(900f, 50f), _fontSemi);
        Box(b, "Line", CC, CC, new Vector2(0f, -28f), new Vector2(120f, 2f), Cyan);
        _banner = b.gameObject;
        _banner.SetActive(false);
    }

    // ── Crosshair + hit marker ────────────────────────────────────────────
    // Marks exactly where WeaponBase.GetAimDirection() raycasts from — a camera's forward vector passes
    // through screen centre, so the centre of this reticle is always where shots are aimed.

    void BuildCrosshair()
    {
        var root = NewRect("Crosshair", transform, CC, CC, Vector2.zero, Vector2.zero);
        _crosshairRoot = root.gameObject;

        _centerDot = Box(root, "Dot", CC, CC, Vector2.zero, new Vector2(6, 6), Cyan);
        _centerDot.sprite = Circle();

        // Four thin ticks: up, down, left, right
        _ticks = new RectTransform[4]; _tickImages = new Image[4];
        Vector2[] sizes = { new Vector2(2, 12), new Vector2(2, 12), new Vector2(12, 2), new Vector2(12, 2) };
        for (int i = 0; i < 4; i++)
        {
            var img = Box(root, "Tick" + i, CC, CC, Vector2.zero, sizes[i], White);
            img.gameObject.AddComponent<Shadow>().effectColor = new Color(0, 0, 0, 0.55f);
            _ticks[i] = img.rectTransform; _tickImages[i] = img;
        }

        // Hit marker: four diagonal ticks around the centre
        _hitMarker = NewRect("HitMarker", root, CC, CC, Vector2.zero, Vector2.zero);
        _hitImages = new Image[4];
        for (int i = 0; i < 4; i++)
        {
            var img = Box(_hitMarker, "Hit" + i, CC, CC, Vector2.zero, new Vector2(2, 14), White);
            float ang = 45f + i * 90f;
            img.rectTransform.localRotation = Quaternion.Euler(0, 0, ang);
            img.rectTransform.anchoredPosition = Quaternion.Euler(0, 0, ang) * new Vector2(0, 17);
            _hitImages[i] = img;
        }
        _hitMarker.gameObject.SetActive(false);
    }

    // ── Damage-direction arcs + shield warning ────────────────────────────

    void BuildDamageArcs()
    {
        _damageArcs = new DamageArc[4];
        for (int i = 0; i < _damageArcs.Length; i++)
        {
            var pivot = NewRect("DamageArc" + i, transform, CC, CC, Vector2.zero, new Vector2(380f, 380f));
            var arcRt = NewRect("Arc", pivot, CC, CC, Vector2.zero, new Vector2(380f, 380f));
            var arc = arcRt.gameObject.AddComponent<HudArc>();
            arc.raycastTarget = false;
            arc.Set(190f, 187.5f, -35f, 70f);
            arc.color = new Color(Alert.r, Alert.g, Alert.b, 0f);
            _damageArcs[i] = new DamageArc { pivot = pivot, arc = arc, life = 0f };
        }
    }

    void BuildShieldWarning()
    {
        var w = NewRect("ShieldWarning", transform, CC, CC, new Vector2(0f, -118f), new Vector2(400f, 24f));
        _shieldWarn = w.gameObject;

        _shieldWarnIcon = Box(w, "Icon", CC, CC, new Vector2(-58f, 0f), new Vector2(14f, 16f), Alert);
        _shieldWarnIcon.sprite = Resources.Load<Sprite>(ResourcePaths.HudArtFolder + "Icons/ShieldAlert");
        _shieldWarnIcon.preserveAspect = true;
        if (_shieldWarnIcon.sprite == null) _shieldWarnIcon.enabled = false;

        _shieldWarnText = Txt(w, "ESCUDO BAJO", 14, Alert, TextAnchor.MiddleLeft, CC, ML, new Vector2(-42f, 0f), new Vector2(200f, 20f), _fontSemi);
        _shieldWarn.SetActive(false);
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
