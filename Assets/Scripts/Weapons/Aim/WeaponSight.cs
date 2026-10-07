using UnityEngine;

namespace OrbitRush
{

/// <summary>
/// A stylish holographic sight bolted onto the top rail of every weapon that has no scope of its own (added by
/// WeaponInventory). It is a small dark frame with a glowing cyan ring, four ticks and a hot-pink dot — a floating
/// reticle you can see on the gun in any view. In first person, aiming slides the gun so this reticle sits exactly
/// on the centre of the screen (see WeaponHandFollow), so what you see through the ring is where the shot goes.
/// Sized and placed from the weapon model's own bounds, so it fits every gun.
/// </summary>
public class WeaponSight : MonoBehaviour
{
    /// <summary>The centre of the reticle (weapon-local +Z is the barrel direction).</summary>
    public Transform ReticleAnchor { get; private set; }

    static readonly Color Cyan = new Color(0f, 0.85f, 1f);
    static readonly Color Pink = new Color(0.98f, 0.02f, 0.62f);

    void Awake() => Build();

    private void Build()
    {
        // The model's extent in this weapon's local space (works while the weapon is inactive)
        Bounds b = new Bounds(Vector3.zero, Vector3.zero);
        bool any = false;
        foreach (var mf in GetComponentsInChildren<MeshFilter>(true))
        {
            if (mf.sharedMesh == null) continue;
            var mb = mf.sharedMesh.bounds;
            for (int i = 0; i < 8; i++)
            {
                Vector3 corner = mb.center + Vector3.Scale(mb.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                Vector3 local = transform.InverseTransformPoint(mf.transform.TransformPoint(corner));
                if (!any) { b = new Bounds(local, Vector3.zero); any = true; } else b.Encapsulate(local);
            }
        }
        if (!any) { b = new Bounds(new Vector3(0f, 0.08f, 0.3f), new Vector3(0.1f, 0.16f, 0.6f)); }

        float top = b.max.y;
        float z = Mathf.Lerp(b.min.z, b.max.z, 0.30f);
        var root = new GameObject("Sight").transform;
        root.SetParent(transform, false);
        root.localPosition = new Vector3(b.center.x, top, z);

        var lit = Shader.Find("Universal Render Pipeline/Lit");
        Material Metal(float shade)
        {
            var m = new Material(lit);
            m.SetColor("_BaseColor", new Color(0.06f, 0.065f, 0.08f) * shade);
            m.SetFloat("_Metallic", 0.8f); m.SetFloat("_Smoothness", 0.5f);
            return m;
        }
        Material Glow(Color c, float power)
        {
            var m = new Material(lit);
            m.SetColor("_BaseColor", c * 0.3f);
            m.EnableKeyword("_EMISSION"); m.SetColor("_EmissionColor", c * power);
            m.SetFloat("_Cull", 0f);
            return m;
        }
        var metal = Metal(1f); var cyan = Glow(Cyan, 3.5f); var pink = Glow(Pink, 4.5f);

        // Mount plate and two side posts
        Part(root, "Base", PrimitiveType.Cube, new Vector3(0f, 0.006f, 0f), new Vector3(0.05f, 0.012f, 0.075f), metal);
        Part(root, "PostL", PrimitiveType.Cube, new Vector3(-0.027f, 0.03f, 0f), new Vector3(0.007f, 0.05f, 0.05f), metal);
        Part(root, "PostR", PrimitiveType.Cube, new Vector3(0.027f, 0.03f, 0f), new Vector3(0.007f, 0.05f, 0.05f), metal);
        Part(root, "Top", PrimitiveType.Cube, new Vector3(0f, 0.058f, 0f), new Vector3(0.062f, 0.007f, 0.05f), metal);

        // Reticle: ring + four ticks + dot, floating in the frame
        Vector3 c0 = new Vector3(0f, 0.034f, 0.0f);
        var ring = new GameObject("Ring", typeof(MeshFilter), typeof(MeshRenderer));
        ring.transform.SetParent(root, false); ring.transform.localPosition = c0;
        ring.GetComponent<MeshFilter>().sharedMesh = Torus(0.0205f, 0.0016f, 40, 6);
        ring.GetComponent<MeshRenderer>().sharedMaterial = cyan;
        for (int i = 0; i < 4; i++)
        {
            float a = i * 90f * Mathf.Deg2Rad;
            var tick = Part(root, "Tick" + i, PrimitiveType.Cube, c0 + new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f) * 0.0125f,
                            (i % 2 == 0) ? new Vector3(0.0085f, 0.0016f, 0.0016f) : new Vector3(0.0016f, 0.0085f, 0.0016f), cyan);
        }
        var dot = Part(root, "Dot", PrimitiveType.Sphere, c0, Vector3.one * 0.0042f, pink);

        var anchor = new GameObject("ReticleAnchor").transform;
        anchor.SetParent(root, false);
        anchor.localPosition = c0;
        ReticleAnchor = anchor;
    }

    private static GameObject Part(Transform parent, string name, PrimitiveType type, Vector3 pos, Vector3 scale, Material mat)
    {
        var go = GameObject.CreatePrimitive(type);
        go.name = name;
        Destroy(go.GetComponent<Collider>());
        go.transform.SetParent(parent, false);
        go.transform.localPosition = pos; go.transform.localScale = scale;
        var r = go.GetComponent<MeshRenderer>();
        r.sharedMaterial = mat;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.receiveShadows = false;
        return go;
    }

    private static Mesh Torus(float major, float minor, int segs, int sides)
    {
        var verts = new Vector3[(segs + 1) * (sides + 1)];
        var norms = new Vector3[verts.Length];
        var tris = new int[segs * sides * 6];
        int vi = 0;
        for (int i = 0; i <= segs; i++)
        {
            float a = i * Mathf.PI * 2f / segs;
            Vector3 center = new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f) * major;
            for (int j = 0; j <= sides; j++)
            {
                float b = j * Mathf.PI * 2f / sides;
                Vector3 n = new Vector3(Mathf.Cos(a) * Mathf.Cos(b), Mathf.Sin(a) * Mathf.Cos(b), Mathf.Sin(b));
                verts[vi] = center + n * minor; norms[vi] = n; vi++;
            }
        }
        int ti = 0;
        for (int i = 0; i < segs; i++)
            for (int j = 0; j < sides; j++)
            {
                int a = i * (sides + 1) + j, b = (i + 1) * (sides + 1) + j;
                tris[ti++] = a; tris[ti++] = a + 1; tris[ti++] = b;
                tris[ti++] = b; tris[ti++] = a + 1; tris[ti++] = b + 1;
            }
        var m = new Mesh { name = "SightRing", vertices = verts, normals = norms, triangles = tris };
        m.RecalculateBounds();
        return m;
    }
}
}
