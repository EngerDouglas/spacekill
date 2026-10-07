using System.Collections.Generic;
using UnityEngine;

namespace OrbitRush
{

public enum EnemyKind { Robot, Drone, Zombie }

/// <summary>
/// The enemy brain. Perception (<see cref="AISenses"/>) and movement (<see cref="EnemyController"/>) are shared; the
/// behaviour per kind lives in the partial files:
///   EnemyAI.Robot.cs  — tactical soldier: patrol, suspicion, cover + warned bursts, search, disabled
///   EnemyAI.Drone.cs  — aerial hunter: orbit patrol, tracking + marking, dive passes, EMP, retreat, kamikaze, space chase
///   EnemyAI.Zombie.cs — horde melee: wander, drawn by noise, chase, grab
/// Enemies near players think every frame; far ones think less often and enemies on empty planets freeze.
/// </summary>
[RequireComponent(typeof(EnemyController))]
[RequireComponent(typeof(EnemyStats))]
public partial class EnemyAI : MonoBehaviour
{
    [Header("Kind")]
    public EnemyKind kind = EnemyKind.Robot;

    [Header("Detection (legacy tuning, now feeds AISenses)")]
    public float detectionRadius = 25f;
    public float loseSightRadius = 35f;
    public LayerMask playerMask;
    public LayerMask obstructionMask;

    [Header("Patrol")]
    public float patrolRadius = 12f;
    public float patrolWaitTime = 2f;

    [Header("Attack")]
    public float attackRange = 18f;
    public float fireInterval = 1.2f;
    [Tooltip("Random aim error in degrees (a damaged drone can't hold its aim).")]
    public float inaccuracyDegrees = 0f;
    public float fireIntervalScale = 1f;
    public float projectileDamage = 15f;
    public float projectileSpeed = 40f;
    public GameObject projectilePrefab;
    public Transform muzzle;

    [Header("Combat movement")]
    public bool strafeWhileAttacking = false;
    public float preferredDistance = 14f;
    public float strafeSwitchTime = 2.5f;

    [Header("Performance")]
    [Tooltip("Players farther than this: the enemy's brain is frozen entirely.")]
    public float freezeDistance = 380f;

    /// <summary>Raised each time the enemy attacks (drives the firing / swing animation).</summary>
    public event System.Action Fired;

    public AISenses Senses { get; private set; }
    public EnemyController Controller { get; private set; }
    public EnemyStats Stats { get; private set; }

    /// <summary>Readable name of the current state (tests and debugging).</summary>
    public string StateName
    {
        get
        {
            switch (kind)
            {
                case EnemyKind.Robot: return _robot.ToString();
                case EnemyKind.Drone: return _drone.ToString();
                default: return _zombie.ToString();
            }
        }
    }

    /// <summary>True while the enemy is in its attack routine (stands and shoots / swings).</summary>
    public bool IsAttacking
    {
        get
        {
            switch (kind)
            {
                case EnemyKind.Robot: return _robot == RobotState.Combat && _burstPhase != BurstPhase.Idle;
                case EnemyKind.Drone: return _drone == DroneState.Attack;
                default: return _zombie == ZombieState.Attack;
            }
        }
    }

    private Vector3 _spawnPoint;
    private float _accum;
    private float _nearestPlayerDistance = 0f;
    private float _distanceTimer;

    /// <summary>All live enemies (flock / horde / alert lookups).</summary>
    public static readonly List<EnemyAI> All = new List<EnemyAI>();

    void OnEnable() { if (!All.Contains(this)) All.Add(this); }
    void OnDisable() => All.Remove(this);

    void Awake()
    {
        Controller = GetComponent<EnemyController>();
        Stats = GetComponent<EnemyStats>();
        Senses = GetComponent<AISenses>();
        if (Senses == null) Senses = gameObject.AddComponent<AISenses>();
    }

    void Start()
    {
        _spawnPoint = transform.position;
        ConfigureSenses();
        Stats.Damaged += OnDamaged;
        Stats.DisabledFor += OnDisabledFor;
        switch (kind)
        {
            case EnemyKind.Robot: StartRobot(); break;
            case EnemyKind.Drone: StartDrone(); break;
            default: StartZombie(); break;
        }
    }

    void OnDestroy()
    {
        if (Stats != null) { Stats.Damaged -= OnDamaged; Stats.DisabledFor -= OnDisabledFor; }
        AlertNetwork.Unregister(this);
    }

    /// <summary>Per-kind sensory profile (set again whenever kind changes).</summary>
    private void ConfigureSenses()
    {
        switch (kind)
        {
            case EnemyKind.Robot:
                Senses.viewDistance = Mathf.Max(55f, detectionRadius * 2f); Senses.viewAngle = 120f; Senses.hearing = 1f;
                Senses.memorySeconds = 12f; Senses.instantRange = 3f; Senses.eyeHeight = 1.5f;
                break;
            case EnemyKind.Drone:
                Senses.viewDistance = 80f; Senses.viewAngle = 100f; Senses.hearing = 0.35f; Senses.footprintRadius = 16f;      // the propellers drown out sound
                Senses.memorySeconds = 30f; Senses.instantRange = 4f; Senses.heatSensor = true; Senses.heatRange = 150f;
                Senses.eyeHeight = 0f; Senses.viewPitch = 35f;                                  // the spotlight sweeps the ground ahead
                break;
            default:
                Senses.viewDistance = 14f; Senses.viewAngle = 60f; Senses.hearing = 3f;          // poor eyes, superb ears
                Senses.memorySeconds = 4f; Senses.instantRange = 2.5f; Senses.eyeHeight = 1.5f;
                Senses.baseFillRate = 1.2f;
                break;
        }
    }

    // ── Think loop with level of detail ───────────────────────────────────

    void Update()
    {
        if (Stats == null || !Stats.IsAlive) return;

        _distanceTimer -= Time.deltaTime;
        if (_distanceTimer <= 0f)
        {
            _distanceTimer = 0.5f;
            _nearestPlayerDistance = NearestPlayerDistance();
        }
        if (_nearestPlayerDistance > freezeDistance) { Controller.StopMoving(); return; }      // nobody around: frozen

        float interval = _nearestPlayerDistance < 70f ? 0f : _nearestPlayerDistance < 160f ? 0.12f : 0.5f;
        _accum += Time.deltaTime;
        if (_accum < interval) return;
        float dt = _accum; _accum = 0f;

        if (Stats.IsDisabled) { TickDisabled(dt); return; }
        if (kind == EnemyKind.Robot) UpdateDisabledEnd();
        Senses.Tick(dt);

        switch (kind)
        {
            case EnemyKind.Robot: TickRobot(dt); break;
            case EnemyKind.Drone: TickDrone(dt); break;
            default: TickZombie(dt); break;
        }
    }

    private float NearestPlayerDistance()
    {
        float best = float.MaxValue;
        var gm = GameManager.Instance;
        if (gm == null) return 0f;
        foreach (var p in gm.Players)
            if (p != null && p.IsAlive) best = Mathf.Min(best, Vector3.Distance(p.transform.position, transform.position));
        return best == float.MaxValue ? 0f : best;
    }

    // ── Reactions ─────────────────────────────────────────────────────────

    private void OnDamaged(PlayerStats attacker)
    {
        if (attacker == null) return;
        // Whoever shoots us is detected, wherever they are
        foreach (var c in Senses.Contacts)
            if (c.stats == attacker)
            {
                c.suspicion = Senses.detectedAt; c.lastDetectedTime = Time.time;
                c.lastSeenPos = attacker.transform.position; c.lastSeenTime = Time.time;
            }
        OnHurt(attacker);
    }

    private void OnDisabledFor(float seconds) => EnterDisabled(seconds);

    // ── Shared helpers ────────────────────────────────────────────────────

    /// <summary>The player's position plus where it will be when a projectile arrives (shots lead moving, flying targets).</summary>
    protected Vector3 PredictedAim(AISenses.Contact c, float leadFactor = 1f)
    {
        Vector3 pos = c.transform.position;
        var col = c.transform.GetComponent<Collider>();
        if (col != null) pos = col.bounds.center;
        if (muzzle == null || projectileSpeed < 1f) return pos;
        float travel = Vector3.Distance(muzzle.position, pos) / projectileSpeed;
        var pc = c.stats != null ? c.stats.GetComponent<PlayerController>() : null;
        float factor = pc != null && (pc.IsFlying || pc.IsJetpacking) ? 1f : 0.65f;        // flying targets: aim where they are going
        return pos + c.Velocity * travel * factor * leadFactor;
    }

    /// <summary>Spawns one bolt along <paramref name="dir"/> with the current accuracy.</summary>
    protected void FireBolt(Vector3 dir, GameObject prefab = null, float damage = -1f, float speed = -1f, System.Action<Projectile> tweak = null)
    {
        prefab = prefab != null ? prefab : projectilePrefab;
        if (prefab == null || muzzle == null) return;
        if (inaccuracyDegrees > 0.01f)
            dir = Quaternion.AngleAxis(Random.Range(0f, 360f), dir) * Quaternion.AngleAxis(Random.Range(0f, inaccuracyDegrees), Vector3.Cross(dir, Random.onUnitSphere).normalized) * dir;
        Fired?.Invoke();
        float dmg = damage >= 0f ? damage : projectileDamage;
        float spd = speed > 0f ? speed : projectileSpeed;
        ProjectileFactory.Spawn(prefab, muzzle.position, dir, gameObject, Controller.CurrentPlanet, p =>
        {
            p.damage = dmg; p.speed = spd; p.affectedByGravity = false;
            tweak?.Invoke(p);
        });
        NoiseSystem.Emit(muzzle.position, Loudness.Shoot * 0.6f, gameObject);
    }

    protected void RaiseFired() => Fired?.Invoke();

    /// <summary>Test hook: forget everything and go back to the idle state of this kind.</summary>
    public void DebugReset()
    {
        Senses.Clear();
        if (kind == EnemyKind.Robot) { _robot = RobotState.Patrol; _burstPhase = BurstPhase.Idle; _stateTime = 0f; Controller.controlEnabled = true; }
        else if (kind == EnemyKind.Drone) { _drone = DroneState.Patrol; _droneStateTime = 0f; _kamikazeCharging = false; Controller.freeFlight = false; }
        else { _zombie = ZombieState.Wander; _wasChasing = false; _winding = false; }
    }

    protected Vector3 Up => Controller.PlanetUp;

    /// <summary>A point on the ground (surface of the current planet) in the planar direction from here.</summary>
    protected Vector3 GroundPointToward(Vector3 direction, float distance)
    {
        Vector3 dir = Vector3.ProjectOnPlane(direction, Up);
        if (dir.sqrMagnitude < 1e-4f) dir = transform.forward;
        Vector3 p = transform.position + dir.normalized * distance;
        var planet = Controller.CurrentPlanet;
        if (planet == null) return p;
        return planet.GetSurfacePoint((p - planet.transform.position).normalized) + Up * 0.1f;
    }

    protected float PlanarDistance(Vector3 point) => Vector3.ProjectOnPlane(point - transform.position, Up).magnitude;

    protected void MoveToward(Vector3 point, float speedFactor)
    {
        Controller.aiSpeedFactor = speedFactor;
        Vector3 to = point - transform.position;
        Controller.SetMoveDirection(to.sqrMagnitude > 0.0001f ? to.normalized : Vector3.zero);
    }

    protected void Halt()
    {
        Controller.StopMoving();
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(1f, 0.5f, 0f, 0.15f);
        Gizmos.DrawSphere(transform.position, detectionRadius);
        Gizmos.color = new Color(1f, 0f, 0f, 0.15f);
        Gizmos.DrawSphere(transform.position, attackRange);
    }
}
}
