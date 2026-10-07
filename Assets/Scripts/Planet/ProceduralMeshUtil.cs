using System.Collections.Generic;
using UnityEngine;

namespace OrbitRush
{

/// <summary>
/// Small procedural mesh helpers — no external art assets needed.
/// Used by PlanetSurfaceDetail to build a higher-resolution planet sphere
/// (so craters read cleanly) and craggy little rock meshes.
/// </summary>
public static class ProceduralMeshUtil
{
    /// <summary>Builds a unit icosphere (radius 1, centered at origin) at the given subdivision level.</summary>
    public static Mesh CreateIcosphere(int subdivisions)
    {
        var vertices = new List<Vector3>();
        var triangles = new List<int>();

        // Base icosahedron — 12 vertices, 20 faces.
        float t = (1f + Mathf.Sqrt(5f)) / 2f;
        Vector3[] baseVerts =
        {
            new(-1,  t,  0), new( 1,  t,  0), new(-1, -t,  0), new( 1, -t,  0),
            new( 0, -1,  t), new( 0,  1,  t), new( 0, -1, -t), new( 0,  1, -t),
            new( t,  0, -1), new( t,  0,  1), new(-t,  0, -1), new(-t,  0,  1),
        };
        foreach (var v in baseVerts) vertices.Add(v.normalized);

        int[] baseTris =
        {
            0,11,5, 0,5,1, 0,1,7, 0,7,10, 0,10,11,
            1,5,9, 5,11,4, 11,10,2, 10,7,6, 7,1,8,
            3,9,4, 3,4,2, 3,2,6, 3,6,8, 3,8,9,
            4,9,5, 2,4,11, 6,2,10, 8,6,7, 9,8,1,
        };
        triangles.AddRange(baseTris);

        var midpointCache = new Dictionary<long, int>();
        for (int s = 0; s < subdivisions; s++)
        {
            var newTriangles = new List<int>(triangles.Count * 4);
            midpointCache.Clear();

            for (int i = 0; i < triangles.Count; i += 3)
            {
                int a = triangles[i], b = triangles[i + 1], c = triangles[i + 2];
                int ab = GetMidpoint(a, b, vertices, midpointCache);
                int bc = GetMidpoint(b, c, vertices, midpointCache);
                int ca = GetMidpoint(c, a, vertices, midpointCache);

                newTriangles.AddRange(new[] { a, ab, ca, b, bc, ab, c, ca, bc, ab, bc, ca });
            }

            triangles = newTriangles;
        }

        var mesh = new Mesh { name = "Icosphere" };
        mesh.SetVertices(vertices);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    private static int GetMidpoint(int a, int b, List<Vector3> vertices, Dictionary<long, int> cache)
    {
        long key = a < b ? ((long)a << 32) + (uint)b : ((long)b << 32) + (uint)a;
        if (cache.TryGetValue(key, out int existing)) return existing;

        Vector3 mid = ((vertices[a] + vertices[b]) * 0.5f).normalized;
        vertices.Add(mid);
        int index = vertices.Count - 1;
        cache[key] = index;
        return index;
    }
}
}
