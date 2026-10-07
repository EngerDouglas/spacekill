using System.Collections.Generic;
using UnityEngine;

namespace OrbitRush
{

/// <summary>
/// Perception shared by every enemy type: a vision cone (distance, angle, line of sight, the planet's horizon
/// blocks it), a suspicion meter that fills while it sees someone, hearing through <see cref="NoiseSystem"/>,
/// memory of where it last saw each contact, and target selection with hysteresis.
/// </summary>
public class AISenses : MonoBehaviour
{
    public class Contact
    {
        public Transform transform;
        public PlayerStats stats;
        public SupportRobot robot;
        public Rigidbody body;
        public float suspicion;
        public bool visible, peripheral, heard;
        public float distance;
        public Vector3 lastSeenPos, lastSeenVel;
        public float lastSeenTime = -999f;
        public float lastDetectedTime = -999f;

        public bool Alive => stats != null ? stats.IsAlive : (robot != null && robot.IsAlive);
        public Vector3 Velocity => body != null ? body.linearVelocity : Vector3.zero;
        public float HealthPercent => stats != null ? stats.HealthPercent : (robot != null ? robot.HealthPercent : 1f);
    }

    [Header("Vision")]
    public float viewDistance = 40f;
    [Tooltip("Full angle of the vision cone in degrees.")]
    public float viewAngle = 110f;
    [Tooltip("Beyond this fraction of the half-angle is peripheral vision (slower detection).")]
    [Range(0.2f, 1f)] public float peripheralStart = 0.6f;
    public float eyeHeight = 1f;
    [Tooltip("Within this range detection is instant, even from behind.")]
    public float instantRange = 3f;

    [Tooltip("Anything this close and below the enemy is seen whatever the cone says (the drone's spotlight footprint). 0 = off.")]
    public float footprintRadius = 0f;

    [Header("Hearing")]
    [Tooltip("Multiplier on noise loudness (1 = normal, drones are deaf-ish, zombies excellent).")]
    public float hearing = 1f;

    [Header("Suspicion")]
    public float suspiciousAt = 0.35f;
    public float detectedAt = 1f;
    public float baseFillRate = 0.7f;
    public float decayRate = 0.3f;

    [Header("Memory")]
    public float memorySeconds = 10f;

    [Header("Heat sensor (drones)")]
    public bool heatSensor = false;
    public float heatRange = 120f;

    /// <summary>Where the enemy is looking from (defaults to the pivot raised by eyeHeight).</summary>
    public Transform eye;
    /// <summary>Extra yaw (degrees) of the "head" relative to the body: the robot's scanning sweep.</summary>
    public float headYaw;
    /// <summary>Pitch (degrees, positive = down) added to the view direction (the drone's ground-sweeping spotlight).</summary>
    public float viewPitch;

    public readonly List<Contact> Contacts = new List<Contact>();
    /// <summary>Position of the loudest recent noise, valid while <see cref="HeardRecently"/>.</summary>
    public Vector3 HeardPoint { get; private set; }
    public float HeardTime { get; private set; } = -999f;
    public float HeardLoudness { get; private set; }
    public bool HeardRecently(float seconds = 3f) => Time.time - HeardTime < seconds;

    /// <summary>Currently selected target (most worth attacking among the detected contacts).</summary>
    public Contact Target { get; private set; }
    /// <summary>Highest suspicion among all contacts.</summary>
    public float TopSuspicion { get; private set; }
    public Contact MostSuspicious { get; private set; }

    private EnemyController _controller;
    private EnemyStats _stats;
    private float _selectedAt;
    private float _heardSuspicionCooldown;

    void Awake()
    {
        _controller = GetComponent<EnemyController>();
        _stats = GetComponent<EnemyStats>();
    }

    private Vector3 EyePosition => eye != null ? eye.position : transform.position + UpDir * eyeHeight * transform.lossyScale.y;
    private Vector3 UpDir => _controller != null ? _controller.PlanetUp : transform.up;

    /// <summary>Direction the vision cone points: the body's forward, turned by the head yaw and pitch.</summary>
    public Vector3 ViewDirection
    {
        get
        {
            Vector3 up = UpDir;
            Vector3 fwd = Vector3.ProjectOnPlane(transform.forward, up);
            if (fwd.sqrMagnitude < 1e-4f) fwd = transform.forward;
            fwd = Quaternion.AngleAxis(headYaw, up) * fwd.normalized;
            Vector3 right = Vector3.Cross(up, fwd);
            return Quaternion.AngleAxis(viewPitch, right) * fwd;
        }
    }

    // ── Tick ──────────────────────────────────────────────────────────────

    public void Tick(float dt)
    {
        RefreshContacts();
        TopSuspicion = 0f; MostSuspicious = null;
        _heardSuspicionCooldown -= dt;

        // Hearing: the loudest recent noise any contact (or anything) made
        if (NoiseSystem.TryHear(transform.position, hearing, 0.6f, out var noise, gameObject))
        {
            if (Time.time - HeardTime > 0.4f || noise.loudness >= HeardLoudness)
            {
                HeardPoint = noise.position; HeardTime = Time.time; HeardLoudness = noise.loudness;
            }
            if (_heardSuspicionCooldown <= 0f)
            {
                _heardSuspicionCooldown = 0.5f;
                // A noise makes whoever caused it suspicious (not instantly detected)
                foreach (var c in Contacts)
                {
                    if (c.transform == null || noise.source == null) continue;
                    if (noise.source != c.transform.gameObject) continue;
                    float loud = Mathf.Clamp01(noise.loudness * hearing / Mathf.Max(1f, Vector3.Distance(transform.position, noise.position)) * 0.12f);
                    c.suspicion = Mathf.Min(detectedAt * 0.99f, c.suspicion + 0.2f + loud * 0.5f);
                    c.heard = true;
                }
            }
        }

        foreach (var c in Contacts)
        {
            if (c.transform == null || !c.Alive) { c.suspicion = 0f; c.visible = false; continue; }
            UpdateContact(c, dt);
            if (c.suspicion > TopSuspicion) { TopSuspicion = c.suspicion; MostSuspicious = c; }
        }

        SelectTarget();
    }

    private void RefreshContacts()
    {
        // Candidates: every player plus the player's support robot (refreshed only when the set changes)
        var gm = GameManager.Instance;
        int count = 0;
        if (gm != null) foreach (var p in gm.Players) { Ensure(p.transform, p, null); count++; }
        if (SupportRobot.Instance != null) Ensure(SupportRobot.Instance.transform, null, SupportRobot.Instance);
        Contacts.RemoveAll(c => c.transform == null);
    }

    private void Ensure(Transform t, PlayerStats stats, SupportRobot robot)
    {
        foreach (var c in Contacts) if (c.transform == t) return;
        Contacts.Add(new Contact { transform = t, stats = stats, robot = robot, body = t.GetComponent<Rigidbody>() });
    }

    private void UpdateContact(Contact c, float dt)
    {
        Vector3 eyePos = EyePosition;
        Vector3 chest = ChestPoint(c);
        Vector3 to = chest - eyePos;
        float d = to.magnitude;
        c.distance = d;

        bool seen = false, peripheral = false;
        float reach = viewDistance;
        if (d <= instantRange)
        {
            seen = true;                                                      // pressed against the enemy: detected even from behind
            c.suspicion = detectedAt; c.lastDetectedTime = Time.time;
        }
        else if (d <= reach)
        {
            float angle = Vector3.Angle(ViewDirection, to);
            float half = viewAngle * 0.5f;
            if (angle <= half && HasLineOfSight(eyePos, chest, c)) { seen = true; peripheral = angle > half * peripheralStart; }
        }

        if (!seen && footprintRadius > 0f && d <= footprintRadius && Vector3.Dot(to, -UpDir) > 0f && HasLineOfSight(eyePos, chest, c))
            seen = true;

        // Heat sensor: a lit jetpack shows up from far away, whatever the cone says
        float heatBoost = 1f;
        if (!seen && heatSensor && c.stats != null && d <= heatRange)
        {
            var pc = c.stats.GetComponent<PlayerController>();
            if (pc != null && (pc.IsJetpacking || pc.Phase == JetpackPhase.Charging) && HasLineOfSight(eyePos, chest, c, ignoreHorizon: false))
            { seen = true; heatBoost = 3f; }
        }

        c.visible = seen; c.peripheral = peripheral;
        if (seen)
        {
            c.lastSeenPos = c.transform.position; c.lastSeenVel = c.Velocity; c.lastSeenTime = Time.time;
            float near = 1f + 2f * (1f - Mathf.Clamp01(d / Mathf.Max(1f, reach)));
            float rate = baseFillRate * near * ActionFactor(c) * heatBoost * (peripheral ? 0.5f : 1f);
            c.suspicion = Mathf.Min(detectedAt, c.suspicion + rate * dt);
            if (c.suspicion >= detectedAt) c.lastDetectedTime = Time.time;
        }
        else
        {
            c.suspicion = Mathf.Max(0f, c.suspicion - decayRate * dt);
        }
    }

    /// <summary>How alarming the contact's behaviour is: running, shooting and jetpacking fill the meter faster, crouching slower.</summary>
    private float ActionFactor(Contact c)
    {
        float f = 1f;
        var pc = c.stats != null ? c.stats.GetComponent<PlayerController>() : null;
        if (pc != null)
        {
            if (pc.IsRunning) f *= 1.6f;
            if (pc.IsFlying || pc.IsJetpacking) f *= 2f;
            if (pc.IsCrouching) f *= 0.5f;
        }
        if (NoiseSystem.TryHear(transform.position, 1000f, 1f, out var n) && n.source == c.transform.gameObject && n.loudness >= Loudness.Shoot) f *= 2f;
        return f;
    }

    private Vector3 ChestPoint(Contact c)
    {
        var col = c.transform.GetComponent<Collider>();
        return col != null ? col.bounds.center : c.transform.position + c.transform.up * 0.5f;
    }

    /// <summary>Nothing solid between the eye and the target; the curve of the planet also hides what lies beyond the horizon.</summary>
    private bool HasLineOfSight(Vector3 from, Vector3 to, Contact c, bool ignoreHorizon = false)
    {
        // Horizon: the segment must not dip below the planet's surface
        var planet = _controller != null ? _controller.CurrentPlanet : null;
        if (!ignoreHorizon && planet != null && planet.shape == PlanetGravity.GravityShape.Sphere && SegmentHitsSphere(from, to, planet.transform.position, planet.radius * 0.96f))
            return false;

        Vector3 dir = to - from;
        float len = dir.magnitude;
        if (len < 0.01f) return true;
        var hits = Physics.RaycastAll(from, dir / len, len, ~0, QueryTriggerInteraction.Ignore);
        foreach (var h in hits)
        {
            Transform t = h.collider.transform;
            if (t.IsChildOf(transform) || t == transform) continue;                 // myself
            if (c != null && (t == c.transform || t.IsChildOf(c.transform))) continue; // the target
            if (t.GetComponentInParent<EnemyStats>() != null) continue;            // other enemies don't block sight
            return false;
        }
        return true;
    }

    private static bool SegmentHitsSphere(Vector3 a, Vector3 b, Vector3 centre, float radius)
    {
        Vector3 ab = b - a;
        float t = Mathf.Clamp01(Vector3.Dot(centre - a, ab) / Mathf.Max(1e-6f, ab.sqrMagnitude));
        return Vector3.Distance(a + ab * t, centre) < radius;
    }

    /// <summary>
    /// Target choice among detected contacts: close, visible, low-health and hard-hitting ones score higher, and the
    /// current target is only dropped for one that is clearly better.
    /// </summary>
    private void SelectTarget()
    {
        Contact best = null; float bestScore = 0f;
        foreach (var c in Contacts)
        {
            if (c.transform == null || !c.Alive) continue;
            bool known = IsDetected(c);
            if (!known) continue;
            float s = Score(c);
            if (s > bestScore) { bestScore = s; best = c; }
        }

        if (Target != null && (Target.transform == null || !Target.Alive)) Target = null;
        if (Target == null) { Target = best; _selectedAt = Time.time; return; }
        if (best != null && best != Target && Time.time - _selectedAt > 1.5f && bestScore > Score(Target) * 1.35f)
        {
            Target = best; _selectedAt = Time.time;
        }
        else if (best == null && Time.time - Target.lastSeenTime > memorySeconds)
        {
            Target = null;      // forgotten
        }
    }

    private float Score(Contact c)
    {
        float proximity = 1f - Mathf.Clamp01(c.distance / Mathf.Max(1f, viewDistance * 1.5f));
        float damage = _stats != null && c.stats != null ? Mathf.Clamp01(_stats.DamageFrom(c.stats) / 60f) : 0f;
        float lowHealth = 1f - Mathf.Clamp01(c.HealthPercent);
        return proximity * 1.0f + damage * 1.2f + (c.visible ? 0.6f : 0f) + lowHealth * 0.4f + 0.01f;
    }

    /// <summary>Debug text: why the first contact is (not) seen — distance, cone angle, horizon and what blocks the line.</summary>
    public string Explain(Contact c)
    {
        if (c == null || c.transform == null) return "no contact";
        Vector3 eyePos = EyePosition; Vector3 chest = ChestPoint(c);
        Vector3 to = chest - eyePos;
        float angle = Vector3.Angle(ViewDirection, to);
        var planet = _controller != null ? _controller.CurrentPlanet : null;
        bool horizon = planet != null && planet.shape == PlanetGravity.GravityShape.Sphere && SegmentHitsSphere(eyePos, chest, planet.transform.position, planet.radius * 0.96f);
        string blocker = "none";
        foreach (var h in Physics.RaycastAll(eyePos, to.normalized, to.magnitude, ~0, QueryTriggerInteraction.Ignore))
        {
            Transform t = h.collider.transform;
            if (t.IsChildOf(transform) || t == transform || t == c.transform || t.IsChildOf(c.transform) || t.GetComponentInParent<EnemyStats>() != null) continue;
            blocker = t.name; break;
        }
        return $"dist={to.magnitude:F1}/{viewDistance} angle={angle:F0}/{viewAngle * 0.5f:F0} horizonBlocks={horizon} blockedBy={blocker} visible={c.visible} suspicion={c.suspicion:F2} eyeH={eyePos.y:F1}";
    }

    /// <summary>Detected now, or detected within the memory span (the enemy still knows about them).</summary>
    public bool IsDetected(Contact c) => c != null && (c.suspicion >= detectedAt || Time.time - c.lastDetectedTime < memorySeconds);
    public bool IsSuspicious(Contact c) => c != null && c.suspicion >= suspiciousAt;
    public float SecondsSinceSeen(Contact c) => c == null ? 999f : Time.time - c.lastSeenTime;

    /// <summary>Forget everything (respawn, disable).</summary>
    public void Clear()
    {
        foreach (var c in Contacts) { c.suspicion = 0f; c.visible = false; c.lastSeenTime = -999f; c.lastDetectedTime = -999f; }
        Target = null; HeardTime = -999f;
    }

    /// <summary>Tell this enemy where something was seen/heard (alert network, horde): it turns suspicious about that spot.</summary>
    public void Alert(Vector3 point, float strength = 0.5f)
    {
        HeardPoint = point; HeardTime = Time.time; HeardLoudness = Mathf.Max(HeardLoudness, 40f);
        foreach (var c in Contacts)
            if (Vector3.Distance(c.transform.position, point) < 40f) c.suspicion = Mathf.Max(c.suspicion, suspiciousAt + strength * 0.3f);
    }
}
}
