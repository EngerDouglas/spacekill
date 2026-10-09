using System;
using System.Collections.Generic;
using UnityEngine;

namespace OrbitRush
{

/// <summary>A named place on a planet (Claro de inicio, Aserradero...). `dir` is the unit direction from the planet's centre in world space.</summary>
[Serializable]
public class PlanetZone
{
    public string name;
    public string role;       // inicio, combate, jefe, horda, captura, agua
    public Vector3 dir;
    public float clear;       // radius of the cleared area (m)
}

/// <summary>
/// Reads Resources/Planets/&lt;map&gt;_zones.json (written by BlenderScripts/bosque_map.py) and fills <see cref="PlanetGravity.zones"/> /
/// <see cref="PlanetGravity.pathArcs"/>. The JSON directions are in Blender's object space; the axis mapping into Unity is found by matching
/// the named anchors (pond, mushrooms, rocks) against the vertices of the planet mesh.
/// </summary>
public static class PlanetZonesLoader
{
    [Serializable] private class AnchorJson { public string material; public float[] dir; }
    [Serializable] private class ZoneJson { public string name, role; public float[] dir; public float clear; }
    [Serializable] private class ArcJson { public string a, b; }
    [Serializable] private class FileJson { public float radius; public ZoneJson[] zones; public ArcJson[] arcs; public AnchorJson[] anchors; public float pathHalfWidth; }

    public static bool Load(string map, Transform planet, PlanetGravity gravity)
    {
        var asset = Resources.Load<TextAsset>("Planets/" + map + "_zones");
        if (asset == null) return false;
        var file = JsonUtility.FromJson<FileJson>(asset.text);
        if (file == null || file.zones == null || file.zones.Length == 0) return false;

        var map3 = FindMapping(planet, file.anchors, out float score);
        if (map3 == null) { Debug.LogWarning($"[Zones] {map}: could not match the zone anchors to the mesh; zones ignored."); return false; }
        if (score < 0.97f) Debug.LogWarning($"[Zones] {map}: weak anchor match ({score:F3}); zones may be misplaced.");

        gravity.zones = new List<PlanetZone>();
        var byName = new Dictionary<string, Vector3>();
        foreach (var z in file.zones)
        {
            var dir = ToWorld(planet, map3.Value, z.dir);
            gravity.zones.Add(new PlanetZone { name = z.name, role = z.role, dir = dir, clear = z.clear });
            byName[z.name] = dir;
        }
        gravity.pathArcs = new List<Vector3[]>();
        if (file.arcs != null)
            foreach (var a in file.arcs)
                if (byName.TryGetValue(a.a, out var A) && byName.TryGetValue(a.b, out var B)) gravity.pathArcs.Add(new[] { A, B });
        gravity.pathHalfWidth = file.pathHalfWidth;
        Debug.Log($"[Zones] {map}: {gravity.zones.Count} zones, {gravity.pathArcs.Count} path arcs (anchor match {score:F3}).");
        return true;
    }

    // ── axis mapping ─────────────────────────────────────────────────────

    private static Vector3 ToWorld(Transform planet, Matrix4x4 m, float[] d)
        => planet.TransformDirection(m.MultiplyVector(new Vector3(d[0], d[1], d[2]))).normalized;

    /// <summary>The 24 rotations that permute and flip the axes; the right one is the one whose images of the anchors land on the anchor materials.</summary>
    private static Matrix4x4? FindMapping(Transform planet, AnchorJson[] anchors, out float bestScore)
    {
        bestScore = 0f;
        if (anchors == null || anchors.Length == 0) return null;

        // World-space directions of the vertices of each anchor material
        var sets = new List<Vector3[]>();
        foreach (var a in anchors)
        {
            var dirs = new List<Vector3>();
            foreach (var r in planet.GetComponentsInChildren<MeshRenderer>(true))
            {
                var mf = r.GetComponent<MeshFilter>();
                var mesh = mf != null ? mf.sharedMesh : null;
                if (mesh == null || !mesh.isReadable) continue;
                var mats = r.sharedMaterials;
                for (int s = 0; s < mats.Length && s < mesh.subMeshCount; s++)
                {
                    if (mats[s] == null || !mats[s].name.StartsWith(a.material)) continue;
                    var verts = mesh.vertices;
                    var idx = mesh.GetIndices(s);
                    for (int i = 0; i < idx.Length; i += 3)       // one sample per triangle keeps it cheap
                        dirs.Add((r.transform.TransformPoint(verts[idx[i]]) - planet.position).normalized);
                }
            }
            sets.Add(dirs.ToArray());
        }

        Matrix4x4? best = null;
        int[][] perms = { new[] { 0, 1, 2 }, new[] { 0, 2, 1 }, new[] { 1, 0, 2 }, new[] { 1, 2, 0 }, new[] { 2, 0, 1 }, new[] { 2, 1, 0 } };
        foreach (var p in perms)
            for (int sx = -1; sx <= 1; sx += 2) for (int sy = -1; sy <= 1; sy += 2) for (int sz = -1; sz <= 1; sz += 2)
            {
                var m = Matrix4x4.zero; m.m33 = 1f;
                m[p[0], 0] = sx; m[p[1], 1] = sy; m[p[2], 2] = sz;       // column j of the input goes to row p[j] of the output
                if (m.determinant < 0f) continue;                       // keep proper rotations only

                float worst = 1f;
                for (int k = 0; k < anchors.Length; k++)
                {
                    if (sets[k].Length == 0) { worst = 0f; break; }
                    var want = ToWorld(planet, m, anchors[k].dir);
                    float top = -1f;
                    foreach (var d in sets[k]) { float dot = Vector3.Dot(want, d); if (dot > top) top = dot; }
                    worst = Mathf.Min(worst, top);
                }
                if (worst > bestScore) { bestScore = worst; best = m; }
            }
        return best;
    }
}

/// <summary>Helpers to turn zone directions into places on the ground.</summary>
public static class PlanetZoneMath
{
    /// <summary>The direction `meters` away from `center` along the surface, at azimuth `az` (radians) around it.</summary>
    public static Vector3 DirAround(Vector3 center, float meters, float az, float radius)
    {
        Vector3 e1 = Vector3.Cross(center, Mathf.Abs(center.y) < 0.9f ? Vector3.up : Vector3.right).normalized;
        Vector3 e2 = Vector3.Cross(center, e1);
        float t = meters / Mathf.Max(1f, radius);
        return (Mathf.Cos(t) * center + Mathf.Sin(t) * (Mathf.Cos(az) * e1 + Mathf.Sin(az) * e2)).normalized;
    }

    /// <summary>Evenly spaced directions along all path arcs.</summary>
    public static IEnumerable<Vector3> PathDirs(PlanetGravity planet, float spacingMeters)
    {
        if (planet.pathArcs == null) yield break;
        foreach (var arc in planet.pathArcs)
        {
            float ang = Mathf.Acos(Mathf.Clamp(Vector3.Dot(arc[0], arc[1]), -1f, 1f));
            int n = Mathf.Max(1, Mathf.RoundToInt(ang * planet.radius / spacingMeters));
            for (int i = 1; i < n; i++)
            {
                float t = ang * i / n;
                yield return (Mathf.Sin(ang - t) / Mathf.Sin(ang) * arc[0] + Mathf.Sin(t) / Mathf.Sin(ang) * arc[1]).normalized;
            }
        }
    }

    public static PlanetZone ByRole(PlanetGravity planet, string role)
    {
        if (planet.zones == null) return null;
        foreach (var z in planet.zones) if (z.role == role) return z;
        return null;
    }
}
}
