using UnityEngine;

namespace OrbitRush
{

/// <summary>Plays Mixamo Humanoid clips on a rigged humanoid enemy (walk / run, rifle aim + firing kick, falling-back death).</summary>
public class EnemyAnimDriver : MonoBehaviour, IDeathBehavior
{
    private CharacterAnimator _anim;
    private EnemyController _controller;
    private EnemyAI _ai;
    private EnemyStats _stats;
    private Rigidbody _body;
    private bool _dead;
    private float _destroyAt;
    private float _swingUntil;

    void Start()
    {
        _controller = GetComponent<EnemyController>();
        _ai = GetComponent<EnemyAI>();
        _body = GetComponent<Rigidbody>();
        _stats = GetComponent<EnemyStats>();

        var animator = GetComponentInChildren<Animator>();
        if (animator == null || animator.avatar == null) { enabled = false; return; }   // not a rigged model

        _anim = animator.gameObject.AddComponent<CharacterAnimator>();
        if (!_anim.Setup(animator)) { enabled = false; return; }
        if (_ai != null) _ai.Fired += () => { if (_anim != null && _anim.Ready && _ai.kind != EnemyKind.Zombie) _anim.FireKick(); };
        if (_ai != null) _ai.Swung += () =>
        {
            if (_anim == null || !_anim.Ready) return;
            _swingUntil = Time.time + 0.75f;
            _anim.ClearBaseTargetsExcept(CharacterAnimator.SwordLight);
            _anim.PlayOneShot(CharacterAnimator.SwordLight, 0.75f);          // the claw swipe
        };
    }

    /// <summary>Starts the death animation and keeps the body around for `lifetime` seconds. Returns false if there is none.</summary>
    public bool TryPlayDeath() => PlayDeath(2.2f);

    public bool PlayDeath(float lifetime)
    {
        if (_anim == null || !_anim.Ready) return false;
        _dead = true;
        _destroyAt = Time.time + lifetime;

        if (_ai != null) _ai.enabled = false;
        if (_controller != null) _controller.enabled = false;
        if (_body != null) { _body.linearVelocity = Vector3.zero; _body.isKinematic = true; }
        foreach (var c in GetComponentsInChildren<Collider>()) c.enabled = false;
        foreach (var hb in GetComponentsInChildren<EnemyHealthBar>(true)) hb.enabled = false;
        var bar = transform.Find("HealthBar");
        if (bar != null) bar.gameObject.SetActive(false);     // the bar's component is on this root: only hide its visual

        _anim.SetUpperTarget(0f);
        _anim.ClearBaseTargetsExcept(CharacterAnimator.Death);
        _anim.PlayOneShot(CharacterAnimator.Death, _anim.ClipLength(CharacterAnimator.Death));
        return true;
    }

    void Update()
    {
        if (_dead)
        {
            if (Time.time >= _destroyAt) Destroy(gameObject);
            return;
        }
        if (_anim == null || !_anim.Ready || _body == null) return;

        Vector3 up = _controller != null ? _controller.PlanetUp : transform.up;
        Vector3 planar = Vector3.ProjectOnPlane(_body.linearVelocity, up);
        float speed = planar.magnitude;
        float top = _controller != null ? Mathf.Max(0.1f, _controller.moveSpeed) : 4.5f;
        bool attacking = _ai != null && _ai.IsAttacking;

        bool zombie = _ai != null && _ai.kind == EnemyKind.Zombie;
        _anim.SetUpperTarget(zombie ? 0f : 1f);        // soldiers always shoulder the rifle; zombies have empty hands
        if (zombie && Time.time < _swingUntil) return;   // the swipe is playing

        string key;
        if (speed < 0.4f) key = CharacterAnimator.Idle;
        else if (zombie)
        {
            float forwardZ = Vector3.Dot(planar, transform.forward);
            key = forwardZ < -0.3f * speed ? CharacterAnimator.Back
                : speed > top * 0.5f ? CharacterAnimator.ZombieRun : CharacterAnimator.Walk;        // shamble, or lurch at a run
            _anim.ClearBaseTargetsExcept(key);
            _anim.SetBase(key, 1f);
            _anim.SetBaseSpeed(key, key == CharacterAnimator.Walk ? Mathf.Clamp(speed / 1.6f, 0.45f, 1f) : Mathf.Clamp(speed / top, 0.8f, 1.4f));
            return;
        }
        else
        {
            float forward = Vector3.Dot(planar, transform.forward);
            bool wounded = _stats != null && _stats.maxHealth > 0f && _stats.Health < _stats.maxHealth * 0.3f;
            if (forward < -0.3f * speed) key = CharacterAnimator.Back;
            else if (wounded) key = CharacterAnimator.ZombieRun;                         // staggering on
            else key = speed > top * 0.8f && !attacking ? CharacterAnimator.Run : CharacterAnimator.Walk;
        }
        _anim.ClearBaseTargetsExcept(key);
        _anim.SetBase(key, 1f);
        _anim.SetBaseSpeed(key, Mathf.Clamp(speed / top * 1.3f, 0.6f, 1.8f));
    }
}
}
