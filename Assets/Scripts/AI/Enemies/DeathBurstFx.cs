using UnityEngine;

namespace OrbitRush
{

/// <summary>
/// One-shot death effect: a ring of glowing cubes that fly outward, tumble and shrink.
/// Spawned and driven entirely in code (no particle assets). Destroys itself when done.
/// </summary>
public class DeathBurstFx : MonoBehaviour
{
    private const int PieceCount = 12;
    private const float Duration = 0.9f;

    private Transform[] _pieces;
    private Vector3[] _velocities;
    private Vector3[] _spin;
    private float _t;

    public static void Play(Vector3 position, Color color, Vector3 up, float scale = 1f)
    {
        var go = new GameObject("DeathBurst");
        go.transform.position = position;
        var fx = go.AddComponent<DeathBurstFx>();
        fx.Build(color, up.sqrMagnitude > 0.001f ? up.normalized : Vector3.up, scale);
    }

    private void Build(Color color, Vector3 up, float scale)
    {
        var shader = Shader.Find("Universal Render Pipeline/Lit");
        var mat = new Material(shader);
        mat.color = color;
        mat.EnableKeyword("_EMISSION");
        mat.SetColor("_EmissionColor", color * 3f);

        _pieces = new Transform[PieceCount];
        _velocities = new Vector3[PieceCount];
        _spin = new Vector3[PieceCount];

        for (int i = 0; i < PieceCount; i++)
        {
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            var col = cube.GetComponent<Collider>();
            if (col != null) Destroy(col);          // purely visual

            cube.transform.SetParent(transform, false);
            cube.transform.localScale = Vector3.one * Random.Range(0.12f, 0.28f) * scale;
            cube.GetComponent<Renderer>().sharedMaterial = mat;

            // Outward and a bit "up" relative to the planet surface.
            Vector3 dir = (Random.onUnitSphere + up * 0.8f).normalized;
            _velocities[i] = dir * Random.Range(3f, 7f) * Mathf.Sqrt(scale);
            _spin[i] = Random.onUnitSphere * Random.Range(180f, 540f);
            _pieces[i] = cube.transform;
        }
    }

    void Update()
    {
        _t += Time.deltaTime;
        float k = Mathf.Clamp01(_t / Duration);

        for (int i = 0; i < _pieces.Length; i++)
        {
            if (_pieces[i] == null) continue;
            _pieces[i].position += _velocities[i] * Time.deltaTime;
            _pieces[i].Rotate(_spin[i] * Time.deltaTime, Space.Self);
            _pieces[i].localScale *= 1f - 2.2f * Time.deltaTime * k;   // shrink faster toward the end
        }

        if (_t >= Duration) Destroy(gameObject);
    }
}
}
