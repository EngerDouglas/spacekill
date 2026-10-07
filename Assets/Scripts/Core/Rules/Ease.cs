using UnityEngine;

namespace OrbitRush
{

/// <summary>Easing helpers shared by the camera, view-models and combat animation.</summary>
public static class Ease
{
    /// <summary>Smoothstep: soft start and finish, t clamped to 0..1.</summary>
    public static float Smooth(float t)
    {
        t = Mathf.Clamp01(t);
        return t * t * (3f - 2f * t);
    }
}
}
