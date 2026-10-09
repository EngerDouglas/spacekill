using System.Collections.Generic;
using UnityEngine;

namespace OrbitRush
{

/// <summary>
/// A weapon vending machine (Resources/Models/Props/Maquina). Solid, lit with a cyan glow so it can be spotted from afar,
/// hologram and beacon turning. Standing near it and pressing E opens the <see cref="ShopPanel"/>.
/// </summary>
public class VendingMachine : MonoBehaviour
{
    public const float ModelScale = 0.8f;
    public const float InteractRange = 3.4f;

    public static readonly List<VendingMachine> All = new List<VendingMachine>();

    public static VendingMachine Nearest(Vector3 position, float range)
    {
        VendingMachine best = null; float bestSq = range * range;
        foreach (var m in All)
        {
            if (m == null) continue;
            float d = (m.transform.position - position).sqrMagnitude;
            if (d < bestSq) { bestSq = d; best = m; }
        }
        return best;
    }

    private Transform _holo, _beacon;

    /// <summary>Creates a machine standing on the surface (normal <paramref name="up"/>), front facing <paramref name="forward"/>.</summary>
    public static VendingMachine Spawn(Vector3 position, Vector3 up, Vector3 forward, Transform parent)
    {
        var go = new GameObject("VendingMachine");
        if (parent != null) go.transform.SetParent(parent, true);
        Vector3 f = Vector3.ProjectOnPlane(forward, up);
        if (f.sqrMagnitude < 0.01f) f = Vector3.ProjectOnPlane(Vector3.forward, up);
        go.transform.SetPositionAndRotation(position, Quaternion.LookRotation(f.normalized, up));
        return go.AddComponent<VendingMachine>();
    }

    void Awake()
    {
        All.Add(this);

        var model = Resources.Load<GameObject>("Models/Props/Maquina");
        if (model != null)
        {
            var visual = Instantiate(model, transform);
            visual.name = "Visual";
            visual.transform.localScale = Vector3.one * ModelScale;
            foreach (var c in visual.GetComponentsInChildren<Collider>(true)) Destroy(c);
            foreach (var t in visual.GetComponentsInChildren<Transform>(true))
            {
                if (t.name == "Holograma") _holo = t;
                else if (t.name == "Baliza") _beacon = t;
            }
        }
        else
        {
            var box = GameObject.CreatePrimitive(PrimitiveType.Cube);
            box.name = "Visual";
            box.transform.SetParent(transform, false);
            box.transform.localPosition = new Vector3(0f, 1.3f * ModelScale, 0f);
            box.transform.localScale = new Vector3(1.1f, 2.6f, 0.9f) * ModelScale;
            Destroy(box.GetComponent<Collider>());
        }

        // Solid body (model: 1.19 x 2.61 x 0.96 m)
        var col = gameObject.AddComponent<BoxCollider>();
        col.center = new Vector3(0f, 1.3f * ModelScale, 0.02f);
        col.size = new Vector3(1.15f, 2.6f, 0.9f) * ModelScale;

        // Cyan glow so the machine is easy to find
        var lightGo = new GameObject("Glow");
        lightGo.transform.SetParent(transform, false);
        lightGo.transform.localPosition = new Vector3(0f, 1.6f * ModelScale, 0.9f);
        var light = lightGo.AddComponent<Light>();
        light.type = LightType.Point; light.color = new Color(0.1f, 0.8f, 1f); light.intensity = 1.6f; light.range = 7f;
    }

    void OnDestroy() => All.Remove(this);

    void Update()
    {
        Vector3 axis = transform.up;
        if (_holo != null) _holo.Rotate(axis, 45f * Time.deltaTime, Space.World);
        if (_beacon != null) _beacon.Rotate(axis, -220f * Time.deltaTime, Space.World);
    }
}
}
