using System.Collections.Generic;
using UnityEngine;

namespace OrbitRush
{

/// <summary>
/// Builds the two AI enemy templates and the spawn points, then hands them to GameManager.
/// No manual prefab creation or Inspector wiring needed.
///
///  • Elite Soldier (ground): Resources/Models/OrbitRush_Soldier — patrols, chases and shoots
///    with its energy rifle.
///  • Guardian Drone (flying): Resources/Models/OrbitRush_Drone — hovers above the surface
///    behind an energy shield, circles its target and fires rapid, weak bolts.
///
/// Both are modelled in Blender per the Orbit Rush enemy sheet. If a model is missing, a
/// simple placeholder is used so the enemies still work.
///
/// Attach alongside GameManager (e.g. on _GameSystems). Runs in the Editor too
/// (ExecuteAlways) so the spawn points are visible without entering Play mode.
/// Right-click → "Regenerate Enemy Setup" to re-roll.
/// </summary>
[ExecuteAlways]
[RequireComponent(typeof(GameManager))]
public class EnemySpawnSetup : MonoBehaviour
{
    [Header("Elite Soldier (ground)")]
    public float enemyHealth = 80f;
    public float moveSpeed = 4.5f;
    public float detectionRadius = 25f;
    public float attackRange = 18f;
    public float fireInterval = 1.2f;
    public float projectileDamage = 15f;

    [Header("Guardian Drone (flying)")]
    public float droneHealth = 50f;
    public float droneShield = 40f;
    public float droneSpeed = 7f;
    public float droneHoverHeight = 5f;
    public float droneDetectionRadius = 32f;
    public float droneAttackRange = 26f;
    public float droneFireInterval = 0.45f;
    public float droneProjectileDamage = 6f;

    [Header("Zombie (melee horde)")]
    public float zombieHealth = 60f;
    public float zombieSpeed = 6.5f;

    [Header("Spawn Points")]
    public int spawnPointsPerPlanet = 4;
    public int seed = 4242;

    void OnEnable() => Regenerate();

    [ContextMenu("Regenerate Enemy Setup")]
    public void Regenerate()
    {
        var gm = GetComponent<GameManager>();
        if (gm == null) return;

        gm.enemyPrefab = BuildSoldierTemplate();
        gm.droneEnemyPrefab = BuildDroneTemplate();
        gm.zombieEnemyPrefab = BuildZombieTemplate();
        gm.enemySpawnPoints = BuildSpawnPoints();
    }

    // ── Elite Soldier ────────────────────────────────────────────────────

    private GameObject BuildSoldierTemplate()
    {
        var enemy = NewTemplateRoot("EnemyTemplate");
        enemy.transform.localScale = Vector3.one * CharacterScale.Enemy;

        // Physics — gravity is applied by EnemyController itself via GravitySystem.
        // Capsule is 2 m tall centred on the pivot, so feet are at local y = -1.
        var rb = enemy.AddComponent<Rigidbody>();
        rb.useGravity = false;
        var col = enemy.AddComponent<CapsuleCollider>();
        col.height = 2f;
        col.radius = 0.5f;

        // Visual: soldier model → old robot model → red capsule.
        var model = LoadModel("Models/OrbitRush_SoldierRig") ?? LoadModel("Models/OrbitRush_Soldier") ?? LoadModel("Models/OrbitRush_Enemy");
        if (model != null)
        {
            var visual = Instantiate(model, enemy.transform);
            visual.name = "Visual";
            visual.transform.localPosition = new Vector3(0f, -1f, 0f);
            visual.transform.localRotation = Quaternion.identity;
            StripColliders(visual);
        }
        else
        {
            AddPlaceholderCapsule(enemy.transform);
        }

        // Muzzle: just past the rifle tip (rifle is centred, ~1.25 m up the model → 0.25 above the pivot).
        var muzzle = NewChild(enemy.transform, "Muzzle", new Vector3(0f, 0.25f, 1.25f));

        var stats = enemy.AddComponent<EnemyStats>();
        stats.maxHealth = enemyHealth;

        var controller = enemy.AddComponent<EnemyController>();
        controller.moveSpeed = moveSpeed;

        var ai = enemy.AddComponent<EnemyAI>();
        ai.kind = EnemyKind.Robot;
        ConfigureAI(ai, muzzle, detectionRadius, attackRange, fireInterval, projectileDamage,
            new Color(1f, 0.3f, 0.1f), 70f);

        enemy.AddComponent<EnemyAnimDriver>();    // plays the Mixamo clips if the model is rigged (no-op otherwise)

        FinishTemplate(enemy, isDrone: false);
        return enemy;
    }

    // ── Guardian Drone ───────────────────────────────────────────────────

    private GameObject BuildDroneTemplate()
    {
        var enemy = NewTemplateRoot("DroneTemplate");
        enemy.transform.localScale = Vector3.one * CharacterScale.Enemy;

        // The drone's pivot is the centre of its body: a sphere collider covers the body, the legs hang below.
        var rb = enemy.AddComponent<Rigidbody>();
        rb.useGravity = false;
        rb.linearDamping = 0.5f;
        var col = enemy.AddComponent<SphereCollider>();
        col.radius = 0.8f;

        var model = LoadModel("Models/OrbitRush_DroneDL3") ?? LoadModel("Models/OrbitRush_Drone");
        if (model != null)
        {
            var visual = Instantiate(model, enemy.transform);
            visual.name = "Visual";
            visual.transform.localPosition = Vector3.zero;
            visual.transform.localRotation = Quaternion.identity;
            StripColliders(visual);
        }
        else
        {
            var visual = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            visual.name = "Visual";
            visual.transform.SetParent(enemy.transform, false);
            visual.transform.localScale = Vector3.one * 1.4f;
            DestroyImmediateOrRuntime(visual.GetComponent<Collider>());
            var mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            mat.color = new Color(1f, 0.75f, 0.05f);
            visual.GetComponent<MeshRenderer>().sharedMaterial = mat;
        }

        // Muzzle at the grin on the front of the body.
        var muzzle = NewChild(enemy.transform, "Muzzle", new Vector3(0f, -0.05f, 1.0f));

        var stats = enemy.AddComponent<EnemyStats>();
        stats.maxHealth = droneHealth;
        stats.maxShield = droneShield;

        var controller = enemy.AddComponent<EnemyController>();
        controller.moveSpeed = droneSpeed;
        controller.flying = true;
        controller.hoverHeight = droneHoverHeight;

        var ai = enemy.AddComponent<EnemyAI>();
        ConfigureAI(ai, muzzle, droneDetectionRadius, droneAttackRange, droneFireInterval, droneProjectileDamage,
            new Color(1f, 0.1f, 0.6f), 55f);
        ai.kind = EnemyKind.Drone;
        ai.strafeWhileAttacking = true;
        ai.preferredDistance = 14f;
        // Slow EMP orb: if it hits, the player's jetpack is jammed for a few seconds
        ai.empPrefab = ProjectileFactory.Create("EMPOrb", new Color(0.55f, 0.3f, 1f), 0.5f,
            damage: 4f, speed: 15f, lifetime: 5f, affectedByGravity: false, gravityMultiplier: 1f, splashRadius: 0f);
        ai.empPrefab.transform.SetParent(ai.transform, false);

        enemy.AddComponent<EnemyShield>();
        enemy.AddComponent<DroneDamageStages>();
        FinishTemplate(enemy, isDrone: true, healthBarHeight: 1.7f);
        return enemy;
    }

    // ── Zombie ───────────────────────────────────────────────────────────

    private GameObject BuildZombieTemplate()
    {
        var enemy = NewTemplateRoot("ZombieTemplate");
        enemy.transform.localScale = Vector3.one * CharacterScale.Enemy;

        var rb = enemy.AddComponent<Rigidbody>();
        rb.useGravity = false;
        var col = enemy.AddComponent<CapsuleCollider>();
        col.height = 2f;
        col.radius = 0.5f;

        var model = LoadModel("Models/OrbitRush_SoldierRig") ?? LoadModel("Models/OrbitRush_Soldier");
        if (model != null)
        {
            var visual = Instantiate(model, enemy.transform);
            visual.name = "Visual";
            visual.transform.localPosition = new Vector3(0f, -1f, 0f);
            visual.transform.localRotation = Quaternion.identity;
            StripColliders(visual);
            TintZombie(visual);
        }
        else AddPlaceholderCapsule(enemy.transform);

        var muzzle = NewChild(enemy.transform, "Muzzle", new Vector3(0f, 0.4f, 0.8f));

        var stats = enemy.AddComponent<EnemyStats>();
        stats.maxHealth = zombieHealth;

        var controller = enemy.AddComponent<EnemyController>();
        controller.moveSpeed = zombieSpeed;
        controller.aiSpeedFactor = 0.2f;

        var ai = enemy.AddComponent<EnemyAI>();
        ai.kind = EnemyKind.Zombie;
        ai.muzzle = muzzle;
        ai.playerMask = 1;
        ai.obstructionMask = 0;

        enemy.AddComponent<EnemyAnimDriver>();
        FinishTemplate(enemy, isDrone: false);
        return enemy;
    }

    /// <summary>Sickly green skin, dimmed.</summary>
    private static void TintZombie(GameObject visual)
    {
        foreach (var r in visual.GetComponentsInChildren<Renderer>(true))
        {
            var mats = r.materials;
            foreach (var m in mats)
            {
                if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", new Color(0.45f, 0.75f, 0.35f));
                else if (m.HasProperty("_Color")) m.SetColor("_Color", new Color(0.45f, 0.75f, 0.35f));
            }
            r.materials = mats;
        }
    }

    // ── Shared template helpers ──────────────────────────────────────────

    private GameObject NewTemplateRoot(string name)
    {
        var existing = transform.Find(name);
        if (existing != null) DestroyImmediateOrRuntime(existing.gameObject);

        var enemy = new GameObject(name);
        enemy.transform.SetParent(transform, false);
        return enemy;
    }

    private static Transform NewChild(Transform parent, string name, Vector3 localPos)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;
        return go.transform;
    }

    private static GameObject LoadModel(string resourcePath) => Resources.Load<GameObject>(resourcePath);

    private static void StripColliders(GameObject model)
    {
        foreach (var c in model.GetComponentsInChildren<Collider>(true)) DestroyImmediateOrRuntime(c);
    }

    private static void AddPlaceholderCapsule(Transform parent)
    {
        var visual = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        visual.name = "Visual";
        visual.transform.SetParent(parent, false);
        visual.transform.localScale = new Vector3(0.8f, 1f, 0.8f);
        DestroyImmediateOrRuntime(visual.GetComponent<Collider>()); // the enemy's own CapsuleCollider handles physics

        var mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        mat.color = new Color(0.85f, 0.15f, 0.15f); // hostile red — distinct from every weapon color
        visual.GetComponent<MeshRenderer>().sharedMaterial = mat;
    }

    private static void ConfigureAI(EnemyAI ai, Transform muzzle, float detection, float range, float interval,
                                    float damage, Color boltColor, float boltSpeed)
    {
        ai.detectionRadius = detection;
        ai.loseSightRadius = detection * 1.4f;
        ai.attackRange = range;
        ai.fireInterval = interval;
        ai.projectileDamage = damage;
        ai.muzzle = muzzle;
        // Everything in this project sits on the Default layer (no custom layers defined yet) —
        // Default is enough for OverlapSphere to find players; ScanForTargets already filters
        // non-players out. obstructionMask stays 0 (matches nothing): pointing it at Default would make
        // the raycast see the enemy's own collider "in the way" and never get a clear line of sight.
        ai.playerMask = 1;
        ai.obstructionMask = 0;

        ai.projectilePrefab = ProjectileFactory.Create("EnemyBolt", boltColor, 0.2f,
            damage: damage, speed: boltSpeed, lifetime: 3f,
            affectedByGravity: false, gravityMultiplier: 1f, splashRadius: 0f);
        ai.projectilePrefab.transform.SetParent(ai.transform, false);
    }

    private static void FinishTemplate(GameObject enemy, bool isDrone, float healthBarHeight = 1.6f)
    {
        // Added after EnemyStats exists (EnemyHealthBar requires it — adding earlier would create a duplicate).
        var home = enemy.AddComponent<EnemyHome>();
        home.isDrone = isDrone;
        var bar = enemy.AddComponent<EnemyHealthBar>();
        bar.height = healthBarHeight;   // measured from the pivot: soldier pivot is mid-body, drone pivot is its centre

        enemy.SetActive(false); // template stays dormant; GameManager activates each clone
    }

    // ── Spawn points ─────────────────────────────────────────────────────

    private Transform[] BuildSpawnPoints()
    {
        var existing = transform.Find("EnemySpawnPoints");
        if (existing != null) DestroyImmediateOrRuntime(existing.gameObject);

        var container = new GameObject("EnemySpawnPoints");
        container.transform.SetParent(transform, false);

        var planets = FindObjectsByType<PlanetGravity>(FindObjectsSortMode.None);
        var rng = new System.Random(seed);
        var points = new List<Transform>();

        foreach (var planet in planets)
        {
            for (int i = 0; i < spawnPointsPerPlanet; i++)
            {
                Vector3 dir = SphereScatter.RandomDirection(rng);
                // City planets have buildings/trees — re-roll so enemies don't spawn inside one.
                for (int attempt = 0; attempt < 30 && !SpawnSafety.IsClear(planet, dir, 1f); attempt++)
                    dir = SphereScatter.RandomDirection(rng);
                Vector3 pos = planet.GetSurfacePoint(dir) + dir * 1f;

                var point = new GameObject($"SpawnPoint_{planet.planetName}_{i}");
                point.transform.SetParent(container.transform, false);
                point.transform.position = pos;
                point.transform.rotation = Quaternion.FromToRotation(Vector3.up, dir);

                points.Add(point.transform);
            }
        }

        return points.ToArray();
    }

    private static void DestroyImmediateOrRuntime(Object obj)
    {
        if (obj == null) return;
        if (Application.isPlaying) Object.Destroy(obj);
        else Object.DestroyImmediate(obj);
    }
}
}
