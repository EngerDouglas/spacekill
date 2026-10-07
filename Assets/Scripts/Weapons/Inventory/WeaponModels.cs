using UnityEngine;

namespace OrbitRush
{

/// <summary>
/// Loads the Blender-made weapon models from Resources/Weapons/:
///   &lt;Kind&gt;.fbx          the weapon itself (barrel along +Z, origin near the grip)
///   &lt;Kind&gt;_Bolt.fbx     the projectile ("bullet leaving the barrel")
///   &lt;Kind&gt;_Impact.fbx   the hit effect (flat, normal = +Y)
///   Grenade.fbx / Grenade_Explosion.fbx
/// where Kind is a WeaponSpawner.WeaponKind name. Everything falls back gracefully if a model is
/// missing, so the weapons keep working with their placeholder visuals.
/// </summary>
public static class WeaponModels
{
    public const string Folder = "Weapons/";

    // Blender authored these at real-world size (pistol 0.38 m, rifle 0.9 m, sniper 1.3 m);
    // scaled up a little so they read well in third person.
    private static float HeldScale(string kind) => kind == "Blaster" ? 1.5f : 1.15f;

    // World length multiplier for each projectile model.
    private static float BoltScale(string kind)
    {
        switch (kind)
        {
            case "Blaster":         return 1.5f;
            case "SpaceShotgun":    return 0.45f;   // 8 pellets per shot — keep them small
            case "PlasmaRifle":     return 0.8f;
            case "GravitySniper":   return 2.0f;
            case "OrbitalLauncher": return 2.0f;
            default:                return 1f;
        }
    }

    private static float ImpactScale(string kind)
    {
        switch (kind)
        {
            case "OrbitalLauncher": return 10f;     // splash radius 6 m
            case "GravitySniper":   return 3.5f;
            case "SpaceShotgun":    return 1.6f;
            default:                return 2.5f;
        }
    }

    public static GameObject Load(string name) => Resources.Load<GameObject>(Folder + name);

    /// <summary>
    /// Instantiates the weapon model under <paramref name="parent"/> and returns a freshly
    /// created "Muzzle" child positioned at the very front of the barrel. Returns null (and
    /// creates nothing) if there is no model for this kind.
    /// </summary>
    public static Transform AttachHeldModel(string kind, Transform parent)
    {
        var model = Load(kind);
        if (model == null) return null;

        var instance = Object.Instantiate(model, parent);
        instance.name = "Visual";
        instance.transform.localPosition = Vector3.zero;
        instance.transform.localRotation = Quaternion.identity;
        instance.transform.localScale = Vector3.one * HeldScale(kind);

        // (The FBX contains meshes only — no colliders — so there's nothing to strip. This also runs
        //  in Edit mode via WeaponSpawner's [ExecuteAlways], where Destroy() isn't allowed.)
        return CreateMuzzle(instance.transform);
    }

    /// <summary>
    /// Puts a "Muzzle" child at the front (max local +Z) of the model's geometry, centred on
    /// the vertices of that front slice, so projectiles leave from the barrel tip.
    /// </summary>
    public static Transform CreateMuzzle(Transform visual)
    {
        float maxZ = float.MinValue;
        var filters = visual.GetComponentsInChildren<MeshFilter>(true);
        foreach (var mf in filters)
        {
            var mesh = mf.sharedMesh;
            if (mesh == null || !mesh.isReadable) continue;
            foreach (var v in mesh.vertices)
            {
                float z = visual.InverseTransformPoint(mf.transform.TransformPoint(v)).z;
                if (z > maxZ) maxZ = z;
            }
        }

        Vector3 pos;
        if (maxZ > float.MinValue)
        {
            // Average x/y of the front-most slice (the barrel tip).
            float slice = 0.03f / Mathf.Max(0.0001f, visual.localScale.z);
            Vector3 sum = Vector3.zero; int n = 0;
            foreach (var mf in filters)
            {
                var mesh = mf.sharedMesh;
                if (mesh == null || !mesh.isReadable) continue;
                foreach (var v in mesh.vertices)
                {
                    Vector3 p = visual.InverseTransformPoint(mf.transform.TransformPoint(v));
                    if (p.z < maxZ - slice) continue;
                    sum += p; n++;
                }
            }
            Vector3 c = n > 0 ? sum / n : Vector3.zero;
            pos = new Vector3(c.x, c.y, maxZ + 0.02f);
        }
        else
        {
            // Mesh not readable: fall back to the renderers' bounds (front, mid-height).
            var renderers = visual.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0) return null;
            Bounds b = renderers[0].bounds;
            foreach (var r in renderers) b.Encapsulate(r.bounds);
            pos = visual.InverseTransformPoint(new Vector3(b.center.x, b.center.y, b.max.z));
        }

        var muzzle = new GameObject("Muzzle").transform;
        muzzle.SetParent(visual, false);
        muzzle.localPosition = pos;
        muzzle.localRotation = Quaternion.identity;
        return muzzle;
    }

    /// <summary>
    /// Swaps a procedural projectile's sphere for the Blender bolt model (keeping its collider and
    /// physics) and wires up the matching impact effect on the Projectile.
    /// </summary>
    public static void AttachBoltModel(GameObject projectile, string kind)
    {
        if (projectile == null) return;

        var proj = projectile.GetComponent<Projectile>();
        if (proj != null)
        {
            proj.impactResource = Folder + kind + "_Impact";
            proj.impactScale = ImpactScale(kind);
        }

        var model = Load(kind + "_Bolt");
        if (model == null) return;

        // Hide the placeholder sphere but keep it as the collider/physics body.
        var sphereRenderer = projectile.GetComponent<Renderer>();
        if (sphereRenderer != null) sphereRenderer.enabled = false;

        var visual = Object.Instantiate(model, projectile.transform);
        visual.name = "BoltVisual";
        visual.transform.localPosition = Vector3.zero;
        visual.transform.localRotation = Quaternion.identity;

        // The projectile root is already scaled (e.g. 0.25); cancel it so the bolt has real world size.
        float rootScale = Mathf.Max(0.0001f, projectile.transform.localScale.x);
        visual.transform.localScale = Vector3.one * (BoltScale(kind) / rootScale);
    }
}
}
