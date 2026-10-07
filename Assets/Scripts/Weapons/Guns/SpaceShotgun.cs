using UnityEngine;

namespace OrbitRush
{

/// <summary>
/// Space shotgun — fires 8 pellets per shot, wide spread.
/// damage: 7 per pellet | fireRate: 0.8s | range: short
/// </summary>
public class SpaceShotgun : WeaponBase
{
    [Header("Shotgun")]
    public int pelletCount = 8;

    protected override string DefinitionName => "SpaceShotgun";

    protected override void OnFire()
    {
        // Compute the aim once per shot (not per pellet) — one raycast, then
        // each pellet gets its own random spread off that same center point.
        Vector3 aimDir = GetAimDirection();
        for (int i = 0; i < pelletCount; i++)
            SpawnProjectile(aimDir);
    }
}
}
