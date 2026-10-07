using UnityEngine;

namespace OrbitRush
{

/// <summary>
/// Planet-aware movement for AI enemies — same gravity-alignment approach as
/// PlayerController (feet snap to the surface, body yaws around planet-up),
/// but driven by EnemyAI's decisions instead of player input.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(EnemyStats))]
public class EnemyController : MonoBehaviour
{
    [Header("Movement")]
    public float moveSpeed = 4.5f;
    public float alignSpeed = 6f;

    [Header("Flying (drones)")]
    [Tooltip("When true the enemy ignores gravity and hovers above the planet surface instead of walking.")]
    public bool flying = false;
    [Tooltip("Hover altitude above the planet surface, in metres.")]
    public float hoverHeight = 5f;
    public float hoverStiffness = 6f;
    public float hoverDamping = 4f;

    [Header("Damage (set by DroneDamageStages)")]
    [Tooltip("0 = steady flight; higher values add drifting sideways kicks and altitude lurches.")]
    public float instability = 0f;
    public float speedScale = 1f;
    [Tooltip("Set by the AI each tick: patrol / sneak / run pace as a fraction of moveSpeed.")]
    public float aiSpeedFactor = 1f;

    [Header("Free flight (drones chasing through open space)")]
    [Tooltip("Set by the AI: ignore the hover-over-a-planet spring and steer straight to a point (can leave the planet).")]
    public bool freeFlight = false;
    public float flightAcceleration = 3f;

    /// <summary>When false the body is inert (spawning out of the ground, knocked out).</summary>
    public bool controlEnabled = true;

    private Vector3? _flightTarget;
    private float _flightSpeed;

    private Rigidbody _rb;
    private PlanetGravity _currentPlanet;
    private Vector3 _planetUp = Vector3.up;

    private Vector3 _desiredMoveDir;   // world space; projected onto the planet surface each tick
    private Vector3? _lookTarget;      // world position to face, or null to face the move direction

    void Awake()
    {
        _rb = GetComponent<Rigidbody>();
        _rb.useGravity = false;
        _rb.angularDamping = 20f;
        _rb.interpolation = RigidbodyInterpolation.Interpolate;
    }

    void FixedUpdate()
    {
        UpdateCurrentPlanet();
        ApplyGravity();
        AlignToPlanet();
        HandleMovement();
    }

    private void UpdateCurrentPlanet()
    {
        PlanetBody.Track(transform.position, ref _currentPlanet, ref _planetUp);
    }

    private void ApplyGravity()
    {
        if (flying && freeFlight)
        {
            ApplyFreeFlight();
            return;
        }
        if (flying)
        {
            ApplyHover();
            return;
        }

        _rb.AddForce(PlanetBody.GravityAt(transform.position), ForceMode.Acceleration);
    }

    /// <summary>Steer toward the flight target with a limited speed; no gravity, no hover spring.</summary>
    private void ApplyFreeFlight()
    {
        if (!_flightTarget.HasValue || !controlEnabled) { _rb.AddForce(-_rb.linearVelocity * 1.5f, ForceMode.Acceleration); return; }
        Vector3 toTarget = _flightTarget.Value - transform.position;
        float dist = toTarget.magnitude;
        float speed = _flightSpeed * speedScale * Mathf.Clamp01(dist / 6f + 0.15f);       // slow down on arrival
        Vector3 desired = dist > 0.01f ? toTarget / dist * speed : Vector3.zero;
        _rb.AddForce((desired - _rb.linearVelocity) * flightAcceleration, ForceMode.Acceleration);
    }

    /// <summary>Spring toward (planet radius + hoverHeight) along planet-up, damped, no gravity.</summary>
    private void ApplyHover()
    {
        if (_currentPlanet == null) return;

        float altitude = PlanetBody.Altitude(transform.position, _currentPlanet);
        float error = hoverHeight - altitude;
        float verticalSpeed = Vector3.Dot(_rb.linearVelocity, _planetUp);

        float accel = error * hoverStiffness - verticalSpeed * hoverDamping;
        if (instability > 0.01f)    // engine trouble: the altitude surges and sags
            accel += (Mathf.PerlinNoise(Time.time * 1.3f, GetInstanceID() * 0.01f) - 0.5f) * 2f * instability * 7f;
        _rb.AddForce(_planetUp * accel, ForceMode.Acceleration);
    }

    private void AlignToPlanet()
    {
        Vector3 hint = _lookTarget.HasValue
            ? _lookTarget.Value - transform.position
            : (_desiredMoveDir.sqrMagnitude > 0.001f ? _desiredMoveDir : transform.forward);
        PlanetBody.AlignUpright(_rb, hint, _planetUp, alignSpeed, Time.fixedDeltaTime);
    }

    private void HandleMovement()
    {
        if (!controlEnabled) return;
        if (freeFlight) return;

        Vector3 moveDir = Vector3.ProjectOnPlane(_desiredMoveDir, _planetUp).normalized;
        Vector3 targetVel = moveDir * moveSpeed * speedScale * aiSpeedFactor;
        Vector3 currentVel = Vector3.ProjectOnPlane(_rb.linearVelocity, _planetUp);
        _rb.AddForce((targetVel - currentVel) * 8f, ForceMode.Acceleration);

        if (instability > 0.01f)    // drifting sideways kicks it can't correct
        {
            float t = Time.time * 0.9f, id = GetInstanceID() * 0.013f;
            Vector3 right = Vector3.Cross(_planetUp, transform.forward);
            Vector3 kick = right * (Mathf.PerlinNoise(t, id) - 0.5f) + transform.forward * (Mathf.PerlinNoise(id, t) - 0.5f);
            _rb.AddForce(kick * 2f * instability * 6f, ForceMode.Acceleration);
        }
    }

    // ── Called by EnemyAI ─────────────────────────────────────────────────

    public void SetMoveDirection(Vector3 worldDirection) => _desiredMoveDir = worldDirection;
    public void StopMoving() => _desiredMoveDir = Vector3.zero;

    /// <summary>Free flight toward a world point at up to <paramref name="speed"/> m/s (pass null to stop).</summary>
    public void FlyTo(Vector3? point, float speed)
    {
        _flightTarget = point; _flightSpeed = speed;
    }
    public Rigidbody Body => _rb;

    /// <summary>Face a specific world position (e.g. the target player), or null to face the move direction.</summary>
    public void LookAt(Vector3? worldPosition) => _lookTarget = worldPosition;

    public PlanetGravity CurrentPlanet => _currentPlanet;
    public Vector3 PlanetUp => _planetUp;
}
}
