using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace OrbitRush
{

/// <summary>
/// Sci-fi neon panel drawn as a single UI mesh — the angular frame style of the Orbit Rush art:
///   • chamfered (diagonally cut) corners, chosen per corner
///   • dark gradient fill
///   • a soft inner glow fading in from the edge, then a crisp neon border line
///   • short bright "bracket" ticks at the ends of every edge
/// Also draws slanted parallelogram tabs (for title labels) and hexagon badges.
///
/// Why a custom Graphic: Unity's built-in Outline tints the whole panel and can't cut corners.
/// Put it on a RectTransform like an Image; set the colours/shape in code, then call
/// <see cref="Refresh"/> if you change them afterwards.
/// </summary>
[RequireComponent(typeof(CanvasRenderer))]
public class NeonPanel : MaskableGraphic
{
    public enum Shape { Chamfer, Parallelogram, Hexagon }

    public Shape shape = Shape.Chamfer;

    [Header("Fill")]
    public Color fillTop    = new Color(0.07f, 0.07f, 0.22f, 0.90f);
    public Color fillBottom = new Color(0.02f, 0.02f, 0.09f, 0.92f);

    [Header("Edge")]
    public Color border = new Color(1f, 0.10f, 0.62f);
    public Color accent = new Color(1f, 1f, 1f, 0.95f);   // the bright corner ticks
    public float borderThickness = 2.5f;
    public float glowThickness = 10f;
    [Range(0f, 1f)] public float glowAlpha = 0.45f;

    [Header("Corners (Chamfer shape)")]
    public float chamfer = 16f;
    public bool cutTL = false, cutTR = true, cutBR = false, cutBL = true;

    [Header("Parallelogram")]
    public float slant = 14f;

    [Header("Corner ticks")]
    public bool cornerAccents = true;
    public float accentLength = 20f;

    /// <summary>Rebuild the mesh after changing any of the fields above at runtime.</summary>
    public void Refresh() => SetVerticesDirty();

    public void SetColors(Color newBorder, Color newFillTop, Color newFillBottom)
    {
        border = newBorder; fillTop = newFillTop; fillBottom = newFillBottom;
        SetVerticesDirty();
    }

    protected override void Awake()
    {
        base.Awake();
        raycastTarget = false;
        color = Color.white;          // all colour comes from the vertex colours below
    }

#if UNITY_EDITOR
    protected override void OnValidate() { base.OnValidate(); SetVerticesDirty(); }
#endif

    // ══════════════════════════════════════════════════════════════════════

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        Rect r = GetPixelAdjustedRect();
        if (r.width < 2f || r.height < 2f) return;

        var outer = Poly(r, 0f);
        int n = outer.Count;

        // ── Fill: triangle fan from the centre, vertically graded ─────────
        int centre = AddVert(vh, r.center, Color.Lerp(fillBottom, fillTop, 0.5f));
        int firstFill = vh.currentVertCount;
        for (int i = 0; i < n; i++)
            AddVert(vh, outer[i], Color.Lerp(fillBottom, fillTop, Mathf.InverseLerp(r.yMin, r.yMax, outer[i].y)));
        for (int i = 0; i < n; i++)
            vh.AddTriangle(centre, firstFill + i, firstFill + (i + 1) % n);

        // ── Inner glow: border colour at the edge fading to nothing ──────
        if (glowThickness > 0.5f && glowAlpha > 0.01f)
        {
            var glowInner = Poly(r, glowThickness);
            Color g0 = border; g0.a *= glowAlpha;
            Color g1 = border; g1.a = 0f;
            Strip(vh, outer, glowInner, g0, g1);
        }

        // ── Crisp neon border ─────────────────────────────────────────────
        var edgeInner = Poly(r, borderThickness);
        Strip(vh, outer, edgeInner, border, border);

        // ── Bright ticks at both ends of every long edge ──────────────────
        if (cornerAccents)
        {
            var tickInner = Poly(r, borderThickness + 2.2f);
            for (int i = 0; i < n; i++)
            {
                int j = (i + 1) % n;
                float len = Vector2.Distance(outer[i], outer[j]);
                if (len < accentLength * 2f + 14f) continue;

                float t = accentLength / len;
                Tick(vh, outer[i], outer[j], tickInner[i], tickInner[j], 0f, t);
                Tick(vh, outer[i], outer[j], tickInner[i], tickInner[j], 1f - t, 1f);
            }
        }
    }

    // ── Geometry ──────────────────────────────────────────────────────────

    /// <summary>The outline polygon (clockwise) of the panel inset by <paramref name="t"/> pixels.</summary>
    private List<Vector2> Poly(Rect r, float t)
    {
        float x0 = r.xMin + t, x1 = r.xMax - t, y0 = r.yMin + t, y1 = r.yMax - t;
        var p = new List<Vector2>(8);

        switch (shape)
        {
            case Shape.Parallelogram:
            {
                float s = slant;
                p.Add(new Vector2(x0 + s, y1)); p.Add(new Vector2(x0 + s, y1));
                p.Add(new Vector2(x1, y1));     p.Add(new Vector2(x1, y1));
                p.Add(new Vector2(x1 - s, y0)); p.Add(new Vector2(x1 - s, y0));
                p.Add(new Vector2(x0, y0));     p.Add(new Vector2(x0, y0));
                break;
            }
            case Shape.Hexagon:
            {
                float w = x1 - x0, midY = (y0 + y1) * 0.5f;
                p.Add(new Vector2(x0 + w * 0.25f, y1));
                p.Add(new Vector2(x0 + w * 0.75f, y1));
                p.Add(new Vector2(x1, midY));
                p.Add(new Vector2(x0 + w * 0.75f, y0));
                p.Add(new Vector2(x0 + w * 0.25f, y0));
                p.Add(new Vector2(x0, midY));
                break;
            }
            default:   // Chamfer
            {
                // Insetting a 45° corner cut by t shortens its legs by t·(2 − √2).
                float k = t * (2f - Mathf.Sqrt(2f));
                float tl = cutTL ? Mathf.Max(0f, chamfer - k) : 0f;
                float tr = cutTR ? Mathf.Max(0f, chamfer - k) : 0f;
                float br = cutBR ? Mathf.Max(0f, chamfer - k) : 0f;
                float bl = cutBL ? Mathf.Max(0f, chamfer - k) : 0f;

                p.Add(new Vector2(x0, y1 - tl));      p.Add(new Vector2(x0 + tl, y1));      // top-left corner
                p.Add(new Vector2(x1 - tr, y1));      p.Add(new Vector2(x1, y1 - tr));      // top-right
                p.Add(new Vector2(x1, y0 + br));      p.Add(new Vector2(x1 - br, y0));      // bottom-right
                p.Add(new Vector2(x0 + bl, y0));      p.Add(new Vector2(x0, y0 + bl));      // bottom-left
                break;
            }
        }
        return p;
    }

    private static int AddVert(VertexHelper vh, Vector2 pos, Color c)
    {
        vh.AddVert(pos, c, Vector2.zero);
        return vh.currentVertCount - 1;
    }

    /// <summary>A ring of quads between two polygons with the same vertex count.</summary>
    private static void Strip(VertexHelper vh, List<Vector2> outer, List<Vector2> inner, Color cOuter, Color cInner)
    {
        int n = outer.Count;
        for (int i = 0; i < n; i++)
        {
            int j = (i + 1) % n;
            int a = AddVert(vh, outer[i], cOuter);
            int b = AddVert(vh, outer[j], cOuter);
            int c = AddVert(vh, inner[j], cInner);
            int d = AddVert(vh, inner[i], cInner);
            vh.AddTriangle(a, b, c);
            vh.AddTriangle(a, c, d);
        }
    }

    /// <summary>One bright tick on the edge i→j, covering fraction [t0, t1] of its length.</summary>
    private void Tick(VertexHelper vh, Vector2 o0, Vector2 o1, Vector2 i0, Vector2 i1, float t0, float t1)
    {
        int a = AddVert(vh, Vector2.Lerp(o0, o1, t0), accent);
        int b = AddVert(vh, Vector2.Lerp(o0, o1, t1), accent);
        int c = AddVert(vh, Vector2.Lerp(i0, i1, t1), accent);
        int d = AddVert(vh, Vector2.Lerp(i0, i1, t0), accent);
        vh.AddTriangle(a, b, c);
        vh.AddTriangle(a, c, d);
    }
}
}
