using System.Collections.Generic;
using UnityEngine;

namespace OrbitRush
{

/// <summary>
/// Dresses each place of a planet with props (Resources/Models/Props/Bosque, from Kenney's CC0 Survival Kit): a campfire and tents at the
/// start clearing, a cabin and workbenches at the sawmill, a camp on the lookout... Positions are metres and degrees around the zone's centre.
/// </summary>
public static class LandmarkProps
{
    private struct P
    {
        public string model; public float r, az, yaw; public bool solid;
        public P(string model, float r, float az, float yaw = 0f, bool solid = true) { this.model = model; this.r = r; this.az = az; this.yaw = yaw; this.solid = solid; }
    }

    private static readonly Dictionary<string, P[]> Sets = new Dictionary<string, P[]>
    {
        ["inicio"] = new[]
        {
            new P("campfire-pit", 0, 0),
            new P("tent", 6.5f, 30), new P("tent", 6.5f, 150), new P("tent-canvas", 7f, 250),
            new P("bedroll", 3.2f, 90, 0, false), new P("bedroll", 3.2f, 200, 0, false),
            new P("barrel", 5f, 330), new P("box", 5.6f, 335), new P("chest", 5.2f, 280), new P("bucket", 2.6f, 310),
            new P("signpost", 13f, 70),
        },
        ["combate"] = new[]
        {
            new P("structure", 13f, 20), new P("structure-canvas", 15f, 80, 20),
            new P("workbench-grind", 7f, 120), new P("workbench", 8f, 150),
            new P("resource-planks", 10f, 200), new P("resource-planks", 10.5f, 207), new P("resource-planks", 11f, 214),
            new P("resource-wood", 6f, 250), new P("resource-wood", 6.3f, 256), new P("resource-wood", 6.2f, 262), new P("resource-wood", 6.4f, 282), new P("resource-wood", 6f, 288),
            new P("tree-log", 11f, 300), new P("tree-log", 12f, 312), new P("tree-log-small", 12f, 322),
            new P("fence", 18f, 330), new P("fence", 18f, 340), new P("fence", 18f, 350),
            new P("barrel", 9f, 40), new P("barrel", 9.4f, 46), new P("box", 9.5f, 60),
            new P("tool-axe", 7.5f, 135, 0, false), new P("signpost", 20f, 200),
        },
        ["agua"] = new[]
        {
            new P("campfire-fishing-stand", 41f, 20), new P("bucket", 40f, 24), new P("box", 41f, 30), new P("fish", 40.5f, 22, 0, false),
            new P("tent", 44f, 60), new P("signpost-single", 37f, 100),
        },
        ["jefe"] = new[]
        {
            new P("signpost", 12f, 0), new P("signpost", 12f, 180),
            new P("fence", 10f, 90), new P("fence", 10f, 100), new P("fence", 10f, 110),
            new P("barrel", 8f, 40), new P("barrel", 8.4f, 46), new P("box-large", 9f, 220), new P("tree-log", 13f, 300),
        },
        ["horda"] = new[]
        {
            new P("tent-canvas-half", 9f, 40), new P("bedroll", 7f, 60, 0, false),
            new P("barrel", 10f, 90), new P("barrel", 10.4f, 96), new P("chest", 8f, 130), new P("box", 9f, 160), new P("signpost-single", 12f, 0),
        },
        ["captura"] = new[]
        {
            new P("fence-doorway", 6f, 0), new P("fence", 9f, 40), new P("fence", 9f, 80), new P("fence", 9f, 120), new P("fence", 9f, 160),
            new P("fence", 9f, 200), new P("fence", 9f, 240), new P("fence", 9f, 280), new P("fence", 9f, 320),
            new P("workbench", 4f, 270), new P("box-large", 5f, 200), new P("signpost", 11f, 90), new P("barrel", 5f, 340),
        },
    };

    public static void Build(PlanetGravity planet, string folder = "Models/Props/Bosque/")
    {
        if (planet == null || !planet.HasZones) return;
        var old = planet.transform.Find("Landmarks");
        if (old != null) Object.Destroy(old.gameObject);

        var container = new GameObject("Landmarks").transform;
        container.SetParent(planet.transform, false);
        SphereScatter.CancelParentScale(container);

        int placed = 0, missing = 0;
        foreach (var zone in planet.zones)
        {
            if (!Sets.TryGetValue(zone.role, out var set)) continue;
            foreach (var p in set)
            {
                var prefab = Resources.Load<GameObject>(folder + "Prop_" + p.model);
                if (prefab == null) { missing++; continue; }
                Place(planet, container, zone, prefab, p);
                placed++;
            }
        }
        Debug.Log($"[Landmarks] {planet.planetName}: {placed} props placed" + (missing > 0 ? $", {missing} models missing" : "") + ".");
    }

    private static void Place(PlanetGravity planet, Transform container, PlanetZone zone, GameObject prefab, P p)
    {
        Vector3 dir = p.r < 0.01f ? zone.dir : PlanetZoneMath.DirAround(zone.dir, p.r, p.az * Mathf.Deg2Rad, planet.radius);
        Vector3 pos = planet.GetSurfacePoint(dir);

        var go = Object.Instantiate(prefab, container);
        go.name = prefab.name;
        foreach (var c in go.GetComponentsInChildren<Collider>(true)) Object.Destroy(c);

        // Collider from the model's own bounds (measured before it is turned)
        if (p.solid)
        {
            var b = new Bounds(); bool first = true;
            foreach (var r in go.GetComponentsInChildren<Renderer>(true)) { if (first) { b = r.bounds; first = false; } else b.Encapsulate(r.bounds); }
            if (!first)
            {
                var col = go.AddComponent<BoxCollider>();
                col.center = go.transform.InverseTransformPoint(b.center);
                col.size = b.size;
            }
        }

        // Stand on the surface, front toward the zone's centre (plus an optional turn)
        Vector3 toCentre = Vector3.ProjectOnPlane(zone.dir * planet.radius - dir * planet.radius, dir);
        if (toCentre.sqrMagnitude < 0.01f) toCentre = Vector3.Cross(dir, Vector3.right);
        go.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(toCentre.normalized, dir) * Quaternion.Euler(0f, p.yaw, 0f));

        if (p.model == "campfire-pit")
        {
            var lightGo = new GameObject("Fire");
            lightGo.transform.SetParent(go.transform, false);
            lightGo.transform.localPosition = new Vector3(0f, 0.7f, 0f);
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Point; light.color = new Color(1f, 0.55f, 0.2f); light.intensity = 2.2f; light.range = 14f;
            lightGo.AddComponent<FlickerLight>();
        }
    }
}

/// <summary>Campfire flicker.</summary>
public class FlickerLight : MonoBehaviour
{
    private Light _light; private float _base, _seed;
    void Start() { _light = GetComponent<Light>(); _base = _light.intensity; _seed = Random.value * 50f; }
    void Update()
    {
        if (_light == null) return;
        _light.intensity = _base * (0.8f + 0.25f * Mathf.PerlinNoise(Time.time * 6f, _seed) + 0.1f * Mathf.Sin(Time.time * 23f + _seed));
    }
}
}
