using UnityEngine;

namespace OrbitRush
{

/// <summary>
/// The planet-gravity routines every walking/flying body repeats (player, enemies, support robot, projectiles,
/// grenades): which planet dominates here, what "up" is, the gravity vector, and turning a body upright.
/// </summary>
public static class PlanetBody
{
    /// <summary>Gravity acceleration at a world position (zero when there is no GravitySystem or no planet nearby).</summary>
    public static Vector3 GravityAt(Vector3 position)
        => GravitySystem.Instance != null ? GravitySystem.Instance.GetGravityVector(position) : Vector3.zero;

    /// <summary>Updates the dominant planet and surface-up for a position; leaves them unchanged in deep space.</summary>
    public static bool Track(Vector3 position, ref PlanetGravity planet, ref Vector3 up)
    {
        var dominant = GravitySystem.Instance != null ? GravitySystem.Instance.GetDominantPlanet(position) : null;
        if (dominant == null) return false;
        planet = dominant;
        up = dominant.GetSurfaceUp(position);
        return true;
    }

    /// <summary>Height above the planet's nominal surface radius.</summary>
    public static float Altitude(Vector3 position, PlanetGravity planet)
        => Vector3.Distance(position, planet.transform.position) - planet.radius;

    /// <summary>
    /// Eases a rigidbody so its up axis matches <paramref name="up"/> while it faces <paramref name="forwardHint"/>
    /// (projected onto the surface plane). Falls back to <paramref name="fallbackRight"/> if the hint is degenerate.
    /// </summary>
    public static void AlignUpright(Rigidbody rb, Vector3 forwardHint, Vector3 up, float speed, float dt)
    {
        Vector3 fwd = Vector3.ProjectOnPlane(forwardHint, up);
        if (fwd.sqrMagnitude < 0.001f) fwd = Vector3.ProjectOnPlane(rb.transform.forward, up);
        if (fwd.sqrMagnitude < 0.001f) fwd = Vector3.ProjectOnPlane(rb.transform.right, up);
        Quaternion target = Quaternion.LookRotation(fwd.normalized, up);
        rb.MoveRotation(Quaternion.Slerp(rb.rotation, target, speed * dt));
    }
}
}
