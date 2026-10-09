using UnityEngine;

namespace OrbitRush
{

/// <summary>Places one or two vending machines on a planet (bigger planets get two), on clear ground, facing a random way.</summary>
[RequireComponent(typeof(PlanetGravity))]
public class VendingMachineSpawner : MonoBehaviour
{
    public int seed = 4711;

    void OnEnable() => Regenerate();

    private static void PlaceAt(PlanetGravity planet, Transform container, string role, float meters, float az)
    {
        var zone = PlanetZoneMath.ByRole(planet, role);
        if (zone == null) return;
        Vector3 dir = PlanetZoneMath.DirAround(zone.dir, meters, az, planet.radius);
        Vector3 toCentre = PlanetZoneMath.DirAround(zone.dir, 0f, 0f, planet.radius);
        Vector3 face = Vector3.Cross(dir, Vector3.Cross(toCentre, dir));         // the front looks toward the zone's centre
        VendingMachine.Spawn(planet.GetSurfacePoint(dir), dir, face.sqrMagnitude > 1e-4f ? face : Vector3.Cross(dir, Vector3.right), container);
    }

    public void Regenerate()
    {
        var planet = GetComponent<PlanetGravity>();
        var old = transform.Find("VendingMachines");
        if (old != null) Destroy(old.gameObject);

        var container = new GameObject("VendingMachines").transform;
        container.SetParent(transform, false);
        SphereScatter.CancelParentScale(container);

        var rng = new System.Random(seed);
        if (planet.HasZones)
        {
            // At the start clearing (next to the campfire) and near the capture point
            PlaceAt(planet, container, "inicio", 11f, 5.45f);
            PlaceAt(planet, container, "captura", 13f, 2.4f);
            return;
        }
        int count = Mathf.Clamp(Mathf.RoundToInt(planet.PickupCopies / 3f), 1, 2);
        for (int i = 0; i < count; i++)
        {
            Vector3 dir = SphereScatter.RandomClearDirection(rng, planet, 0.05f, clearRadius: 2.2f, tries: 60);
            Vector3 pos = planet.GetSurfacePoint(dir);
            Vector3 side = Vector3.Cross(dir, Random.onUnitSphere).normalized;
            if (side.sqrMagnitude < 0.01f) side = Vector3.Cross(dir, Vector3.right).normalized;
            VendingMachine.Spawn(pos, dir, side, container);
        }
    }
}
}
