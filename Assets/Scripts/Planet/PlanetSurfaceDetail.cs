using System.Collections.Generic;
using UnityEngine;

namespace OrbitRush
{

/// <summary>
/// Adds procedural craters and scattered lunar rocks to a planet for extra
/// visual realism — no external art assets required. Rebuilds the planet's
/// own mesh at a higher resolution (so craters read cleanly instead of
/// looking blocky on Unity's low-poly built-in sphere) and scatters small
/// craggy rock meshes across the surface, nestled into the terrain.
///
/// Visual only: the SphereCollider / PlanetGravity radius stay a perfect
/// sphere, so gravity and ground-checks are unaffected — you won't physically
/// fall into a crater, it's a look, not walkable terrain.
///
/// Attach to a planet GameObject alongside PlanetGravity. Runs in the Editor
/// too (ExecuteAlways) so you can see the result without entering Play mode.
/// Right-click the component → "Regenerate Surface" to re-roll (same seed =
/// same layout, so it's safe to tweak numbers and regenerate repeatedly).
/// </summary>
[ExecuteAlways]
[RequireComponent(typeof(MeshFilter))]
public class PlanetSurfaceDetail : MonoBehaviour
{
    // Matches Unity's built-in sphere primitive convention (radius 0.5 at scale 1),
    // so this can replace that mesh without needing to touch the planet's transform scale.
    private const float MeshLocalRadius = 0.5f;

    [Header("Base Mesh")]
    [Tooltip("Higher = smoother craters, more triangles. 3 is a good default for a planet this size.")]
    [Range(1, 4)] public int meshSubdivisions = 3;

    [Header("Craters")]
    public int craterCount = 12;
    [Tooltip("Crater angular size in radians (arc across the sphere), min/max.")]
    [Range(0.02f, 0.5f)] public float craterMinAngularSize = 0.06f;
    [Range(0.02f, 0.6f)] public float craterMaxAngularSize = 0.18f;
    [Tooltip("Bowl depth as a fraction of the planet's local mesh radius.")]
    [Range(0f, 0.25f)] public float craterDepth = 0.05f;
    [Tooltip("Small raised lip right at the crater rim, as a fraction of local radius.")]
    [Range(0f, 0.05f)] public float craterRimHeight = 0.012f;

    [Header("Rocks")]
    public int rockCount = 35;
    [Range(0.005f, 0.05f)] public float rockMinScale = 0.015f;
    [Range(0.01f, 0.08f)] public float rockMaxScale = 0.035f;
    public Material rockMaterial;
    [Tooltip("Optional — if set, these prefabs are scattered instead of the built-in procedural rocks.")]
    public GameObject[] rockPrefabVariants;

    [Header("Seed")]
    public int seed = 12345;

    private struct Crater
    {
        public Vector3 center;
        public float angularRadius;
        public float depth;
        public float rimHeight;
    }

    private List<Crater> _craters = new();

    void OnEnable() => Regenerate();

    [ContextMenu("Regenerate Surface")]
    public void Regenerate()
    {
        var rng = new System.Random(seed);

        GenerateCraters(rng);
        ApplyCratersToPlanetMesh();
        ScatterRocks(rng);
    }

    // ── Craters ──────────────────────────────────────────────────────────

    private void GenerateCraters(System.Random rng)
    {
        _craters = new List<Crater>(craterCount);
        for (int i = 0; i < craterCount; i++)
        {
            _craters.Add(new Crater
            {
                center = RandomDirection(rng),
                angularRadius = NextFloat(rng, craterMinAngularSize, craterMaxAngularSize),
                depth = craterDepth * NextFloat(rng, 0.6f, 1f),
                rimHeight = craterRimHeight * NextFloat(rng, 0.6f, 1f),
            });
        }
    }

    private void ApplyCratersToPlanetMesh()
    {
        var mesh = ProceduralMeshUtil.CreateIcosphere(meshSubdivisions);
        var verts = mesh.vertices; // unit-length directions from the icosphere

        for (int i = 0; i < verts.Length; i++)
        {
            Vector3 dir = verts[i];
            verts[i] = dir * (MeshLocalRadius * GetSurfaceMultiplier(dir));
        }

        mesh.vertices = verts;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        mesh.name = "Planet Surface (procedural)";

        GetComponent<MeshFilter>().sharedMesh = mesh;
    }

    /// <summary>1 = base surface, &lt;1 = crater floor, slightly &gt;1 = raised rim.</summary>
    private float GetSurfaceMultiplier(Vector3 dir)
    {
        float mult = 1f;
        foreach (var c in _craters)
        {
            float ang = Vector3.Angle(dir, c.center) * Mathf.Deg2Rad;
            if (ang >= c.angularRadius) continue;

            float t = ang / c.angularRadius; // 0 at center .. 1 at rim
            float bowl = Mathf.Cos(t * Mathf.PI * 0.5f);               // 1 -> 0, smooth bowl
            float rim = Mathf.Exp(-Mathf.Pow((t - 0.85f) * 10f, 2f));  // small raised lip near the edge

            mult -= c.depth * bowl;
            mult += c.rimHeight * rim;
        }
        return Mathf.Max(mult, 0.5f); // safety floor so overlapping craters can't invert the mesh
    }

    // ── Rocks ────────────────────────────────────────────────────────────

    private void ScatterRocks(System.Random rng)
    {
        var existing = transform.Find("Rocks");
        if (existing != null) DestroyImmediateOrRuntime(existing.gameObject);

        var container = new GameObject("Rocks");
        container.transform.SetParent(transform, false);

        bool useCustomPrefabs = rockPrefabVariants != null && rockPrefabVariants.Length > 0;
        Mesh[] proceduralVariants = useCustomPrefabs ? null : BuildRockMeshVariants(rng, 4);

        for (int i = 0; i < rockCount; i++)
        {
            Vector3 dir = RandomDirection(rng);
            Vector3 localPos = dir * (MeshLocalRadius * GetSurfaceMultiplier(dir));

            Quaternion align = Quaternion.FromToRotation(Vector3.up, dir);
            Quaternion spin = Quaternion.AngleAxis(NextFloat(rng, 0f, 360f), dir);
            Quaternion rot = spin * align;
            float scale = NextFloat(rng, rockMinScale, rockMaxScale);

            GameObject rock;
            if (useCustomPrefabs)
            {
                var prefab = rockPrefabVariants[rng.Next(rockPrefabVariants.Length)];
                if (prefab == null) continue;
                rock = Instantiate(prefab, container.transform);
            }
            else
            {
                rock = new GameObject($"Rock_{i}");
                rock.transform.SetParent(container.transform, false);
                rock.AddComponent<MeshFilter>().sharedMesh = proceduralVariants[rng.Next(proceduralVariants.Length)];
                var mr = rock.AddComponent<MeshRenderer>();
                mr.sharedMaterial = rockMaterial != null ? rockMaterial : GetDefaultRockMaterial();
            }

            rock.transform.localPosition = localPos;
            rock.transform.localRotation = rot;
            rock.transform.localScale = Vector3.one * scale;
        }
    }

    private Mesh[] BuildRockMeshVariants(System.Random rng, int count)
    {
        var variants = new Mesh[count];
        for (int i = 0; i < count; i++)
        {
            var mesh = ProceduralMeshUtil.CreateIcosphere(1);
            var verts = mesh.vertices;
            for (int v = 0; v < verts.Length; v++)
                verts[v] *= NextFloat(rng, 0.7f, 1.3f); // craggy, irregular jitter per vertex

            mesh.vertices = verts;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            mesh.name = $"Rock Variant {i}";
            variants[i] = mesh;
        }
        return variants;
    }

    private static Material _defaultRockMaterial;
    private static Material GetDefaultRockMaterial()
    {
        if (_defaultRockMaterial == null)
        {
            _defaultRockMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            _defaultRockMaterial.color = new Color(0.45f, 0.42f, 0.40f); // dull grey — lunar rock, not painted plastic
        }
        return _defaultRockMaterial;
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    private static Vector3 RandomDirection(System.Random rng)
    {
        // Uniform random point on a unit sphere.
        float z = NextFloat(rng, -1f, 1f);
        float theta = NextFloat(rng, 0f, Mathf.PI * 2f);
        float r = Mathf.Sqrt(Mathf.Max(0f, 1f - z * z));
        return new Vector3(r * Mathf.Cos(theta), r * Mathf.Sin(theta), z);
    }

    private static float NextFloat(System.Random rng, float min, float max)
        => min + (float)rng.NextDouble() * (max - min);

    private static void DestroyImmediateOrRuntime(GameObject go)
    {
        if (Application.isPlaying) Object.Destroy(go);
        else Object.DestroyImmediate(go);
    }
}
}
