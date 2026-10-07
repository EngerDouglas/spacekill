using UnityEngine;

namespace OrbitRush
{

/// <summary>
/// Scatters grenade and special-bomb pickups across a planet's surface —
/// procedural, no prefab assets needed, mirrors WeaponSpawner. Each pickup
/// wraps a disabled GrenadeBase-derived template (GrenadeBase itself
/// deactivates its own GameObject in Awake, so no extra bookkeeping is
/// needed there); walking over the pickup calls
/// WeaponInventory.TryPickupGrenade.
///
/// Attach to a planet GameObject alongside PlanetGravity. Runs in the Editor
/// too (ExecuteAlways). Right-click → "Regenerate Grenades" to re-roll.
/// </summary>
[ExecuteAlways]
[RequireComponent(typeof(PlanetGravity))]
public class GrenadeSpawner : MonoBehaviour
{
    public enum GrenadeKind { GravityGrenade, PlasmaGrenade, AntiGravityBomb, MeteorGrenade }

    static readonly Color[] GrenadeColors =
    {
        new(0.35f, 0.55f, 1.00f), // GravityGrenade - blue
        new(1.00f, 0.45f, 0.10f), // PlasmaGrenade - orange
        new(0.55f, 1.00f, 0.65f), // AntiGravityBomb - mint
        new(1.00f, 0.55f, 0.20f), // MeteorGrenade - amber
    };
    static readonly Color SpecialColor = new(0.75f, 0.10f, 0.85f); // BlackHoleBomb - violet, reads as "dangerous/rare"

    [Header("Loadout")]
    public GrenadeKind[] grenadesToScatter =
    {
        GrenadeKind.GravityGrenade, GrenadeKind.PlasmaGrenade,
        GrenadeKind.AntiGravityBomb, GrenadeKind.MeteorGrenade,
    };
    [Tooltip("Scatters one BlackHoleBomb pickup too, filling the special-bomb slot instead of the regular one.")]
    public bool scatterSpecial = true;
    public int pickupAmount = 1;

    [Header("Placement")]
    public int seed = 3939;
    public float pickupTriggerRadius = 1f;
    public float hoverHeight = 0.3f;

    private PlanetGravity _planet;

    void OnEnable() => Regenerate();

    [ContextMenu("Regenerate Grenades")]
    public void Regenerate()
    {
        _planet = GetComponent<PlanetGravity>();
        if (_planet == null) return;

        var existing = transform.Find("GrenadePickups");
        if (existing != null) DestroyImmediateOrRuntime(existing.gameObject);

        var container = new GameObject("GrenadePickups");
        container.transform.SetParent(transform, false);
        SphereScatter.CancelParentScale(container.transform); // planet's own scale would otherwise blow up every pickup ~40x

        var rng = new System.Random(seed);

        foreach (var kind in grenadesToScatter)
        {
            if (kind == GrenadeKind.MeteorGrenade) continue;   // meteors were removed from the game
            var template = BuildGrenadeTemplate(kind);
            SpawnPickup(template, kind.ToString(), GrenadeColors[(int)kind % GrenadeColors.Length],
                isSpecial: false, container.transform, rng);
        }

        if (scatterSpecial)
        {
            var template = BuildSpecialTemplate();
            SpawnPickup(template, "BlackHoleBomb", SpecialColor, isSpecial: true, container.transform, rng);
        }
    }

    /// <summary>Black-hole bomb template (also used by WeaponInventory for the player's starting special).</summary>
    public static GameObject BuildSpecialTemplate()
    {
        var template = new GameObject("BlackHoleBombTemplate");
        template.AddComponent<SphereCollider>().radius = 0.15f;
        AddGrenadeVisual(template.transform);
        template.AddComponent<BlackHoleBomb>();
        return template;
    }

    private static void AddGrenadeVisual(Transform parent)
    {
        var model = WeaponModels.Load("Grenade");
        if (model == null) return;

        var visual = Instantiate(model, parent);
        visual.name = "Visual";
        visual.transform.localPosition = Vector3.zero;
        visual.transform.localRotation = Quaternion.identity;
        visual.transform.localScale = Vector3.one * 2.5f;   // ~0.5 m: easy to follow in flight
    }

    public static GameObject BuildGrenadeTemplate(GrenadeKind kind)
    {
        var go = new GameObject($"{kind}Template");

        // GrenadeBase only requires a Rigidbody — without a collider too it'd
        // never physically hit anything once thrown (no bounce, no OnCollisionEnter
        // for PlasmaGrenade's sticky behavior).
        var col = go.AddComponent<SphereCollider>();
        col.radius = 0.15f;

        // Blender grenade model (the template had no visual at all, so thrown grenades were invisible).
        AddGrenadeVisual(go.transform);

        switch (kind)
        {
            case GrenadeKind.GravityGrenade:
                go.AddComponent<GravityGrenade>();
                break;
            case GrenadeKind.PlasmaGrenade:
                go.AddComponent<PlasmaGrenade>();
                break;
            case GrenadeKind.AntiGravityBomb:
                go.AddComponent<AntiGravityBomb>();
                break;
            case GrenadeKind.MeteorGrenade:
                var meteor = go.AddComponent<MeteorGrenade>();
                meteor.meteorPrefab = ProjectileFactory.Create("Meteor", new Color(1f, 0.5f, 0.15f), 0.3f,
                    damage: meteor.meteorDamage, speed: meteor.meteorSpeed, lifetime: 4f,
                    affectedByGravity: true, gravityMultiplier: 1.5f, splashRadius: meteor.meteorSplash);
                break;
        }

        return go;
    }

    private void SpawnPickup(GameObject template, string displayName, Color color, bool isSpecial, Transform parent, System.Random rng)
    {
        Vector3 dir = SphereScatter.RandomClearDirection(rng, _planet, hoverHeight);
        Vector3 surfacePos = _planet.GetSurfacePoint(dir) + dir * hoverHeight;
        Quaternion rot = Quaternion.FromToRotation(Vector3.up, dir);

        var pickupGO = new GameObject($"Pickup_{displayName}");
        pickupGO.transform.SetParent(parent, false);
        pickupGO.transform.position = surfacePos;
        pickupGO.transform.rotation = rot;

        var col = pickupGO.AddComponent<SphereCollider>();
        col.isTrigger = true;
        col.radius = pickupTriggerRadius;

        var visual = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        visual.name = "Visual";
        visual.transform.SetParent(pickupGO.transform, false);
        visual.transform.localScale = Vector3.one * 0.35f;
        visual.transform.localPosition = new Vector3(0f, 0.15f, 0f);
        DestroyImmediateOrRuntime(visual.GetComponent<Collider>());

        // With the Blender grenade model, the coloured sphere shrinks into a small indicator light
        // floating above it, so each grenade kind is still recognisable by colour.
        var grenadeModel = WeaponModels.Load("Grenade");
        if (grenadeModel != null)
        {
            var body = Instantiate(grenadeModel, pickupGO.transform);
            body.name = "GrenadeModel";
            body.transform.localPosition = new Vector3(0f, 0.2f, 0f);
            body.transform.localRotation = Quaternion.identity;
            body.transform.localScale = Vector3.one * 2f;

            visual.transform.localScale = Vector3.one * 0.12f;
            visual.transform.localPosition = new Vector3(0f, 0.7f, 0f);
        }

        var mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        mat.color = color;
        visual.GetComponent<MeshRenderer>().sharedMaterial = mat;

        template.transform.SetParent(pickupGO.transform, false);

        var pickup = pickupGO.AddComponent<GrenadePickup>();
        pickup.grenadePrefab = template;
        pickup.isSpecial = isSpecial;
        pickup.pickupAmount = pickupAmount;
        pickup.displayName = displayName;
    }

    private static void DestroyImmediateOrRuntime(Object obj)
    {
        if (obj == null) return;
        if (Application.isPlaying) Object.Destroy(obj);
        else Object.DestroyImmediate(obj);
    }
}
}
