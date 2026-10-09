using System.Collections.Generic;
using UnityEngine;

namespace OrbitRush
{

/// <summary>
/// Which enemy templates a planet uses. A planet without a roster (Ciudad Destruida) keeps the default Elite Soldier /
/// Guardian Drone / zombie; a planet with one spawns only its own enemies. Built by EnemySpawnSetup, read by GameManager
/// and ZombieSpawner. An empty list means "none of that kind on this planet".
/// </summary>
public class EnemyRoster
{
    public readonly List<GameObject> soldiers = new List<GameObject>();   // spawned `enemiesPerPlanet` times (may fly: e.g. the rusty gun drone)
    public readonly List<GameObject> drones = new List<GameObject>();     // spawned `dronesPerPlanet` times
    public readonly List<GameObject> zombies = new List<GameObject>();    // the melee horde (rises with noise)

    private static readonly Dictionary<string, EnemyRoster> ByPlanet = new Dictionary<string, EnemyRoster>();

    public static void Clear() => ByPlanet.Clear();
    public static void Set(string planetName, EnemyRoster roster) => ByPlanet[planetName] = roster;
    public static EnemyRoster For(string planetName) => planetName != null && ByPlanet.TryGetValue(planetName, out var r) ? r : null;

    public static GameObject Pick(List<GameObject> list) => list == null || list.Count == 0 ? null : list[Random.Range(0, list.Count)];
}
}
