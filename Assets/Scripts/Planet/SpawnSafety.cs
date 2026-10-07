using UnityEngine;

namespace OrbitRush
{

/// <summary>
/// Spawn-position safety shared by the spawn setups: on planets with buildings, trees and
/// cover (the city planets), a random point on the surface can land inside geometry.
/// </summary>
public static class SpawnSafety
{
    /// <summary>
    /// True when nothing solid is around the spawn position. Ignores the planet's own ground
    /// sphere and triggers (pickups). Only meaningful in Play mode, where the planets'
    /// building colliders exist; in the Editor it always returns true.
    /// </summary>
    public static bool IsClear(PlanetGravity planet, Vector3 dir, float hoverHeight)
    {
        if (!Application.isPlaying) return true;

        Vector3 center = planet.GetSurfacePoint(dir) + dir * (hoverHeight + 1f);
        foreach (var hit in Physics.OverlapSphere(center, 1.3f))
        {
            if (hit.isTrigger || planet.IsGround(hit)) continue;
            return false;
        }
        return true;
    }
}
}
