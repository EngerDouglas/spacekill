using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

namespace OrbitRush
{

/// <summary>
/// The "sonic dash" look while the jetpack carries you straight to a planet: a shock ring bursts from the centre at take-off,
/// speed streaks rush outward, the lens bulges and splits colours (the screen "diverges"), and the field of view widens
/// (the FOV part lives in PlayerController.FovBoost). Everything fades in and out with <see cref="PlayerController.SonicBlend"/>.
/// </summary>
public class SonicWarpFx : MonoBehaviour
{
    [Header("Look")]
    public float lensDistortion = 0.62f;
    public float chromaticAberration = 1f;
    [Range(0f, 1f)] public float streakAlpha = 0.85f;

    private PlayerController _controller;
    private Volume _volume;
    private LensDistortion _lens;
    private ChromaticAberration _chroma;
    private MotionBlur _motion;
    private Canvas _canvas;
    private RectTransform _streakA, _streakB, _ring;
    private Image _streakImgA, _streakImgB, _ringImg;
    private float _ringT = 1f;

    void Start()
    {
        _controller = GetComponent<PlayerController>();
        if (_controller == null) { enabled = false; return; }
        _controller.SonicStarted += OnSonicStarted;
        BuildVolume();
        BuildOverlay();
    }

    void OnDestroy()
    {
        if (_controller != null) _controller.SonicStarted -= OnSonicStarted;
        if (_volume != null) Destroy(_volume.gameObject);
        if (_canvas != null) Destroy(_canvas.gameObject);
    }

    private void OnSonicStarted() => _ringT = 0f;

    // ── Construction ──────────────────────────────────────────────────────

    private void BuildVolume()
    {
        var go = new GameObject("SonicWarpVolume");
        var profile = ScriptableObject.CreateInstance<VolumeProfile>();
        _lens = profile.Add<LensDistortion>(true);
        _lens.intensity.Override(0f); _lens.scale.Override(0.92f);
        _chroma = profile.Add<ChromaticAberration>(true);
        _chroma.intensity.Override(0f);
        _motion = profile.Add<MotionBlur>(true);
        _motion.intensity.Override(0f); _motion.quality.Override(MotionBlurQuality.Low);
        _volume = go.AddComponent<Volume>();
        _volume.isGlobal = true; _volume.priority = 95f; _volume.profile = profile; _volume.weight = 0f;
    }

    private void BuildOverlay()
    {
        var go = new GameObject("SonicWarpCanvas");
        _canvas = go.AddComponent<Canvas>();
        _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _canvas.sortingOrder = 9;                                   // under the HUD (10)

        var streaks = StreakSprite();
        _streakA = Layer(go.transform, "StreaksA", streaks, out _streakImgA);
        _streakB = Layer(go.transform, "StreaksB", streaks, out _streakImgB);
        _ring = Layer(go.transform, "ShockRing", RingSprite(), out _ringImg);
        _ring.sizeDelta = new Vector2(900f, 900f);
        go.SetActive(true);
    }

    private static RectTransform Layer(Transform parent, string name, Sprite sprite, out Image img)
    {
        var rt = UiFactory.NewRect(name, parent, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(2600f, 2600f));
        img = rt.gameObject.AddComponent<Image>();
        img.sprite = sprite; img.raycastTarget = false;
        img.color = new Color(1f, 1f, 1f, 0f);
        return rt;
    }

    private static Sprite _streakSprite, _ringSprite;

    /// <summary>Thin radial streaks that begin away from the centre and run to the edge of the texture.</summary>
    private static Sprite StreakSprite()
    {
        if (_streakSprite != null) return _streakSprite;
        const int n = 512;
        var px = new Color32[n * n];
        var rng = new System.Random(7);
        float c = (n - 1) * 0.5f;
        for (int i = 0; i < 170; i++)
        {
            float a = (float)(rng.NextDouble() * Mathf.PI * 2f);
            float r0 = Mathf.Lerp(0.18f, 0.55f, (float)rng.NextDouble()) * c;
            float r1 = Mathf.Min(c, r0 + Mathf.Lerp(40f, 190f, (float)rng.NextDouble()));
            float w = Mathf.Lerp(0.6f, 1.8f, (float)rng.NextDouble());
            Vector2 dir = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
            for (float r = r0; r < r1; r += 0.5f)
            {
                float k = (r - r0) / (r1 - r0);
                float alpha = Mathf.Sin(k * Mathf.PI);                    // fades in and out along the streak
                for (float ww = -w; ww <= w; ww += 0.5f)
                {
                    Vector2 p = new Vector2(c, c) + dir * r + new Vector2(-dir.y, dir.x) * ww;
                    int x = Mathf.RoundToInt(p.x), y = Mathf.RoundToInt(p.y);
                    if (x < 0 || y < 0 || x >= n || y >= n) continue;
                    byte v = (byte)Mathf.Clamp(Mathf.Max(px[y * n + x].a, alpha * 255f * (1f - Mathf.Abs(ww) / (w + 0.01f) * 0.5f)), 0f, 255f);
                    px[y * n + x] = new Color32(255, 255, 255, v);
                }
            }
        }
        var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "SonicStreaks" };
        tex.SetPixels32(px); tex.Apply();
        _streakSprite = Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f));
        return _streakSprite;
    }

    private static Sprite RingSprite()
    {
        if (_ringSprite != null) return _ringSprite;
        _ringSprite = UiFactory.RadialSprite(256, d => Mathf.Clamp01(1f - Mathf.Abs(d - 0.9f) / 0.07f) + 0.12f * Mathf.Clamp01(1f - Mathf.Abs(d - 0.8f) / 0.2f), "SonicRing");
        return _ringSprite;
    }

    // ── Per frame ─────────────────────────────────────────────────────────

    void Update()
    {
        if (_controller == null || _volume == null) return;
        float k = Mathf.Clamp01(_controller.SonicBlend);
        float eased = Ease.Smooth(k);

        _volume.weight = eased;
        _volume.enabled = eased > 0.001f;
        _lens.intensity.value = lensDistortion * eased * (0.9f + 0.1f * Mathf.Sin(Time.unscaledTime * 31f));
        _chroma.intensity.value = chromaticAberration * eased;
        _motion.intensity.value = 0.55f * eased;

        // Streaks rush outward in two offset layers, so there is always motion toward the edges
        float t = Time.unscaledTime;
        Animate(_streakA, _streakImgA, Mathf.Repeat(t * 1.6f, 1f), eased, 0.8f);
        Animate(_streakB, _streakImgB, Mathf.Repeat(t * 1.6f + 0.5f, 1f), eased, 1.15f);

        // Shock ring at take-off
        if (_ringT < 1f)
        {
            _ringT += Time.unscaledDeltaTime / 0.55f;
            float e = 1f - Mathf.Pow(1f - Mathf.Clamp01(_ringT), 3f);
            _ring.localScale = Vector3.one * Mathf.Lerp(0.15f, 3.2f, e);
            _ringImg.color = new Color(0.75f, 0.97f, 1f, (1f - Mathf.Clamp01(_ringT)) * 0.9f);
        }
        else _ringImg.color = new Color(1f, 1f, 1f, 0f);
    }

    private void Animate(RectTransform rt, Image img, float phase, float blend, float baseScale)
    {
        float scale = baseScale * Mathf.Lerp(0.85f, 1.9f, phase);
        rt.localScale = Vector3.one * scale;
        rt.localRotation = Quaternion.Euler(0f, 0f, Mathf.Sin(Time.unscaledTime * 0.9f + baseScale) * 4f);
        float edge = Mathf.Sin(phase * Mathf.PI);                       // fade at both ends of each rush
        img.color = new Color(0.85f, 0.97f, 1f, edge * blend * streakAlpha);
    }
}
}
