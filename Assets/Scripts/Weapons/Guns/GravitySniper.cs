using UnityEngine;

namespace OrbitRush
{

/// <summary>
/// High-damage sniper. Projectiles are heavily affected by gravity —
/// requires leading the target and accounting for planetary pull.
/// damage: 80 | fireRate: 2.5s | gravityAffected: true
/// </summary>
public class GravitySniper : WeaponBase
{
    [Header("Sniper")]
    [Range(1f, 3f)] public float gravityEffect = 2f;

    protected override string DefinitionName => "GravitySniper";

    protected override void OnFire()
    {
        if (projectilePrefab == null || muzzle == null) return;

        Vector3 aimDir = GetAimDirection();

        ProjectileFactory.Spawn(projectilePrefab, muzzle.position, aimDir, _owner?.gameObject, _owner?.CurrentPlanet, p =>
        {
            if (damage > 0f) p.damage = damage;
            p.affectedByGravity = true;
            p.gravityMultiplier = gravityEffect;
            p.speed = projectileSpeed;
        });
    }
}
}
