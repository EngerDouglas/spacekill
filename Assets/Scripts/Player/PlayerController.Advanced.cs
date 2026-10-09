using UnityEngine;
using UnityEngine.InputSystem;

namespace OrbitRush
{

/// <summary>
/// Advanced planet movement: long jump, Mario-style air spin and orbit assist (circling a small planet when
/// jumping fast sideways).
/// </summary>
public partial class PlayerController
{
    [Header("Long Jump")]
    [Tooltip("Run + crouch (Z / pad left bumper) + jump: lower but much faster and longer.")]
    public float longJumpUpFraction = 0.65f;
    public float longJumpForwardSpeed = 11f;

    [Header("Air Spin")]
    [Tooltip("Press jump again in the air: slows the fall for a moment and lets you correct your course.")]
    public float airSpinDuration = 0.4f;
    [Range(0f, 1f)] public float airSpinFallCut = 0.75f;
    [Range(0f, 1f)] public float airSpinGravity = 0.3f;
    public float airSpinSteerSpeed = 7f;

    [Header("Orbit")]
    [Tooltip("Sideways speed (as a fraction of the circular-orbit speed) from which you start circling instead of falling.")]
    [Range(0.2f, 1f)] public float orbitMinSpeedFraction = 0.55f;
    [Tooltip("Above this fraction of the circular-orbit speed you are too fast to be held: you leave the planet.")]
    [Range(1f, 3f)] public float orbitMaxSpeedFraction = 1.35f;
    [Tooltip("Orbits fade out after this many seconds so nobody circles forever dodging attacks.")]
    public float orbitMaxSeconds = 2.5f;
    public float orbitMinAltitude = 2.5f;

    private bool _crouchPolled, _crouchSim;
    private bool _crouchHeld => _crouchPolled || _crouchSim;
    private bool _spinQueued, _spinUsed;
    private float _spinTimer;
    private bool _orbiting, _orbitSpent;
    private float _orbitTimer;

    /// <summary>Fired when the air spin starts (the character model does a full turn).</summary>
    public event System.Action AirSpun;
    /// <summary>True while circling a planet (see orbit rules above).</summary>
    public bool IsOrbiting => _orbiting;
    public bool IsCrouching => _crouchHeld;

    private void PollAdvancedInput()
    {
        var kb = Keyboard.current; var gp = Gamepad.current;
        _crouchPolled = (kb != null && kb.zKey.isPressed) || (gp != null && gp.leftShoulder.isPressed);
    }

    public void SimulateCrouch(bool pressed) => _crouchSim = pressed;

    /// <summary>Test hook: turn the player and camera so the crosshair points at a world position.</summary>
    public void DebugAimAt(Vector3 worldPoint)
    {
        Vector3 dir = (worldPoint - transform.position).normalized;
        Vector3 flat = Vector3.ProjectOnPlane(dir, _planetUp);
        if (flat.sqrMagnitude > 1e-4f)
        {
            var rot = Quaternion.LookRotation(flat.normalized, _planetUp);
            _rb.rotation = rot; transform.rotation = rot;
        }
        _pitch = -Mathf.Asin(Mathf.Clamp(Vector3.Dot(dir, _planetUp), -1f, 1f)) * Mathf.Rad2Deg;
        if (cameraTarget != null) cameraTarget.localRotation = Quaternion.Euler(_pitch, 0f, 0f);
    }

    /// <summary>Called when a ground jump is applied: turns it into a long jump when running + crouching.</summary>
    private void ApplyLongJumpBoost()
    {
        if (!(_sprintHeld && _crouchHeld && _moveInput.sqrMagnitude > 0.1f)) return;
        Vector3 up = _planetUp;
        _rb.linearVelocity -= up * (Vector3.Dot(_rb.linearVelocity, up) * (1f - longJumpUpFraction));       // lower arc
        Vector3 fwd = Vector3.ProjectOnPlane(transform.right * _moveInput.x + transform.forward * _moveInput.y, up).normalized;
        _rb.AddForce(fwd * longJumpForwardSpeed, ForceMode.VelocityChange);
    }

    private void HandleAirSpin()
    {
        bool wanted = _spinQueued;
        _spinQueued = false;
        if (_spinTimer > 0f) _spinTimer -= Time.fixedDeltaTime;

        float upSpeed = Vector3.Dot(_rb.linearVelocity, _planetUp);
        if (wanted && !_isGrounded && _coyoteTimer <= 0f && !_spinUsed && upSpeed < 3f)
        {
            _spinUsed = true;
            _spinTimer = airSpinDuration;
            if (upSpeed < 0f) _rb.linearVelocity -= _planetUp * (upSpeed * airSpinFallCut);   // soften the fall
            AirSpun?.Invoke();
        }

        if (_spinTimer > 0f && _moveInput.sqrMagnitude > 0.01f)
        {
            // Course correction: a gentle shove toward where you press
            Vector3 wish = Vector3.ProjectOnPlane(transform.right * _moveInput.x + transform.forward * _moveInput.y, _planetUp).normalized;
            _rb.AddForce(wish * (airSpinSteerSpeed * Time.fixedDeltaTime * 4f), ForceMode.VelocityChange);
        }
    }

    /// <summary>Adjusts gravity for spin and orbit. Returns the scale to multiply the gravity vector by.</summary>
    private float AdjustGravityForMovement(ref Vector3 gravity, float scale)
    {
        if (_spinTimer > 0f) scale *= airSpinGravity;
                UpdateOrbit(ref gravity);
        return scale;
    }

    private void UpdateOrbit(ref Vector3 gravity)
    {
        float dt = Time.fixedDeltaTime;
        var planet = _currentPlanet;
        bool candidate = planet != null && !_isGrounded && !_orbitSpent && gravity.sqrMagnitude > 0.01f
                         && planet.shape == PlanetGravity.GravityShape.Sphere;
        if (!candidate) { if (_orbiting) EndOrbit(false); return; }

        Vector3 toCentre = planet.transform.position - transform.position;
        float r = toCentre.magnitude;
        float altitude = r - planet.radius;
        Vector3 up = _planetUp;
        Vector3 v = _rb.linearVelocity;
        Vector3 tangential = Vector3.ProjectOnPlane(v, up);
        float vt = tangential.magnitude;
        float g = gravity.magnitude;
        float vCircular = Mathf.Sqrt(g * Mathf.Max(r, 1f));

        if (!_orbiting)
        {
            bool fastEnough = vt >= vCircular * orbitMinSpeedFraction && vt <= vCircular * orbitMaxSpeedFraction;
            if (fastEnough && altitude > orbitMinAltitude)
            {
                _orbiting = true; _orbitTimer = 0f;
                if (HUD.Instance != null) HUD.Instance.ShowEvent("EN ÓRBITA", 1.2f);
            }
            else return;
        }

        _orbitTimer += dt;
        bool tooSlow = vt < vCircular * orbitMinSpeedFraction * 0.8f;
        if (_orbitTimer >= orbitMaxSeconds || tooSlow || altitude < orbitMinAltitude * 0.4f) { EndOrbit(true); return; }

        // Hold altitude: the pull becomes exactly the centripetal force this speed needs (v^2/r), and the speed
        // bleeds off so the orbit decays into a landing after a lap or two.
        float needed = Mathf.Clamp(vt * vt / Mathf.Max(r, 1f), g * 0.25f, g * 1.2f);
        gravity = toCentre.normalized * needed;
        float bleed = vt * dt / Mathf.Max(0.5f, orbitMaxSeconds - _orbitTimer);
        _rb.linearVelocity = v - tangential.normalized * bleed;
        // Radial speed is damped so the path stays a circle
        float radial = Vector3.Dot(v, up);
        _rb.linearVelocity -= up * (radial * Mathf.Clamp01(4f * dt));
    }

    private void EndOrbit(bool spent)
    {
        _orbiting = false;
        if (spent) _orbitSpent = true;       // one orbit per flight: you must touch ground (or another planet) again
    }

    private void ResetAirState()
    {
        _spinUsed = false; _spinTimer = 0f;
        _orbitSpent = false; _orbiting = false;
    }
}
}
