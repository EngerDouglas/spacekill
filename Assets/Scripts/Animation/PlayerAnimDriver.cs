using UnityEngine;

namespace OrbitRush
{

/// <summary>Chooses which Mixamo clips the player's CharacterAnimator plays from the controller's real movement.</summary>
public class PlayerAnimDriver : MonoBehaviour
{
    private CharacterAnimator _anim;
    private Transform _visual;
    private PlayerController _controller;
    private PlayerStats _stats;
    private MeleeCombat _melee;
    private WeaponInventory _inventory;
    private Rigidbody _body;

    private float _oneShotUntil;
    private bool _dead;
    private string _deathKey = CharacterAnimator.Death;
    private float _idleTime, _fidgetUntil, _yaw;
    private float _peakSpeed;          // fastest recent ground speed (for the run-to-stop animation)

    public void Init(CharacterAnimator anim, Transform visual)
    {
        _anim = anim; _visual = visual;
        _controller = GetComponent<PlayerController>();
        _stats = GetComponent<PlayerStats>();
        _inventory = GetComponent<WeaponInventory>();
        _body = GetComponent<Rigidbody>();
        if (_controller != null) { _controller.Landed += OnLanded; _controller.Jumped += OnJumped; _controller.AirSpun += OnAirSpun; }
        WeaponBase.AnyFired += OnWeaponFired;
    }

    void OnDestroy()
    {
        if (_controller != null) { _controller.Landed -= OnLanded; _controller.Jumped -= OnJumped; _controller.AirSpun -= OnAirSpun; }
        WeaponBase.AnyFired -= OnWeaponFired;
        if (_melee != null) _melee.AnimRequested -= OnMeleeAnim;
    }

    // Air spin: the model does a full turn around the up axis while the controller slows the fall
    private float _spinElapsed = 1f;
    private const float SpinDuration = 0.4f;
    private void OnAirSpun() => _spinElapsed = 0f;

    void LateUpdate()
    {
        if (_spinElapsed >= SpinDuration || _visual == null) return;
        _spinElapsed += Time.deltaTime;
        float angle = Ease.Smooth(_spinElapsed / SpinDuration) * 360f;
        _visual.localRotation = Quaternion.Euler(0f, angle, 0f) * _visual.localRotation;
    }

    private void OnLanded(float impact)
    {
        if (impact < 5f || _dead) return;
        StartOneShot(CharacterAnimator.Landing, 0.42f);
    }

    private void OnJumped()
    {
        if (_dead) return;
        var weapon = _inventory != null ? _inventory.ActiveWeapon : null;
        string key = (_melee != null && _melee.BlocksFiring) ? CharacterAnimator.SwordJump
                   : IsPistol(weapon) ? CharacterAnimator.Jump        // pistol-class guns: Pistol Jump
                   : CharacterAnimator.RifleJump;                     // everything else (rifles, shotgun, unarmed): Jump Up w/ rifle
        StartOneShot(key, 0.5f);
    }

    private void OnWeaponFired(WeaponBase w)
    {
        if (w != null && w.GetComponentInParent<PlayerStats>() == _stats) _anim.FireKick();
    }

    private void OnMeleeAnim(string key, float duration) => StartOneShot(key, duration);

    private void StartOneShot(string key, float duration)
    {
        if (_anim == null || !_anim.Ready) return;
        _oneShotUntil = Time.time + duration;
        _anim.ClearBaseTargetsExcept(key);
        _anim.PlayOneShot(key, duration);
    }

    private static bool IsPistol(WeaponBase w)
        => w != null && w.grip == WeaponBase.GripType.Pistol;

    void Update()
    {
        if (_anim == null || !_anim.Ready || _controller == null) return;

        if (_melee == null)
        {
            _melee = GetComponent<MeleeCombat>();
            if (_melee != null) _melee.AnimRequested += OnMeleeAnim;
        }

        if (!_stats.IsAlive)
        {
            if (!_dead)
            {
                _dead = true;
                // Dying with the katana out plays the two-handed sword death
                _deathKey = (_melee != null && _melee.BlocksFiring) ? CharacterAnimator.SwordDeath : CharacterAnimator.Death;
                _anim.ClearBaseTargetsExcept(_deathKey);
                _anim.PlayOneShot(_deathKey, _anim.ClipLength(_deathKey));
            }
            _anim.SetUpperTarget(0f);
            return;
        }
        if (_dead) { _dead = false; _oneShotUntil = 0f; _anim.SetBase(_deathKey, 0f); }

        var weapon = _inventory != null ? _inventory.ActiveWeapon : null;
        bool sword = (_melee != null && _melee.BlocksFiring);
        bool armed = !sword && weapon != null;
        _anim.SetPistolStance(IsPistol(weapon));
        _anim.SetUpperTarget(armed ? 1f : 0f);


        Vector3 v = _controller.LocalPlanarVelocity;
        float speed = _controller.PlanarSpeed;
        float top = Mathf.Max(0.1f, _controller.moveSpeed);

        // Track the run-up so letting go of the stick plays the stopping animation
        if (_controller.IsGrounded) _peakSpeed = Mathf.Max(speed, Mathf.MoveTowards(_peakSpeed, 0f, 14f * Time.deltaTime));
        else _peakSpeed = 0f;

        // Idle fidget (Gunplay) — any movement, jump or shot cancels it
        if (_fidgetUntil > 0f && (speed > 0.5f || !_controller.IsGrounded || Time.time >= _fidgetUntil)) { _fidgetUntil = 0f; _oneShotUntil = 0f; _anim.SetBase(CharacterAnimator.Gunplay, 0f); }
        if (speed > 0.5f || !_controller.IsGrounded || _fidgetUntil > 0f) _idleTime = 0f;
        else _idleTime += Time.deltaTime;

        if (speed < 0.5f || !_controller.IsGrounded) { _yaw = Mathf.Lerp(_yaw, 0f, 1f - Mathf.Exp(-9f * Time.deltaTime)); if (_visual != null) _visual.localRotation = Quaternion.Euler(0f, _yaw, 0f); }

        if (Time.time < _oneShotUntil) return;     // landing / jump / slash / dodge is playing

        if (!_controller.IsGrounded)
        {
            float vy = _body != null ? Vector3.Dot(_body.linearVelocity, transform.up) : 0f;
            // Jetpack flight (launch, cruise, being captured, thrusting) uses the Flying clip; falling fast without thrust still falls
            string key = (_controller.IsFlying || _controller.IsJetpacking) && !(vy < -14f && !_controller.IsJetpacking) ? CharacterAnimator.Flying
                       : vy < -14f ? CharacterAnimator.Fall : CharacterAnimator.Air;
            _anim.ClearBaseTargetsExcept(key);
            _anim.SetBase(key, 1f);
            return;
        }

        if (speed < 0.5f)
        {
            if (_idleTime > 9f && armed)
            {
                _idleTime = 0f;
                float len = _anim.ClipLength(CharacterAnimator.Gunplay);
                _fidgetUntil = Time.time + len;
                StartOneShot(CharacterAnimator.Gunplay, len);
                return;
            }
            string idle = sword ? CharacterAnimator.SwordIdle : CharacterAnimator.Idle;
            _anim.ClearBaseTargetsExcept(idle);
            _anim.SetBase(idle, 1f);
            return;
        }

        float f = v.z / speed, l = v.x / speed;
        string pick;
        float yaw = 0f;
        if (f < -0.35f) pick = CharacterAnimator.Back;                                   // backing away
        else if (l < -0.75f && !sword) pick = CharacterAnimator.StrafeLeft;            // hard left
        else
        {
            bool running = speed > (_controller.walkSpeed + top) * 0.5f;       // walking pace -> Walk, Sprint held -> Run
            pick = sword ? CharacterAnimator.SwordWalk : (running ? CharacterAnimator.Run : CharacterAnimator.Walk);
            yaw = Mathf.Clamp(Mathf.Atan2(l, Mathf.Max(f, 0.15f)) * Mathf.Rad2Deg, -75f, 75f) * 0.8f;   // lean the body into a sideways run
        }
        _anim.ClearBaseTargetsExcept(pick);
        _anim.SetBase(pick, 1f);
        float reference = pick == CharacterAnimator.Run ? top : Mathf.Max(0.1f, _controller.walkSpeed);   // play each clip at its own pace
        _anim.SetBaseSpeed(pick, Mathf.Clamp(speed / reference * (pick == CharacterAnimator.Run ? 1.35f : 1f), 0.6f, 2.0f));
        _yaw = Mathf.Lerp(_yaw, yaw, 1f - Mathf.Exp(-9f * Time.deltaTime));
        if (_visual != null) _visual.localRotation = Quaternion.Euler(0f, _yaw, 0f);
    }
}
}
