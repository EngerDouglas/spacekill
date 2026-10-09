using UnityEngine;

namespace OrbitRush
{

/// <summary>Scatters loose coins over a planet (added next to the weapon spawner). Bigger planets get more.</summary>
[RequireComponent(typeof(PlanetGravity))]
public class CoinSpawner : MonoBehaviour
{
    public int seed = 991;
    public int goldPerCopy = 3, platinumPerCopy = 1;

    void OnEnable() => Regenerate();

    public void Regenerate()
    {
        var planet = GetComponent<PlanetGravity>();
        var old = transform.Find("Coins");
        if (old != null) Destroy(old.gameObject);

        var container = new GameObject("Coins").transform;
        container.SetParent(transform, false);
        SphereScatter.CancelParentScale(container);

        var rng = new System.Random(seed);
        if (planet.HasZones) { PlaceOnRoute(planet, container, rng); return; }
        int copies = planet.PickupCopies;
        Place(planet, container, rng, CoinType.Oro, goldPerCopy * copies);
        Place(planet, container, rng, CoinType.Platino, platinumPerCopy * copies);
        Place(planet, container, rng, CoinType.Elite, Mathf.Max(1, copies / 3));
    }

    /// <summary>A trail of Oro every ~20 m along the path, and the better coins waiting in each place.</summary>
    private static void PlaceOnRoute(PlanetGravity planet, Transform container, System.Random rng)
    {
        foreach (var d in PlanetZoneMath.PathDirs(planet, 20f))
        {
            Vector3 dir = PlanetZoneMath.DirAround(d, SphereScatter.NextFloat(rng, -1.5f, 1.5f), SphereScatter.NextFloat(rng, 0f, 6.28f), planet.radius);
            Coin.Spawn(CoinType.Oro, planet.GetSurfacePoint(dir) + dir * 0.3f, dir, container);
        }
        foreach (var z in planet.zones)
        {
            int plat = z.role == "combate" ? 3 : z.role == "horda" ? 2 : z.role == "jefe" ? 2 : 0;
            int elite = z.role == "jefe" || z.role == "captura" ? 1 : 0;
            int gold = z.role == "inicio" ? 0 : 4;
            for (int i = 0; i < gold; i++) PlaceNear(planet, container, rng, z, CoinType.Oro);
            for (int i = 0; i < plat; i++) PlaceNear(planet, container, rng, z, CoinType.Platino);
            for (int i = 0; i < elite; i++) PlaceNear(planet, container, rng, z, CoinType.Elite);
        }
    }

    private static void PlaceNear(PlanetGravity planet, Transform container, System.Random rng, PlanetZone z, CoinType type)
    {
        Vector3 dir = PlanetZoneMath.DirAround(z.dir, SphereScatter.NextFloat(rng, 4f, Mathf.Max(8f, z.clear * 0.8f)), SphereScatter.NextFloat(rng, 0f, 6.28f), planet.radius);
        Coin.Spawn(type, planet.GetSurfacePoint(dir) + dir * 0.3f, dir, container);
    }

    private static void Place(PlanetGravity planet, Transform container, System.Random rng, CoinType type, int count)
    {
        for (int i = 0; i < count; i++)
        {
            Vector3 dir = SphereScatter.RandomClearDirection(rng, planet, 0.6f);
            Coin.Spawn(type, planet.GetSurfacePoint(dir) + dir * 0.3f, dir, container);
        }
    }
}
}
