using System.Collections.Generic;
using UnityEngine;

namespace OrbitRush
{

/// <summary>
/// Aerial hunter, the only enemy that travels between planets. A spotlight sweeps the ground while it orbits its
/// planet; once it finds a player it fixes the light on them, MARKS them on everyone's radar, tracks from above and
/// behind, makes shooting dives, retreats to recharge and — nearly destroyed —
/// beeps and rams its target. Drones in a group move as a flock and attack from different angles. They follow a
/// player through open space.
/// </summary>
public partial class EnemyAI
{
    public enum DroneState { Patrol, Track, Attack, Retreat, Kamikaze }
    private enum DivePhase { Approach, Burst, Climb, Cooldown }

    [Header("Drone")]
    public float patrolSpeed = 7f;
    public float trackSpeed = 14f;
    public float spaceSpeed = 26f;
    public float diveSpeed = 24f;
    public int diveShots = 4;
    public float diveShotInterval = 0.1f;
    public float markSeconds = 7f;
    public float kamikazeHealthFraction = 0.25f;
    public float kamikazeDamage = 55f;
    public float kamikazeRadius = 7f;
    public float energyMax = 100f;

    private DroneState _drone = DroneState.Patrol;
    private DivePhase _dive = DivePhase.Cooldown;
    private float _droneStateTime, _diveTimer, _markTimer, _energy;
    private int _diveShotsLeft;
    private Vector3 _orbitAxis;
    private Light _spot;
    private bool _kamikazeBeeped, _kamikazeCharging;
    private float _kamikazeTime;
    private Vector3 _climbPoint;
    private float _retreatTime;
    private AudioSource _beep;

    private void StartDrone()
    {
        _energy = energyMax;
        _orbitAxis = Random.onUnitSphere;
        BuildSpotlight();
        Controller.freeFlight = false;
    }

    private void BuildSpotlight()
    {
        var go = new GameObject("Spotlight");
        go.transform.SetParent(transform, false);
        go.transform.localPosition = new Vector3(0f, -0.2f, 0.5f);
        _spot = go.AddComponent<Light>();
        _spot.type = LightType.Spot;
        _spot.spotAngle = 45f; _spot.innerSpotAngle = 20f;
        _spot.range = 60f; _spot.intensity = 6f;
        _spot.color = new Color(0.7f, 1f, 1f);
        _spot.shadows = LightShadows.None;
    }

    // ── Main tick ─────────────────────────────────────────────────────────

    private void TickDrone(float dt)
    {
        _droneStateTime += dt;
        _energy = Mathf.Min(energyMax, _energy + (_drone == DroneState.Retreat ? 25f : 6f) * dt);
        _markTimer -= dt;

        var target = Senses.Target;
        bool detected = target != null && Senses.IsDetected(target);

        // Nearly destroyed: ram the target
        float hp = Stats.maxHealth > 0f ? Stats.Health / Stats.maxHealth : 1f;
        if (hp <= kamikazeHealthFraction && _drone != DroneState.Kamikaze && target != null && !_kamikazeCharging)
        {
            _kamikazeCharging = true; _kamikazeTime = 0f; SetDrone(DroneState.Kamikaze);
        }

        switch (_drone)
        {
            case DroneState.Patrol: TickDronePatrol(dt, detected); break;
            case DroneState.Track: TickDroneTrack(dt, target, detected); break;
            case DroneState.Attack: TickDroneAttack(dt, target, detected); break;
            case DroneState.Retreat: TickDroneRetreat(dt, target); break;
            case DroneState.Kamikaze: TickKamikaze(dt, target); break;
        }
        AimSpotlight(target);
    }

    private void SetDrone(DroneState s)
    {
        if (_drone == s) return;
        _drone = s; _droneStateTime = 0f;
        if (s == DroneState.Attack) { _dive = DivePhase.Approach; _diveTimer = 0f; }
    }

    // ── Patrol: orbit the planet sweeping the ground ──────────────────────

    private void TickDronePatrol(float dt, bool detected)
    {
        Controller.freeFlight = false;
        Controller.aiSpeedFactor = patrolSpeed / Mathf.Max(0.1f, Controller.moveSpeed);
        Senses.viewPitch = 35f;
        Senses.headYaw = Mathf.Sin(Time.time * 0.7f) * 45f;           // the light wanders left and right as it flies

        Vector3 tangent = Vector3.Cross(_orbitAxis, Up);
        tangent = Vector3.ProjectOnPlane(tangent, Up);
        if (tangent.sqrMagnitude < 0.01f) tangent = transform.forward;
        Controller.SetMoveDirection(tangent.normalized);
        Controller.LookAt(null);

        if (detected || (Senses.TopSuspicion >= Senses.suspiciousAt && Senses.MostSuspicious != null && Senses.MostSuspicious.visible))
        {
            if (detected) { SetDrone(DroneState.Track); _markTimer = 0f; }
        }
    }

    // ── Track: light on the target, from above and behind; marks ────

    private void TickDroneTrack(float dt, AISenses.Contact target, bool detected)
    {
        if (target == null || !Stats.IsAlive)
        {
            if (Senses.SecondsSinceSeen(Senses.MostSuspicious) > Senses.memorySeconds) SetDrone(DroneState.Patrol);
            Controller.StopMoving();
            return;
        }
        Senses.headYaw = 0f; Senses.viewPitch = 20f;

        // Marking: reveals the player to everyone on the radar
        if (_markTimer <= 0f && target.stats != null)
        {
            RadarMarks.Mark(target.stats, markSeconds, gameObject);
            _markTimer = markSeconds + 2f;
        }

        // Past the memory: back to orbiting
        if (!target.visible && Senses.SecondsSinceSeen(target) > Senses.memorySeconds) { SetDrone(DroneState.Patrol); return; }

        Vector3 goal = TrackingGoal(target);
        FlyDroneTo(goal, trackSpeed, target);
        Controller.LookAt(target.transform.position);

        // After a spell of tracking: a diving attack (if there is energy)
        if (_droneStateTime > 2.4f && target.visible && _energy > 25f && target.distance < 70f) SetDrone(DroneState.Attack);
    }

    /// <summary>Hover spot above and behind the target; each flock member takes a different angle.</summary>
    private Vector3 TrackingGoal(AISenses.Contact target)
    {
        Vector3 tUp = target.transform.up;
        Vector3 behind = -Vector3.ProjectOnPlane(target.transform.forward, tUp).normalized;
        if (behind.sqrMagnitude < 0.1f) behind = -transform.forward;
        FlockSlot(out int idx, out int count, out Vector3 separation);
        float angle = count > 1 ? 360f / count * idx : 0f;
        Vector3 ring = Quaternion.AngleAxis(angle, tUp) * behind * 12f;
        return target.transform.position + ring + tUp * 9f + separation;
    }

    /// <summary>Flies the drone toward a point: hovering on the planet when the target is on foot, free flight in space.</summary>
    private void FlyDroneTo(Vector3 goal, float speed, AISenses.Contact target)
    {
        var pc = target != null && target.stats != null ? target.stats.GetComponent<PlayerController>() : null;
        bool targetFlying = false;
        bool otherPlanet = pc != null && pc.CurrentPlanet != null && pc.CurrentPlanet != Controller.CurrentPlanet;
        float distance = target != null ? Vector3.Distance(transform.position, target.transform.position) : 0f;
        bool free = targetFlying || otherPlanet || distance > 110f;

        Controller.freeFlight = free;
        if (free) { Controller.FlyTo(goal, free && (targetFlying || otherPlanet) ? spaceSpeed : speed); }
        else
        {
            Controller.FlyTo(null, 0f);
            Controller.aiSpeedFactor = speed / Mathf.Max(0.1f, Controller.moveSpeed);
            Vector3 flat = Vector3.ProjectOnPlane(goal - transform.position, Up);
            Controller.SetMoveDirection(flat.magnitude > 1.5f ? flat.normalized : Vector3.zero);
        }
    }

    // ── Attack: shooting dives ────────────────────────────────────────────

    private void TickDroneAttack(float dt, AISenses.Contact target, bool detected)
    {
        if (target == null) { SetDrone(DroneState.Patrol); return; }
        Controller.freeFlight = true;
        _diveTimer -= dt;
        Controller.LookAt(target.transform.position);

        switch (_dive)
        {
            case DivePhase.Approach:
            {
                Vector3 aim = target.transform.position + target.transform.up * 1.2f;
                Controller.FlyTo(aim, diveSpeed);
                if (target.distance < 14f || _droneStateTime > 5f) { _dive = DivePhase.Burst; _diveShotsLeft = diveShots; _diveTimer = 0f; }
                break;
            }
            case DivePhase.Burst:
                Controller.FlyTo(target.transform.position + target.transform.up * 1.2f, diveSpeed * 0.6f);
                if (_diveTimer <= 0f)
                {
                    if (_diveShotsLeft > 0 && muzzle != null)
                    {
                        FireBolt((PredictedAim(target) - muzzle.position).normalized);
                        _diveShotsLeft--; _diveTimer = diveShotInterval * fireIntervalScale;
                    }
                    else
                    {
                        _energy -= 25f;
                        Vector3 away = (transform.position - target.transform.position).normalized;
                        _climbPoint = transform.position + Up * 16f + Vector3.ProjectOnPlane(away, Up) * 18f;
                        _dive = DivePhase.Climb; _diveTimer = 1.6f;
                    }
                }
                break;
            case DivePhase.Climb:
                Controller.FlyTo(_climbPoint, diveSpeed * 0.8f);
                if (_diveTimer <= 0f) { _dive = DivePhase.Cooldown; _diveTimer = 1.5f; }
                break;
            case DivePhase.Cooldown:
                if (_diveTimer <= 0f)
                {
                    if (_energy < 12f) SetDrone(DroneState.Retreat);
                    else SetDrone(DroneState.Track);
                }
                else FlyDroneTo(TrackingGoal(target), trackSpeed, target);
                break;
        }
    }

    // ── Retreat: climb away and recharge ──────────────────────────────────

    private void TickDroneRetreat(float dt, AISenses.Contact target)
    {
        Controller.freeFlight = true;
        Vector3 from = target != null ? target.transform.position : transform.position;
        Vector3 away = Vector3.ProjectOnPlane(transform.position - from, Up).normalized;
        Controller.FlyTo(transform.position + Up * 6f + away * 12f, trackSpeed);
        if (_droneStateTime > 6f && _energy >= energyMax * 0.6f) SetDrone(target != null ? DroneState.Track : DroneState.Patrol);
    }

    // ── Kamikaze ──────────────────────────────────────────────────────────

    private void TickKamikaze(float dt, AISenses.Contact target)
    {
        _kamikazeTime += dt;
        Controller.freeFlight = true;
        if (target == null) { _kamikazeCharging = false; SetDrone(DroneState.Patrol); return; }

        if (_kamikazeTime < 1.2f)
        {
            // Warning beeps while it hovers in place
            Controller.FlyTo(transform.position, 0f);
            if (!_kamikazeBeeped || Mathf.Repeat(_kamikazeTime, 0.2f) < dt) PlayBeep();
            _kamikazeBeeped = true;
            return;
        }
        Controller.FlyTo(target.transform.position + target.transform.up * 0.8f, 24f);
        Controller.LookAt(target.transform.position);
        if (target.distance < 2.4f || _kamikazeTime > 6f) ExplodeKamikaze();
    }

    private void ExplodeKamikaze()
    {
        Vector3 centre = transform.position;
        var hit = new HashSet<IDamageable>();
        foreach (var col in Physics.OverlapSphere(centre, kamikazeRadius, ~0, QueryTriggerInteraction.Ignore))
        {
            var d = col.GetComponentInParent<IDamageable>();
            if (d == null || d is EnemyStats || !hit.Add(d)) continue;
            float falloff = 1f - Mathf.Clamp01(Vector3.Distance(centre, col.ClosestPoint(centre)) / kamikazeRadius) * 0.6f;
            d.RegisterHit(centre);                                // HUD damage-direction arc (and shield ripple)
            d.TakeDamage(kamikazeDamage * falloff, null);
        }
        DeathBurstFx.Play(centre, new Color(1f, 0.4f, 0.1f), Up, 2.2f);
        NoiseSystem.Emit(centre, Loudness.Explosion, gameObject);
        CameraRig.AddTrauma(0.5f);
        Stats.TakeDamage(1e9f, null);
    }

    private void PlayBeep()
    {
        if (_beep == null)
        {
            _beep = gameObject.AddComponent<AudioSource>();
            _beep.spatialBlend = 1f; _beep.maxDistance = 60f; _beep.playOnAwake = false;
            int rate = 22050; int samples = rate / 8;
            var data = new float[samples];
            for (int i = 0; i < samples; i++) data[i] = Mathf.Sin(2f * Mathf.PI * 1800f * i / rate) * 0.4f * (1f - (float)i / samples);
            var clip = AudioClip.Create("beep", samples, 1, rate, false);
            clip.SetData(data, 0);
            _beep.clip = clip;
        }
        _beep.Play();
    }

    // ── Flock ─────────────────────────────────────────────────────────────

    /// <summary>Position of this drone within its group (so each takes a different angle) and a push away from close mates.</summary>
    private void FlockSlot(out int index, out int count, out Vector3 separation)
    {
        index = 0; count = 1; separation = Vector3.zero;
        var mates = new List<EnemyAI>();
        foreach (var other in All)
        {
            if (other == null || other.kind != EnemyKind.Drone || !other.Stats.IsAlive) continue;
            if (Vector3.Distance(other.transform.position, transform.position) <= 80f) mates.Add(other);
        }
        if (mates.Count == 0) return;
        mates.Sort((a, b) => a.GetInstanceID().CompareTo(b.GetInstanceID()));
        count = Mathf.Min(4, mates.Count);
        index = Mathf.Max(0, mates.IndexOf(this)) % count;
        foreach (var m in mates)
        {
            if (m == this) continue;
            Vector3 d = transform.position - m.transform.position;
            if (d.magnitude < 6f && d.sqrMagnitude > 0.01f) separation += d.normalized * (6f - d.magnitude);
        }
    }

    private void AimSpotlight(AISenses.Contact target)
    {
        if (_spot == null) return;
        if (_drone != DroneState.Patrol && target != null)
        {
            _spot.transform.rotation = Quaternion.Slerp(_spot.transform.rotation, Quaternion.LookRotation((target.transform.position - _spot.transform.position).normalized), 0.2f);
            _spot.intensity = 9f; _spot.color = _drone == DroneState.Kamikaze ? new Color(1f, 0.2f, 0.1f) : new Color(1f, 0.55f, 0.85f);
        }
        else
        {
            _spot.transform.rotation = Quaternion.LookRotation(Senses.ViewDirection);
            _spot.intensity = 6f; _spot.color = new Color(0.7f, 1f, 1f);
        }
    }

    private void TickDroneDisabled(float dt) { }
}

/// <summary>
/// Players revealed by drones. The radar (and a warning banner for the marked player) reads this.
/// </summary>
public static class RadarMarks
{
    private static readonly Dictionary<PlayerStats, float> _until = new Dictionary<PlayerStats, float>();

    public static void Mark(PlayerStats player, float seconds, GameObject by = null)
    {
        if (player == null) return;
        bool already = IsMarked(player);
        _until[player] = Time.time + seconds;
        if (!already && player.GetComponent<PlayerController>() != null && HUD.Instance != null)
            HUD.Instance.ShowEvent("UN DRON TE HA MARCADO", 2.5f);
    }

    public static bool IsMarked(PlayerStats player) => player != null && _until.TryGetValue(player, out var t) && Time.time < t;
    public static float RemainingFor(PlayerStats player) => player != null && _until.TryGetValue(player, out var t) ? Mathf.Max(0f, t - Time.time) : 0f;

    public static IEnumerable<PlayerStats> Marked()
    {
        foreach (var kv in _until) if (kv.Key != null && Time.time < kv.Value) yield return kv.Key;
    }
}
}
