using UnityEngine;

namespace OrbitRush
{

/// <summary>
/// Follows which gravity field governs a body and eases between fields over <see cref="blendTime"/> seconds, so
/// crossing from one planet's pull to another's doesn't snap the body's "up" (and the camera with it).
/// </summary>
public class GravityBlender
{
    public float blendTime = 0.5f;

    public PlanetGravity Planet { get; private set; }
    public Vector3 Up { get; private set; } = Vector3.up;
    /// <summary>Gravity acceleration to apply this step (already blended; zero in open space).</summary>
    public Vector3 Gravity { get; private set; }

    private PlanetGravity _previous;
    private Vector3 _fromUp = Vector3.up;
    private Vector3 _fromGravity;
    private float _t = 1f;
    private bool _initialised;

    public void Update(Vector3 position, float dt)
    {
        var dominant = GravitySystem.Instance != null ? GravitySystem.Instance.GetDominantPlanet(position) : null;

        if (dominant != null && dominant != Planet)
        {
            if (!_initialised) { Up = dominant.GetSurfaceUp(position); _initialised = true; _t = 1f; }
            else { _previous = Planet; _fromUp = Up; _fromGravity = Gravity; _t = 0f; }
            Planet = dominant;
        }

        if (dominant == null)
        {
            // Open space: keep the last orientation, no pull
            Gravity = Vector3.Lerp(Gravity, Vector3.zero, 1f - Mathf.Exp(-6f * dt));
            return;
        }

        Vector3 targetUp = dominant.GetSurfaceUp(position);
        Vector3 targetGravity = dominant.GetGravityVector(position);
        _t = blendTime <= 0.001f ? 1f : Mathf.Min(1f, _t + dt / blendTime);
        float k = Ease.Smooth(_t);

        if (_t < 1f)
        {
            Up = Vector3.Slerp(_fromUp, targetUp, k).normalized;
            Gravity = Vector3.Lerp(_fromGravity, targetGravity, k);
        }
        else
        {
            Up = targetUp;
            Gravity = targetGravity;
        }
    }
}
}
