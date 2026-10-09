using UnityEngine;

namespace OrbitRush
{

/// <summary>
/// Tiny UI animation toolkit that works while the game is paused (everything runs on unscaled time):
///   • <see cref="UiIntro"/>   fade + slide + pop-in with a little overshoot
///   • <see cref="Stagger"/>   gives every child of a panel its own intro, one after another, sliding in from the nearest screen edge
///   • <see cref="UiFadeOut"/> fades an object out, then destroys it
/// </summary>
public static class UiAnim
{
    public static float EaseOutBack(float k)
    {
        k = Mathf.Clamp01(k);
        const float c1 = 1.70158f, c3 = c1 + 1f;
        float x = k - 1f;
        return 1f + c3 * x * x * x + c1 * x * x;
    }

    public static float EaseOutCubic(float k) { k = Mathf.Clamp01(k); float x = 1f - k; return 1f - x * x * x; }

    /// <summary>Adds an intro to one element.</summary>
    public static UiIntro Intro(RectTransform rt, float delay, float duration, Vector2 slide, float scaleFrom = 0.92f, bool fade = true, bool gentle = false)
    {
        var intro = rt.gameObject.AddComponent<UiIntro>();
        intro.delay = delay; intro.duration = duration; intro.slide = slide; intro.scaleFrom = scaleFrom; intro.fade = fade; intro.gentle = gentle;
        return intro;
    }

    /// <summary>
    /// Every direct child of `parent` pops in one after another. Children anchored near an edge slide in from it;
    /// centred ones rise a little and scale up. Names starting with "Shade" (background dimmers) are left alone.
    /// </summary>
    public static void Stagger(Transform parent, float step = 0.04f, float duration = 0.45f, float startDelay = 0f, float distance = 90f, bool gentle = false)
    {
        int i = 0;
        foreach (Transform child in parent)
        {
            var rt = child as RectTransform;
            if (rt == null || child.name.StartsWith("Shade")) continue;

            Vector2 a = (rt.anchorMin + rt.anchorMax) * 0.5f;
            Vector2 slide;
            if (a.x < 0.3f) slide = new Vector2(-distance, 0f);
            else if (a.x > 0.7f) slide = new Vector2(distance, 0f);
            else if (a.y > 0.75f) slide = new Vector2(0f, distance * 0.5f);
            else if (a.y < 0.25f) slide = new Vector2(0f, -distance * 0.5f);
            else slide = new Vector2(0f, -distance * 0.3f);

            Intro(rt, startDelay + i * step, duration, slide, gentle ? 1f : 0.94f, true, gentle);
            i++;
        }
    }

    public static void FadeOutAndDestroy(GameObject go, float duration = 0.18f)
    {
        if (go == null) return;
        var f = go.GetComponent<UiFadeOut>();
        if (f == null) f = go.AddComponent<UiFadeOut>();
        f.duration = duration;
    }
}

/// <summary>Fade / slide / pop-in for one UI element, then removes itself.</summary>
public class UiIntro : MonoBehaviour
{
    public float delay, duration = 0.45f;
    public Vector2 slide;
    public float scaleFrom = 0.92f;
    public bool fade = true;
    /// <summary>Smooth ease with no overshoot (the minimalist menus); false keeps the springy pop.</summary>
    public bool gentle;

    private RectTransform _rt;
    private CanvasGroup _group;
    private bool _addedGroup;
    private Vector2 _endPos;
    private Vector3 _endScale;
    private float _t;

    void Awake()
    {
        _rt = (RectTransform)transform;
        _group = GetComponent<CanvasGroup>();
        if (_group == null) { _group = gameObject.AddComponent<CanvasGroup>(); _addedGroup = true; }
    }

    void Start()
    {
        _endPos = _rt.anchoredPosition;
        _endScale = _rt.localScale;
        Apply(0f);
    }

    void Update()
    {
        _t += Time.unscaledDeltaTime;
        float k = Mathf.Clamp01((_t - delay) / Mathf.Max(0.01f, duration));
        Apply(k);
        if (k >= 1f)
        {
            if (_addedGroup && _group != null) Destroy(_group);
            Destroy(this);
        }
    }

    private void Apply(float k)
    {
        float e = gentle ? UiAnim.EaseOutCubic(k) : UiAnim.EaseOutBack(k);
        _rt.anchoredPosition = Vector2.LerpUnclamped(_endPos + slide, _endPos, e);
        _rt.localScale = Vector3.LerpUnclamped(_endScale * scaleFrom, _endScale, e);
        if (fade && _group != null) _group.alpha = UiAnim.EaseOutCubic(k * 1.4f);
    }
}

/// <summary>Fades the object out (and stops it blocking clicks), then destroys it.</summary>
public class UiFadeOut : MonoBehaviour
{
    public float duration = 0.18f;
    private CanvasGroup _group;
    private float _t;

    void Start()
    {
        _group = GetComponent<CanvasGroup>();
        if (_group == null) _group = gameObject.AddComponent<CanvasGroup>();
        _group.blocksRaycasts = false;
        _group.interactable = false;
    }

    void Update()
    {
        _t += Time.unscaledDeltaTime;
        float k = Mathf.Clamp01(_t / Mathf.Max(0.01f, duration));
        _group.alpha = 1f - k;
        transform.localScale = Vector3.one * Mathf.Lerp(1f, 0.97f, k);
        if (k >= 1f) Destroy(gameObject);
    }
}
}
