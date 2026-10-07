using UnityEngine;

namespace OrbitRush
{

/// <summary>
/// Builds simple placeholder projectile GameObjects procedurally — no art
/// assets needed. Each is a small sphere with a Rigidbody + Projectile,
/// tuned to one weapon's stats.
///
/// The returned template is inactive: Projectile's own lifetime timer runs
/// in FixedUpdate, so if the template itself stayed active it would destroy
/// itself after a few seconds sitting idle as a reference. WeaponBase (and
/// any custom OnFire) must call SetActive(true) on the clone right after
/// Instantiate — Unity's Instantiate preserves the inactive state otherwise.
/// </summary>
public static class ProjectileFactory
{
    /// <summary>
    /// Fires a clone of an inactive template: activates it, lets the caller tune it, then Init()s it.
    /// The one place that knows about the "template stays inactive" rule — weapons, enemies and the robot all use it.
    /// </summary>
    public static Projectile Spawn(GameObject template, Vector3 position, Vector3 direction, GameObject owner,
                                   PlanetGravity planet, System.Action<Projectile> configure = null)
    {
        var go = Object.Instantiate(template, position, Quaternion.LookRotation(direction));
        go.transform.SetParent(null, true);
        go.SetActive(true);
        var proj = go.GetComponent<Projectile>();
        if (proj == null) return null;
        configure?.Invoke(proj);
        proj.Init(direction, owner, planet);
        return proj;
    }

    public static GameObject Create(string name, Color color, float scale, float damage, float speed,
        float lifetime, bool affectedByGravity, float gravityMultiplier, float splashRadius)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        go.name = name;
        go.transform.localScale = Vector3.one * scale;

        var mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        mat.color = color;
        mat.EnableKeyword("_EMISSION");
        mat.SetColor("_EmissionColor", color * 1.5f);
        go.GetComponent<MeshRenderer>().sharedMaterial = mat;

        var rb = go.AddComponent<Rigidbody>();
        rb.mass = 0.2f;

        var proj = go.AddComponent<Projectile>();
        proj.damage = damage;
        proj.speed = speed;
        proj.lifetime = lifetime;
        proj.affectedByGravity = affectedByGravity;
        proj.gravityMultiplier = gravityMultiplier;
        proj.splashRadius = splashRadius;

        go.SetActive(false); // stays dormant as a template; the firing code reactivates each clone
        return go;
    }
}
}
