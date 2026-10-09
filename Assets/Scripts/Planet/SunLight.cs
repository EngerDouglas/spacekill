using UnityEngine;

namespace OrbitRush
{

/// <summary>
/// Aims the scene's directional light from the Sun toward the player. The planets are spread around the Sun, so one fixed
/// direction could only light one of them; pointing the light from the Sun at wherever the player is gives every planet a lit
/// side facing the Sun and a night side away from it, and walking around a planet turns day into night.
/// </summary>
public class SunLight : MonoBehaviour
{
    /// <summary>World position of the Sun's centre (set by GalaxyLoader).</summary>
    public Vector3 sunPosition;

    [Tooltip("How quickly the light turns when the player changes planet or walks around one (higher = snappier).")]
    public float turnSpeed = 3f;

    private Light _light;
    private Transform _target;

    void LateUpdate()
    {
        if (_light == null) _light = FindLight();
        if (_light == null) return;

        if (_target == null)
        {
            var player = GameObject.FindWithTag("Player");
            _target = player != null ? player.transform : (Camera.main != null ? Camera.main.transform : null);
            if (_target == null) return;
        }

        Vector3 dir = _target.position - sunPosition;
        if (dir.sqrMagnitude < 1f) return;
        var want = Quaternion.LookRotation(dir.normalized);
        _light.transform.rotation = Quaternion.Slerp(_light.transform.rotation, want, 1f - Mathf.Exp(-turnSpeed * Time.deltaTime));
    }

    private static Light FindLight()
    {
        if (RenderSettings.sun != null) return RenderSettings.sun;
        foreach (var l in FindObjectsByType<Light>(FindObjectsSortMode.None))
            if (l.type == LightType.Directional) return l;
        return null;
    }
}
}
