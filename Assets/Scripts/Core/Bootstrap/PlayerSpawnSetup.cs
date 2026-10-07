using System.Collections.Generic;
using UnityEngine;

namespace OrbitRush
{

/// <summary>
/// Scatters player spawn/respawn points across every planet in the scene and
/// hands them to GameManager.spawnPoints — used both by GameManager.GetSpawnPoint()
/// and by PlayerRespawn after death.
///
/// Attach alongside GameManager (e.g. on _GameSystems). Runs in the Editor
/// too (ExecuteAlways) so the spawn points are visible without entering Play
/// mode. Right-click → "Regenerate Spawn Points" to re-roll.
/// </summary>
[ExecuteAlways]
[RequireComponent(typeof(GameManager))]
public class PlayerSpawnSetup : MonoBehaviour
{
    [Header("Spawn Points")]
    public int spawnPointsPerPlanet = 4;
    public int seed = 1357;
    public float hoverHeight = 1f;

    void OnEnable() => Regenerate();

    [ContextMenu("Regenerate Spawn Points")]
    public void Regenerate()
    {
        var gm = GetComponent<GameManager>();
        if (gm == null) return;

        var existing = transform.Find("PlayerSpawnPoints");
        if (existing != null) DestroyImmediateOrRuntime(existing.gameObject);

        var container = new GameObject("PlayerSpawnPoints");
        container.transform.SetParent(transform, false);

        var planets = FindObjectsByType<PlanetGravity>(FindObjectsSortMode.None);
        var rng = new System.Random(seed);
        var points = new List<Transform>();

        foreach (var planet in planets)
        {
            for (int i = 0; i < spawnPointsPerPlanet; i++)
            {
                Vector3 dir = SphereScatter.RandomDirection(rng);
                // Planets with buildings/trees: re-roll a few times so nobody spawns inside one.
                for (int attempt = 0; attempt < 30 && !IsSpawnClear(planet, dir); attempt++)
                    dir = SphereScatter.RandomDirection(rng);
                Vector3 pos = planet.GetSurfacePoint(dir) + dir * hoverHeight;

                var point = new GameObject($"SpawnPoint_{planet.planetName}_{i}");
                point.transform.SetParent(container.transform, false);
                point.transform.position = pos;
                point.transform.rotation = Quaternion.FromToRotation(Vector3.up, dir);

                points.Add(point.transform);
            }
        }

        gm.spawnPoints = points.ToArray();
    }

    /// <summary>
    /// True when there's nothing solid around the spawn position. Ignores the planet's own
    /// ground sphere and triggers (pickups). Only checked in Play mode, where the planets'
    /// building colliders exist.
    /// </summary>
    private bool IsSpawnClear(PlanetGravity planet, Vector3 dir)
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

    private static void DestroyImmediateOrRuntime(Object obj)
    {
        if (obj == null) return;
        if (Application.isPlaying) Object.Destroy(obj);
        else Object.DestroyImmediate(obj);
    }
}
}
