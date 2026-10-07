using UnityEngine;
using UnityEngine.InputSystem;

namespace OrbitRush
{

/// <summary>
/// Planetary player controller.
/// - Gravity always pulls toward the dominant planet center.
/// - Player body aligns so feet point to planet surface.
/// - Yaw rotates around the planet-up axis; pitch only tilts the camera.
///
/// Movement feel:
/// - Ground speed accelerates and decelerates progressively (no instant start/stop).
/// - In the air you keep your momentum (essential for hopping between planets) and steer with a capped
///   "air acceleration" instead of being braked to a stop.
/// - Jump has coyote time (jump a moment after leaving an edge), input buffering (press a moment before
///   landing), variable height (release early for a short hop) and a snappier fall.
/// - The collider is frictionless so you slide along walls and buildings instead of sticking to them.
/// - Jetpack thrust ramps up and down; sprint and dash add a field-of-view kick.
/// - Look input is polled from the mouse / right stick every frame with light smoothing.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(PlayerStats))]
public partial class PlayerController : MonoBehaviour
{
    [Header("Movement")]
    [Tooltip("Normal walking speed (m/s).")]
    public float walkSpeed = 4.5f;
    [Tooltip("Running speed while the Sprint input (Shift / gamepad) is held (m/s).")]
    public float moveSpeed = 8f;
    [Tooltip("Unused since walking and running became separate speeds (kept so saved scenes still load).")]
    public float sprintMultiplier = 1.6f;
    public float jumpForce = 6.5f;

    [Header("Movement Feel")]
    [Tooltip("How fast you reach full speed on the ground (m/s²). Lower = floatier, higher = snappier.")]
    public float groundAcceleration = 36f;
    [Tooltip("How fast you slow down on the ground when you let go (m/s²).")]
    public float groundDeceleration = 40f;
    [Tooltip("Steering strength in the air (m/s²). Momentum is kept; this only adds speed toward where you press.")]
    public float airAcceleration = 16f;
    [Tooltip("Top speed you can reach by air steering, as a multiple of your current run speed.")]
    public float airSpeedMultiplier = 1.1f;
    [Tooltip("Slows you slightly when coasting inside a planet's atmosphere (scaled by the planet's air resistance).")]
    public float atmosphereDrag = 1.2f;

    [Header("Jump Feel")]
    [Tooltip("Seconds after walking off an edge during which you can still jump.")]
    public float coyoteTime = 0.12f;
    [Tooltip("Seconds a jump press is remembered before landing.")]
    public float jumpBufferTime = 0.14f;
    [Tooltip("Releasing jump early cuts the rise to this fraction of the speed (short hop).")]
    [Range(0.1f, 1f)] public float jumpCutFactor = 0.45f;
    [Tooltip("Extra gravity while falling near a planet, for a snappier landing.")]
    public float fallGravityMultiplier = 1.5f;

    [Header("Alignment")]
    public float alignSpeed = 8f;       // how fast feet snap to planet surface

    [Header("Look")]
    public float lookSensitivity = 0.15f;
    [Tooltip("Smoothing time for mouse look in seconds. 0 = raw. Keep it small — it adds a little latency.")]
    [Range(0f, 0.08f)] public float lookSmoothing = 0.02f;
    [Tooltip("Right-stick turn speed in degrees per second at full deflection.")]
    public float stickLookSpeed = 170f;
    public Transform cameraTarget;      // empty child at eye level
    [Tooltip("Starting camera pitch in degrees (positive = looking down). Standing on a small planet, a level " +
              "(0°) camera shows mostly sky since the ground curves away — angling down by default keeps the " +
              "horizon and crosshair over actual terrain instead of empty space.")]
    public float defaultPitch = 15f;

    [Header("Camera Kick")]
    [Tooltip("Extra field of view (degrees) while sprinting. Applied by WeaponAim.")]
    public float sprintFovBoost = 7f;
    [Tooltip("Extra field of view (degrees) for a moment after a dash.")]
    public float dashFovBoost = 10f;

    [Header("Jetpack")]
    public float jetpackForce = 15f;
    public float jetpackCostPerSecond = 25f;
    [Tooltip("Seconds for thrust to ramp from 0 to full (and back down) so it doesn't kick like a switch.")]
    public float jetpackRamp = 0.12f;

    [Header("Dash")]
    [Tooltip("Left Alt (keyboard) / D-pad Up (gamepad). On the ground dashes along your move direction; " +
             "in the air dashes where the camera looks, to hop between planets.")]
    public float dashImpulse = 16f;
    public float dashStaminaCost = 30f;
    public float dashCooldown = 1.2f;
    [Tooltip("How long normal movement control is suspended after a dash (seconds) so the surface-speed " +
             "damping doesn't cancel the impulse. Airborne dashes use 2x this.")]
    public float dashControlLockout = 0.25f;

    [Header("Ground Check")]
    public float groundCheckDistance = 1.3f;
    public LayerMask groundMask;

    private Rigidbody _rb;
    private PlayerStats _stats;
    private PlayerInput _playerInput;
    private CapsuleCollider _capsule;
    private PlanetGravity _currentPlanet;
    private readonly GravityBlender _gravity = new GravityBlender();     // eases between overlapping gravity fields (0.5 s)

    // Derived from the CapsuleCollider's actual size (scaled by the transform)
    // instead of hardcoded numbers, so ground-check keeps working correctly
    // if the character is resized — recomputed once in Awake since scale
    // doesn't change at runtime in this game.
    private float _capsuleHalfHeight = 1f;
    private float _capsuleRadius = 0.5f;

    private Vector2 _moveInput;
    private Vector2 _yawAccumulator;   // look yaw accumulated between FixedUpdates — applied through the Rigidbody
    private Vector2 _lookSmoothed;
    private bool _jumpHeld;
    private bool _jetpackHeld;
    private bool _sprintHeld;

    private float _jumpBufferTimer;
    private float _coyoteTimer;
    private bool _jumpedRecently;      // true from the jump impulse until we start falling (for the short-hop cut)

    private bool _dashQueued;
    private float _dashCooldownTimer;
    private float _dashLockTimer;
    private float _dashFovTimer;

    private float _jetpackThrust;      // 0..1 ramp
    private bool _jetpacking;
    private float _sprintAmount;       // 0..1
    private float _fovBoost;

    private bool _isGrounded;
    private bool _wasGrounded;
    private Vector3 _planetUp = Vector3.up;
    private float _pitch;

    /// <summary>Fired when the player touches ground after being airborne. Argument = impact speed (m/s).</summary>
    public event System.Action<float> Landed;
    /// <summary>Fired when a jump impulse is applied.</summary>
    public event System.Action Jumped;

    void Awake()
    {
        transform.localScale = Vector3.one * CharacterScale.Player;     // smaller character, bigger-looking map (set before the capsule is measured below)
        _rb = GetComponent<Rigidbody>();
        _stats = GetComponent<PlayerStats>();
        _playerInput = GetComponent<PlayerInput>();
        var capsules = GetComponents<CapsuleCollider>();
        _capsule = capsules.Length > 0 ? capsules[0] : null;
        if (capsules.Length > 1)
        {
            Debug.LogWarning($"[PlayerController] {name} has {capsules.Length} CapsuleColliders — only the " +
                "first is used for grounding math. Extra overlapping colliders on one Rigidbody cause duplicate " +
                "PhysX contact manifolds per ground contact, a known source of physics jitter. Remove the extras.", this);
        }
        _pitch = defaultPitch;

        _rb.useGravity = false;
        // No FreezeRotation — we handle rotation manually via MoveRotation.
        // High angular drag prevents physics from spinning us on collisions.
        _rb.angularDamping = 20f;
        _rb.interpolation = RigidbodyInterpolation.Interpolate;
        _rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;   // no tunnelling through thin walls when dashing

        if (_capsule != null)
        {
            Vector3 scale = transform.lossyScale;
            _capsuleHalfHeight = _capsule.height * 0.5f * scale.y;
            _capsuleRadius = _capsule.radius * Mathf.Max(scale.x, scale.z);

            // Frictionless: speed is controlled by the code below, so friction would only make the player
            // stick to walls, building edges and cover when pressing against them.
            _capsule.sharedMaterial = new PhysicsMaterial("PlayerSlide")
            {
                dynamicFriction = 0f,
                staticFriction = 0f,
                bounciness = 0f,
                frictionCombine = PhysicsMaterialCombine.Minimum,
                bounceCombine = PhysicsMaterialCombine.Minimum,
            };
        }

        Cursor.lockState = CursorLockMode.Locked;
    }

    // ── Input System callbacks (PlayerInput → Send Messages) ──────────────

    public void OnMove(InputValue v) => _moveInput = v.Get<Vector2>();

    // Look is polled directly from the devices in Update (see PollLook) — a message-driven value would
    // keep the last delta applied after the mouse stops, and can't smooth or support sticks properly.
    public void OnLook(InputValue v) { }

    public void OnJump(InputValue v)
    {
        _jumpHeld = v.isPressed;
        if (v.isPressed) { _jumpBufferTimer = jumpBufferTime; _spinQueued = true; GrabMash(); }     // remembered for a short while, not forever
    }
    public void OnJetpack(InputValue v) => _jetpackHeld = v.isPressed;
    public void OnSprint(InputValue v)  => _sprintHeld  = v.isPressed;

    /// <summary>
    /// Adds a sudden speed along <paramref name="worldDirection"/> (dodge, melee lunge) and suspends normal
    /// movement control for <paramref name="lockSeconds"/> so ground damping doesn't cancel it.
    /// </summary>
    public void Burst(Vector3 worldDirection, float speed, float lockSeconds)
    {
        if (worldDirection.sqrMagnitude < 0.0001f) return;
        _rb.AddForce(worldDirection.normalized * speed, ForceMode.VelocityChange);
        _dashLockTimer = Mathf.Max(_dashLockTimer, lockSeconds);
    }

    /// <summary>Move input in the player's own frame (x = right, y = forward).</summary>
    public Vector2 MoveInput => _moveInput;

    // ── Scripted input (used by MovementSelfTest; harmless otherwise) ─────

    public void SimulateMove(Vector2 move) => _moveInput = move;
    public void SimulateJump(bool pressed) { _jumpHeld = pressed; if (pressed) _jumpBufferTimer = jumpBufferTime; }
    public void SimulateSprint(bool pressed) => _sprintHeld = pressed;

    /// <summary>False while the menu / pause screen has disabled the player's input or the game is paused.</summary>
    private bool InputActive => Time.timeScale > 0f && (_playerInput == null || _playerInput.enabled);

    // ──────────────────────────────────────────────────────────────────────

    void FixedUpdate()
    {
        UpdateCurrentPlanet();
        CheckGrounded();
        UpdateTimers();
        ApplyGravity();
        HandleYaw();
        AlignToPlanet();
        HandleMovement();
        HandleJetpackSystem();
        HandleDash();
        HandleJump();
        HandleAirSpin();
    }

    void Update()
    {
        if (InputActive)
        {
            PollLook();
            PollDashInput();
            PollAdvancedInput();
        }
        else
        {
            _lookSmoothed = Vector2.zero;
            _dashQueued = false;
        }

        HandlePitch();
        if (_dashCooldownTimer > 0f) _dashCooldownTimer -= Time.deltaTime;
        UpdateFovBoost();
    }

    // ── Look ──────────────────────────────────────────────────────────────

    private float _pitchDelta;   // pitch degrees to apply this frame (already multiplied by sensitivity)

    private void PollLook()
    {
        Vector2 raw = Vector2.zero;

        var mouse = Mouse.current;
        if (mouse != null && Cursor.lockState == CursorLockMode.Locked)
            raw += mouse.delta.ReadValue();

        var pad = Gamepad.current;
        if (pad != null)
        {
            Vector2 stick = pad.rightStick.ReadValue();
            float mag = stick.magnitude;
            if (mag > 0.15f)
            {
                // Rescale the dead zone away and apply a curve: fine aiming near the centre, full speed at the edge
                float t = Mathf.Clamp01((mag - 0.15f) / 0.85f);
                stick = stick / mag * Mathf.Pow(t, 1.7f);
                raw += stick * (stickLookSpeed * Time.deltaTime / Mathf.Max(0.0001f, lookSensitivity));
            }
        }

        // Exponential smoothing: the sum of all deltas is preserved, just spread over a few milliseconds.
        float k = lookSmoothing <= 0.0001f ? 1f : 1f - Mathf.Exp(-Time.deltaTime / lookSmoothing);
        _lookSmoothed = Vector2.Lerp(_lookSmoothed, raw, k);

        _yawAccumulator += new Vector2(_lookSmoothed.x, 0f);
        _pitchDelta += _lookSmoothed.y * lookSensitivity;
    }

    // Read directly from the devices (not a PlayerInput action) so no changes to the
    // .inputactions asset are needed. Latched here, consumed in FixedUpdate.
    private void PollDashInput()
    {
        var kb = Keyboard.current;
        var gp = Gamepad.current;
        if ((kb != null && kb.leftAltKey.wasPressedThisFrame) ||
            (gp != null && gp.dpad.up.wasPressedThisFrame))
            _dashQueued = true;
    }

    // ── Timers / state ────────────────────────────────────────────────────

    private void UpdateTimers()
    {
        float dt = Time.fixedDeltaTime;

        if (_isGrounded) _coyoteTimer = coyoteTime;
        else _coyoteTimer = Mathf.Max(0f, _coyoteTimer - dt);

        _jumpBufferTimer = Mathf.Max(0f, _jumpBufferTimer - dt);

        // Smooth sprint amount (drives the FOV kick) — only counts while actually moving fast.
        bool sprinting = _sprintHeld && PlanarSpeed > walkSpeed * 1.3f;
        _sprintAmount = Mathf.MoveTowards(_sprintAmount, sprinting ? 1f : 0f, dt * 4f);
    }

    private void UpdateFovBoost()
    {
        if (_dashFovTimer > 0f) _dashFovTimer -= Time.deltaTime;
        float dashK = Mathf.Clamp01(_dashFovTimer / 0.35f);
        float target = _sprintAmount * sprintFovBoost + dashK * dashFovBoost + SonicBlend * sonicFovBoost;
        _fovBoost = Mathf.Lerp(_fovBoost, target, 1f - Mathf.Exp(-9f * Time.deltaTime));
    }

    // ── Dash ──────────────────────────────────────────────────────────────

    private void HandleDash()
    {
        if (_dashLockTimer > 0f) _dashLockTimer -= Time.fixedDeltaTime;
        if (!_dashQueued) return;
        _dashQueued = false;

        if (_dashCooldownTimer > 0f || IsStunned) return;
        if (_phase == JetpackPhase.Cruise && !_stats.UseJetpack(boostFuel)) return;     // boosting in space burns fuel
        if (!_stats.UseStamina(dashStaminaCost)) return;

        Vector3 dir;
        if (!_isGrounded && cameraTarget != null)
        {
            dir = cameraTarget.forward;   // airborne: dash where you're looking
        }
        else
        {
            dir = Vector3.ProjectOnPlane(transform.right * _moveInput.x + transform.forward * _moveInput.y, _planetUp);
            if (dir.sqrMagnitude < 0.01f) dir = transform.forward;
        }

        _rb.AddForce(dir.normalized * dashImpulse, ForceMode.VelocityChange);
        _dashCooldownTimer = dashCooldown;
        _dashLockTimer = _isGrounded ? dashControlLockout : dashControlLockout * 2f;
        _dashFovTimer = 0.35f;
    }

    // ── Planet & gravity ──────────────────────────────────────────────────

    private void UpdateCurrentPlanet()
    {
        _gravity.Update(transform.position, Time.fixedDeltaTime);
        if (_gravity.Planet != null) { _currentPlanet = _gravity.Planet; _planetUp = _gravity.Up; }
    }

    private void ApplyGravity()
    {
        Vector3 gravity = _gravity.Gravity;
        float scale = _stats.gravityResistance;

        // Snappier fall: heavier gravity while descending close to a planet (not while jetpacking, and not
        // out in space between planets where it would just yank you toward the nearest one).
        if (_currentPlanet != null && !_isGrounded && !_jetpacking)
        {
            float upSpeed = Vector3.Dot(_rb.linearVelocity, _planetUp);
            float altitude = PlanetBody.Altitude(transform.position, _currentPlanet);
            if (upSpeed < 0f && altitude < _currentPlanet.radius)
                scale *= fallGravityMultiplier;
        }

        scale = AdjustGravityForMovement(ref gravity, scale);
        _rb.AddForce(gravity * scale, ForceMode.Acceleration);
    }

    // ── Rotation ──────────────────────────────────────────────────────────

    /// <summary>
    /// Smoothly rotates the player so transform.up matches _planetUp,
    /// while keeping the current horizontal facing direction.
    /// </summary>
    private void AlignToPlanet()
    {
        PlanetBody.AlignUpright(_rb, transform.forward, _planetUp, alignSpeed * (_phase == JetpackPhase.Captured ? 1.8f : 1f), Time.fixedDeltaTime);
    }

    /// <summary>
    /// Yaw rotates the player body around the planet-up axis. Runs in
    /// FixedUpdate and goes through the Rigidbody (MoveRotation), same as
    /// AlignToPlanet — both must drive rotation the same way, or the two
    /// fight each other and the camera visibly vibrates whenever the player
    /// is both moving and turning.
    /// </summary>
    private void HandleYaw()
    {
        float yawDelta = _yawAccumulator.x * lookSensitivity;
        _yawAccumulator = Vector2.zero;

        if (Mathf.Abs(yawDelta) < 0.0001f) return;

        Quaternion yawRotation = Quaternion.AngleAxis(yawDelta, _planetUp);
        _rb.MoveRotation(yawRotation * _rb.rotation);
    }

    /// <summary>Pitch only tilts the camera target child — not Rigidbody-driven, so it's fine to update every render frame.</summary>
    private void HandlePitch()
    {
        _pitch = Mathf.Clamp(_pitch - _pitchDelta, -80f, 80f);
        _pitchDelta = 0f;
        if (cameraTarget != null)
            cameraTarget.localRotation = Quaternion.Euler(_pitch, 0f, 0f);
    }

    // ── Movement ──────────────────────────────────────────────────────────

    private void HandleMovement()
    {
        if (_dashLockTimer > 0f) return;   // let the dash impulse carry through

        float inputMag = Mathf.Clamp01(_moveInput.magnitude);
        bool hasInput = inputMag > 0.05f;
        float speed = (_sprintHeld ? moveSpeed : walkSpeed) * MoveSpeedScale;
        if (IsStunned) hasInput = false;

        // Wish direction along the planet surface
        Vector3 wish = Vector3.ProjectOnPlane(transform.right * _moveInput.x + transform.forward * _moveInput.y, _planetUp);
        wish = wish.sqrMagnitude > 0.0001f ? wish.normalized : Vector3.zero;

        Vector3 planar = Vector3.ProjectOnPlane(_rb.linearVelocity, _planetUp);
        float dt = Time.fixedDeltaTime;

        if (_isGrounded)
        {
            // Progressive ramp up to the target velocity — and a progressive stop when you let go.
            Vector3 target = hasInput ? wish * (speed * inputMag) : Vector3.zero;
            float accel = hasInput ? groundAcceleration : groundDeceleration;
            Vector3 next = Vector3.MoveTowards(planar, target, accel * dt);
            _rb.AddForce(next - planar, ForceMode.VelocityChange);
        }
        else
        {
            if (hasInput)
            {
                // Steer in the air: add speed toward where you press, but only up to the air cap along that
                // direction. Momentum from a jump, dash or jetpack is never braked.
                float cap = speed * airSpeedMultiplier;
                float current = Vector3.Dot(planar, wish);
                float add = Mathf.Clamp(cap * inputMag - current, 0f, airAcceleration * dt);
                _rb.AddForce(wish * add, ForceMode.VelocityChange);
            }

            // Coasting inside an atmosphere loses a little speed; in open space nothing slows you down.
            if (!hasInput && _currentPlanet != null && _currentPlanet.hasAtmosphere &&
                _currentPlanet.IsInAtmosphere(transform.position))
            {
                float drag = Mathf.Clamp01(atmosphereDrag * _currentPlanet.airResistance * dt);
                _rb.AddForce(-planar * drag, ForceMode.VelocityChange);
            }
        }
    }

    // ── Jump & ground ─────────────────────────────────────────────────────

    private void CheckGrounded()
    {
        _wasGrounded = _isGrounded;
        // The scene's groundMask only covers one layer; the planets of the loaded map live on the Default layer,
        // so always include it. Triggers (pickups, zones) never count as ground.
        int mask = groundMask == 0 ? ~0 : ((int)groundMask | 1);

        // Ratios below (0.8 / 1.1 / 0.6) match what was hand-tuned for the
        // original height-2 capsule — scaling them by the capsule's actual
        // current size keeps grounded-detection correct no matter how big or
        // small the character is, instead of silently breaking on resize.
        Vector3 origin = transform.position + _planetUp * (_capsuleHalfHeight * 0.8f);
        float castDistance = _capsuleHalfHeight * 1.1f;
        float castRadius = _capsuleRadius * 0.6f;
        _isGrounded = Physics.SphereCast(origin, castRadius, -_planetUp, out _, castDistance, mask, QueryTriggerInteraction.Ignore);

        // Fallback: math-based check using the planet's declared radius
        // Handles cases where the collider and the PlanetGravity.radius don't align perfectly
        if (!_isGrounded && _currentPlanet != null && !_currentPlanet.useMathSurface)
        {
            // Planet with hills: compare against the real terrain height under the player instead of a perfect sphere
            Vector3 radial = (transform.position - _currentPlanet.transform.position).normalized;
            Vector3 surface = _currentPlanet.GetSurfacePoint(radial);
            _isGrounded = Vector3.Dot(transform.position - surface, radial) < _capsuleHalfHeight * 1.5f;
        }
        if (!_isGrounded && _currentPlanet != null && _currentPlanet.useMathSurface)
        {
            float distToCenter = Vector3.Distance(transform.position, _currentPlanet.transform.position);
            float distToSurface = distToCenter - _currentPlanet.radius;
            _isGrounded = distToSurface < _capsuleHalfHeight * 1.5f;
        }

        // Rising off the ground with a jump doesn't count as landing; touching down after a fall does.
        if (_isGrounded && !_wasGrounded)
        {
            float impact = Mathf.Max(0f, -Vector3.Dot(_rb.linearVelocity, _planetUp));
            _jumpedRecently = false;
            OnTouchdown(impact);
            ResetAirState();
            if (impact > 2f) Landed?.Invoke(impact);
        }
    }

    // Visualize ground check in Scene view
    void OnDrawGizmosSelected()
    {
        if (!Application.isPlaying) return;
        Gizmos.color = _isGrounded ? Color.green : Color.red;
        Gizmos.DrawWireSphere(transform.position - _planetUp * (_capsuleHalfHeight * 0.8f - _capsuleRadius * 0.6f), _capsuleRadius * 0.6f);
    }

    private void HandleJump()
    {
        if (IsStunned) { _jumpBufferTimer = 0f; return; }
        float upSpeed = Vector3.Dot(_rb.linearVelocity, _planetUp);

        // Buffered press + (grounded or still within coyote time) → jump.
        if (_jumpBufferTimer > 0f && _coyoteTimer > 0f)
        {
            _jumpBufferTimer = 0f;
            _coyoteTimer = 0f;

            // Cancel downward velocity so the jump is always the full strength
            if (upSpeed < 0f) _rb.linearVelocity -= _planetUp * upSpeed;

            _rb.AddForce(_planetUp * jumpForce, ForceMode.VelocityChange);
            if (_isGrounded) ApplyLongJumpBoost();
            _jumpedRecently = true;
            Jumped?.Invoke();
            return;
        }

        // Variable jump height: let go of jump while still rising → cut the rise short.
        if (_jumpedRecently)
        {
            if (!_jumpHeld && upSpeed > 0f)
            {
                _rb.linearVelocity -= _planetUp * (upSpeed * (1f - jumpCutFactor));
                _jumpedRecently = false;
            }
            else if (upSpeed <= 0f)
            {
                _jumpedRecently = false;      // reached the top — nothing left to cut
            }
        }
    }

    // ── Public ────────────────────────────────────────────────────────────

    public float DashCooldownPercent => dashCooldown > 0f ? Mathf.Clamp01(_dashCooldownTimer / dashCooldown) : 0f;
    public PlanetGravity CurrentPlanet => _currentPlanet;
    public bool IsGrounded => _isGrounded;
    public bool IsJetpacking => _jetpacking;

    /// <summary>Speed along the planet surface (m/s).</summary>
    /// <summary>True while running (Sprint held and actually moving faster than a walk).</summary>
    public bool IsRunning => _sprintHeld && PlanarSpeed > (walkSpeed + moveSpeed) * 0.5f;
    public float PlanarSpeed => Vector3.ProjectOnPlane(_rb != null ? _rb.linearVelocity : Vector3.zero, _planetUp).magnitude;

    /// <summary>Surface velocity in the player's own frame: x = right, z = forward (m/s).</summary>
    public Vector3 LocalPlanarVelocity
    {
        get
        {
            if (_rb == null) return Vector3.zero;
            return transform.InverseTransformDirection(Vector3.ProjectOnPlane(_rb.linearVelocity, _planetUp));
        }
    }

    /// <summary>Extra field of view (degrees) from sprinting / dashing — added by WeaponAim.</summary>
    public float FovBoost => _fovBoost;
}
}
