using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace OrbitRush
{

/// <summary>
/// Loads the main map: one model per planet (Resources/Planets/Planeta_Bosque, Planeta_Ciudad_Destruida, Planeta_Desierto) plus
/// the Sun (Resources/Planets/Sol), each authored in Blender and kept as its own file so a planet can be reworked on its own. Every
/// planet model keeps its place in the galaxy (its pivot is the planet's centre). It replaces the scene's old planets and the
/// PvpPlanetLoader city planets.
///
/// Runs on every scene load that contains a GameManager (before the first Start), no scene edits needed.
/// Each planet gets: ground collision, a MeshCollider with its buildings / trees / rocks, a PlanetGravity and
/// weapon / grenade pickups. Collision meshes and per-planet radii come from Resources/Planets/MapCol/&lt;map&gt; and
/// &lt;map&gt;_layout.json (built by GalaxySetup, one run per planet file); albedo maps are matched to materials by name
/// (Resources/Planets/GalaxyTextures). The Sun is render-only; <see cref="SunLight"/> aims the scene's directional light from
/// the Sun toward the player, so every planet gets a day and a night.
///
/// To go back to a previous map point <see cref="MapNames"/> at its models, or set <see cref="Enabled"/> to false.
/// </summary>
public static class GalaxyLoader
{
    public static bool Enabled = true;

    /// <summary>The planet models to load, one file each. A missing file is skipped with a warning.</summary>
    private static readonly string[] MapNames = { "Planeta_Bosque", "Planeta_Ciudad_Destruida", "Planeta_Desierto" };
    private const string SunModel = "Sol";
    private const string RootName = "OrbitRush_Galaxy";
    private const float FarClip = 12000f;     // the planets are ~1900-3300 m apart and the Sun is 448 m wide
    private static bool? _available;

    /// <summary>True when at least one planet model exists in Resources (and the loader is enabled).</summary>
    public static bool Available
    {
        get
        {
            if (!Enabled) return false;
            if (_available == null)
            {
                _available = false;
                foreach (var m in MapNames)
                    if (Resources.Load<GameObject>("Planets/" + m) != null) { _available = true; break; }
            }
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
            case "Ciudad_Destruida": return "Ciudad Destruida";
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
        if (child.Contains("Bosque")) return PlanetGravity.BiomeType.Forest;
        if (child.Contains("Ciudad")) return PlanetGravity.BiomeType.Ruins;
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

        var root = new GameObject(RootName).transform;

        // Instantiate every planet model first, so the old planets can be retired before the new ones get a PlanetGravity
        var models = new List<(string map, Transform model)>();
        foreach (var map in MapNames)
        {
            var prefab = Resources.Load<GameObject>("Planets/" + map);
            if (prefab == null) { Debug.LogWarning($"[GalaxyLoader] Model 'Planets/{map}' not found: skipped."); continue; }
            var inst = Object.Instantiate(prefab, root);
            inst.name = prefab.name;      // a one-planet FBX imports with the planet itself as the model's root, so keep its name
            models.Add((map, inst.transform));
        }

        // Retire the old planets (the scene's two and any city planets from PvpPlanetLoader)
        foreach (var old in Object.FindObjectsByType<PlanetGravity>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (old.transform.IsChildOf(root)) continue;
            old.gameObject.SetActive(false);
        }

        int built = 0;
        foreach (var (map, model) in models)
        {
            var layoutAsset = Resources.Load<TextAsset>("Planets/" + map + "_layout");
            var layout = layoutAsset != null ? JsonUtility.FromJson<Layout>(layoutAsset.text) : null;
            if (layout == null || layout.planets == null)
            {
                Debug.LogWarning($"[GalaxyLoader] '{map}_layout.json' missing: run GalaxySetup.BuildCollision for this planet.");
                continue;
            }
            foreach (var info in layout.planets)
            {
                // Unity merges the model's single top-level object into the model root and names it after the file
                // (Planeta_Bosque, not Bosque): the planet is the root itself when it holds the "<child>_Malla" mesh directly.
                var planet = model.Find(info.child);
                if (planet == null && model.Find(info.child + "_Malla") != null) planet = model;
                if (planet == null) { Debug.LogWarning($"[GalaxyLoader] '{info.child}' not found in {map}."); continue; }
                Build(map, info, planet);
                built++;
            }
        }

        InstallSun(root);
        RaiseFarClip();

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
        Debug.Log($"[GalaxyLoader] Map loaded: {built} planet(s) from {models.Count} model(s).");
    }

    private static void Build(string map, PlanetInfo info, Transform planet)
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
            var gm = Resources.Load<Mesh>($"Planets/MapCol/{map}/GND_{info.child}");
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
        var obs = Resources.Load<Mesh>($"Planets/MapCol/{map}/OBS_{info.child}");
        if (obs != null)
        {
            var holder = new GameObject("Obstacles");
            holder.transform.SetParent(meshParent, false);
            holder.AddComponent<MeshCollider>().sharedMesh = obs;
        }
        else Debug.LogWarning($"[GalaxyLoader] No obstacle mesh for {info.child} ({map}).");

        var gravity = planet.gameObject.AddComponent<PlanetGravity>();
        gravity.planetName = Display(info.child);
        gravity.radius = radius;
        Debug.Log($"[GalaxyLoader] planet {info.child}: centre {planet.position} radius {radius:F1}");
        gravity.gravityStrength = 12f;
        gravity.influenceMultiplier = 2.5f;    // spec: the field reaches 2-2.5 planet radii
        gravity.hasAtmosphere = false;
        gravity.biome = Biome(info.child);
        gravity.groundCollider = ground;
        gravity.useMathSurface = !hasRelief;

        PlanetZonesLoader.Load(map, planet, gravity);        // routes and places, when this map has them: the spawners below use them
        planet.gameObject.AddComponent<WeaponSpawner>();
        planet.gameObject.AddComponent<GrenadeSpawner>();
        planet.gameObject.AddComponent<CoinSpawner>();
        planet.gameObject.AddComponent<VendingMachineSpawner>();
        LandmarkProps.Build(gravity);                          // props of each place (campfire, tents, cabin...)
    }

    // ── Sun ───────────────────────────────────────────────────────────────

    /// <summary>The Sun: a render-only model at the galaxy's centre (no collider, no gravity, no shadows) and the light that follows the player.</summary>
    private static void InstallSun(Transform root)
    {
        var prefab = Resources.Load<GameObject>("Planets/" + SunModel);
        if (prefab == null) return;

        var sun = Object.Instantiate(prefab, root);
        sun.name = SunModel;
        foreach (var c in sun.GetComponentsInChildren<Collider>(true)) Object.Destroy(c);
        Vector3 centre = sun.transform.position;
        bool first = true;
        foreach (var r in sun.GetComponentsInChildren<Renderer>(true))
        {
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            if (first) { centre = r.bounds.center; first = false; }
        }
        sun.AddComponent<SunLight>().sunPosition = centre;
    }

    /// <summary>The galaxy is thousands of metres across: the scene's camera (far clip 1000) would cut off the Sun and the other planets.</summary>
    private static void RaiseFarClip()
    {
        foreach (var cam in Camera.allCameras)
            if (cam.farClipPlane < FarClip) cam.farClipPlane = FarClip;
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

                // The ground gets the triplanar detail shader (a whole-planet texture is a blur at this size)
                if (m != null && IsGround(m.name))
                {
                    var ground = MakeGroundMaterial(m);
                    if (ground != null) { mats[i] = ground; changed = true; continue; }
                }

                if (m == null || !m.HasProperty("_BaseMap") || m.GetTexture("_BaseMap") != null) continue;

                var tex = FindTexture(m.name);
                if (tex != null)
                {
                    m = new Material(m);              // instance: don't touch the shared imported material
                    m.SetTexture("_BaseMap", tex);
                    m.SetColor("_BaseColor", Color.white);
                    mats[i] = m; changed = true;
                }
                else if (m.name.StartsWith("CD_Suelo"))           // the city ground texture wasn't supplied: dark, worn asphalt grey
                {
                    m = new Material(m);
                    m.SetColor("_BaseColor", new Color(0.2f, 0.19f, 0.18f));
                    m.SetFloat("_Smoothness", 0.1f);
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

    private static bool IsGround(string materialName) => materialName.StartsWith("Suelo_") || materialName.StartsWith("CD_Suelo");

    /// <summary>
    /// A material using OrbitRush/PlanetGround for a planet's ground: the planet's own large-scale colour (its texture if it ships one,
    /// otherwise a tint) with a tiling detail texture on top, projected from three axes in metres.
    /// Returns null (so the old flat-colour fallback runs) if the shader isn't available.
    /// </summary>
    private static Material MakeGroundMaterial(Material source)
    {
        var shader = Shader.Find("OrbitRush/PlanetGround");
        if (shader == null) { Debug.LogWarning("[GalaxyLoader] Shader 'OrbitRush/PlanetGround' not found: flat ground colour used."); return null; }

        string n = source.name, detailName;
        Color tint; float smoothness, tileA, tileB;
        if (n.StartsWith("CD_Suelo"))               { detailName = "Ground_Asphalt"; tint = new Color(0.20f, 0.19f, 0.18f); smoothness = 0.12f; tileA = 4f; tileB = 30f; }
        else if (n.StartsWith("Suelo_Desierto"))    { detailName = "Ground_Desert";  tint = new Color(0.84f, 0.68f, 0.42f); smoothness = 0.08f; tileA = 7f; tileB = 55f; }
        else if (n.StartsWith("Suelo_Camino"))      { detailName = "Ground_Forest";  tint = new Color(0.36f, 0.24f, 0.14f); smoothness = 0.05f; tileA = 3f; tileB = 18f; }   // trodden dirt path
        else                                         { detailName = "Ground_Forest";  tint = Color.white;                     smoothness = 0.15f; tileA = 6f; tileB = 41f; }

        var m = new Material(shader) { name = n + " (ground)" };
        var baseTex = FindTexture(n);                       // the Bosque ships a colour map for its ground; the others use the tint
        if (baseTex != null) { m.SetTexture("_BaseMap", baseTex); m.SetColor("_BaseColor", Color.white); }
        else m.SetColor("_BaseColor", tint);

        var detail = Resources.Load<Texture2D>("Planets/GalaxyTextures/" + detailName);
        if (detail != null)
        {
            detail.anisoLevel = 8;                          // the ground is seen at grazing angles most of the time
            m.SetTexture("_DetailMap", detail);
        }
        else Debug.LogWarning($"[GalaxyLoader] Detail texture '{detailName}' missing.");
        m.SetFloat("_DetailTileA", tileA);
        m.SetFloat("_DetailTileB", tileB);
        m.SetFloat("_Smoothness", smoothness);
        return m;
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
