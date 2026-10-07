using UnityEngine;

namespace OrbitRush
{

/// <summary>
/// Smooths and protects the third-person camera. Applied in LateUpdate on top of whatever position WeaponAim chose
/// (normal / aim-down-sights / scope), following the usual third-person camera practice:
///   • COLLISION — a sphere cast from the head to the wanted camera spot; if a wall, tree or building is in the way the
///     camera slides in close to it (fast) and eases back out afterwards (slow), instead of clipping through geometry or snapping.
///   • BOOM LAG — the camera trails a little behind the player's velocity (spring-damped, frame-rate independent),
///     which hides small stutters and gives a sense of speed.
///   • LANDING / JUMP SPRING — a damped spring dips the camera on hard landings and lifts it on take-off.
///   • TRAUMA SHAKE — damage and hard landings add smooth Perlin-noise shake (no random jitter) that decays quickly.
/// All smoothing uses exponential damping (1 − e^(−λ·dt)), so it feels the same at 30 or 240 fps.
/// </summary>
[DefaultExecutionOrder(200)]
public class CameraRig : MonoBehaviour
{
    [Header("Collision")]
    public float collisionRadius = 0.22f;
    public float minDistance = 0.35f;
    [Tooltip("How fast the camera moves in when something is in the way (higher = snappier).")]
    public float pullInLambda = 30f;
    [Tooltip("How fast it eases back out once the way is clear.")]
    public float pushOutLambda = 3.5f;

    [Header("Boom lag")]
    [Tooltip("Seconds of velocity the camera trails behind.")]
    public float lagTime = 0.045f;
    public float maxLag = 0.45f;
    public float lagLambda = 7f;

    [Header("Landing spring")]
    public float springStiffness = 150f;
    public float springDamping = 15f;

    [Header("Shake")]
    public float shakeFrequency = 22f;
    public float maxShakeOffset = 0.1f;
    public float maxShakeRoll = 1.6f;
    public float traumaDecay = 1.8f;

    private static CameraRig _instance;
    private Camera _cam;
    private PlayerController _pc;
    private WeaponAim _aim;
    private Rigidbody _rb;
    private Transform _pivot;
    private readonly RaycastHit[] _hits = new RaycastHit[10];

    private float _curDist = -1f;
    private float _blockedTimer;
    private Vector3 _lag;
    private float _springY, _springVel;
    private float _trauma;
    private float _seed;
    private Quaternion _baseLocalRot = Quaternion.identity;
    private bool _rotCached;

    /// <summary>Adds screen shake (0..1). Small values for hits, larger for big impacts.</summary>
    public static void AddTrauma(float amount)
    {
        if (_instance != null) _instance._trauma = Mathf.Clamp01(_instance._trauma + amount);
    }

    void Awake() { _instance = this; _seed = Random.value * 100f; }
    void OnDestroy() { if (_instance == this) _instance = null; }

    void Start()
    {
        _pc = GetComponent<PlayerController>();
        _aim = GetComponent<WeaponAim>();
        _rb = GetComponent<Rigidbody>();
        if (_pc != null)
        {
            _pivot = _pc.cameraTarget;
            _pc.Landed += OnLanded;
            _pc.Jumped += OnJumped;
        }
    }

    private void OnLanded(float impact)
    {
        _springVel -= Mathf.Clamp(impact * 0.35f, 0f, 4f);             // dips the camera on landing
        if (impact > 14f) AddTrauma(Mathf.Clamp01((impact - 14f) / 40f) * 0.45f);
    }

    private void OnJumped() => _springVel += 0.8f;                    // a small lift on take-off

    void LateUpdate()
    {
        if (_pc == null || _pivot == null) return;
        if (_cam == null) _cam = Camera.main;
        if (_cam == null) return;

        float dt = Mathf.Min(Time.deltaTime, 0.05f);
        if (dt <= 0f) return;

        Transform cam = _cam.transform;
        if (!_rotCached) { _baseLocalRot = cam.localRotation; _rotCached = true; }
        float scale = Mathf.Max(0.0001f, cam.parent != null ? cam.parent.lossyScale.x : 1f);
        float scopeBlend = _aim != null ? Mathf.Max(_aim.ScopeBlend, _aim.FirstPersonBlend) : 0f;      // scope or first person: rigid at eye level
        float free = 1f - scopeBlend;                                  // no lag / spring while looking through the scope

        // ── boom lag ──
        Vector3 vel = _rb != null ? _rb.linearVelocity : Vector3.zero;
        Vector3 velLocal = _pivot.InverseTransformDirection(vel);
        Vector3 lagTarget = -Vector3.ClampMagnitude(velLocal * lagTime, maxLag) * free;
        _lag = Vector3.Lerp(_lag, lagTarget, 1f - Mathf.Exp(-lagLambda * dt));

        // ── landing spring ──
        float stepDt = dt;
        _springVel += (-springStiffness * _springY - springDamping * _springVel) * stepDt;
        _springY += _springVel * stepDt;
        _springY = Mathf.Clamp(_springY, -0.45f, 0.45f);

        // Desired local position = what WeaponAim set, plus lag and spring (converted from world metres to local units)
        Vector3 desiredLocal = cam.localPosition + (_lag + _pivot.InverseTransformDirection(_pc.transform.up) * (_springY * free)) / scale;
        cam.localPosition = desiredLocal;

        // ── collision ──
        Vector3 head = _pivot.position;
        Vector3 wanted = cam.position;
        Vector3 toCam = wanted - head;
        float fullDist = toCam.magnitude;
        if (fullDist > 0.001f)
        {
            Vector3 dir = toCam / fullDist;
            float allowed = fullDist;
            bool blocked = false;
            if (scopeBlend < 0.5f)
            {
                int n = Physics.SphereCastNonAlloc(head, collisionRadius, dir, _hits, fullDist, ~0, QueryTriggerInteraction.Ignore);
                float nearest = float.MaxValue;
                for (int i = 0; i < n; i++)
                {
                    var h = _hits[i];
                    if (h.distance <= 0.0001f) continue;               // started inside it (our own body)
                    var col = h.collider;
                    if (col.transform.IsChildOf(_pc.transform)) continue;
                    if (col.GetComponentInParent<EnemyStats>() != null || col.GetComponentInParent<SupportRobot>() != null) continue;
                    if (col.GetComponentInParent<Projectile>() != null) continue;
                    if (h.distance < nearest) nearest = h.distance;
                }
                if (nearest < fullDist) { allowed = Mathf.Max(minDistance, nearest - 0.04f); blocked = true; }
            }

            if (_curDist < 0f) _curDist = fullDist;
            if (blocked) _blockedTimer = 0.4f; else _blockedTimer -= dt;

            float lambda = allowed < _curDist ? pullInLambda : (_blockedTimer > 0f ? pushOutLambda : 14f);
            _curDist = Mathf.Lerp(_curDist, allowed, 1f - Mathf.Exp(-lambda * dt));
            _curDist = Mathf.Min(_curDist, fullDist);
            cam.position = head + dir * _curDist;
        }

        // ── trauma shake ──
        if (_trauma > 0.001f)
        {
            _trauma = Mathf.MoveTowards(_trauma, 0f, traumaDecay * dt);
            float shake = _trauma * _trauma;
            float t = Time.time * shakeFrequency;
            Vector3 off = new Vector3(Noise(t, 0f), Noise(t, 7f), Noise(t, 13f)) * (maxShakeOffset * shake);
            cam.position += cam.TransformDirection(off);
            cam.localRotation = _baseLocalRot * Quaternion.Euler(0f, 0f, Noise(t, 21f) * maxShakeRoll * shake);
        }
        else cam.localRotation = _baseLocalRot;
    }

    private float Noise(float t, float channel) => (Mathf.PerlinNoise(t * 0.5f + _seed, channel) - 0.5f) * 2f;
}
}
