using UnityEngine;

namespace OrbitRush
{

/// <summary>
/// Standard blaster — single-shot, reliable, no gravity on projectile.
/// damage: 12 | fireRate: 0.3s | speed: 90
/// </summary>
public class Blaster : WeaponBase
{
    protected override string DefinitionName => "Blaster";

    protected override void OnFire()
    {
        SpawnProjectile(GetAimDirection());
    }
}
}
