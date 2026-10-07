using System.Collections.Generic;
using UnityEngine;

namespace OrbitRush
{

/// <summary>
/// Tactical soldier: heavy, methodical, ranged, never leaves its planet.
///   Patrol     fixed route around its post, sweeping the scan laser from side to side
///   Suspicion  heard / glimpsed something: stops, points the laser, creeps over to look
///   Combat     takes cover, keeps a medium distance, fires warned bursts (the laser turns red first)
///   Search     lost the target: goes to the last known spot and sweeps the area in widening circles
///   Disabled   EMP or a critical hit: sparks, immobile, takes extra damage
/// Alerts nearby robots when it spots someone; its battery on the back takes extra damage.
/// </summary>
public partial class EnemyAI
{
    public enum RobotState { Patrol, Suspicion, Combat, Search, Disabled }
    private enum BurstPhase { Idle, Telegraph, Burst, Cooldown }

    [Header("Robot")]
    public float scanSweepDegrees = 55f;
    public float scanSpeed = 0.9f;
    public int burstShots = 3;
    public float burstShotInterval = 0.14f;
    public float telegraphTime = 0.55f;
    public float burstCooldown = 1.5f;
    public float minCombatRange = 11f;
    public float maxCombatRange = 22f;
    public float alertRadius = 70f;

    private RobotState _robot = RobotState.Patrol;
    private BurstPhase _burstPhase = BurstPhase.Idle;
    private float _burstTimer;
    private int _shotsLeft;

    private readonly List<Vector3> _route = new List<Vector3>();
    private bool _routeBuilt;
    private int _routeIndex;
    private float _waitTimer;
    private Vector3 _investigate;
    private float _stateTime;
    private Vector3 _standPoint;
    private float _standTimer;
    private float _lastSeenTarget;
    private LineRenderer _laser;
    private Vector3 _searchOrigin;
    private int _searchStep;
    private Vector3 _searchPoint;
    private float _disabledEnd;
    private float _sparkTimer;
    private static readonly Color LaserCyan = new Color(0.1f, 0.9f, 1f);
    private static readonly Color LaserAmber = new Color(1f, 0.75f, 0.1f);
    private static readonly Color LaserRed = new Color(1f, 0.1f, 0.1f);

    // ── Setup ─────────────────────────────────────────────────────────────

    private void StartRobot()
    {
        Stats.backWeakness = 2.5f;                  // battery on its back
        Stats.canBeDisabled = true;
        BuildLaser();
        AlertNetwork.Register(this);
    }

    private void BuildRoute()
    {
        _route.Clear();
        float radius = Mathf.Max(8f, patrolRadius);
        int n = 4;
        Vector3 start = Random.insideUnitSphere;
        for (int i = 0; i < n; i++)
        {
            Vector3 dir = Quaternion.AngleAxis(360f / n * i, Up) * Vector3.ProjectOnPlane(start.sqrMagnitude > 0.01f ? start : Vector3.forward, Up).normalized;
            _route.Add(GroundPointFrom(_spawnPoint, dir, radius));
        }
        _routeIndex = 0;
    }

    /// <summary>A ground point at <paramref name="distance"/> metres from <paramref name="origin"/> along the surface.</summary>
    private Vector3 GroundPointFrom(Vector3 origin, Vector3 direction, float distance)
    {
        Vector3 p = origin + Vector3.ProjectOnPlane(direction, Up).normalized * distance;
        var planet = Controller.CurrentPlanet;
        if (planet == null) return p;
        return planet.GetSurfacePoint((p - planet.transform.position).normalized) + Up * 0.1f;
    }

    private void BuildLaser()
    {
        if (muzzle == null) return;
        var go = new GameObject("ScanLaser");
        go.transform.SetParent(transform, false);
        _laser = go.AddComponent<LineRenderer>();
        _laser.positionCount = 2;
        _laser.widthMultiplier = 0.045f;
        _laser.useWorldSpace = true;
        _laser.material = new Material(Shader.Find("Sprites/Default"));
        _laser.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        _laser.receiveShadows = false;
        _laser.numCapVertices = 2;
    }

    // ── Main tick ─────────────────────────────────────────────────────────

    private void TickRobot(float dt)
    {
        _stateTime += dt;
        var target = Senses.Target;
        bool detected = target != null && target.suspicion >= Senses.detectedAt;      // robots act on what they notice now; memory is handled by the Search state

        switch (_robot)
        {
            case RobotState.Patrol: TickPatrol(dt, detected); break;
            case RobotState.Suspicion: TickSuspicion(dt, detected); break;
            case RobotState.Combat: TickCombat(dt, target, detected); break;
            case RobotState.Search: TickSearch(dt, detected); break;
        }
        UpdateLaser();
    }

    private void SetRobot(RobotState s)
    {
        if (_robot == s) return;
        _robot = s; _stateTime = 0f;
        if (s != RobotState.Combat) { _burstPhase = BurstPhase.Idle; }
        if (s == RobotState.Combat)
        {
            _standTimer = 0f; _lastSeenTarget = Time.time;
            AlertNetwork.Broadcast(this, transform.position, alertRadius, Senses.Target != null ? Senses.Target.lastSeenPos : transform.position);
        }
    }

    // ── Patrol ────────────────────────────────────────────────────────────

    private void TickPatrol(float dt, bool detected)
    {
        Senses.headYaw = Mathf.Sin(Time.time * scanSpeed) * scanSweepDegrees;      // the head scans from side to side
        Senses.viewPitch = 6f;

        if (detected) { SetRobot(RobotState.Combat); return; }
        if (Senses.TopSuspicion >= Senses.suspiciousAt || Senses.HeardRecently(1.5f))
        {
            _investigate = Senses.MostSuspicious != null && Senses.TopSuspicion >= Senses.suspiciousAt ? Senses.MostSuspicious.lastSeenPos : Senses.HeardPoint;
            SetRobot(RobotState.Suspicion);
            return;
        }

        if (!_routeBuilt && Controller.CurrentPlanet != null) { BuildRoute(); _routeBuilt = true; }     // needs the planet to project onto the surface
        if (_route.Count == 0) { Halt(); return; }
        Vector3 wp = _route[_routeIndex];
        if (PlanarDistance(wp) < 1.8f)
        {
            Halt();
            _waitTimer -= dt;
            if (_waitTimer <= 0f) { _routeIndex = (_routeIndex + 1) % _route.Count; _waitTimer = patrolWaitTime; }
            return;
        }
        MoveToward(wp, 0.45f);
        Controller.LookAt(null);
        _waitTimer = patrolWaitTime;
    }

    // ── Suspicion ─────────────────────────────────────────────────────────

    private void TickSuspicion(float dt, bool detected)
    {
        if (detected) { SetRobot(RobotState.Combat); return; }

        // Keep refreshing the point of interest from what it sees / hears
        if (Senses.MostSuspicious != null && Senses.TopSuspicion >= Senses.suspiciousAt) _investigate = Senses.MostSuspicious.lastSeenPos;
        else if (Senses.HeardRecently(1.0f)) _investigate = Senses.HeardPoint;

        Senses.headYaw = Mathf.Lerp(Senses.headYaw, 0f, 1f - Mathf.Exp(-6f * dt));
        Controller.LookAt(_investigate);
        if (_stateTime < 0.9f) { Halt(); return; }                            // stops and aims first

        if (PlanarDistance(_investigate) > 3f && _stateTime < 9f) { MoveToward(_investigate, 0.4f); return; }

        Halt();
        Senses.headYaw = Mathf.Sin(Time.time * scanSpeed * 1.6f) * scanSweepDegrees;     // looks around the spot
        if (_stateTime > 4.5f || Senses.TopSuspicion < 0.1f) SetRobot(Senses.TopSuspicion > 0.25f ? RobotState.Search : RobotState.Patrol);
        if (_robot == RobotState.Search) { _searchOrigin = _investigate; _searchStep = 0; _searchPoint = _investigate; }
    }

    // ── Combat ────────────────────────────────────────────────────────────

    private void TickCombat(float dt, AISenses.Contact target, bool detected)
    {
        if (target == null) { BeginSearch(); return; }
        bool visible = target.visible;
        if (visible) _lastSeenTarget = Time.time;
        if (!visible && Time.time - _lastSeenTarget > 1.4f) { BeginSearch(); return; }

        Senses.headYaw = 0f;
        Vector3 aimPoint = PredictedAim(target);
        Controller.LookAt(aimPoint);
        Senses.viewPitch = 0f;
        float dist = Vector3.Distance(transform.position, target.transform.position);

        // Positioning: take cover and keep a medium distance
        _standTimer -= dt;
        if (_standTimer <= 0f) { _standPoint = ChooseStandPoint(target); _standTimer = 2.2f; }

        bool shooting = _burstPhase == BurstPhase.Telegraph || _burstPhase == BurstPhase.Burst;
        if (shooting) Halt();
        else if (dist < minCombatRange) MoveToward(transform.position - (target.transform.position - transform.position).normalized * 6f, 0.9f);   // back off
        else if (PlanarDistance(_standPoint) > 1.5f) MoveToward(_standPoint, 0.9f);
        else Halt();

        // Warned bursts
        _burstTimer -= dt;
        switch (_burstPhase)
        {
            case BurstPhase.Idle:
                if (visible && dist <= maxCombatRange * 1.3f && _burstTimer <= 0f) { _burstPhase = BurstPhase.Telegraph; _burstTimer = telegraphTime; }
                break;
            case BurstPhase.Telegraph:
                if (!visible) { _burstPhase = BurstPhase.Idle; _burstTimer = 0.4f; break; }
                if (_burstTimer <= 0f) { _burstPhase = BurstPhase.Burst; _shotsLeft = Mathf.Max(1, burstShots); _burstTimer = 0f; }
                break;
            case BurstPhase.Burst:
                if (_burstTimer <= 0f)
                {
                    if (_shotsLeft > 0 && muzzle != null)
                    {
                        FireBolt((PredictedAim(target) - muzzle.position).normalized);
                        _shotsLeft--;
                        _burstTimer = burstShotInterval * fireIntervalScale;
                    }
                    else { _burstPhase = BurstPhase.Cooldown; _burstTimer = burstCooldown * fireIntervalScale; }
                }
                break;
            case BurstPhase.Cooldown:
                if (_burstTimer <= 0f) _burstPhase = BurstPhase.Idle;
                break;
        }
    }

    /// <summary>Picks a nearby spot hidden from the target (cover) at a comfortable distance.</summary>
    private Vector3 ChooseStandPoint(AISenses.Contact target)
    {
        Vector3 best = transform.position; float bestScore = -999f;
        Vector3 tEye = target.transform.position + Up * 1.2f;
        float preferred = (minCombatRange + maxCombatRange) * 0.5f;
        for (int i = 0; i < 9; i++)
        {
            float angle = i * 40f;
            float radius = i == 0 ? 0f : Random.Range(4f, 9f);
            Vector3 cand = i == 0 ? transform.position : GroundPointFrom(transform.position, Quaternion.AngleAxis(angle, Up) * transform.forward, radius);
            float d = Vector3.Distance(cand, target.transform.position);
            bool covered = Physics.Linecast(tEye, cand + Up * 1.2f, out var hit, ~0, QueryTriggerInteraction.Ignore)
                           && hit.collider.GetComponentInParent<EnemyStats>() == null && hit.collider.GetComponentInParent<PlayerStats>() == null;
            float score = (covered ? 2.5f : 0f) - Mathf.Abs(d - preferred) * 0.12f - radius * 0.05f;
            if (d < minCombatRange * 0.8f) score -= 3f;
            if (score > bestScore) { bestScore = score; best = cand; }
        }
        return best;
    }

    // ── Search ────────────────────────────────────────────────────────────

    private void BeginSearch()
    {
        var t = Senses.Target;
        _searchOrigin = t != null ? t.lastSeenPos : (Senses.MostSuspicious != null ? Senses.MostSuspicious.lastSeenPos : transform.position);
        _searchStep = 0; _searchPoint = _searchOrigin;
        SetRobot(RobotState.Search);
    }

    private void TickSearch(float dt, bool detected)
    {
        if (detected && Senses.Target != null && Senses.Target.visible) { SetRobot(RobotState.Combat); return; }

        Senses.headYaw = Mathf.Sin(Time.time * scanSpeed * 1.4f) * (scanSweepDegrees + 20f);
        Senses.viewPitch = 4f;
        Controller.LookAt(null);
        if (_stateTime > Senses.memorySeconds) { SetRobot(RobotState.Patrol); return; }

        if (PlanarDistance(_searchPoint) < 2f)
        {
            // Widening circles around the last known position
            _searchStep++;
            float radius = 3f + 3.5f * _searchStep;
            _searchPoint = GroundPointFrom(_searchOrigin, Quaternion.AngleAxis(_searchStep * 70f, Up) * Vector3.ProjectOnPlane(transform.forward, Up), radius);
        }
        MoveToward(_searchPoint, 0.75f);
        if (Senses.TopSuspicion >= Senses.suspiciousAt && Senses.MostSuspicious != null && Senses.MostSuspicious.visible)
        {
            _investigate = Senses.MostSuspicious.lastSeenPos; SetRobot(RobotState.Suspicion);
        }
    }

    // ── Disabled ──────────────────────────────────────────────────────────

    private void EnterDisabled(float seconds)
    {
        if (kind != EnemyKind.Robot) return;
        _robot = RobotState.Disabled; _stateTime = 0f; _disabledEnd = Time.time + seconds;
        _burstPhase = BurstPhase.Idle;
        Controller.StopMoving(); Controller.controlEnabled = false;
        Senses.Clear();
    }

    private void TickDisabled(float dt)
    {
        if (kind == EnemyKind.Robot)
        {
            _sparkTimer -= dt;
            if (_sparkTimer <= 0f)
            {
                _sparkTimer = 0.4f;
                DeathBurstFx.Play(transform.position + Up * 1.2f, new Color(1f, 0.85f, 0.3f), Up, 0.35f);       // sparks
            }
            if (_laser != null) _laser.enabled = false;
        }
        else if (kind == EnemyKind.Drone) TickDroneDisabled(dt);
    }

    private void UpdateDisabledEnd()
    {
        if (_robot == RobotState.Disabled && !Stats.IsDisabled)
        {
            Controller.controlEnabled = true;
            _robot = RobotState.Suspicion; _stateTime = 0f; _investigate = transform.position + transform.forward * 4f;
        }
    }

    // ── Reactions / alerts ────────────────────────────────────────────────

    private void OnHurt(PlayerStats attacker)
    {
        if (kind == EnemyKind.Robot && _robot != RobotState.Combat && _robot != RobotState.Disabled)
        {
            SetRobot(RobotState.Combat);
        }
        if (kind == EnemyKind.Zombie) ZombieHurt(attacker);
    }

    /// <summary>Another robot spotted someone: come and take a look.</summary>
    public void ReceiveAlert(Vector3 point)
    {
        if (kind != EnemyKind.Robot || !Stats.IsAlive) return;
        Senses.Alert(point, 0.6f);
        if (_robot == RobotState.Patrol || _robot == RobotState.Search)
        {
            _investigate = point; SetRobot(RobotState.Suspicion);
        }
    }

    // ── Laser ─────────────────────────────────────────────────────────────

    private void UpdateLaser()
    {
        if (_laser == null || muzzle == null) return;
        _laser.enabled = _robot != RobotState.Disabled;
        if (!_laser.enabled) return;

        Vector3 origin = muzzle.position;
        Vector3 dir;
        Color c;
        switch (_robot)
        {
            case RobotState.Combat:
                var t = Senses.Target;
                dir = t != null ? (PredictedAim(t) - origin).normalized : Senses.ViewDirection;
                c = (_burstPhase == BurstPhase.Telegraph || _burstPhase == BurstPhase.Burst) ? LaserRed : LaserAmber;
                if (_burstPhase == BurstPhase.Telegraph) c = Color.Lerp(LaserAmber, LaserRed, Mathf.PingPong(Time.time * 14f, 1f));
                break;
            case RobotState.Suspicion:
                dir = (Senses.HeardRecently(2f) ? Senses.HeardPoint : _investigate) - origin;
                dir = dir.sqrMagnitude > 0.01f ? dir.normalized : Senses.ViewDirection;
                c = LaserAmber;
                break;
            default:
                dir = Senses.ViewDirection; c = LaserCyan;
                break;
        }

        float len = 28f;
        if (Physics.Raycast(origin, dir, out var hit, len, ~0, QueryTriggerInteraction.Ignore) && !hit.collider.transform.IsChildOf(transform)) len = hit.distance;
        _laser.SetPosition(0, origin);
        _laser.SetPosition(1, origin + dir * len);
        _laser.startColor = c; _laser.endColor = new Color(c.r, c.g, c.b, 0.15f);
    }

}

/// <summary>Robots warn each other: when one spots someone, the others nearby go to investigate.</summary>
public static class AlertNetwork
{
    private static readonly List<EnemyAI> _members = new List<EnemyAI>();
    public static void Register(EnemyAI ai) { if (!_members.Contains(ai)) _members.Add(ai); }
    public static void Unregister(EnemyAI ai) => _members.Remove(ai);

    public static void Broadcast(EnemyAI from, Vector3 origin, float radius, Vector3 point)
    {
        _members.RemoveAll(m => m == null);
        foreach (var m in _members)
            if (m != from && Vector3.Distance(m.transform.position, origin) <= radius) m.ReceiveAlert(point);
    }
}
}
