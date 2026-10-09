using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace OrbitRush
{

/// <summary>
/// Zombie: slow and dim alone, terrifying in a horde. Poor eyes, superb ears.
///   Wander    shambles about, stops, moans
///   Attracted drawn toward the loudest recent noise; neighbours flow the same way
///   Chase     runs at a seen player (faster than walking, slower than the player's sprint); the horde follows
///   Attack    short warned swing; may grab the player, who cannot take off until freed
/// </summary>
public partial class EnemyAI
{
    public enum ZombieState { Wander, Attracted, Chase, Attack, Grabbing, Rising }

    [Header("Zombie")]
    public float zombieWanderFactor = 0.22f;
    public float zombieAttractedFactor = 0.55f;
    public float zombieMeleeRange = 1.9f;
    public float zombieWindup = 0.55f;
    public float zombieMeleeCooldown = 1.3f;
    public float zombieMeleeDamage = 12f;
    [Range(0f, 1f)] public float zombieGrabChance = 0.35f;
    public float zombieGrabDps = 5f;
    public float zombieGrabMaxSeconds = 7f;
    public float hordeContagionRadius = 28f;

    private ZombieState _zombie = ZombieState.Wander;
    private float _zombieTime, _wanderTimer, _meleeTimer, _windupTimer;
    private bool _winding;
    private Vector3 _wanderDir;
    private Vector3 _attractPoint;
    private AISenses.Contact _chaseTarget;
    private PlayerController _grabbed;
    private float _grabTime;
    private bool _wasChasing;

    /// <summary>Raised when a zombie starts a swing (the animation driver plays the strike).</summary>
    public event System.Action Swung;

    private void StartZombie()
    {
        _wanderDir = RandomPlanarDirection();
        _wanderTimer = Random.Range(1f, 4f);
        HordeRegistry.Register(this);
    }

    private Vector3 RandomPlanarDirection()
    {
        Vector3 r = Vector3.ProjectOnPlane(Random.onUnitSphere, Up);
        return r.sqrMagnitude < 0.01f ? transform.forward : r.normalized;
    }

    // ── Spawning out of the ground ────────────────────────────────────────

    /// <summary>Rise out of the ground over a couple of seconds (called by the spawner right after instantiating).</summary>
    public void BeginRising(float seconds = 1.6f)
    {
        StartCoroutine(RiseRoutine(seconds));
    }

    private IEnumerator RiseRoutine(float seconds)
    {
        _zombie = ZombieState.Rising;
        Controller.controlEnabled = false;
        var rb = Controller.Body;
        if (rb != null) rb.isKinematic = true;
        Vector3 end = transform.position;
        Vector3 start = end - Up * 2.4f;
        for (float t = 0f; t < seconds; t += Time.deltaTime)
        {
            transform.position = Vector3.Lerp(start, end, Ease.Smooth(t / seconds));
            yield return null;
        }
        transform.position = end;
        if (rb != null) { rb.position = end; rb.isKinematic = false; }
        Controller.controlEnabled = true;
        _zombie = ZombieState.Wander; _zombieTime = 0f;
    }

    // ── Main tick ─────────────────────────────────────────────────────────

    private void TickZombie(float dt)
    {
        if (_zombie == ZombieState.Rising) return;
        _zombieTime += dt;
        _meleeTimer -= dt;

        var target = Senses.Target;
        bool detected = target != null && Senses.IsDetected(target);
        bool chasing = detected && (target.visible || Senses.SecondsSinceSeen(target) < Senses.memorySeconds);

        if (_zombie == ZombieState.Grabbing) { TickGrab(dt); return; }

        if (chasing)
        {
            if (!_wasChasing) { _wasChasing = true; HordeRegistry.Contagion(this, target, hordeContagionRadius); }
            _chaseTarget = target;
            float d = Vector3.Distance(transform.position, target.transform.position);
            if (_zombie == ZombieState.Attack || d <= zombieMeleeRange) TickAttack(dt, target, d);
            else { SetZombie(ZombieState.Chase); TickChase(dt, target); }
            return;
        }
        _wasChasing = false; _winding = false;

        // Noise attracts: the loudest recent one, or the point the horde is flowing to
        if (Senses.HeardRecently(4f))
        {
            _attractPoint = Senses.HeardPoint;
            SetZombie(ZombieState.Attracted);
        }
        else if (_zombie == ZombieState.Attracted && PlanarDistance(_attractPoint) < 3f)
        {
            SetZombie(ZombieState.Wander);
        }

        if (_zombie == ZombieState.Attracted) { MoveHorde(_attractPoint, zombieAttractedFactor); Controller.LookAt(null); }
        else TickWander(dt);
    }

    private void SetZombie(ZombieState s)
    {
        if (_zombie == s) return;
        _zombie = s; _zombieTime = 0f;
    }

    // ── Wander ────────────────────────────────────────────────────────────

    private void TickWander(float dt)
    {
        SetZombie(ZombieState.Wander);
        _wanderTimer -= dt;
        if (_wanderTimer <= 0f)
        {
            // Walk for a while, then stop (and moan) for a while
            bool walkNext = Controller.aiSpeedFactor < 0.01f || Random.value < 0.6f;
            _wanderDir = walkNext ? RandomPlanarDirection() : Vector3.zero;
            _wanderTimer = walkNext ? Random.Range(3f, 6f) : Random.Range(1.5f, 3.5f);
        }
        if (_wanderDir == Vector3.zero) { Controller.aiSpeedFactor = 0f; Halt(); return; }
        Controller.aiSpeedFactor = zombieWanderFactor;
        Controller.SetMoveDirection(WithSeparation(_wanderDir));
        Controller.LookAt(null);
    }

    // ── Horde movement ────────────────────────────────────────────────────

    private void MoveHorde(Vector3 point, float factor)
    {
        Controller.aiSpeedFactor = factor;
        Vector3 to = Vector3.ProjectOnPlane(point - transform.position, Up);
        Controller.SetMoveDirection(to.sqrMagnitude > 0.01f ? WithSeparation(to.normalized) : Vector3.zero);
    }

    /// <summary>Steers away from zombies standing too close, so the horde spreads out like a crowd instead of stacking.</summary>
    private Vector3 WithSeparation(Vector3 dir)
    {
        Vector3 push = Vector3.zero;
        foreach (var z in HordeRegistry.Members)
        {
            if (z == this || z == null) continue;
            Vector3 d = transform.position - z.transform.position;
            float dist = d.magnitude;
            if (dist < 1.6f && dist > 0.01f) push += d / dist * (1.6f - dist);
        }
        Vector3 result = dir + Vector3.ProjectOnPlane(push, Up) * 1.4f;
        return result.sqrMagnitude > 0.001f ? result.normalized : dir;
    }

    // ── Chase / attack ────────────────────────────────────────────────────

    private void TickChase(float dt, AISenses.Contact target)
    {
        Vector3 goal = target.visible ? target.transform.position : target.lastSeenPos;      // out of sight: runs to where it last saw them
        MoveHorde(goal, 1f);
        Controller.LookAt(goal);
    }

    private void TickAttack(float dt, AISenses.Contact target, float distance)
    {
        SetZombie(ZombieState.Attack);
        Halt();
        Controller.LookAt(target.transform.position);

        if (!_winding)
        {
            if (_meleeTimer <= 0f && distance <= zombieMeleeRange * 1.2f)
            {
                _winding = true; _windupTimer = zombieWindup;
                Swung?.Invoke(); RaiseFired();
            }
            else if (distance > zombieMeleeRange * 1.5f) { SetZombie(ZombieState.Chase); }
            return;
        }

        _windupTimer -= dt;
        if (_windupTimer > 0f) return;
        _winding = false;
        _meleeTimer = zombieMeleeCooldown;

        // The swing lands if the player is still close and roughly in front
        float now = Vector3.Distance(transform.position, target.transform.position);
        Vector3 to = (target.transform.position - transform.position).normalized;
        if (now <= zombieMeleeRange * 1.35f && Vector3.Dot(transform.forward, to) > 0.2f && target.stats != null)
        {
            target.stats.NoteSource("ZOMBI");
            target.stats.RegisterHit(transform.position);       // HUD damage-direction arc
            target.stats.TakeDamage(zombieMeleeDamage, null);
            NoiseSystem.Emit(transform.position, Loudness.Walk, gameObject);
            var pc = target.stats.GetComponent<PlayerController>();
            if (pc != null && Random.value < zombieGrabChance && pc.TryGrab(this))
            {
                _grabbed = pc; _grabTime = 0f; SetZombie(ZombieState.Grabbing);
            }
        }
    }

    // ── Grab ──────────────────────────────────────────────────────────────

    private void TickGrab(float dt)
    {
        _grabTime += dt;
        if (_grabbed == null || !_grabbed.IsGrabbedBy(this) || _grabTime > zombieGrabMaxSeconds || !_grabbed.GetComponent<PlayerStats>().IsAlive)
        {
            ReleaseGrab(); return;
        }
        var stats = _grabbed.GetComponent<PlayerStats>();
        Controller.LookAt(_grabbed.transform.position);
        Vector3 hold = _grabbed.transform.position - _grabbed.transform.forward * 0.9f;
        MoveHorde(hold, 0.5f);
        stats.NoteSource("ZOMBI");
        stats.TakeDamage(zombieGrabDps * dt, null);
    }

    /// <summary>Called by the player when they break free (or by us when the grab ends).</summary>
    public void ReleaseGrab()
    {
        if (_grabbed != null) _grabbed.ReleaseGrab(this);
        _grabbed = null;
        if (_zombie == ZombieState.Grabbing) { _meleeTimer = zombieMeleeCooldown * 1.5f; SetZombie(ZombieState.Chase); }
    }

    private void ZombieHurt(PlayerStats attacker) { }

    /// <summary>Another zombie started chasing: everyone nearby joins in (the horde snowballs).</summary>
    public void JoinChase(Vector3 point)
    {
        if (kind != EnemyKind.Zombie || !Stats.IsAlive) return;
        Senses.Alert(point, 1f);
        foreach (var c in Senses.Contacts)
            if (Vector3.Distance(c.transform.position, point) < 60f) { c.suspicion = Senses.detectedAt; c.lastDetectedTime = Time.time; c.lastSeenPos = c.transform.position; c.lastSeenTime = Time.time; }
    }
}

/// <summary>All zombies (separation, contagion).</summary>
public static class HordeRegistry
{
    public static readonly List<EnemyAI> Members = new List<EnemyAI>();
    public static void Register(EnemyAI z) { Members.RemoveAll(m => m == null); if (!Members.Contains(z)) Members.Add(z); }

    public static void Contagion(EnemyAI source, AISenses.Contact target, float radius)
    {
        foreach (var z in Members)
        {
            if (z == null || z == source || !z.Stats.IsAlive) continue;
            if (Vector3.Distance(z.transform.position, source.transform.position) <= radius) z.JoinChase(target.transform.position);
        }
    }
}
}
