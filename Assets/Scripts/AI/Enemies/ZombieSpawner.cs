using System.Collections.Generic;
using UnityEngine;

namespace OrbitRush
{

/// <summary>
/// Zombies rise out of the ground in set zones when there is noise nearby. A few already wander each planet at the
/// start of the match; the total is capped. Keep the horde in mind: gunfire is what summons it.
/// </summary>
public class ZombieSpawner : MonoBehaviour
{
    public GameObject zombiePrefab;
    public int maxZombies = 28;
    public int initialPerPlanet = 3;
    public float noiseRadius = 75f;
    public float zoneCooldown = 22f;
    public float minDistanceFromPlayer = 18f;
    public int perSpawn = 2;

    private class Zone { public Transform point; public float readyAt; }
    private readonly List<Zone> _zones = new List<Zone>();
    private float _checkTimer;

    public void Init(GameObject prefab, IEnumerable<Transform> points, int perPlanet)
    {
        zombiePrefab = prefab;
        initialPerPlanet = perPlanet;
        foreach (var p in points)
        {
            if (p == null) continue;
            var info = p.GetComponent<SpawnPointInfo>();
            if (info != null && info.role != "horda") continue;       // planets with places: the horde belongs to its own place
            _zones.Add(new Zone { point = p, readyAt = Time.time + 10f });
        }

        // The initial stragglers: wandering, not risen from the ground
        var byPlanet = new Dictionary<string, List<Zone>>();
        foreach (var z in _zones)
        {
            string key = PlanetKey(z.point.name);
            if (!byPlanet.TryGetValue(key, out var list)) byPlanet[key] = list = new List<Zone>();
            list.Add(z);
        }
        foreach (var kv in byPlanet)
            for (int i = 0; i < initialPerPlanet && kv.Value.Count > 0; i++)
                Spawn(kv.Value[Random.Range(0, kv.Value.Count)].point, rising: false);
    }

    private static string PlanetKey(string pointName)
    {
        // SpawnPoint_<planet>_<n>
        int last = pointName.LastIndexOf('_');
        return last > 0 ? pointName.Substring(0, last) : pointName;
    }

    void Update()
    {
        _checkTimer -= Time.deltaTime;
        if (_checkTimer > 0f || zombiePrefab == null) return;
        _checkTimer = 1f;
        if (AliveCount() >= maxZombies) return;

        // A loud noise near a zone with nobody watching: zombies rise there
        foreach (var n in NoiseSystem.Recent)
        {
            if (Time.time - n.time > 2.5f || n.loudness < Loudness.Shoot * 0.8f) continue;
            Zone best = null; float bestDist = noiseRadius;
            foreach (var z in _zones)
            {
                if (z.point == null || Time.time < z.readyAt) continue;
                float d = Vector3.Distance(z.point.position, n.position);
                if (d < bestDist && !PlayerNear(z.point.position)) { bestDist = d; best = z; }
            }
            if (best == null) continue;
            best.readyAt = Time.time + zoneCooldown;
            for (int i = 0; i < perSpawn && AliveCount() < maxZombies; i++)
                Spawn(best.point, rising: true, jitter: 2.5f);
            break;
        }
    }

    private bool PlayerNear(Vector3 p)
    {
        var gm = GameManager.Instance;
        if (gm == null) return false;
        foreach (var pl in gm.Players)
            if (pl != null && pl.IsAlive && Vector3.Distance(pl.transform.position, p) < minDistanceFromPlayer) return true;
        return false;
    }

    private int AliveCount()
    {
        int n = 0;
        foreach (var z in HordeRegistry.Members) if (z != null && z.Stats != null && z.Stats.IsAlive) n++;
        return n;
    }

    public GameObject Spawn(Transform point, bool rising, float jitter = 0f)
    {
        if (zombiePrefab == null || point == null) return null;
        // A planet with its own roster raises its own melee enemies (none = no horde there)
        var prefab = zombiePrefab;
        var roster = EnemyRoster.For(PlanetKey(point.name).Replace("SpawnPoint_", ""));
        if (roster != null) { prefab = EnemyRoster.Pick(roster.zombies); if (prefab == null) return null; }
        Vector3 offset = jitter > 0f ? Vector3.ProjectOnPlane(Random.insideUnitSphere * jitter, point.up) : Vector3.zero;
        var go = Instantiate(prefab, point.position + offset, point.rotation);
        go.SetActive(true);
        var home = go.GetComponent<EnemyHome>();
        if (home == null) home = go.AddComponent<EnemyHome>();
        if (rising)
        {
            var ai = go.GetComponent<EnemyAI>();
            if (ai != null) ai.BeginRising();
            NoiseSystem.Emit(go.transform.position, Loudness.Walk, go);
        }
        return go;
    }
}
}
