using UnityEngine;

namespace OrbitRush
{

/// <summary>
/// Small floating health bar above an AI enemy so players can see damage landing.
/// Built from two quads (no UI/canvas needed); always faces the camera.
/// </summary>
[RequireComponent(typeof(EnemyStats))]
public class EnemyHealthBar : MonoBehaviour
{
    public float height = 2.5f;
    public float width = 1.2f;
    public float thickness = 0.12f;

    private EnemyStats _stats;
    private Transform _root;
    private Transform _fill;
    private Renderer _fillRenderer;
    private MaterialPropertyBlock _block;

    void Start()
    {
        _stats = GetComponent<EnemyStats>();

        _root = new GameObject("HealthBar").transform;
        _root.SetParent(transform, false);
        _root.localPosition = new Vector3(0f, height, 0f);

        MakeQuad("Back", new Color(0.05f, 0.05f, 0.08f), width + 0.06f, thickness + 0.06f, 0f);
        _fill = MakeQuad("Fill", Color.green, width, thickness, -0.001f, out _fillRenderer);
        _block = new MaterialPropertyBlock();
    }

    void LateUpdate()
    {
        if (_root == null) return;

        float pct = _stats != null ? _stats.HealthPercent : 0f;

        // Shrink the fill from the right, keeping its left edge fixed. (Scale must keep the bar's real
        // width/thickness — setting it to (pct, 1, 1) made the quad a 1 m square above every enemy.)
        _fill.localScale = new Vector3(Mathf.Max(0.0001f, pct * width), thickness, 1f);
        _fill.localPosition = new Vector3(-(1f - pct) * width * 0.5f, 0f, -0.001f);

        Color c = Color.Lerp(new Color(1f, 0.15f, 0.1f), new Color(0.2f, 1f, 0.3f), pct);
        _fillRenderer.GetPropertyBlock(_block);
        _block.SetColor("_BaseColor", c);
        _block.SetColor("_Color", c);
        _fillRenderer.SetPropertyBlock(_block);

        var cam = Camera.main;
        if (cam != null) _root.rotation = cam.transform.rotation;
    }

    private Transform MakeQuad(string name, Color color, float w, float h, float z)
        => MakeQuad(name, color, w, h, z, out _);

    private Transform MakeQuad(string name, Color color, float w, float h, float z, out Renderer renderer)
    {
        var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
        quad.name = name;
        var col = quad.GetComponent<Collider>();
        if (col != null) Destroy(col);   // visual only — must not block shots

        quad.transform.SetParent(_root, false);
        quad.transform.localPosition = new Vector3(0f, 0f, z);
        quad.transform.localScale = new Vector3(w, h, 1f);

        // Unlit so the bar reads the same in any lighting.
        var shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null) shader = Shader.Find("Sprites/Default");
        var mat = new Material(shader);
        mat.color = color;
        if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);

        renderer = quad.GetComponent<Renderer>();
        renderer.sharedMaterial = mat;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        return quad.transform;
    }
}
}
