using UnityEngine;
using UnityEngine.Rendering;

namespace OrbitRush
{

/// <summary>
/// Energy shield around an enemy that has one (the Guardian Drone). It is almost invisible — just a faint rim —
/// until it is hit: then a blue flash blooms at the point of impact and a ripple ring spreads across the surface.
/// When the shield breaks, a last bright shimmer runs over the whole bubble before it disappears.
/// Visual only (no collider); EnemyStats soaks the damage into the shield first.
/// </summary>
[RequireComponent(typeof(EnemyStats))]
public class EnemyShield : MonoBehaviour
{
    public float diameter = 2.4f;
    public Color color = new Color(0.10f, 0.55f, 1f);
    [Tooltip("How long one impact ripple lasts (seconds).")]
    public float rippleTime = 0.75f;

    private EnemyStats _stats;
    private Material _material;
    private Transform _bubble;
    private bool _wasUp = true;
    private float _breakTimer;
    private float _lastHitSeen = -100f;
    private float _lastPointTime = -100f;

    private readonly Vector4[] _hits = new Vector4[4];       // xyz = direction (bubble space), w = age 0..1 (-1 = free)
    private int _nextHit;
    private static readonly int[] HitIds = { Shader.PropertyToID("_Hit0"), Shader.PropertyToID("_Hit1"), Shader.PropertyToID("_Hit2"), Shader.PropertyToID("_Hit3") };

    void Start()
    {
        _stats = GetComponent<EnemyStats>();
        _stats.HitAt += OnHitAt;

        var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        go.name = "ShieldBubble";
        var col = go.GetComponent<Collider>();
        if (col != null) Destroy(col);
        go.transform.SetParent(transform, false);
        go.transform.localPosition = Vector3.zero;
        // Size by the real mesh extent so the bubble is exactly `diameter` metres wide
        var mf = go.GetComponent<MeshFilter>();
        float unit = mf != null && mf.sharedMesh != null ? Mathf.Max(0.01f, mf.sharedMesh.bounds.size.x) : 1f;
        go.transform.localScale = Vector3.one * (diameter / unit);
        _bubble = go.transform;

        var r = go.GetComponent<Renderer>();
        r.shadowCastingMode = ShadowCastingMode.Off;
        r.receiveShadows = false;

        var shader = Shader.Find("OrbitRush/EnergyShield");
        if (shader != null)
        {
            _material = new Material(shader);
            _material.SetColor("_Color", color);
        }
        else   // shader missing: a very faint plain bubble
        {
            _material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            _material.SetFloat("_Surface", 1f); _material.SetFloat("_Blend", 0f);
            _material.SetOverrideTag("RenderType", "Transparent");
            _material.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha); _material.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
            _material.SetInt("_ZWrite", 0); _material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            _material.renderQueue = (int)RenderQueue.Transparent;
            _material.SetColor("_BaseColor", new Color(color.r, color.g, color.b, 0.03f));
        }
        r.sharedMaterial = _material;

        for (int i = 0; i < _hits.Length; i++) _hits[i] = new Vector4(0, 0, 1, -1f);
        Push();
    }

    void OnDestroy()
    {
        if (_stats != null) _stats.HitAt -= OnHitAt;
    }

    /// <summary>A shot landed at this world position: start a ripple there.</summary>
    private void OnHitAt(Vector3 worldPoint)
    {
        if (_bubble == null) return;
        _lastPointTime = Time.time;
        Vector3 dir = _bubble.InverseTransformPoint(worldPoint);
        if (dir.sqrMagnitude < 1e-6f) dir = Vector3.forward;
        StartRipple(dir.normalized);
    }

    private void StartRipple(Vector3 localDir)
    {
        _hits[_nextHit] = new Vector4(localDir.x, localDir.y, localDir.z, 0f);
        _nextHit = (_nextHit + 1) % _hits.Length;
    }

    void Update()
    {
        if (_stats == null || _bubble == null) return;

        bool up = _stats.Shield > 0f;

        // Damage that did not report a point (melee, splash): ripple on the side facing the camera
        if (_stats.LastHitTime > _lastHitSeen)
        {
            _lastHitSeen = _stats.LastHitTime;
            if (up && Time.time - _lastPointTime > 0.05f)
            {
                var cam = Camera.main;
                Vector3 toCam = cam != null ? cam.transform.position - _bubble.position : Random.onUnitSphere;
                StartRipple(_bubble.InverseTransformDirection(toCam).normalized);
            }
        }

        // Shield just broke: a last bright shimmer over the whole bubble
        if (_wasUp && !up)
        {
            _breakTimer = 0.5f;
            for (int i = 0; i < 3; i++) StartRipple(Random.onUnitSphere);
        }
        _wasUp = up;

        float flash = 0f;
        if (!up)
        {
            _breakTimer -= Time.deltaTime;
            flash = Mathf.Clamp01(_breakTimer / 0.5f);
        }
        bool visible = up || _breakTimer > 0f;
        if (_bubble.gameObject.activeSelf != visible) _bubble.gameObject.SetActive(visible);
        if (!visible) return;

        // Age the ripples
        float dt = Time.deltaTime / Mathf.Max(0.05f, rippleTime);
        for (int i = 0; i < _hits.Length; i++)
        {
            if (_hits[i].w < 0f) continue;
            _hits[i].w += dt;
            if (_hits[i].w >= 1f) _hits[i].w = -1f;
        }

        _material.SetFloat("_Flash", flash);
        Push();
    }

    private void Push()
    {
        for (int i = 0; i < _hits.Length; i++) _material.SetVector(HitIds[i], _hits[i]);
    }
}
}
