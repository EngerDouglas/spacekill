using System.Collections.Generic;
using UnityEngine;

namespace OrbitRush
{

/// <summary>
/// Singleton that manages all gravity sources in the scene.
/// Any object with IGravityAffected will be pulled by all registered planets.
/// </summary>
public class GravitySystem : MonoBehaviour
{
    public static GravitySystem Instance { get; private set; }

    private readonly List<PlanetGravity> _planets = new();

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    public void Register(PlanetGravity planet) => _planets.Add(planet);
    public void Unregister(PlanetGravity planet) => _planets.Remove(planet);

    /// <summary>
    /// The planet whose field governs a position: among the fields that reach it, the highest priority wins and
    /// ties go to the closest one. Null in open space.
    /// </summary>
    public PlanetGravity GetDominantPlanet(Vector3 position)
    {
        PlanetGravity dominant = null;
        float bestDistance = float.MaxValue;

        foreach (var planet in _planets)
        {
            if (planet.GetGravityStrength(position) <= 0f) continue;
            float distance = planet.SurfaceDistance(position);
            if (dominant == null || planet.priority > dominant.priority
                || (planet.priority == dominant.priority && distance < bestDistance))
            {
                dominant = planet;
                bestDistance = distance;
            }
        }
        return dominant;
    }

    public IReadOnlyList<PlanetGravity> Planets => _planets;

    /// <summary>
    /// Applies cumulative gravity from all planets to a velocity vector.
    /// </summary>
    public Vector3 ApplyAllGravity(Vector3 position, Vector3 currentVelocity, float mass, float deltaTime)
    {
        Vector3 totalForce = Vector3.zero;

        foreach (var planet in _planets)
        {
            Vector3 dir = (planet.transform.position - position).normalized;
            float strength = planet.GetGravityStrength(position);
            totalForce += dir * strength;
        }

        return currentVelocity + (totalForce / mass) * deltaTime;
    }

    /// <summary>
    /// Gravity at a world position: the dominant field's pull (fields don't add up, so overlaps never double the pull).
    /// Zero in open space.
    /// </summary>
    public Vector3 GetGravityVector(Vector3 position)
    {
        var dominant = GetDominantPlanet(position);
        return dominant != null ? dominant.GetGravityVector(position) : Vector3.zero;
    }
}
}
