using UnityEngine;

namespace OrbitRush
{

/// <summary>
/// Represents a planet with gravitational influence.
/// Attach this to a sphere GameObject representing the planet.
/// </summary>
public class PlanetGravity : MonoBehaviour
{
    [Header("Planet Properties")]
    public string planetName = "Unknown Planet";
    public float radius = 20f;
    public float gravityStrength = 15f;
    [Tooltip("The field reaches radius × this (normally 2–2.5).")]
    public float influenceMultiplier = 2.5f;
    [Tooltip("Outer fraction of the field where gravity fades from full to zero.")]
    [Range(0.01f, 1f)] public float edgeFraction = 0.2f;
    [Tooltip("If two fields overlap, the higher priority wins; ties go to the closest one.")]
    public int priority = 0;

    [Header("Shape")]
    public GravityShape shape = GravityShape.Sphere;
    [Tooltip("Cylinder only: half the length of the tube along its local Y axis.")]
    public float cylinderHalfLength = 30f;

    [Header("Atmosphere")]
    public bool hasAtmosphere = true;
    [Range(0f, 1f)] public float airResistance = 0.1f;

    [Header("Time Dilation")]
    [Range(0.1f, 5f)] public float timeScale = 1f;

    [Header("Core")]
    public bool coreAccessible = false;

    [Header("Terrain (planets with hills / dunes)")]
    [Tooltip("Mesh collider of the ground. When set, spawns follow the real terrain instead of a perfect sphere of `radius`.")]
    public Collider groundCollider;
    [Tooltip("Planets with relief turn this off: the 'close enough to the sphere = grounded' fallback would fire in valleys.")]
    public bool useMathSurface = true;

    [Header("Biome")]
    public BiomeType biome = BiomeType.Rocky;

    [Header("Resources")]
    public ResourceType[] resources;

    public enum GravityShape { Sphere, Cylinder, Plane, Irregular }
    public enum BiomeType { Rocky, Ice, Volcanic, Gas, Desert, Alien }
    public enum ResourceType { Crystal, Gas, AlienMetal, Lava, Ice }

    // Use Start (not OnEnable) so GravitySystem.Instance already exists
    void Start() => GravitySystem.Instance?.Register(this);
    void OnDestroy() => GravitySystem.Instance?.Unregister(this);

    // ── Gravity field ─────────────────────────────────────────────────────

    /// <summary>Distance from the field's centre/axis/plane at which gravity reaches zero.</summary>
    public float FieldRadius => radius * influenceMultiplier;

    /// <summary>
    /// Gravity pull magnitude at a world position. Constant (Mario Galaxy style) everywhere inside the field —
    /// jumps feel the same wherever you are — and only fades out across the outer <see cref="edgeFraction"/> band,
    /// so you can feel yourself leaving the planet.
    /// </summary>
    public float GetGravityStrength(Vector3 worldPos)
    {
        float d = FieldDistance(worldPos);
        float edge = FieldRadius;
        if (d >= edge) return 0f;
        float inner = edge * (1f - Mathf.Clamp(edgeFraction, 0.01f, 1f));
        if (d <= inner) return gravityStrength;
        return gravityStrength * Mathf.SmoothStep(1f, 0f, (d - inner) / (edge - inner));
    }

    /// <summary>Distance used for the field test: to the centre (sphere), the axis (cylinder), the plane (flat) or the surface (irregular).</summary>
    public float FieldDistance(Vector3 worldPos)
    {
        switch (shape)
        {
            case GravityShape.Cylinder:
            {
                Vector3 local = transform.InverseTransformPoint(worldPos);
                float axial = Mathf.Max(0f, Mathf.Abs(local.y * transform.lossyScale.y) - cylinderHalfLength);
                float radial = new Vector2(local.x * transform.lossyScale.x, local.z * transform.lossyScale.z).magnitude;
                return Mathf.Sqrt(radial * radial + axial * axial);
            }
            case GravityShape.Plane:
            {
                Vector3 local = transform.InverseTransformPoint(worldPos);
                float lateral = Mathf.Max(0f, new Vector2(local.x * transform.lossyScale.x, local.z * transform.lossyScale.z).magnitude - radius);
                float height = local.y * transform.lossyScale.y;
                return Mathf.Sqrt(height * height + lateral * lateral);      // `radius` = half-size of the platform; the field reaches radius × influence above it
            }
            case GravityShape.Irregular:
                return Vector3.Distance(transform.position, worldPos);
            default:
                return Vector3.Distance(transform.position, worldPos);
        }
    }

    /// <summary>How far above the ground (sphere radius / cylinder radius / plane) this point is. Used to pick the closest field.</summary>
    public float SurfaceDistance(Vector3 worldPos)
    {
        switch (shape)
        {
            case GravityShape.Plane:
                return Mathf.Abs(transform.InverseTransformPoint(worldPos).y * transform.lossyScale.y);
            case GravityShape.Cylinder:
                return FieldDistance(worldPos) - radius;
            default:
                return FieldDistance(worldPos) - radius;
        }
    }

    /// <summary>Direction gravity pulls at worldPos (unit vector).</summary>
    public Vector3 GetGravityDirection(Vector3 worldPos)
    {
        switch (shape)
        {
            case GravityShape.Cylinder:
            {
                Vector3 toAxis = transform.position - worldPos;
                toAxis -= Vector3.Project(toAxis, transform.up);          // only the part perpendicular to the axis
                return toAxis.sqrMagnitude > 1e-6f ? toAxis.normalized : -transform.right;
            }
            case GravityShape.Plane:
                return transform.InverseTransformPoint(worldPos).y >= 0f ? -transform.up : transform.up;
            case GravityShape.Irregular:
            {
                // Toward the nearest surface: cast at the centre and use the surface normal; falls back to the centre direction
                Vector3 toCentre = transform.position - worldPos;
                if (groundCollider != null && groundCollider.enabled &&
                    groundCollider.Raycast(new Ray(worldPos, toCentre.normalized), out var hit, toCentre.magnitude + radius))
                    return -hit.normal;
                return toCentre.normalized;
            }
            default:
                return (transform.position - worldPos).normalized;
        }
    }

    /// <summary>Gravity acceleration vector at worldPos (direction × strength).</summary>
    public Vector3 GetGravityVector(Vector3 worldPos) => GetGravityDirection(worldPos) * GetGravityStrength(worldPos);

    /// <summary>"Up" direction from the planet surface at worldPos (feet-to-head).</summary>
    public Vector3 GetSurfaceUp(Vector3 worldPos) => -GetGravityDirection(worldPos);

    /// <summary>
    /// Point on the ground in direction `dir` from the planet centre: the real terrain when a ground collider
    /// is assigned (ray cast in from outside), otherwise the sphere of `radius`.
    /// </summary>
    public Vector3 GetSurfacePoint(Vector3 dir)
    {
        dir = dir.normalized;
        if (groundCollider != null && groundCollider.enabled)
        {
            float far = radius * 1.6f + 30f;
            var ray = new Ray(transform.position + dir * far, -dir);
            if (groundCollider.Raycast(ray, out var hit, far * 2f)) return hit.point;
        }
        return transform.position + dir * radius;
    }

    /// <summary>True when `hit` is this planet's own ground (not an obstacle on it).</summary>
    public bool IsGround(Collider hit) => hit is SphereCollider || (groundCollider != null && hit == groundCollider);

    public bool IsInAtmosphere(Vector3 worldPos)
    {
        float dist = Vector3.Distance(worldPos, transform.position);
        return hasAtmosphere && dist <= radius * 1.5f;
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0f, 0.8f, 1f, 0.25f);
        Gizmos.DrawSphere(transform.position, radius);

        Gizmos.color = new Color(0.3f, 1f, 0.3f, 0.08f);
        Gizmos.DrawSphere(transform.position, radius * influenceMultiplier);
    }
}
}
