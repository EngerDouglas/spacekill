using System.Collections.Generic;
using UnityEngine;

namespace OrbitRush
{

public enum JetpackPhase { Idle, Charging, Launch, Cruise, Captured }

/// <summary>
/// Jetpack flight between planets, in five phases:
///   Charge   (hold on the ground, aim at a planet; slow, glowing, the trajectory is drawn)
///   Launch   (release: strong impulse, own gravity ignored for a moment, loud)
///   Cruise   (open space: small thrust corrections cost fuel, boost, trail)
///   Capture  (entering another planet's field: feet turn to the surface)
///   Landing  (normal / forced shockwave if braced / stunned if careless)
/// While airborne inside a field, holding the jetpack still gives plain upward thrust.
/// </summary>
public partial class PlayerController
{
    [Header("Jetpack flight")]
    [Tooltip("Seconds of holding on the ground for a full-strength launch.")]
    public float chargeTime = 1.2f;
    public float minChargeToLaunch = 0.2f;
    public float launchSpeedMin = 28f;
    public float launchSpeedMax = 62f;
    [Tooltip("Fuel (of 100) a launch costs at minimum and maximum charge.")]
    public float launchFuelMin = 20f;
    public float launchFuelMax = 40f;
    public float minFuelToCharge = 12f;
    [Tooltip("Seconds between launches, so nobody can flee non-stop.")]
    public float launchCooldown = 1.5f;
    [Tooltip("Seconds after take-off in which touching the ground does not count as landing.")]
    public float launchGroundGrace = 0.6f;
    [Tooltip("Walking speed multiplier while charging (the pack glows: rivals know you are about to flee).")]
    [Range(0.05f, 1f)] public float chargeMoveScale = 0.55f;
    [Tooltip("Degrees around the crosshair in which a planet is selected as the target.")]
    public float aimAssistAngle = 22f;
    [Tooltip("Seconds after launch in which the pull of the home planet is ignored.")]
    public float launchGravityOff = 0.6f;
    public float cruiseThrust = 9f;
    public float cruiseFuelPerSecond = 12f;
    public float boostFuel = 12f;
    [Tooltip("Retro-thrust (m/s^2) used to brake while a planet is catching you and the jetpack is held.")]
    public float retroBrake = 45f;

    [Header("Sonic dash (aimed at a planet)")]
    [Tooltip("Speed (m/s) of the straight dash to the selected planet, at minimum and maximum charge.")]
    public float sonicSpeedMin = 55f;
    public float sonicSpeedMax = 90f;
    [Tooltip("How quickly the velocity turns toward the planet (1/s). High = a straight line.")]
    public float sonicTurn = 7f;
    public float sonicTimeout = 10f;
    [Tooltip("Extra field of view (degrees) at full sonic speed.")]
    public float sonicFovBoost = 34f;
    [Tooltip("Speed the planet's pull brings you down to as you are captured (free braking, so the landing is survivable).")]
    public float captureSpeed = 26f;
    public float captureDrag = 2.4f;
    [Tooltip("Deceleration (m/s^2) of the free soft-landing profile after a capture.")]
    public float softLandingDecel = 20f;

    [Header("Landing")]
    [Tooltip("Impact speed (m/s) above which a landing is dangerous.")]
    public float hardLandingSpeed = 22f;
    public float forcedLandRadius = 14f;
    public float forcedLandDamage = 45f;
    public float forcedLandForce = 1400f;
    public float stunSeconds = 1.3f;

    private JetpackPhase _phase = JetpackPhase.Idle;
    private float _charge;
    private float _launchCooldownUntil;
    private float _launchTime;
    private float _gravityOffUntil;
    private float _stunUntil;
    private float _recoverUntil;
    private float _lastThrustTime = -10f;
    private float _footstepTimer;
    private PlanetGravity _launchPlanet;
    private bool _wasOverheated;
    private PlanetGravity _sonicTarget;
    private bool _sonicActive;
    private float _sonicSpeed, _sonicBlend;
    private bool _requireRelease;
    private float _groundedInFlight;
    private bool _fullChargeHinted;
    private float _fullChargeTime;

    /// <summary>Standing on the ground, forgiving the one-frame gaps of uneven terrain, stairs and slopes.</summary>
    private bool OnGroundForJetpack => _isGrounded || _coyoteTimer > 0.04f;
    private float _blockedMsgAt = -10f;

    /// <summary>0..1 how deep into the sonic dash we are (drives the screen effect and the FOV kick).</summary>
    public float SonicBlend => _sonicBlend;
    public bool IsSonic => _sonicActive;
    public event System.Action SonicStarted;
    private bool _leftHomeField;
    private PlanetGravity _aimTarget;

    public JetpackPhase Phase => _phase;
    /// <summary>0..1 how charged the launch is.</summary>
    public float ChargeAmount => _charge;
    public bool IsFlying => _phase == JetpackPhase.Launch || _phase == JetpackPhase.Cruise || _phase == JetpackPhase.Captured;
    public bool IsStunned => Time.time < _stunUntil;
    public PlanetGravity AimTarget => _aimTarget;
    /// <summary>Seconds until the next launch is allowed.</summary>
    public float LaunchCooldownLeft => Mathf.Max(0f, _launchCooldownUntil - Time.time);

    public event System.Action JetpackLaunched;
    /// <summary>(impact speed, forced shockwave landing?, stunned?)</summary>
    public event System.Action<float, bool, bool> HardLanded;

    /// <summary>Multiplier on walking / running speed (charging, stunned, landing recovery).</summary>
    private float MoveSpeedScale
    {
        get
        {
            if (IsStunned) return 0f;
            float s = _phase == JetpackPhase.Charging ? chargeMoveScale : 1f;
            if (IsGrabbed) s *= grabMoveScale;
            if (Time.time < _recoverUntil) s *= 0.5f;
            return s;
        }
    }

    // ── Per-tick state machine ────────────────────────────────────────────

    private void HandleJetpackSystem()
    {
        float dt = Time.fixedDeltaTime;
        bool locked = _stats.JetpackLocked || IsStunned || IsGrabbed;
        _stats.JetpackOnGround = _isGrounded && _phase == JetpackPhase.Idle;

        bool thrusting = false;
        TickSonic(dt);

        // Safety net: a flight phase with our feet on the ground for a while means the landing was missed — recover
        if ((_phase == JetpackPhase.Cruise || _phase == JetpackPhase.Captured || (_phase == JetpackPhase.Launch && Time.time - _launchTime > launchGroundGrace + 0.3f)) && _isGrounded)
        {
            _groundedInFlight += dt;
            if (_groundedInFlight > 0.5f) { _phase = JetpackPhase.Idle; EndSonic(); _requireRelease = true; _groundedInFlight = 0f; }
        }
        else _groundedInFlight = 0f;

        switch (_phase)
        {
            case JetpackPhase.Idle:
                if (!_jetpackHeld) _requireRelease = false;
                if (_jetpackHeld && !locked && !_requireRelease)
                {
                    if (OnGroundForJetpack)
                    {
                        if (Time.time >= _launchCooldownUntil && _stats.jetpackEnergy >= minFuelToCharge)
                        {
                            _phase = JetpackPhase.Charging;
                            _charge = 0f; _fullChargeHinted = false; _fullChargeTime = 0f;
                        }
                        else NotifyNotReady();
                    }
                    else thrusting = HoverThrust(dt);
                }
                break;

            case JetpackPhase.Charging:
                if (locked || !OnGroundForJetpack) { CancelCharge(); break; }
                if (_jetpackHeld)
                {
                    _charge = Mathf.Min(1f, _charge + dt / Mathf.Max(0.1f, chargeTime));
                    if (_charge >= 1f)
                    {
                        _fullChargeTime += dt;
                        if (!_fullChargeHinted && HUD.Instance != null) { _fullChargeHinted = true; HUD.Instance.ShowEvent("CARGA AL MÁXIMO — DESPEGANDO", 1.2f); }
                        if (_fullChargeTime > 0.7f) Launch();       // never leave the player stuck holding a full charge
                    }
                    else _fullChargeTime = 0f;
                }
                else if (_charge >= minChargeToLaunch) Launch();
                else CancelCharge();
                break;

            case JetpackPhase.Launch:
                thrusting = true;
                if (Time.time - _launchTime > 0.35f) _phase = JetpackPhase.Cruise;
                break;

            case JetpackPhase.Cruise:
            case JetpackPhase.Captured:
                // Remember that we have left the home planet's field (so coming back to it counts as a capture too)
                if (_gravity.Planet != _launchPlanet || _gravity.Gravity.sqrMagnitude < 0.01f) _leftHomeField = true;
                if (_phase == JetpackPhase.Cruise && Time.time - _launchTime > 0.7f && _gravity.Planet != null
                    && _gravity.Gravity.sqrMagnitude > 0.25f && (_gravity.Planet != _launchPlanet || _leftHomeField))
                {
                    _phase = JetpackPhase.Captured;          // a planet's field has caught us: feet turn to its surface
                }
                if (_phase == JetpackPhase.Captured) CaptureBraking(dt);
                if (_jetpackHeld && !locked && !_sonicActive)
                    thrusting = _phase == JetpackPhase.Cruise ? CruiseThrust(dt) : RetroThrust(dt);   // captured: braking thrust for the landing
                break;
        }

        if (_phase == JetpackPhase.Charging) UpdateAimTarget();
        else if (_phase == JetpackPhase.Idle) _aimTarget = null;

        // Ramp bookkeeping for the thrust flame / animation
        _jetpackThrust = thrusting ? Mathf.MoveTowards(_jetpackThrust, 1f, dt / Mathf.Max(0.02f, jetpackRamp))
                                   : Mathf.MoveTowards(_jetpackThrust, 0f, dt / Mathf.Max(0.02f, jetpackRamp * 1.6f));
        _jetpacking = _jetpackThrust > 0.05f;
        if (thrusting) _lastThrustTime = Time.time;

        // Status messages (rising edges only)
        if (_stats.JetpackOverheated && !_wasOverheated && HUD.Instance != null) HUD.Instance.ShowEvent("JETPACK SOBRECALENTADO", 2.2f);
        _wasOverheated = _stats.JetpackOverheated;

        EmitFootsteps(dt);
    }

    /// <summary>Plain upward thrust inside a field (the old jetpack behaviour).</summary>
    private bool HoverThrust(float dt)
    {
        if (!_stats.UseJetpack(jetpackCostPerSecond * dt)) return false;
        _rb.AddForce(_planetUp * jetpackForce, ForceMode.Acceleration);
        return true;
    }

    /// <summary>Braking thrust against the direction of travel while being captured, to land softly.</summary>
    private bool RetroThrust(float dt)
    {
        Vector3 v = _rb.linearVelocity;
        if (v.magnitude < 6f) return HoverThrust(dt);
        if (!_stats.UseJetpack(cruiseFuelPerSecond * 1.6f * dt)) return false;
        _rb.AddForce(-v.normalized * retroBrake, ForceMode.Acceleration);
        return true;
    }

    /// <summary>Open-space correction: steer toward where you push / look, costs fuel continuously.</summary>
    private bool CruiseThrust(float dt)
    {
        if (!_stats.UseJetpack(cruiseFuelPerSecond * dt)) return false;
        Transform aim = cameraTarget != null ? cameraTarget : transform;
        Vector3 dir = aim.forward * _moveInput.y + aim.right * _moveInput.x;
        if (dir.sqrMagnitude < 0.01f) dir = aim.forward;
        _rb.AddForce(dir.normalized * cruiseThrust, ForceMode.Acceleration);
        return true;
    }

    // ── Sonic dash ────────────────────────────────────────────────────────

    private void TickSonic(float dt)
    {
        if (_sonicActive)
        {
            bool arrived = _gravity.Planet == _sonicTarget && _gravity.Gravity.sqrMagnitude > 0.25f && Time.time - _launchTime > 0.5f;
            if (_sonicTarget == null || arrived || Time.time - _launchTime > sonicTimeout || _phase == JetpackPhase.Idle || IsStunned) EndSonic();
            else
            {
                // A straight line to the planet: the velocity is turned toward its centre and gravity stays off
                Vector3 toTarget = (_sonicTarget.transform.position - _rb.position).normalized;
                float lift = 1f - Mathf.Clamp01((Time.time - _launchTime) / 0.7f);             // climb away from the ground first
                Vector3 desired = (toTarget + _planetUp * (0.55f * lift)).normalized * _sonicSpeed;
                _rb.linearVelocity = Vector3.Lerp(_rb.linearVelocity, desired, 1f - Mathf.Exp(-sonicTurn * dt));
                _gravityOffUntil = Time.time + 0.25f;
            }
        }
        _sonicBlend = Mathf.MoveTowards(_sonicBlend, _sonicActive ? 1f : 0f, dt * (_sonicActive ? 4f : 2.2f));
    }

    private void EndSonic() { _sonicActive = false; _sonicTarget = null; }

    /// <summary>
    /// The planet's pull slows an arriving dash for free, and a soft-landing profile limits how fast you may sink toward the
    /// surface for your height (like a retro-burn you don't have to pay for). Bracing for a forced landing (crouch) switches
    /// the assist off so you hit hard and make the shockwave.
    /// </summary>
    private void CaptureBraking(float dt)
    {
        Vector3 v = _rb.linearVelocity;
        float speed = v.magnitude;
        if (speed > captureSpeed)
            _rb.AddForce(-v.normalized * ((speed - captureSpeed) * captureDrag), ForceMode.Acceleration);

        if (_crouchHeld || _currentPlanet == null) return;
        float altitude = PlanetBody.Altitude(transform.position, _currentPlanet);
        float down = -Vector3.Dot(v, _planetUp);
        if (down <= 0f) return;
        float allowed = Mathf.Sqrt(2f * softLandingDecel * Mathf.Max(0f, altitude - 1.5f)) + 4f;
        if (down > allowed) _rb.AddForce(_planetUp * ((down - allowed) * 9f), ForceMode.Acceleration);
    }

    private void NotifyNotReady()
    {
        if (Time.time - _blockedMsgAt < 1.6f || HUD.Instance == null) return;
        _blockedMsgAt = Time.time;
        if (Time.time < _launchCooldownUntil) HUD.Instance.ShowEvent("JETPACK ENFRIANDO", 1.2f);
        else if (_stats.jetpackEnergy < minFuelToCharge) HUD.Instance.ShowEvent("JETPACK SIN COMBUSTIBLE — ESPERA A QUE RECARGUE", 1.5f);
    }

    private void CancelCharge()
    {
        _phase = JetpackPhase.Idle;
        _charge = 0f;
    }

    // ── Aiming ────────────────────────────────────────────────────────────

    private Vector3 CameraForward() => cameraTarget != null ? cameraTarget.forward : transform.forward;

    /// <summary>The planet (other than the current one) closest to the crosshair, within the assist angle.</summary>
    private void UpdateAimTarget()
    {
        _aimTarget = null;
        if (GravitySystem.Instance == null) return;
        Vector3 origin = transform.position;
        Vector3 fwd = CameraForward();
        float best = aimAssistAngle;
        foreach (var planet in GravitySystem.Instance.Planets)
        {
            if (planet == _currentPlanet) continue;
            Vector3 to = planet.transform.position - origin;
            float dist = to.magnitude;
            if (dist < 1f) continue;
            float apparent = Mathf.Asin(Mathf.Clamp01(planet.radius / dist)) * Mathf.Rad2Deg;       // the planet's own angular size counts
            float off = Vector3.Angle(fwd, to) - apparent;
            if (off < best) { best = off; _aimTarget = planet; }
        }
    }

    /// <summary>Direction the launch would go: the camera's, pulled toward the selected planet.</summary>
    public Vector3 LaunchDirection()
    {
        Vector3 dir = CameraForward();
        if (_aimTarget != null)
            dir = Vector3.Slerp(dir, (_aimTarget.transform.position - transform.position).normalized, 0.8f);
        float up = Vector3.Dot(dir, _planetUp);
        if (up < 0.24f) dir = (dir + _planetUp * (0.24f - up)).normalized;       // never into the ground (and clear of low obstacles)
        return dir.normalized;
    }

    public float LaunchSpeedFor(float charge) => Mathf.Lerp(launchSpeedMin, launchSpeedMax, Mathf.Clamp01(charge));

    /// <summary>Predicted flight path for the current charge (dotted line while charging). Returns true if it ends on a planet.</summary>
    public bool PredictLaunch(List<Vector3> points, float charge, float maxTime = 9f)
    {
        points.Clear();
        if (GravitySystem.Instance == null) return false;
        Vector3 pos = transform.position + _planetUp * 0.4f;
        Vector3 vel = LaunchDirection() * LaunchSpeedFor(charge) + Vector3.ProjectOnPlane(_rb.linearVelocity, _planetUp) * 0.3f;
        const float step = 0.1f;
        float t = 0f;
        points.Add(pos);
        while (t < maxTime)
        {
            Vector3 g = t < launchGravityOff ? Vector3.zero : GravitySystem.Instance.GetGravityVector(pos);
            vel += g * step;
            pos += vel * step;
            t += step;
            points.Add(pos);
            foreach (var p in GravitySystem.Instance.Planets)
                if (t > 0.5f && Vector3.Distance(pos, p.transform.position) <= p.radius) return true;
        }
        return false;
    }

    // ── Launch ────────────────────────────────────────────────────────────

    private void Launch()
    {
        float fuel = Mathf.Lerp(launchFuelMin, launchFuelMax, _charge);
        if (_stats.jetpackEnergy < fuel * 0.5f || !_stats.UseJetpack(fuel)) { CancelCharge(); return; }

        Vector3 dir = LaunchDirection();
        float speed = LaunchSpeedFor(_charge);
        _rb.position += _planetUp * 1.1f;                      // clear the ground so the ground check lets go
        _rb.linearVelocity = dir * speed + Vector3.ProjectOnPlane(_rb.linearVelocity, _planetUp) * 0.3f;
        _isGrounded = false;
        _coyoteTimer = 0f;

        _phase = JetpackPhase.Launch;
        _launchTime = Time.time;
        _launchPlanet = _currentPlanet;
        _leftHomeField = false;
        _sonicTarget = _aimTarget;
        _sonicActive = _sonicTarget != null;
        _sonicSpeed = Mathf.Lerp(sonicSpeedMin, sonicSpeedMax, _charge);
        // With a target the dash flies straight (gravity off until the planet captures us); without one the home planet
        // must not drag us back down either, so the pull stays off while we leave its field.
        _gravityOffUntil = Time.time + (_sonicActive ? sonicTimeout : Mathf.Max(launchGravityOff, 2.5f));
        if (_sonicActive) { _rb.linearVelocity = dir * _sonicSpeed; SonicStarted?.Invoke(); }
        _dashLockTimer = Mathf.Max(_dashLockTimer, 0.6f);      // surface-speed damping must not eat the impulse
        _launchCooldownUntil = Time.time + launchCooldown;
        _charge = 0f;
        _requireRelease = false;

        NoiseSystem.Emit(transform.position, Loudness.JetpackTakeoff, gameObject);
        CameraRig.AddTrauma(0.35f);
        JetpackLaunched?.Invoke();
    }

    // ── Landing ───────────────────────────────────────────────────────────

    /// <summary>Called from the ground check when we touch down after being airborne.</summary>
    private void OnTouchdown(float impact)
    {
        bool wasFlight = IsFlying;
        // Just after take-off the ground check still sees the ground below us (especially on a shallow launch): that is
        // not a landing, or the flight would be cancelled the instant it starts.
        if (_phase == JetpackPhase.Launch && Time.time - _launchTime < launchGroundGrace) return;
        if (wasFlight) { _phase = JetpackPhase.Idle; EndSonic(); _requireRelease = true; _launchCooldownUntil = Mathf.Min(_launchCooldownUntil, Time.time + 1f); }
        if (impact < 4f) return;

        bool hard = impact >= hardLandingSpeed;
        bool braced = _crouchHeld && impact >= hardLandingSpeed * 0.7f;     // crouching on purpose = forced landing
        bool softenedByThrust = Time.time - _lastThrustTime < 0.4f;

        if (braced) { Shockwave(); HardLanded?.Invoke(impact, true, false); return; }
        if (hard && !(softenedByThrust && impact < hardLandingSpeed * 1.5f))
        {
            _stunUntil = Time.time + stunSeconds;
            if (HUD.Instance != null) HUD.Instance.ShowEvent("ATERRIZAJE DESCUIDADO", 1.6f);
            CameraRig.AddTrauma(0.5f);
            HardLanded?.Invoke(impact, false, true);
            return;
        }
        if (wasFlight) _recoverUntil = Time.time + 0.2f;      // normal landing: a small recovery, then carry on
    }

    /// <summary>Forced landing: a shockwave that damages and pushes away everyone nearby.</summary>
    private void Shockwave()
    {
        Vector3 centre = transform.position;
        var done = new HashSet<IDamageable>();
        foreach (var col in Physics.OverlapSphere(centre, forcedLandRadius, ~0, QueryTriggerInteraction.Ignore))
        {
            if (col.transform.IsChildOf(transform)) continue;
            var target = col.GetComponentInParent<IDamageable>();
            float dist = Vector3.Distance(centre, col.ClosestPoint(centre));
            if (target != null && !ReferenceEquals(target, _stats) && done.Add(target))
            {
                float falloff = 1f - Mathf.Clamp01(dist / forcedLandRadius) * 0.6f;
                target.RegisterHit(col.ClosestPoint(centre));
                target.TakeDamage(forcedLandDamage * falloff, _stats);
            }
            var body = col.attachedRigidbody;
            if (body != null && body != _rb) body.AddExplosionForce(forcedLandForce, centre, forcedLandRadius, 0.4f, ForceMode.Impulse);
        }
        DeathBurstFx.Play(centre, new Color(1f, 0.55f, 0.1f), _planetUp);
        NoiseSystem.Emit(centre, Loudness.Explosion, gameObject);
        CameraRig.AddTrauma(0.7f);
        if (HUD.Instance != null) HUD.Instance.ShowEvent("ATERRIZAJE FORZADO", 1.4f);
    }

    // ── Noise from moving ─────────────────────────────────────────────────

    private void EmitFootsteps(float dt)
    {
        _footstepTimer -= dt;
        if (_footstepTimer > 0f || !_isGrounded) return;
        float speed = PlanarSpeed;
        if (speed < 0.8f) return;
        _footstepTimer = 0.5f;
        NoiseSystem.Emit(transform.position, IsRunning ? Loudness.Run : Loudness.Walk, gameObject);
    }
}
}
