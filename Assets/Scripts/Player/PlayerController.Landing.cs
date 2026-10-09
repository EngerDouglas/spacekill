using UnityEngine;

namespace OrbitRush
{

/// <summary>
/// Landing, stun and footstep noise. A hard landing stuns, and landing braced (crouched) at speed makes a shockwave that
/// damages and pushes away everyone nearby.
/// </summary>
public partial class PlayerController
{
    [Header("Landing")]
    [Tooltip("Impact speed (m/s) above which a landing is dangerous.")]
    public float hardLandingSpeed = 22f;
    public float forcedLandRadius = 14f;
    public float forcedLandDamage = 45f;
    public float forcedLandForce = 1400f;
    public float stunSeconds = 1.3f;

    private float _stunUntil;
    private float _footstepTimer;

    public bool IsStunned => Time.time < _stunUntil;

    /// <summary>(impact speed, forced shockwave landing?, stunned?)</summary>
    public event System.Action<float, bool, bool> HardLanded;

    /// <summary>Multiplier on walking / running speed (stunned, grabbed).</summary>
    private float MoveSpeedScale
    {
        get
        {
            if (IsStunned) return 0f;
            return IsGrabbed ? grabMoveScale : 1f;
        }
    }

    /// <summary>Called from the ground check when we touch down after being airborne.</summary>
    private void OnTouchdown(float impact)
    {
        if (impact < 4f) return;

        bool hard = impact >= hardLandingSpeed;
        bool braced = _crouchHeld && impact >= hardLandingSpeed * 0.7f;     // crouching on purpose = forced landing

        if (braced) { Shockwave(); HardLanded?.Invoke(impact, true, false); return; }
        if (hard)
        {
            _stunUntil = Time.time + stunSeconds;
            if (HUD.Instance != null) HUD.Instance.ShowEvent("ATERRIZAJE DESCUIDADO", 1.6f);
            CameraRig.AddTrauma(0.5f);
            HardLanded?.Invoke(impact, false, true);
        }
    }

    /// <summary>Forced landing: a shockwave that damages and pushes away everyone nearby.</summary>
    private void Shockwave()
    {
        Vector3 centre = transform.position;
        var done = new System.Collections.Generic.HashSet<IDamageable>();
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
