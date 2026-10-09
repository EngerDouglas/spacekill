using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Lets a script outside Unity ask the open Editor to rebuild the planet collision: create the file
/// Temp/orbitrush_build_maps.request and the Editor (when idle) imports the models, runs GalaxySetup.BuildAll and writes
/// Temp/orbitrush_build_maps.result. Handy when the Editor can't be closed for a batch-mode run.
/// </summary>
[InitializeOnLoad]
static class MapPipelineTrigger
{
    static readonly string Request = Path.Combine(Directory.GetCurrentDirectory(), "Temp", "orbitrush_build_maps.request");
    static readonly string Result  = Path.Combine(Directory.GetCurrentDirectory(), "Temp", "orbitrush_build_maps.result");

    static MapPipelineTrigger() => EditorApplication.update += Poll;

    static readonly string DumpRequest = Path.Combine(Directory.GetCurrentDirectory(), "Temp", "orbitrush_dump_maps.request");
    static readonly string DumpResult  = Path.Combine(Directory.GetCurrentDirectory(), "Temp", "orbitrush_dump_maps.result");

    static readonly string GroundRequest = Path.Combine(Directory.GetCurrentDirectory(), "Temp", "orbitrush_ground_check.request");
    static readonly string GroundResult  = Path.Combine(Directory.GetCurrentDirectory(), "Temp", "orbitrush_ground_check.result");

    static void Poll()
    {
        if (File.Exists(GroundRequest) && !EditorApplication.isCompiling && !EditorApplication.isUpdating && !EditorApplication.isPlayingOrWillChangePlaymode)
        {
            File.Delete(GroundRequest);
            string r;
            try { r = GroundCheck(); } catch (System.Exception e) { r = "EXCEPTION\n" + e; }
            File.WriteAllText(GroundResult, r);
        }
        if (File.Exists(DumpRequest) && !EditorApplication.isCompiling && !EditorApplication.isUpdating)
        {
            File.Delete(DumpRequest);
            File.WriteAllText(DumpResult, DumpModels());
        }
        if (!File.Exists(Request)) return;
        if (EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode) return;

        File.Delete(Request);
        string log;
        try
        {
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            GalaxySetup.BuildAll();
            log = "DONE\n" + GalaxySetup.LastReport;
        }
        catch (System.Exception e) { log = "EXCEPTION\n" + e; }
        File.WriteAllText(Result, log);
    }

    /// <summary>Lists every transform of each planet model as Unity imported it (name, local position, scales, components).</summary>
    static string DumpModels()
    {
        var sb = new System.Text.StringBuilder();
        foreach (var name in new[] { "Planeta_Bosque", "Planeta_Ciudad_Destruida", "Planeta_Desierto", "Sol" })
        {
            var go = AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/Resources/Planets/{name}.fbx");
            sb.AppendLine("== " + name + (go == null ? "  (NOT FOUND)" : ""));
            if (go == null) continue;
            Walk(go.transform, 0, sb);
        }
        return sb.ToString();
    }

    static void Walk(Transform t, int depth, System.Text.StringBuilder sb)
    {
        var comps = new System.Collections.Generic.List<string>();
        foreach (var c in t.GetComponents<Component>()) if (!(c is Transform)) comps.Add(c.GetType().Name);
        sb.AppendLine($"{new string(' ', depth * 2)}{t.name}  localPos={t.localPosition}  localScale={t.localScale}  lossyScale={t.lossyScale}  [{string.Join(",", comps)}]");
        foreach (Transform child in t) Walk(child, depth + 1, sb);
    }

    /// <summary>
    /// For the planets whose ground collider is a perfect sphere (Bosque, Desierto): fires rays at the visible ground mesh from random
    /// directions and reports how far its surface is from that sphere. A big dip means the player seems to float there.
    /// </summary>
    static string GroundCheck()
    {
        var sb = new System.Text.StringBuilder();
        foreach (var map in new[] { "Planeta_Bosque", "Planeta_Desierto" })
        {
            var go = AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/Resources/Planets/{map}.fbx");
            var layoutAsset = AssetDatabase.LoadAssetAtPath<TextAsset>($"Assets/Resources/Planets/{map}_layout.json");
            if (go == null || layoutAsset == null) { sb.AppendLine($"{map}: missing model or layout"); continue; }
            float radius = JsonUtility.FromJson<LayoutProbe>(layoutAsset.text).planets[0].radiusMean;

            var mf = go.GetComponentInChildren<MeshFilter>();
            var mesh = mf.sharedMesh;
            var ground = new Mesh { indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            ground.vertices = mesh.vertices;
            ground.triangles = mesh.GetTriangles(0);

            var holder = new GameObject("GroundCheck_Temp");
            try
            {
                float scale = mf.transform.lossyScale.x;                     // the FBX carries a x100 unit scale
                holder.transform.position = Vector3.zero;
                holder.transform.localScale = Vector3.one * scale;
                holder.AddComponent<MeshCollider>().sharedMesh = ground;
                Physics.SyncTransforms();

                var rng = new System.Random(11);
                float worst = 0f, dip = 0f, bump = 0f; int hits = 0;
                for (int i = 0; i < 2000; i++)
                {
                    var d = new Vector3((float)Gauss(rng), (float)Gauss(rng), (float)Gauss(rng)).normalized;
                    if (Physics.Raycast(d * (radius * 2f), -d, out var hit, radius * 3f))
                    {
                        float dev = hit.point.magnitude - radius;
                        worst = Mathf.Max(worst, Mathf.Abs(dev)); dip = Mathf.Min(dip, dev); bump = Mathf.Max(bump, dev); hits++;
                    }
                }
                sb.AppendLine($"{map}: collider radius {radius:F2} m, rays hit {hits}/2000, visible ground is between {dip:+0.000;-0.000} and {bump:+0.000;-0.000} m of it (max |dev| {worst:F3} m)");
            }
            finally { Object.DestroyImmediate(holder); Object.DestroyImmediate(ground); }
        }
        return sb.ToString();
    }

    static double Gauss(System.Random r)
    {
        double u1 = 1.0 - r.NextDouble(), u2 = r.NextDouble();
        return System.Math.Sqrt(-2.0 * System.Math.Log(u1)) * System.Math.Cos(2.0 * System.Math.PI * u2);
    }

    [System.Serializable] class PlanetProbe { public float radiusMean; }
    [System.Serializable] class LayoutProbe { public PlanetProbe[] planets; }
}
