using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace OrbitRush
{

/// <summary>Step 1: the planet cards.</summary>
public partial class DropSelector
{
    private class Card
    {
        public PlanetGravity planet;
        public RectTransform rt;
        public CanvasGroup group;
        public NeonPanel panel;
        public Text hint;
        public RenderTexture tex;
        public float hover;
        public Vector2 basePos;
    }

    private readonly List<Card> _cards = new List<Card>();
    private PlanetGravity _chosen;
    private RectTransform _selRoot;

    private static readonly Vector2 TL = new Vector2(0f, 1f);
    private static readonly Vector2 TC = new Vector2(0.5f, 1f);
    private static readonly Vector2 BC = new Vector2(0.5f, 0f);
    private static readonly Vector2 MC = new Vector2(0.5f, 0.5f);

    private IEnumerator SelectPlanetPhase(int startIndex)
    {
        BuildSelectUi();
        _cam.enabled = false;
        yield return null;                  // let the layout settle before the previews are rendered
        RenderPreviews();
        yield return FadeTo(0f, 0.4f);

        int focus = Mathf.Clamp(startIndex, 0, _cards.Count - 1);
        float t0 = Time.unscaledTime;
        Vector2 lastMouse = MousePos();
        bool picked = false;

        while (!picked)
        {
            float t = Time.unscaledTime - t0;
            float dt = Time.unscaledDeltaTime;
            Vector2 mp = MousePos();
            var kb = Keyboard.current; var pad = Gamepad.current; var mouse = Mouse.current;

            if ((mp - lastMouse).sqrMagnitude > 4f)
            {
                for (int i = 0; i < _cards.Count; i++) if (Over(_cards[i].rt, mp)) focus = i;
                lastMouse = mp;
            }

            int move = 0;
            if (kb != null)
            {
                if (kb.rightArrowKey.wasPressedThisFrame || kb.dKey.wasPressedThisFrame) move = 1;
                else if (kb.leftArrowKey.wasPressedThisFrame || kb.aKey.wasPressedThisFrame) move = -1;
            }
            if (pad != null)
            {
                if (pad.dpad.right.wasPressedThisFrame) move = 1;
                else if (pad.dpad.left.wasPressedThisFrame) move = -1;
            }
            focus = Mathf.Clamp(focus + move, 0, _cards.Count - 1);

            if (mouse != null && mouse.leftButton.wasPressedThisFrame)
                for (int i = 0; i < _cards.Count; i++) if (Over(_cards[i].rt, mp)) { focus = i; picked = true; }
            if (ConfirmPressed()) picked = true;

            if (BackPressed() && _onCancel != null) { _chosen = null; yield break; }

            for (int i = 0; i < _cards.Count; i++)
            {
                var c = _cards[i];
                c.hover = Mathf.MoveTowards(c.hover, i == focus ? 1f : 0f, dt * 6f);
                float e = Mathf.Clamp01((t - 0.1f - i * 0.09f) / 0.45f);
                e = UiAnim.EaseOutCubic(e);
                c.group.alpha = e;
                c.rt.anchoredPosition = c.basePos + new Vector2(0f, -70f * (1f - e) + 20f * c.hover);
                c.rt.localScale = Vector3.one * (1f + 0.035f * c.hover);
                Color border = Color.Lerp(new Color(1f, 1f, 1f, 0.28f), Cyan, c.hover);
                if (c.panel.border != border) c.panel.SetColors(border, c.panel.fillTop, c.panel.fillBottom);
                c.hint.color = new Color(Cyan.r, Cyan.g, Cyan.b, c.hover * (0.6f + 0.4f * Mathf.Sin(Time.unscaledTime * 5f)));
            }
            yield return null;

            if (picked) _chosen = _cards[focus].planet;
        }
    }

    private void DestroySelectUi()
    {
        if (_selRoot != null) Destroy(_selRoot.gameObject);
        foreach (var c in _cards) if (c.tex != null) c.tex.Release();
        _cards.Clear();
    }

    private void BuildSelectUi()
    {
        DestroySelectUi();
        _selRoot = Stretch("Select", transform);
        _selRoot.SetAsFirstSibling();

        FullImage("Bg", _selRoot, new Color(0.012f, 0.018f, 0.04f, 1f));
        var grid = FullImage("Grid", _selRoot, Color.white);
        grid.sprite = GridSprite(); grid.type = Image.Type.Tiled;
        var vig = FullImage("Vignette", _selRoot, new Color(0f, 0f, 0f, 0.85f));
        vig.sprite = VignetteSprite();

        // Header
        Txt(_selRoot, "//  DESPLIEGUE   ·   FASE 01 / 02", 20, Cyan, TextAnchor.UpperLeft, TL, new Vector2(96f, -70f), new Vector2(900f, 30f)).rectTransform.pivot = TL;
        var title = Txt(_selRoot, "ELIGE DÓNDE LUCHAR", 68, White, TextAnchor.UpperLeft, TL, new Vector2(96f, -104f), new Vector2(1200f, 90f), true, FontStyle.BoldAndItalic);
        title.rectTransform.pivot = TL;
        var sub = Txt(_selRoot, "Selecciona un planeta. Después marcarás el punto exacto de caída desde el satélite.", 22, Soft, TextAnchor.UpperLeft, TL, new Vector2(96f, -196f), new Vector2(1200f, 34f));
        sub.rectTransform.pivot = TL;
        var line = Box(_selRoot, TL, TL, new Vector2(96f, -246f), new Vector2(1728f, 2f), new Color(1f, 1f, 1f, 0.16f));
        Box(_selRoot, TL, TL, new Vector2(96f, -246f), new Vector2(180f, 2f), Cyan);

        // Cards
        int n = _planets.Count;
        const float gap = 36f;
        float cw = Mathf.Min(430f, (1728f - (n - 1) * gap) / n);
        const float ch = 660f;
        for (int i = 0; i < n; i++)
        {
            var p = _planets[i];
            var info = Describe(p);
            var c = new Card { planet = p };
            c.basePos = new Vector2((i - (n - 1) * 0.5f) * (cw + gap), -70f);

            c.rt = UiFactory.NewRect("Card_" + p.planetName, _selRoot, MC, MC, c.basePos, new Vector2(cw, ch));
            c.group = c.rt.gameObject.AddComponent<CanvasGroup>();
            c.group.blocksRaycasts = false;
            c.panel = c.rt.gameObject.AddComponent<NeonPanel>();
            c.panel.border = new Color(1f, 1f, 1f, 0.28f);
            c.panel.chamfer = 26f; c.panel.cutTL = true; c.panel.cutTR = false; c.panel.cutBR = true; c.panel.cutBL = false;
            c.panel.fillTop = new Color(0.06f, 0.08f, 0.14f, 0.92f);
            c.panel.fillBottom = new Color(0.02f, 0.03f, 0.07f, 0.96f);
            c.panel.glowAlpha = 0.2f; c.panel.borderThickness = 2f;

            // Live render of the planet
            c.tex = new RenderTexture(784, 532, 24, RenderTextureFormat.ARGB32) { name = "PlanetPreview_" + p.planetName, antiAliasing = 4 };
            var imgRt = UiFactory.NewRect("Preview", c.rt, TC, TC, new Vector2(0f, -16f), new Vector2(cw - 32f, 270f));
            var raw = imgRt.gameObject.AddComponent<RawImage>();
            raw.texture = c.tex; raw.raycastTarget = false;
            Txt(imgRt, (i + 1).ToString("00"), 54, new Color(1f, 1f, 1f, 0.92f), TextAnchor.UpperLeft, TL, new Vector2(14f, -8f), new Vector2(120f, 66f), true, FontStyle.BoldAndItalic).rectTransform.pivot = TL;
            Box(imgRt, new Vector2(0f, 0f), new Vector2(0f, 0f), Vector2.zero, new Vector2(cw - 32f, 2f), new Color(1f, 1f, 1f, 0.2f));

            float w = cw - 48f;
            Txt(c.rt, info.biome, 16, Cyan, TextAnchor.UpperLeft, TC, new Vector2(0f, -304f), new Vector2(w, 22f));
            Txt(c.rt, info.name, 40, White, TextAnchor.UpperLeft, TC, new Vector2(0f, -328f), new Vector2(w, 52f), true, FontStyle.BoldAndItalic);
            Txt(c.rt, info.desc, 16, Soft, TextAnchor.UpperLeft, TC, new Vector2(0f, -388f), new Vector2(w, 70f));

            float y = 472f;
            StatRow(c.rt, 24f, y, w, "TAMAÑO", info.size, info.sizeTxt, Cyan);
            StatRow(c.rt, 24f, y + 30f, w, "GRAVEDAD", info.grav, info.gravTxt, Cyan);
            StatRow(c.rt, 24f, y + 60f, w, "AMENAZA", info.threat, info.threatTxt, Color.Lerp(Cyan, Danger, info.threat));
            StatRow(c.rt, 24f, y + 90f, w, "ARMAMENTO", info.loot, info.lootTxt, Cyan);

            c.hint = Txt(c.rt, "→  ELEGIR PLANETA", 16, Cyan, TextAnchor.MiddleCenter, BC, new Vector2(0f, 22f), new Vector2(w, 24f));
            _cards.Add(c);
        }

        // Footer
        string back = _onCancel != null ? "      ·      ESC  volver" : "";
        Txt(_selRoot, "←  →  /  RATÓN  navegar      ·      ENTER  /  CLIC  elegir" + back, 18, Soft, TextAnchor.MiddleCenter, BC, new Vector2(0f, 44f), new Vector2(1400f, 28f));
    }

    /// <summary>One "LABEL ▬▬▬▬ value" line. Position is measured from the parent's top-left corner.</summary>
    private static void StatRow(Transform parent, float x, float y, float w, string label, float frac, string value, Color color)
    {
        var l = Txt(parent, label, 14, Soft, TextAnchor.MiddleLeft, TL, new Vector2(x, -y), new Vector2(110f, 22f));
        l.rectTransform.pivot = TL;
        float barX = x + 112f, barW = w - 112f - 86f;
        var track = Box(parent, TL, TL, new Vector2(barX, -y - 8f), new Vector2(barW, 6f), new Color(1f, 1f, 1f, 0.12f));
        var fill = Box(track.transform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), Vector2.zero, new Vector2(barW * Mathf.Clamp01(frac), 6f), color);
        var v = Txt(parent, value, 15, White, TextAnchor.MiddleRight, TL, new Vector2(x + w - 80f, -y), new Vector2(80f, 22f), true, FontStyle.Bold);
        v.rectTransform.pivot = TL;
    }

    /// <summary>Renders each planet once (day side toward the Sun) into its card.</summary>
    private void RenderPreviews()
    {
        _cam.fieldOfView = 30f;
        _cam.clearFlags = CameraClearFlags.SolidColor;
        _cam.backgroundColor = new Color(0.02f, 0.03f, 0.06f, 1f);
        foreach (var c in _cards)
        {
            try
            {
                var p = c.planet;
                Vector3 centre = p.transform.position;
                float r = p.radius;
                Vector3 toSun = SunDirection(p);
                Vector3 d = Quaternion.Euler(-14f, 38f, 0f) * toSun;
                float dist = r * 1.12f / Mathf.Sin(15f * Mathf.Deg2Rad);
                _cam.transform.position = centre + d * dist;
                _cam.transform.rotation = Quaternion.LookRotation(-d, Vector3.up);
                _cam.nearClipPlane = Mathf.Max(1f, dist - r * 2f);
                _cam.targetTexture = c.tex;
                LightFor(p);
                _cam.Render();
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("[DropSelector] Planet preview failed: " + e.Message);
            }
            finally { _cam.targetTexture = null; }
        }
        _cam.nearClipPlane = 0.3f;
        _cam.fieldOfView = 35f;
        _cam.clearFlags = RenderSettings.skybox != null ? CameraClearFlags.Skybox : CameraClearFlags.SolidColor;
        RestoreLight();
    }
}
}
