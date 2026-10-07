using UnityEngine;

namespace OrbitRush
{

/// <summary>
/// Scatters one of each weapon type across a planet's surface as pickups —
/// fully procedural, no prefab assets needed. Builds each weapon GameObject
/// from scratch (the matching WeaponBase subclass, on a disabled child used
/// as WeaponPickup's template) plus a simple placeholder mesh so it's visible
/// lying on the ground.
///
/// Attach to a planet GameObject alongside PlanetGravity. Runs in the Editor
/// too (ExecuteAlways) so you can see weapons on the ground without entering
/// Play mode. Right-click the component → "Regenerate Weapons" to re-roll.
///
/// Each weapon also gets a matching procedural projectile (via
/// ProjectileFactory) wired to its projectilePrefab, so picking one up and
/// firing actually spawns and does damage — no separate projectile prefab
/// setup needed.
///
/// The held weapon (not just the ground pickup) gets a visual too, so it's
/// not invisible once equipped. Drop real models into weaponVisualOverrides
/// (e.g. from BlenderScripts/generate_weapons.py) to replace the placeholder
/// cube — no other code changes needed, it's picked up automatically.
/// </summary>
[ExecuteAlways]
[RequireComponent(typeof(PlanetGravity))]
public class WeaponSpawner : MonoBehaviour
{
    public enum WeaponKind { Blaster, SpaceShotgun, PlasmaRifle, GravitySniper, OrbitalLauncher }

    static readonly Color[] KindColors =
    {
        new(0.90f, 0.75f, 0.10f), // Blaster - yellow
        new(0.90f, 0.25f, 0.15f), // SpaceShotgun - red
        new(0.20f, 0.85f, 0.95f), // PlasmaRifle - cyan
        new(0.60f, 0.30f, 0.95f), // GravitySniper - purple
        new(0.30f, 0.90f, 0.35f), // OrbitalLauncher - green
    };

    [Header("Loadout")]
    public WeaponKind[] weaponsToScatter =
    {
        WeaponKind.Blaster, WeaponKind.SpaceShotgun, WeaponKind.PlasmaRifle,
        WeaponKind.GravitySniper, WeaponKind.OrbitalLauncher,
    };

    [Header("Placement")]
    public int seed = 777;
    public float pickupTriggerRadius = 1.2f;
    [Tooltip("How far above the surface the pickup sits, so it doesn't clip into terrain.")]
    public float hoverHeight = 0.4f;

    [Header("Visuals (optional)")]
    [Tooltip("Optional — drop in real weapon models (e.g. imported from BlenderScripts/generate_weapons.py) to " +
             "replace the placeholder cube for that weapon kind, both on the ground pickup and in the player's " +
             "hand. Index matches WeaponKind order: Blaster, SpaceShotgun, PlasmaRifle, GravitySniper, OrbitalLauncher. " +
             "Leave entries empty to keep the placeholder for that weapon. If the model has a child named " +
             "\"Muzzle\", that's used as the fire point automatically.")]
    public GameObject[] weaponVisualOverrides = new GameObject[5];

    private PlanetGravity _planet;

    void OnEnable() => Regenerate();

    [ContextMenu("Regenerate Weapons")]
    public void Regenerate()
    {
        _planet = GetComponent<PlanetGravity>();
        if (_planet == null) return;

        var existing = transform.Find("WeaponPickups");
        if (existing != null) DestroyImmediateOrRuntime(existing.gameObject);

        var container = new GameObject("WeaponPickups");
        container.transform.SetParent(transform, false);
        // The planet's own transform is scaled up (radius baked into a x40-ish
        // scale) — without this, everything parented under it inherits that
        // scale too, so a "0.18" visual ends up ~40x too big. Counteract it so
        // sizes below are real world units again.
        SphereScatter.CancelParentScale(container.transform);

        var rng = new System.Random(seed);
        foreach (var kind in weaponsToScatter)
            SpawnPickup(kind, container.transform, rng);
    }

    private void SpawnPickup(WeaponKind kind, Transform parent, System.Random rng)
    {
        Vector3 dir = SphereScatter.RandomClearDirection(rng, _planet, hoverHeight);
        Vector3 surfacePos = _planet.GetSurfacePoint(dir) + dir * hoverHeight;
        Quaternion rot = Quaternion.FromToRotation(Vector3.up, dir);

        // ── Pickup root: trigger collider + WeaponPickup ──
        var pickupGO = new GameObject($"Pickup_{kind}");
        pickupGO.transform.SetParent(parent, false);
        pickupGO.transform.position = surfacePos;
        pickupGO.transform.rotation = rot;

        var col = pickupGO.AddComponent<SphereCollider>();
        col.isTrigger = true;
        col.radius = pickupTriggerRadius;

        // ── Ground-pickup visual — real model if one's been assigned, placeholder cube otherwise ──
        BuildVisual(kind, pickupGO.transform, out _);

        // ── Disabled template: the actual weapon, cloned into the player's
        //    inventory on pickup (WeaponInventory.AddWeapon does the Instantiate).
        //    Gets its own visual too, so it isn't invisible once equipped. ──
        var template = new GameObject($"{kind}Template");
        template.transform.SetParent(pickupGO.transform, false);
        AddWeaponComponent(template, kind);

        BuildVisual(kind, template.transform, out Transform modelMuzzle);

        var weaponBase = template.GetComponent<WeaponBase>();
        weaponBase.muzzle = modelMuzzle != null ? modelMuzzle : CreateFallbackMuzzle(template.transform);
        weaponBase.projectilePrefab = BuildProjectileTemplate(kind, template.transform);

        template.SetActive(false);

        var pickup = pickupGO.AddComponent<WeaponPickup>();
        pickup.weaponPrefab = template;
        pickup.displayName = kind.ToString();
    }

    /// <summary>
    /// Builds the weapon's visual under <paramref name="parent"/> — a real model from
    /// weaponVisualOverrides if one's assigned for this kind, otherwise a placeholder cube.
    /// Outputs the model's own "Muzzle" child transform if it has one (null otherwise —
    /// callers fall back to CreateFallbackMuzzle).
    /// </summary>
    private void BuildVisual(WeaponKind kind, Transform parent, out Transform muzzle)
    {
        var overridePrefab = weaponVisualOverrides != null && (int)kind < weaponVisualOverrides.Length
            ? weaponVisualOverrides[(int)kind]
            : null;

        if (overridePrefab != null)
        {
            var instance = Instantiate(overridePrefab, parent);
            instance.name = "Visual";
            instance.transform.localPosition = Vector3.zero;
            instance.transform.localRotation = Quaternion.identity;

            muzzle = instance.transform.Find("Muzzle");
            return;
        }

        // Blender-made model from Resources/Weapons/<Kind>.fbx, if present (muzzle is measured from the mesh).
        var modelMuzzle = WeaponModels.AttachHeldModel(kind.ToString(), parent);
        if (modelMuzzle != null)
        {
            muzzle = modelMuzzle;
            return;
        }

        var visual = GameObject.CreatePrimitive(PrimitiveType.Cube);
        visual.name = "Visual";
        visual.transform.SetParent(parent, false);
        visual.transform.localScale = new Vector3(0.18f, 0.18f, 0.7f);
        visual.transform.localPosition = new Vector3(0f, 0.2f, 0f);
        DestroyImmediateOrRuntime(visual.GetComponent<Collider>());

        var mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        mat.color = KindColors[(int)kind % KindColors.Length];
        visual.GetComponent<MeshRenderer>().sharedMaterial = mat;

        muzzle = null;
    }

    private static Transform CreateFallbackMuzzle(Transform parent)
    {
        var muzzle = new GameObject("Muzzle");
        muzzle.transform.SetParent(parent, false);
        muzzle.transform.localPosition = new Vector3(0f, 0f, 0.8f);
        return muzzle.transform;
    }

    /// <summary>
    /// Builds the projectile matching a weapon's own documented stats
    /// (see each weapon class's header comment) and parents it under the
    /// weapon template so it travels/cleans up together with it.
    /// GravitySniper and OrbitalLauncher override damage/gravity/splash on
    /// every shot in their own OnFire() — the values here are still set for
    /// consistency and so the prefab is sane if instantiated directly.
    /// </summary>
    private static GameObject BuildProjectileTemplate(WeaponKind kind, Transform parent)
    {
        GameObject proj = kind switch
        {
            WeaponKind.Blaster => ProjectileFactory.Create("BlasterBolt", KindColors[0], 0.25f,
                damage: 12f, speed: 90f, lifetime: 3f, affectedByGravity: false, gravityMultiplier: 1f, splashRadius: 0f),
            WeaponKind.SpaceShotgun => ProjectileFactory.Create("ShotgunPellet", KindColors[1], 0.15f,
                damage: 7f, speed: 60f, lifetime: 1.2f, affectedByGravity: false, gravityMultiplier: 1f, splashRadius: 0f),
            WeaponKind.PlasmaRifle => ProjectileFactory.Create("PlasmaBolt", KindColors[2], 0.2f,
                damage: 9f, speed: 110f, lifetime: 2.5f, affectedByGravity: false, gravityMultiplier: 1f, splashRadius: 0f),
            WeaponKind.GravitySniper => ProjectileFactory.Create("SniperSlug", KindColors[3], 0.3f,
                damage: 80f, speed: 200f, lifetime: 6f, affectedByGravity: true, gravityMultiplier: 2f, splashRadius: 0f),
            WeaponKind.OrbitalLauncher => ProjectileFactory.Create("OrbitalShell", KindColors[4], 0.5f,
                damage: 120f, speed: 40f, lifetime: 10f, affectedByGravity: true, gravityMultiplier: 2.5f, splashRadius: 6f),
            _ => null,
        };

        // Blender bolt + impact models (Resources/Weapons/<Kind>_Bolt / _Impact), if present.
        WeaponModels.AttachBoltModel(proj, kind.ToString());

        if (proj != null) proj.transform.SetParent(parent, false);
        return proj;
    }

    private static void AddWeaponComponent(GameObject go, WeaponKind kind)
    {
        switch (kind)
        {
            case WeaponKind.Blaster:        go.AddComponent<Blaster>(); break;
            case WeaponKind.SpaceShotgun:    go.AddComponent<SpaceShotgun>(); break;
            case WeaponKind.PlasmaRifle:     go.AddComponent<PlasmaRifle>(); break;
            case WeaponKind.GravitySniper:   go.AddComponent<GravitySniper>(); break;
            case WeaponKind.OrbitalLauncher: go.AddComponent<OrbitalLauncher>(); break;
        }
    }

    private static void DestroyImmediateOrRuntime(Object obj)
    {
        if (obj == null) return;
        if (Application.isPlaying) Object.Destroy(obj);
        else Object.DestroyImmediate(obj);
    }
}
}
