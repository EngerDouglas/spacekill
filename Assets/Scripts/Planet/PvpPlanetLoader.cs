using UnityEngine;
using UnityEngine.SceneManagement;

namespace OrbitRush
{

/// <summary>
/// Adds the three city planets built in Blender (planetasPVEVP.blend) to the game:
/// Downtown, Industrial and Gardens. Models live in Assets/Resources/Planets/.
///
/// Runs automatically on every scene load that contains a GameManager — no scene edits
/// needed. Each planet gets: the visual model, a SphereCollider for the ground, a
/// MeshCollider built from the model's "COL_*" mesh (buildings, towers, trees, cover),
/// a PlanetGravity, and weapon/grenade spawners. Spawn setups are then regenerated so
/// players, enemies and pickups also use the new planets.
///
/// To remove the planets, delete this file (or set <see cref="Enabled"/> to false).
/// </summary>
public static class PvpPlanetLoader
{
    public static bool Enabled = true;

    private const string RootName = "OrbitRush_CityPlanets";

    private struct PlanetDef
    {
        public string key;          // matches Resources/Planets/Planet_<key>
        public string displayName;
        public Vector3 position;
        public float radius;        // radius of the ground sphere in the model
        public PlanetGravity.BiomeType biome;
    }

    // Existing planets sit at z = -42.6 and z = 249.6 (radius 20); these go between and beside them.
    private static readonly PlanetDef[] Planets =
    {
        new PlanetDef { key = "Downtown",   displayName = "Downtown",   position = new Vector3(   0f, 0f, 110f), radius = 40f, biome = PlanetGravity.BiomeType.Alien },
        new PlanetDef { key = "Industrial", displayName = "Industrial", position = new Vector3(-135f, 5f,  30f), radius = 26f, biome = PlanetGravity.BiomeType.Volcanic },
        new PlanetDef { key = "Gardens",    displayName = "Gardens",    position = new Vector3( 135f, 10f, 70f), radius = 24f, biome = PlanetGravity.BiomeType.Rocky },
    };

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Register()
    {
        // sceneLoaded fires after Awake/OnEnable and before the first Start, which is
        // exactly when GameManager/GravitySystem exist but nothing has begun the match.
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (!Enabled || GalaxyLoader.Available) return;     // the full galaxy replaces these planets
        if (Object.FindFirstObjectByType<GameManager>() == null) return;
        if (GameObject.Find(RootName) != null) return;

        var root = new GameObject(RootName);
        int built = 0;
        foreach (var def in Planets)
            if (Build(def, root.transform)) built++;

        if (built == 0) { Object.Destroy(root); return; }

        // New colliders must be visible to the physics queries the spawn setups make.
        Physics.SyncTransforms();

        // Spawn setups ran in OnEnable before these planets existed — redo them.
        foreach (var s in Object.FindObjectsByType<PlayerSpawnSetup>(FindObjectsSortMode.None)) s.Regenerate();
        foreach (var s in Object.FindObjectsByType<EnemySpawnSetup>(FindObjectsSortMode.None)) s.Regenerate();

        Debug.Log($"[PvpPlanetLoader] Added {built} city planet(s).");
    }

    private static bool Build(PlanetDef def, Transform parent)
    {
        var prefab = Resources.Load<GameObject>($"Planets/Planet_{def.key}");
        if (prefab == null)
        {
            Debug.LogWarning($"[PvpPlanetLoader] Resources/Planets/Planet_{def.key} not found — skipping.");
            return false;
        }

        var planet = new GameObject($"Planet_{def.key}");
        planet.transform.SetParent(parent, false);
        planet.transform.position = def.position;

        var model = Object.Instantiate(prefab, planet.transform);
        model.name = "Model";
        model.transform.localPosition = Vector3.zero;
        model.transform.localRotation = Quaternion.identity;
        model.transform.localScale = Vector3.one;

        // Buildings/trees/cover collision: use the model's COL_ mesh, hidden from rendering.
        foreach (var mf in model.GetComponentsInChildren<MeshFilter>(true))
        {
            if (!mf.name.StartsWith("COL_")) continue;

            if (mf.sharedMesh != null)
            {
                var meshCol = mf.gameObject.AddComponent<MeshCollider>();
                meshCol.sharedMesh = mf.sharedMesh;
            }
            var r = mf.GetComponent<Renderer>();
            if (r != null) r.enabled = false;
        }

        // Ground = perfect sphere (matches how PlanetGravity and the player's ground check think).
        var sphere = planet.AddComponent<SphereCollider>();
        sphere.radius = def.radius;

        var gravity = planet.AddComponent<PlanetGravity>();
        gravity.planetName = def.displayName;
        gravity.radius = def.radius;
        gravity.gravityStrength = 12f;       // same as the original planets
        gravity.influenceMultiplier = 4f;    // tighter than default so the big ones don't swallow the others
        gravity.hasAtmosphere = false;
        gravity.biome = def.biome;

        // Same pickups the original planets have.
        planet.AddComponent<WeaponSpawner>();
        planet.AddComponent<GrenadeSpawner>();
        return true;
    }
}
}
