using UnityEngine;

namespace OrbitRush
{

/// <summary>
/// Lobs a heavy shell on a high arc that comes down on your crosshair and explodes.
/// damage: 120 (splash) | fireRate: 5s | splash radius: 6 m
///
/// The shot used to fire almost straight up and then drift off toward whichever planet pulled hardest,
/// never landing near the target. Now the launch velocity is solved so that the shell, flying under the
/// planets' gravity, arrives at the aimed point after a short flight time.
/// </summary>
public class OrbitalLauncher : WeaponBase
{
    [Header("Orbital")]
    [Tooltip("Shortest and longest flight time of the lob, in seconds. Longer = higher arc.")]
    public float minFlightTime = 1.2f;
    public float maxFlightTime = 3.4f;
    [Tooltip("Never lob closer than this, so the blast (6 m) can't catch the shooter.")]
    public float minRange = 14f;
    [Tooltip("Furthest point it will aim at (metres).")]
    public float maxRange = 140f;
    public float shellGravityMultiplier = 2.5f;

    protected override string DefinitionName => "OrbitalLauncher";

    protected override void OnFire()
    {
        if (projectilePrefab == null || muzzle == null) return;

        Vector3 from = muzzle.position;
        Vector3 aim = PlayerAim.GetAimPoint(_owner != null ? _owner.transform : null, maxRange);

        // Keep the target inside [minRange, maxRange] along the line to the aim point
        Vector3 toAim = aim - from;
        float dist = toAim.magnitude;
        Vector3 dir = dist > 0.01f ? toAim / dist : muzzle.forward;
        dist = Mathf.Clamp(dist, minRange, maxRange);
        Vector3 target = from + dir * dist;

        // Solve the launch velocity: target = from + v0*T + 0.5*g*T²  →  v0 = (target-from)/T − 0.5*g*T
        float flight = Mathf.Clamp(dist / 24f, minFlightTime, maxFlightTime);
        Vector3 mid = (from + target) * 0.5f;
        Vector3 gravity = (GravitySystem.Instance != null ? GravitySystem.Instance.GetGravityVector(mid) : Vector3.zero) * shellGravityMultiplier;
        Vector3 launchVelocity = (target - from) / flight - 0.5f * gravity * flight;

        ProjectileFactory.Spawn(projectilePrefab, from, launchVelocity.normalized, _owner?.gameObject, _owner?.CurrentPlanet, p =>
        {
            if (damage > 0f) p.damage = damage;
            p.orbital = false;                 // plain ballistic arc under the planets' gravity
            p.affectedByGravity = true;
            p.gravityMultiplier = shellGravityMultiplier;
            p.splashRadius = 6f;
            p.speed = launchVelocity.magnitude;
        });
    }
}
}
