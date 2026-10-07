using UnityEngine;

namespace OrbitRush
{

/// <summary>
/// One-shot hit / explosion effect built from a Blender model (static emissive meshes: sparks,
/// cracks, fireballs, dust). It pops up from small to full size, then shrinks away and destroys
/// itself. Used for projectile impacts and grenade explosions.
/// </summary>
public class ImpactFx : MonoBehaviour
{
    private Vector3 _baseScale;
    private float _duration;
    private float _t;

    /// <summary>
    /// Spawns the effect at <paramref name="position"/>, with its flat side (+Y in the model)
    /// aligned to <paramref name="normal"/>. Silently does nothing if the model isn't found.
    /// </summary>
    public static void Play(string resourcePath, Vector3 position, Vector3 normal, float scale, float duration = 0.45f)
    {
        if (string.IsNullOrEmpty(resourcePath)) return;

        var prefab = Resources.Load<GameObject>(resourcePath);
        if (prefab == null) return;

        Vector3 up = normal.sqrMagnitude > 0.001f ? normal.normalized : Vector3.up;
        var go = Object.Instantiate(prefab, position + up * 0.02f, Quaternion.FromToRotation(Vector3.up, up));
        go.name = "ImpactFx";

        // Purely visual: must not block movement or shots.
        foreach (var c in go.GetComponentsInChildren<Collider>(true)) Object.Destroy(c);
        foreach (var r in go.GetComponentsInChildren<Renderer>(true))
        {
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
        }

        var fx = go.AddComponent<ImpactFx>();
        fx._baseScale = Vector3.one * scale;
        fx._duration = Mathf.Max(0.05f, duration);
        go.transform.localScale = fx._baseScale * 0.3f;
    }

    void Update()
    {
        _t += Time.deltaTime;
        float k = Mathf.Clamp01(_t / _duration);

        // Quick burst out (first 35%), then a smooth shrink.
        float s = k < 0.35f
            ? Mathf.Lerp(0.3f, 1f, 1f - Mathf.Pow(1f - k / 0.35f, 3f))
            : Mathf.Lerp(1f, 0f, (k - 0.35f) / 0.65f);

        transform.localScale = _baseScale * s;
        if (k >= 1f) Destroy(gameObject);
    }
}
}
