using UnityEngine;
using UnityEngine.UI;

namespace OrbitRush
{

/// <summary>Small UGUI / material helpers for the drop selector (everything is built in code).</summary>
public partial class DropSelector
{
    static Font _fontR, _fontS;
    static Font FontRegular => _fontR != null ? _fontR : (_fontR = UiFactory.LoadFont());
    static Font FontSemi => _fontS != null ? _fontS : (_fontS = UiFactory.LoadFontSemiBold());

    static Sprite _gridSprite, _vignetteSprite, _ringSprite;

    static RectTransform Stretch(string name, Transform parent)
    {
        var rt = UiFactory.NewRect(name, parent, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
        return rt;
    }

    static Image FullImage(string name, Transform parent, Color c)
    {
        var img = Stretch(name, parent).gameObject.AddComponent<Image>();
        img.color = c; img.raycastTarget = false;
        return img;
    }

    static Image Box(Transform parent, Vector2 anchor, Vector2 pivot, Vector2 pos, Vector2 size, Color c)
    {
        var img = UiFactory.NewRect("Box", parent, anchor, pivot, pos, size).gameObject.AddComponent<Image>();
        img.color = c; img.raycastTarget = false;
        return img;
    }

    static Text Txt(Transform parent, string text, int size, Color c, TextAnchor align, Vector2 anchor, Vector2 pos, Vector2 box,
                    bool semi = true, FontStyle style = FontStyle.Normal)
    {
        var rt = UiFactory.NewRect("Text", parent, anchor, anchor, pos, box);
        var t = rt.gameObject.AddComponent<Text>();
        t.font = semi ? FontSemi : FontRegular;
        t.text = text; t.fontSize = size; t.color = c; t.alignment = align; t.fontStyle = style;
        t.horizontalOverflow = HorizontalWrapMode.Wrap; t.verticalOverflow = VerticalWrapMode.Overflow;
        t.supportRichText = true; t.raycastTarget = false;
        return t;
    }

    static NeonPanel Panel(Transform parent, Vector2 anchor, Vector2 pivot, Vector2 pos, Vector2 size, Color border, float chamfer = 18f)
    {
        var rt = UiFactory.NewRect("Panel", parent, anchor, pivot, pos, size);
        var p = rt.gameObject.AddComponent<NeonPanel>();
        p.border = border; p.chamfer = chamfer;
        p.cutTL = true; p.cutTR = false; p.cutBR = true; p.cutBL = false;
        p.fillTop = new Color(0.05f, 0.07f, 0.12f, 0.88f);
        p.fillBottom = new Color(0.02f, 0.03f, 0.07f, 0.94f);
        p.glowAlpha = 0.25f; p.borderThickness = 2f;
        return p;
    }

    /// <summary>Thin tiled grid (the techy backdrop of the selection screen).</summary>
    static Sprite GridSprite()
    {
        if (_gridSprite != null) return _gridSprite;
        const int n = 64;
        var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Bilinear };
        var px = new Color32[n * n];
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                bool line = x == 0 || y == 0;
                px[y * n + x] = line ? new Color32(120, 200, 255, 26) : new Color32(0, 0, 0, 0);
            }
        tex.SetPixels32(px); tex.Apply();
        _gridSprite = Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
        return _gridSprite;
    }

    /// <summary>Transparent in the middle, opaque toward the screen edges.</summary>
    static Sprite VignetteSprite()
    {
        if (_vignetteSprite != null) return _vignetteSprite;
        _vignetteSprite = UiFactory.RadialSprite(256, d => Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.45f, 1.15f, d)), "DropVignette", false);
        return _vignetteSprite;
    }

    static Sprite RingSprite()
    {
        if (_ringSprite != null) return _ringSprite;
        _ringSprite = UiFactory.RadialSprite(128, d => (d > 0.86f && d < 0.96f) ? 1f : (d > 0.96f && d < 1f ? 0.4f : 0f), "DropRing");
        return _ringSprite;
    }

    // ── 3D helpers ────────────────────────────────────────────────────────

    static Material _lineMat;

    static Material LineMaterial()
    {
        if (_lineMat != null) return _lineMat;
        var sh = Shader.Find("Sprites/Default");
        if (sh == null) sh = Shader.Find("Universal Render Pipeline/Unlit");
        _lineMat = new Material(sh) { name = "DropLine" };
        return _lineMat;
    }

    static LineRenderer NewLine(Transform parent, string name, Color c, float width, int points = 2, bool loop = false)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var lr = go.AddComponent<LineRenderer>();
        lr.sharedMaterial = LineMaterial();
        lr.useWorldSpace = true;
        lr.positionCount = points;
        lr.loop = loop;
        lr.startWidth = lr.endWidth = width;
        lr.startColor = lr.endColor = c;
        lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        lr.receiveShadows = false;
        lr.numCapVertices = 0;
        return lr;
    }

    static Material Lit(Color c, Color emission, float metallic = 0.6f, float smooth = 0.5f)
    {
        var sh = Shader.Find("Universal Render Pipeline/Lit");
        if (sh == null) sh = Shader.Find("Sprites/Default");
        var m = new Material(sh);
        m.SetColor("_BaseColor", c);
        m.color = c;
        if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", metallic);
        if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smooth);
        if (emission.maxColorComponent > 0.01f && m.HasProperty("_EmissionColor"))
        {
            m.EnableKeyword("_EMISSION");
            m.SetColor("_EmissionColor", emission);
        }
        return m;
    }

    static GameObject Prim(PrimitiveType type, Transform parent, Vector3 localPos, Vector3 scale, Material mat, string name = null)
    {
        var go = GameObject.CreatePrimitive(type);
        if (name != null) go.name = name;
        var col = go.GetComponent<Collider>();
        if (col != null) Destroy(col);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;
        go.transform.localScale = scale;
        var r = go.GetComponent<Renderer>();
        r.sharedMaterial = mat;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.receiveShadows = false;
        return go;
    }
}
}
