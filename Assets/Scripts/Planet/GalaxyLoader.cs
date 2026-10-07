using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace OrbitRush
{

/// <summary>
/// Loads the main map: Resources/Planets/Planetas.fbx — six planets laid out in a hexagon around the origin
/// (Bosque, Desierto, Tanques Industriales, Neon Downtown, Sky Gardens, Nieve) — and replaces the scene's old
/// planets and the PvpPlanetLoader city planets with it.
///
/// Runs on every scene load that contains a GameManager (before the first Start), no scene edits needed.
/// Each planet gets: ground collision, a MeshCollider with its buildings / trees / tanks / rocks, a PlanetGravity and
/// weapon / grenade pickups. Collision meshes and per-planet radii come from Resources/Planets/MapCol and
/// Planetas_layout.json (built by GalaxySetup); albedo maps are matched to materials by name
/// (Resources/Planets/GalaxyTextures). The model's "Sol" empty gives the sun's direction.
///
/// To go back to the old map delete Planetas.fbx or set <see cref="Enabled"/> to false.
/// </summary>
public static class GalaxyLoader
{
    public static bool Enabled = true;

    private const string MapName = "Planetas";
    private const string RootName = "OrbitRush_Galaxy";
    private static bool? _available;

    /// <summary>True when the map model exists in Resources (and the loader is enabled).</summary>
    public static bool Available
    {
        get
        {
            if (!Enabled) return false;
            if (_available == null) _available = Resources.Load<GameObject>("Planets/" + MapName) != null;
            return _available.Value;
        }
    }

    [System.Serializable] private class PlanetInfo { public string child; public float radiusMin, radiusMax, radiusMean; }
    [System.Serializable] private class Layout { public PlanetInfo[] planets; public bool hasSun; public Vector3 sunEuler; public float unitScale; }

    private static string Display(string child)
    {
        switch (child)
        {
            case "Bosque": return "Bosque";
            case "Desierto": return "Desierto";
            case "Industrial_Tanques": return "Tanques Industriales";
            case "NeonDistrict_Downtown": return "Neon Downtown";
            case "NeonDistrict_Industrial": return "Neon Industrial";
            case "NeonDistrict_SkyGardens": return "Sky Gardens";
            case "Nieve": return "Nieve";
            default: return child.Replace('_', ' ');
        }
    }

    private static PlanetGravity.BiomeType Biome(string child)
    {
        if (child.Contains("Nieve")) return PlanetGravity.BiomeType.Ice;
        if (child.Contains("Desierto")) return PlanetGravity.BiomeType.Desert;
        if (child.Contains("Industrial")) return PlanetGravity.BiomeType.Volcanic;
        if (child.Contains("Neon")) return PlanetGravity.BiomeType.Alien;
        return PlanetGravity.BiomeType.Rocky;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Register()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (!Available) return;
        if (Object.FindFirstObjectByType<GameManager>() == null) return;
        if (GameObject.Find(RootName) != null) return;

        var prefab = Resources.Load<GameObject>("Planets/" + MapName);
        var root = Object.Instantiate(prefab);
        root.name = RootName;
        root.transform.position = Vector3.zero;

        var layoutAsset = Resources.Load<TextAsset>("Planets/" + MapName + "_layout");
        var layout = layoutAsset != null ? JsonUtility.FromJson<Layout>(layoutAsset.text) : null;

        // Retire the old planets (the scene's two and any city planets from PvpPlanetLoader)
        foreach (var old in Object.FindObjectsByType<PlanetGravity>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (old.transform.IsChildOf(root.transform)) continue;
            old.gameObject.SetActive(false);
        }

        int built = 0;
        if (layout != null && layout.planets != null)
        {
            foreach (var info in layout.planets)
            {
                var planet = root.transform.Find(info.child);
                if (planet == null) { Debug.LogWarning($"[GalaxyLoader] '{info.child}' not found in the map model."); continue; }
                Build(info, planet);
                built++;
            }
        }
        else Debug.LogWarning("[GalaxyLoader] layout json missing — run GalaxySetup.BuildCollision.");

        ApplySun(layout);

        Physics.SyncTransforms();
        foreach (var s in Object.FindObjectsByType<PlayerSpawnSetup>(FindObjectsSortMode.None)) s.Regenerate();
        foreach (var s in Object.FindObjectsByType<EnemySpawnSetup>(FindObjectsSortMode.None)) s.Regenerate();

        // The scene's player starts next to the old planets: put it on a spawn point of the new map
        var gm = Object.FindFirstObjectByType<GameManager>();
        var player = GameObject.FindWithTag("Player");
        if (gm != null && player != null && gm.spawnPoints != null && gm.spawnPoints.Length > 0)
        {
            var point = gm.GetSpawnPoint();
            var rb = player.GetComponent<Rigidbody>();
            if (rb != null) { rb.position = point.position; rb.rotation = point.rotation; rb.linearVelocity = Vector3.zero; rb.angularVelocity = Vector3.zero; }
            player.transform.SetPositionAndRotation(point.position, point.rotation);
        }
        Debug.Log($"[GalaxyLoader] Map '{MapName}' loaded: {built} planet(s).");
    }

    private static void Build(PlanetInfo info, Transform planet)
    {
        // The model's imported colliders (none expected) would fight the ones below
        foreach (var c in planet.GetComponentsInChildren<Collider>(true)) Object.Destroy(c);

        ApplyTextures(planet);

        var meshTransform = planet.Find(info.child + "_Malla");
        Transform meshParent = meshTransform != null ? meshTransform : planet;

        // Ground: a perfect sphere when the terrain is one, the terrain mesh itself when it has hills
        bool hasRelief = info.radiusMax - info.radiusMin > 0.5f;
        float radius = hasRelief ? info.radiusMax : info.radiusMean;
        Collider ground = null;
        if (hasRelief)
        {
            var gm = Resources.Load<Mesh>($"Planets/MapCol/{MapName}/GND_{info.child}");
            if (gm != null)
            {
                var holder = new GameObject("GroundCollision");
                holder.transform.SetParent(meshParent, false);
                var mc = holder.AddComponent<MeshCollider>();
                mc.sharedMesh = gm;
                ground = mc;
            }
        }
        if (ground == null)
        {
            var sphere = planet.gameObject.AddComponent<SphereCollider>();
            float s = Mathf.Max(0.0001f, planet.lossyScale.x);     // the model carries a x100 scale: the sphere radius is in local units
            sphere.radius = info.radiusMean / s;
            ground = sphere;
            hasRelief = false;
        }

        // Obstacles: buildings, trees, tanks, rocks
        var obs = Resources.Load<Mesh>($"Planets/MapCol/{MapName}/OBS_{info.child}");
        if (obs != null)
        {
            var holder = new GameObject("Obstacles");
            holder.transform.SetParent(meshParent, false);
            holder.AddComponent<MeshCollider>().sharedMesh = obs;
        }
        else Debug.LogWarning($"[GalaxyLoader] No obstacle mesh for {info.child}.");

        var gravity = planet.gameObject.AddComponent<PlanetGravity>();
        gravity.planetName = Display(info.child);
        gravity.radius = radius;
        Debug.Log($"[GalaxyLoader] planet {info.child}: centre {planet.position} radius {radius:F1}");
        gravity.gravityStrength = 12f;
        gravity.influenceMultiplier = 2.5f;    // spec: the field reaches 2-2.5 planet radii; open space in between is the jetpack "cruise"
        gravity.hasAtmosphere = false;
        gravity.biome = Biome(info.child);
        gravity.groundCollider = ground;
        gravity.useMathSurface = !hasRelief;

        planet.gameObject.AddComponent<WeaponSpawner>();
        planet.gameObject.AddComponent<GrenadeSpawner>();
    }

    // ── Sun ───────────────────────────────────────────────────────────────

    private static void ApplySun(Layout layout)
    {
        if (layout == null || !layout.hasSun) return;
        Light sun = RenderSettings.sun;
        if (sun == null)
            foreach (var l in Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
                if (l.type == LightType.Directional) { sun = l; break; }
        if (sun == null) return;
        sun.transform.rotation = Quaternion.Euler(layout.sunEuler);
    }

    // ── Albedo maps ───────────────────────────────────────────────────────

    private static readonly Dictionary<string, Texture2D> TextureCache = new Dictionary<string, Texture2D>();

    private static void ApplyTextures(Transform planet)
    {
        foreach (var r in planet.GetComponentsInChildren<Renderer>(true))
        {
            var mats = r.sharedMaterials;
            bool changed = false;
            for (int i = 0; i < mats.Length; i++)
            {
                var m = mats[i];
                if (m == null || !m.HasProperty("_BaseMap") || m.GetTexture("_BaseMap") != null) continue;

                var tex = FindTexture(m.name);
                if (tex != null)
                {
                    m = new Material(m);              // instance: don't touch the shared imported material
                    m.SetTexture("_BaseMap", tex);
                    m.SetColor("_BaseColor", Color.white);
                    mats[i] = m; changed = true;
                }
                else if (m.name.StartsWith("Suelo_Desierto"))    // the desert ground texture wasn't supplied: warm sand
                {
                    m = new Material(m);
                    m.SetColor("_BaseColor", new Color(0.84f, 0.68f, 0.42f));
                    m.SetFloat("_Smoothness", 0.1f);
                    mats[i] = m; changed = true;
                }
            }
            if (changed) r.sharedMaterials = mats;
        }
    }

    private static Texture2D FindTexture(string materialName)
    {
        if (TextureCache.TryGetValue(materialName, out var cached)) return cached;

        string n = materialName;
        var candidates = new List<string> { n };
        if (n.EndsWith(".001")) candidates.Add(n.Substring(0, n.Length - 4));
        if (n.EndsWith("_Mat")) candidates.Add(n.Substring(0, n.Length - 4));
        if (n.EndsWith("_Mat.001")) candidates.Add(n.Substring(0, n.Length - 8));
        if (n == "tre__fin_.001") candidates.Add("tree_fin");

        Texture2D found = null;
        foreach (var c in candidates)
        {
            found = Resources.Load<Texture2D>("Planets/GalaxyTextures/" + c);
            if (found != null) break;
        }
        TextureCache[materialName] = found;
        return found;
    }
}
}
