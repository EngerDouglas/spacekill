using UnityEngine;
using UnityEngine.UI;

namespace OrbitRush
{

/// <summary>
/// A flat ring sector drawn as one mesh (like NeonPanel, no sprite needed): used for the weapon wheel's segments and the
/// HUD's damage-direction arcs. Angles are in degrees, 0 = up, increasing clockwise. The arc is centred on the rect's centre.
/// </summary>
[RequireComponent(typeof(CanvasRenderer))]
public class HudArc : MaskableGraphic
{
    public float outerRadius = 100f;
    public float innerRadius = 94f;
    public float startAngle = -30f;
    public float sweepAngle = 60f;

    public override Texture mainTexture => s_WhiteTexture;

    /// <summary>Changes the shape in one call and rebuilds the mesh only if something actually changed.</summary>
    public void Set(float outer, float inner, float start, float sweep)
    {
        if (Mathf.Approximately(outer, outerRadius) && Mathf.Approximately(inner, innerRadius)
            && Mathf.Approximately(start, startAngle) && Mathf.Approximately(sweep, sweepAngle)) return;
        outerRadius = outer; innerRadius = inner; startAngle = start; sweepAngle = sweep;
        SetVerticesDirty();
    }

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        int steps = Mathf.Max(2, Mathf.CeilToInt(Mathf.Abs(sweepAngle) / 3f));
        Vector2 c = rectTransform.rect.center;
        Color32 col = color;

        for (int i = 0; i <= steps; i++)
        {
            float a = (startAngle + sweepAngle * i / steps) * Mathf.Deg2Rad;
            var dir = new Vector2(Mathf.Sin(a), Mathf.Cos(a));
            vh.AddVert(c + dir * outerRadius, col, Vector2.zero);
            vh.AddVert(c + dir * innerRadius, col, Vector2.zero);
        }
        for (int i = 0; i < steps; i++)
        {
            int k = i * 2;
            vh.AddTriangle(k, k + 2, k + 1);
            vh.AddTriangle(k + 1, k + 2, k + 3);
        }
    }
}
}
