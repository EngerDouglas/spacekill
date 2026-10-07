using UnityEngine;

namespace OrbitRush
{

/// <summary>
/// Shared "uniform random point on a unit sphere" helper — every
/// planet-surface scatter system (weapons, enemies, grenades, spawn points)
/// uses this instead of each reimplementing the same math.
/// </summary>
public static class SphereScatter
{
    public static Vector3 RandomDirection(System.Random rng)
    {
        float z = NextFloat(rng, -1f, 1f);
        float theta = NextFloat(rng, 0f, Mathf.PI * 2f);
        float r = Mathf.Sqrt(Mathf.Max(0f, 1f - z * z));
        return new Vector3(r * Mathf.Cos(theta), r * Mathf.Sin(theta), z);
    }

    /// <summary>
    /// A random direction whose surface point (at the pickup hover height) isn't inside a building / tree / tank.
    /// Only checks in Play mode (that's when the planets' obstacle colliders exist); falls back to the last try.
    /// </summary>
    public static Vector3 RandomClearDirection(System.Random rng, PlanetGravity planet, float hoverHeight, float clearRadius = 0.9f, int tries = 40)
    {
        Vector3 dir = RandomDirection(rng);
        if (!Application.isPlaying || planet == null) return dir;
        for (int i = 0; i < tries; i++)
        {
            Vector3 p = planet.GetSurfacePoint(dir) + dir * hoverHeight;
            bool clear = true;
            foreach (var hit in Physics.OverlapSphere(p, clearRadius))
            {
                if (hit.isTrigger || planet.IsGround(hit)) continue;   // pickups and the planet's own ground
                clear = false; break;
            }
            if (clear) return dir;
            dir = RandomDirection(rng);
        }
        return dir;
    }

    public static float NextFloat(System.Random rng, float min, float max)
        => min + (float)rng.NextDouble() * (max - min);

    /// <summary>
    /// Sets localScale so the transform's effective world scale becomes
    /// 1,1,1 regardless of its parent's scale. Planets are scaled way up
    /// (radius baked into ~x40 scale) — anything parented under one without
    /// this inherits that scale too, so a "0.18" visual silently becomes
    /// ~40x too big. Call once on a container right after parenting it under
    /// a planet, before placing real-world-sized things inside it.
    /// </summary>
    public static void CancelParentScale(Transform t)
    {
        var parentScale = t.parent != null ? t.parent.lossyScale : Vector3.one;
        t.localScale = new Vector3(
            parentScale.x != 0f ? 1f / parentScale.x : 1f,
            parentScale.y != 0f ? 1f / parentScale.y : 1f,
            parentScale.z != 0f ? 1f / parentScale.z : 1f);
    }
}
}
