using OrbitRush;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// One-time setup for a planets map FBX in Resources/Planets (e.g. Planetas.fbx). Run with
///   Unity.exe -batchmode -quit -projectPath ... -executeMethod GalaxySetup.BuildCollision -mapName Planetas
/// It writes, per planet child ("X" with a mesh child "X_Malla"):
///   Resources/Planets/MapCol/&lt;map&gt;/GND_X.asset  the terrain (submesh 0) — hills, dunes, the real ground
///   Resources/Planets/MapCol/&lt;map&gt;/OBS_X.asset  the tall solid things (buildings, trees, tanks, rocks)
///   Resources/Planets/&lt;map&gt;_layout.json         per-planet terrain radii and the sun's direction
/// GalaxyLoader reads these at runtime.
/// </summary>
public static class GalaxySetup
{
    static string Arg(string name, string fallback)
    {
        var a = System.Environment.GetCommandLineArgs();
        for (int i = 0; i < a.Length - 1; i++) if (a[i] == name) return a[i + 1];
        return fallback;
    }
    static string MapName => Arg("-mapName", "Planeta_Bosque");
    static string FbxPathOf(string map) => $"Assets/Resources/Planets/{map}.fbx";
    static string FbxPath => FbxPathOf(MapName);

    /// <summary>The planet models GalaxyLoader loads (one FBX each). BuildAll processes all of them.</summary>
    public static readonly string[] AllMaps = { "Planeta_Bosque", "Planeta_Ciudad_Destruida", "Planeta_Desierto" };

    /// <summary>What the last BuildAll / BuildCollision did, for the log and for the editor trigger.</summary>
    public static string LastReport = "";

    // Materials whose geometry is decoration only: never collides
    static readonly string[] NoCollision = { "Humo", "Agua", "Waterfall", "Fuego", "Hielo_Lago", "Junco", "Pasto", "Flor_", "Hongo_", "Lodo", "Grass", "Fog", "Cloud", "Nube", "Smoke", "Particle", "Glow_Halo", "CD_Fuego", "CD_Neon" };
    const float MinHeight = 0.9f;      // obstacles must stick out of the ground by at least this many metres
    /// <summary>The ruined city is covered in knee-high debris (tens of thousands of pieces): only things taller than the player count as solid there.</summary>
    static float MinHeightFor(string map) => map.Contains("Ciudad") ? 1.6f : MinHeight;

    [System.Serializable] class PlanetInfo { public string child; public float radiusMin, radiusMax, radiusMean; }
    [System.Serializable] class Layout { public PlanetInfo[] planets; public bool hasSun; public Vector3 sunEuler; public float unitScale; }

    public static void ExtractTextures()
    {
        var importer = AssetImporter.GetAtPath(FbxPath) as ModelImporter;
        if (importer == null) { Debug.Log("[GalaxySetup] no importer"); EditorApplication.Exit(1); return; }
        string texDir = $"Assets/Resources/Planets/{MapName}Textures";
        if (!Directory.Exists(texDir)) Directory.CreateDirectory(texDir);
        AssetDatabase.Refresh();
        importer.ExtractTextures(texDir);
        AssetDatabase.ImportAsset(FbxPath, ImportAssetOptions.ForceUpdate);
        AssetDatabase.SaveAssets();
        Debug.Log($"[GalaxySetup] extracted textures: {Directory.GetFiles(texDir, "*.*").Length}");
        EditorApplication.Exit(0);
    }

    /// <summary>Builds the collision and layout of one planet model (-mapName). Batch mode exits afterwards.</summary>
    public static void BuildCollision()
    {
        LastReport = "";
        bool ok = BuildMap(MapName);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        if (Application.isBatchMode) EditorApplication.Exit(ok ? 0 : 1);
    }

    /// <summary>Builds the collision and layout of every planet model in <see cref="AllMaps"/>.</summary>
    [MenuItem("OrbitRush/Rebuild planet collision (all maps)")]
    public static void BuildAll()
    {
        LastReport = "";
        bool all = true;
        foreach (var m in AllMaps) all &= BuildMap(m);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("[GalaxySetup] BuildAll finished. " + (all ? "OK" : "WITH ERRORS") + "\n" + LastReport);
        if (Application.isBatchMode) EditorApplication.Exit(all ? 0 : 1);
    }

    static void Report(string line) { Debug.Log(line); LastReport += line + "\n"; }

    static bool BuildMap(string mapName)
    {
        var go = AssetDatabase.LoadAssetAtPath<GameObject>(FbxPathOf(mapName));
        if (go == null) { Report("[GalaxySetup] no model " + FbxPathOf(mapName)); return false; }
        string outDir = $"Assets/Resources/Planets/MapCol/{mapName}";
        Directory.CreateDirectory(outDir);

        var layout = new Layout { unitScale = 1f };
        var infos = new List<PlanetInfo>();

        foreach (var mf in go.GetComponentsInChildren<MeshFilter>(true))
        {
            string key = mf.name.EndsWith("_Malla") ? mf.name.Substring(0, mf.name.Length - 6) : mf.name;
            var mesh = mf.sharedMesh;
            if (mesh == null) continue;
            float scale = mf.transform.lossyScale.x;             // the FBX may carry a x100 unit scale: work in metres
            layout.unitScale = scale;
            var rend = mf.GetComponent<Renderer>();
            var mats = rend != null ? rend.sharedMaterials : new Material[0];
            var verts = mesh.vertices;
            var wv = new Vector3[verts.Length];
            for (int i = 0; i < verts.Length; i++) wv[i] = verts[i] * scale;

            // ── Terrain: submesh 0 ──
            var groundTris = mesh.GetTriangles(0).ToList();
            float gMin = float.MaxValue, gMax = 0f; double gSum = 0; int gCount = 0;
            foreach (int vi in groundTris.Distinct())
            {
                float r = wv[vi].magnitude;
                gMin = Mathf.Min(gMin, r); gMax = Mathf.Max(gMax, r); gSum += r; gCount++;
            }
            infos.Add(new PlanetInfo { child = key, radiusMin = gMin, radiusMax = gMax, radiusMean = (float)(gSum / Mathf.Max(1, gCount)) });
            SaveMesh(verts, groundTris, $"{outDir}/GND_{key}.asset", "GND_" + key);

            // ── Obstacles: every other collidable submesh, tall connected pieces only ──
            var tris = new List<int>();
            for (int sm = 1; sm < mesh.subMeshCount; sm++)
            {
                string mn = sm < mats.Length && mats[sm] != null ? mats[sm].name : "";
                if (NoCollision.Any(x => mn.StartsWith(x))) continue;
                tris.AddRange(mesh.GetTriangles(sm));
            }

            var weld = new Dictionary<(int, int, int), int>();
            var map = new int[verts.Length];
            for (int i = 0; i < verts.Length; i++)
            {
                var k = (Mathf.RoundToInt(wv[i].x * 200f), Mathf.RoundToInt(wv[i].y * 200f), Mathf.RoundToInt(wv[i].z * 200f));
                if (!weld.TryGetValue(k, out int id)) { id = weld.Count; weld[k] = id; }
                map[i] = id;
            }
            var parent = Enumerable.Range(0, weld.Count).ToArray();
            int Find(int x) { while (parent[x] != x) { parent[x] = parent[parent[x]]; x = parent[x]; } return x; }
            for (int i = 0; i < tris.Count; i += 3)
            {
                int a = Find(map[tris[i]]), b = Find(map[tris[i + 1]]);
                if (a != b) parent[b] = a;
                int c = Find(map[tris[i + 2]]); a = Find(a);
                if (a != c) parent[c] = a;
            }
            var rmin = new Dictionary<int, float>(); var rmax = new Dictionary<int, float>();
            for (int i = 0; i < tris.Count; i++)
            {
                int root = Find(map[tris[i]]);
                float r = wv[tris[i]].magnitude;
                if (!rmin.ContainsKey(root)) { rmin[root] = r; rmax[root] = r; }
                else { if (r < rmin[root]) rmin[root] = r; if (r > rmax[root]) rmax[root] = r; }
            }
            var kept = new List<int>(); var comps = new HashSet<int>();
            for (int i = 0; i < tris.Count; i += 3)
            {
                int root = Find(map[tris[i]]);
                if (rmax[root] - rmin[root] < MinHeightFor(mapName)) continue;
                comps.Add(root);
                kept.Add(tris[i]); kept.Add(tris[i + 1]); kept.Add(tris[i + 2]);
            }
            SaveMesh(verts, kept, $"{outDir}/OBS_{key}.asset", "OBS_" + key);
            Report($"[GalaxySetup] {mapName}/{key}: scale={scale} terrain tris={groundTris.Count / 3} radius {gMin:F1}..{gMax:F1} (mean {gSum / Mathf.Max(1, gCount):F1}); obstacles {comps.Count} pieces, {kept.Count / 3} tris");
        }
        layout.planets = infos.ToArray();

        // The sun: an empty called "Sol" whose rotation is the light's direction
        foreach (var t in go.GetComponentsInChildren<Transform>(true))
            if (t.name == "Sol") { layout.hasSun = true; layout.sunEuler = t.rotation.eulerAngles; }

        File.WriteAllText($"Assets/Resources/Planets/{mapName}_layout.json", JsonUtility.ToJson(layout, true));
        return true;
    }

    /// <summary>Saves the given triangles of the model's own vertex array (local space) as a compact mesh asset.</summary>
    static void SaveMesh(Vector3[] verts, List<int> tris, string path, string name)
    {
        var remap = new Dictionary<int, int>(); var nv = new List<Vector3>(); var nt = new List<int>(tris.Count);
        foreach (int vi in tris)
        {
            if (!remap.TryGetValue(vi, out int ni)) { ni = nv.Count; nv.Add(verts[vi]); remap[vi] = ni; }
            nt.Add(ni);
        }
        var m = new Mesh { name = name, indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
        m.SetVertices(nv); m.SetTriangles(nt, 0); m.RecalculateBounds();
        AssetDatabase.CreateAsset(m, path);
    }
}
