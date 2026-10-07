using UnityEngine;
using UnityEngine.UI;

namespace OrbitRush
{

/// <summary>
/// Full-screen death overlay, built entirely in code (same neon look as the HUD):
/// a red-black vignette fades in over the world, "ELIMINADO" slams in with a glitch flicker, who killed you,
/// your tally, a respawn countdown bar and finally a "press to respawn" prompt.
/// PlayerRespawn drives it: <see cref="Show"/> on death, <see cref="SetCountdown"/> each frame, <see cref="Hide"/> on respawn.
/// </summary>
public class DeathScreen : MonoBehaviour
{
    public static DeathScreen Instance { get; private set; }

    static readonly Color Pink = new Color(0.98f, 0.01f, 0.62f);
    static readonly Color Cyan = new Color(0f, 0.78f, 0.99f);
    static readonly Color Red  = new Color(1f, 0.12f, 0.16f);
    static readonly Color White = new Color(0.96f, 0.96f, 1f);
    static readonly Color Muted = new Color(0.68f, 0.72f, 0.88f);

    private CanvasGroup _group;
    private Image _vignette, _flash;
    private RectTransform _panel, _barFill;
    private Text _title, _titleShadow, _killer, _stats, _countdown, _prompt;
    private float _shownAt, _fade;
    private bool _visible;
    private float _progress;
    private bool _ready;
    private static Sprite _vignetteSprite;
    private int _lastSecond = -1;
    private float _punch;

    /// <summary>Creates the screen on first use.</summary>
    public static DeathScreen Get()
    {
        if (Instance != null) return Instance;
        var go = new GameObject("DeathScreen");
        DontDestroyOnLoad(go);
        return go.AddComponent<DeathScreen>();
    }

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        Build();
        _group.alpha = 0f;
    }

    // ══════════════════════════════════════════════════════════════════════

    public void Show(string killer, int kills, int aiKills)
    {
        _visible = true; _ready = false; _progress = 0f;
        _shownAt = Time.unscaledTime;
        _killer.text = string.IsNullOrEmpty(killer) ? "" : $"ELIMINADO POR  <color=#FA029F>{killer}</color>";
        _stats.text = $"BAJAS  <b>{kills}</b>      ENEMIGOS IA  <b>{aiKills}</b>";
        _prompt.gameObject.SetActive(false);
        _countdown.gameObject.SetActive(true);
        _barFill.parent.gameObject.SetActive(true);
        _flash.color = new Color(1f, 0.1f, 0.15f, 0.55f);       // red flash on the moment of death
        _lastSecond = -1;
        ScreenBlur.Request(this, true);                         // the world goes out of focus behind the screen
        if (HUD.Instance != null) HUD.Instance.SetHidden(true);
    }

    /// <summary>progress 0..1 of the respawn wait; when ready the bar becomes the "press to respawn" prompt.</summary>
    public void SetCountdown(float secondsLeft, float progress, bool ready)
    {
        _progress = Mathf.Clamp01(progress);
        _ready = ready;
        int sec = Mathf.CeilToInt(Mathf.Max(0f, secondsLeft));
        if (!ready && sec != _lastSecond) { _lastSecond = sec; _punch = 1f; }      // the number punches every second
        _countdown.text = ready ? "" : $"REAPARECIENDO EN  {sec}";
        _countdown.gameObject.SetActive(!ready);
        _prompt.gameObject.SetActive(ready);
    }

    public void Hide()
    {
        _visible = false;
        ScreenBlur.Request(this, false);
        if (HUD.Instance != null) HUD.Instance.SetHidden(false);
    }

    // ══════════════════════════════════════════════════════════════════════

    void Update()
    {
        float target = _visible ? 1f : 0f;
        _fade = Mathf.MoveTowards(_fade, target, Time.unscaledDeltaTime * (_visible ? 2.2f : 3.5f));
        _group.alpha = _fade;
        if (_fade <= 0f) return;

        float t = Time.unscaledTime - _shownAt;

        // Flash fades fast; the vignette creeps in
        var fc = _flash.color; fc.a = Mathf.MoveTowards(fc.a, 0f, Time.unscaledDeltaTime * 1.2f); _flash.color = fc;
        var vc = _vignette.color; vc.a = Mathf.Lerp(0f, 0.94f, Mathf.Clamp01(t / 1.2f)); _vignette.color = vc;

        // Title: slams in (big → normal) with a short glitch flicker
        float slam = Mathf.Clamp01(t / 0.35f);
        float scale = Mathf.Lerp(1.8f, 1f, 1f - (1f - slam) * (1f - slam));
        _title.rectTransform.localScale = Vector3.one * scale;
        _titleShadow.rectTransform.localScale = Vector3.one * scale;
        bool glitch = t < 0.9f && Mathf.PerlinNoise(t * 40f, 3f) > 0.62f;
        _title.rectTransform.anchoredPosition = new Vector2(glitch ? Random.Range(-14f, 14f) : 0f, 0f);
        _titleShadow.rectTransform.anchoredPosition = new Vector2(glitch ? Random.Range(8f, 24f) : 5f, glitch ? Random.Range(-6f, 6f) : -4f);
        _title.color = glitch ? Cyan : White;
        _titleShadow.color = new Color(Pink.r, Pink.g, Pink.b, Mathf.Clamp01(slam));

        // The title breathes once it has landed
        if (slam >= 1f)
        {
            float breathe = 1f + 0.018f * Mathf.Sin(t * 2.2f);
            _title.rectTransform.localScale = Vector3.one * breathe;
            _titleShadow.rectTransform.localScale = Vector3.one * breathe;
        }

        // Panel: rises and pops in a moment later; its lines follow one by one
        float reveal = Mathf.Clamp01((t - 0.5f) / 0.55f);
        _panel.GetComponent<CanvasGroup>().alpha = UiAnim.EaseOutCubic(reveal * 1.3f);
        _panel.localScale = Vector3.one * Mathf.LerpUnclamped(0.86f, 1f, UiAnim.EaseOutBack(reveal));
        _panel.anchoredPosition = new Vector2(0f, Mathf.Lerp(-60f, 0f, UiAnim.EaseOutCubic(reveal)));
        _killer.color = new Color(White.r, White.g, White.b, Mathf.Clamp01((t - 0.75f) / 0.3f));
        _stats.color = new Color(Muted.r, Muted.g, Muted.b, Mathf.Clamp01((t - 0.95f) / 0.3f));

        // Countdown number punch
        _punch = Mathf.MoveTowards(_punch, 0f, Time.unscaledDeltaTime * 4f);
        _countdown.rectTransform.localScale = Vector3.one * (1f + 0.35f * _punch * _punch);

        // Progress bar
        _barFill.anchorMax = new Vector2(_ready ? 1f : _progress, 1f);
        var barImg = _barFill.GetComponent<Image>();
        barImg.color = _ready ? Color.Lerp(Cyan, White, 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 8f)) : Pink;

        if (_ready) _prompt.color = new Color(1f, 1f, 1f, 0.55f + 0.45f * Mathf.Sin(Time.unscaledTime * 6f));
    }

    // ══════════════════════════════════════════════════════════════════════
    // BUILD
    // ══════════════════════════════════════════════════════════════════════

    private void Build()
    {
        var canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 40;            // above the HUD (10) and the scope overlay (15)
        var cs = gameObject.AddComponent<CanvasScaler>();
        cs.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        cs.referenceResolution = new Vector2(1920, 1080);
        cs.matchWidthOrHeight = 0.5f;
        _group = gameObject.AddComponent<CanvasGroup>();
        _group.blocksRaycasts = false; _group.interactable = false;

        var font = LoadFont();

        // Dark red vignette over everything
        _vignette = FullScreen("Vignette", new Color(0.12f, 0f, 0.02f, 0f));
        _vignette.sprite = Vignette();
        _flash = FullScreen("Flash", new Color(1f, 0.1f, 0.15f, 0f));

        // Title block, upper third
        _titleShadow = MakeText("TitleShadow", transform, font, "ELIMINADO", 150, Pink, TextAnchor.MiddleCenter, FontStyle.BoldAndItalic,
                                new Vector2(0.5f, 0.64f), new Vector2(1400, 220));
        _title = MakeText("Title", transform, font, "ELIMINADO", 150, White, TextAnchor.MiddleCenter, FontStyle.BoldAndItalic,
                          new Vector2(0.5f, 0.64f), new Vector2(1400, 220));

        // Neon panel with the details
        var panelGo = new GameObject("Panel", typeof(RectTransform));
        panelGo.layer = LayerMask.NameToLayer("UI");
        _panel = panelGo.GetComponent<RectTransform>();
        _panel.SetParent(transform, false);
        _panel.anchorMin = _panel.anchorMax = new Vector2(0.5f, 0.38f);
        _panel.sizeDelta = new Vector2(760, 250);
        panelGo.AddComponent<CanvasGroup>();
        var np = panelGo.AddComponent<NeonPanel>();
        np.border = Pink; np.chamfer = 22f; np.cutTL = false; np.cutTR = true; np.cutBR = false; np.cutBL = true;
        np.raycastTarget = false;

        _killer = MakeText("Killer", _panel, font, "", 32, White, TextAnchor.MiddleCenter, FontStyle.BoldAndItalic, new Vector2(0.5f, 0.82f), new Vector2(720, 50));
        _stats = MakeText("Stats", _panel, font, "", 24, Muted, TextAnchor.MiddleCenter, FontStyle.Bold, new Vector2(0.5f, 0.62f), new Vector2(720, 40));

        // Respawn bar
        var track = new GameObject("BarTrack", typeof(RectTransform));
        track.layer = LayerMask.NameToLayer("UI");
        var trt = track.GetComponent<RectTransform>();
        trt.SetParent(_panel, false);
        trt.anchorMin = trt.anchorMax = new Vector2(0.5f, 0.34f);
        trt.sizeDelta = new Vector2(620, 14);
        var timg = track.AddComponent<Image>();
        timg.color = new Color(0.03f, 0.03f, 0.1f, 0.95f); timg.raycastTarget = false;
        var fill = new GameObject("Fill", typeof(RectTransform));
        fill.layer = LayerMask.NameToLayer("UI");
        _barFill = fill.GetComponent<RectTransform>();
        _barFill.SetParent(trt, false);
        _barFill.anchorMin = Vector2.zero; _barFill.anchorMax = new Vector2(0f, 1f);
        _barFill.offsetMin = Vector2.zero; _barFill.offsetMax = Vector2.zero;
        var fimg = fill.AddComponent<Image>(); fimg.color = Pink; fimg.raycastTarget = false;

        _countdown = MakeText("Countdown", _panel, font, "", 30, Cyan, TextAnchor.MiddleCenter, FontStyle.BoldAndItalic, new Vector2(0.5f, 0.14f), new Vector2(720, 44));
        _prompt = MakeText("Prompt", _panel, font, "PRESIONA  ESPACIO / CLIC / A  PARA REAPARECER", 24, White, TextAnchor.MiddleCenter, FontStyle.BoldAndItalic, new Vector2(0.5f, 0.14f), new Vector2(740, 44));
        _prompt.gameObject.SetActive(false);
    }

    private Image FullScreen(string name, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.layer = LayerMask.NameToLayer("UI");
        var rt = go.GetComponent<RectTransform>();
        rt.SetParent(transform, false);
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
        var img = go.AddComponent<Image>();
        img.color = color; img.raycastTarget = false;
        return img;
    }

    private static Text MakeText(string name, Transform parent, Font font, string text, int size, Color color, TextAnchor align,
                                 FontStyle style, Vector2 anchor, Vector2 box)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.layer = LayerMask.NameToLayer("UI");
        var rt = go.GetComponent<RectTransform>();
        rt.SetParent(parent, false);
        rt.anchorMin = rt.anchorMax = anchor; rt.sizeDelta = box; rt.anchoredPosition = Vector2.zero;
        var t = go.AddComponent<Text>();
        t.font = font; t.text = text; t.fontSize = size; t.fontStyle = style; t.color = color; t.alignment = align;
        t.horizontalOverflow = HorizontalWrapMode.Overflow; t.verticalOverflow = VerticalWrapMode.Overflow;
        t.supportRichText = true; t.raycastTarget = false;
        return t;
    }

    private static Font LoadFont()
    {
        var f = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (f == null) f = Resources.GetBuiltinResource<Font>("Arial.ttf");
        if (f == null) f = Font.CreateDynamicFontFromOSFont("Arial", 16);
        return f;
    }

    private static Sprite Vignette()
    {
        if (_vignetteSprite != null) return _vignetteSprite;
        const int n = 256;
        var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        var px = new Color32[n * n];
        float c = (n - 1) * 0.5f;
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float d = Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c)) / c;
                float a = Mathf.Lerp(0.38f, 1f, Mathf.InverseLerp(0.25f, 1.1f, d));    // always a little dark, heavy at the edges
                px[y * n + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(a) * 255f));
            }
        tex.SetPixels32(px); tex.Apply();
        _vignetteSprite = Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f));
        return _vignetteSprite;
    }
}
}
