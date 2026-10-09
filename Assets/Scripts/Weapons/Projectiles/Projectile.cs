using UnityEngine;

namespace OrbitRush
{

/// <summary>
/// A projectile that is optionally affected by planetary gravity.
/// Works for bullets, plasma bolts, and orbital rounds.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class Projectile : MonoBehaviour
{
    [Header("Damage")]
    public float damage = 12f;
    public float splashRadius = 0f;     // 0 = no splash

    [Header("Physics")]
    public float speed = 90f;
    public bool affectedByGravity = false;
    [Range(0f, 2f)] public float gravityMultiplier = 1f;
    public float lifetime = 8f;

    [Header("Orbital")]
    public bool orbital = false;           // orbits a planet before falling
    public float orbitalDuration = 2f;

    [Header("Impact effect")]
    [Tooltip("Resources path of the hit-effect model (set by WeaponModels). Empty = no effect.")]
    public string impactResource;
    public float impactScale = 2.5f;

    private Rigidbody _rb;
    private Collider _collider;
    private static readonly System.Collections.Generic.List<Projectile> Active = new System.Collections.Generic.List<Projectile>();
    private float _timer;
    private float _orbitalTimer;
    private GameObject _owner;
    private PlanetGravity _targetPlanet;  // for orbital shots

    void Awake()
    {
        _rb = GetComponent<Rigidbody>();
        _rb.useGravity = false;
        _rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        _collider = GetComponent<Collider>();
        Active.Add(this);
    }

    void OnDestroy() => Active.Remove(this);

    public void Init(Vector3 direction, GameObject owner, PlanetGravity targetPlanet = null)
    {
        _owner = owner;
        _targetPlanet = targetPlanet;
        _rb.linearVelocity = direction.normalized * speed;
        IgnoreFriendlyColliders();
    }

    /// <summary>
    /// Projectiles must not collide with each other or with whoever fired them. A shotgun spawns 8 pellets on
    /// the same spot — they used to collide with each other and vanish at the muzzle — and rounds leaving the
    /// barrel were also nudging their own shooter.
    /// </summary>
    private void IgnoreFriendlyColliders()
    {
        if (_collider == null) return;

        foreach (var other in Active)
            if (other != null && other != this && other._collider != null)
                Physics.IgnoreCollision(_collider, other._collider);

        if (_owner != null)
            foreach (var c in _owner.GetComponentsInChildren<Collider>())
                Physics.IgnoreCollision(_collider, c);
    }

    void FixedUpdate()
    {
        _timer += Time.fixedDeltaTime;
        if (_timer >= lifetime) { Destroy(gameObject); return; }

        if (orbital)
        {
            HandleOrbital();
            return;
        }

        if (affectedByGravity)
        {
            Vector3 gravity = PlanetBody.GravityAt(transform.position);
            _rb.AddForce(gravity * gravityMultiplier, ForceMode.Acceleration);
        }
    }

    private void HandleOrbital()
    {
        _orbitalTimer += Time.fixedDeltaTime;

        // First phase: orbit around target planet
        if (_orbitalTimer < orbitalDuration && _targetPlanet != null)
        {
            Vector3 toPlanet = (_targetPlanet.transform.position - transform.position).normalized;
            float orbitGrav = _targetPlanet.GetGravityStrength(transform.position) * 3f;
            _rb.AddForce(toPlanet * orbitGrav, ForceMode.Acceleration);
        }
        else
        {
            // Second phase: fall with gravity
            Vector3 gravity = PlanetBody.GravityAt(transform.position);
            _rb.AddForce(gravity * gravityMultiplier, ForceMode.Acceleration);
        }
    }

    void OnCollisionEnter(Collision col)
    {
        // Don't hit the owner
        if (col.gameObject == _owner) return;

        // Friendly bolts pass through (support robot -> player, AI enemy -> AI enemy)
        if (_owner != null && FactionRules.PassesThrough(FactionRules.Of(_owner), FactionRules.Of(col.gameObject))) return;

        // Splash damage
        if (splashRadius > 0f)
        {
            DealSplashDamage(transform.position);
        }
        else
        {
            var target = col.gameObject.GetComponent<IDamageable>();
            if (target != null)
            {
                target.RegisterHit(col.contactCount > 0 ? col.GetContact(0).point : transform.position);
                if (_owner != null) target.NoteSource(ShooterLabel(_owner));
                target.TakeDamage(damage, _owner != null ? _owner.GetComponent<PlayerStats>() : null);
                NotifyHitMarker(target);
            }
        }

        // Hit effect: flat side facing out of whatever we struck (the planet's up for splash hits).
        if (!string.IsNullOrEmpty(impactResource))
        {
            Vector3 point = transform.position, normal = -transform.forward;
            if (col.contactCount > 0)
            {
                var contact = col.GetContact(0);
                point = contact.point;
                normal = contact.normal;
            }
            ImpactFx.Play(impactResource, point, normal, impactScale);
        }

        Destroy(gameObject);
    }

    private void DealSplashDamage(Vector3 center)
    {
        var attackerStats = _owner != null ? _owner.GetComponent<PlayerStats>() : null;

        Collider[] hits = Physics.OverlapSphere(center, splashRadius);
        foreach (var hit in hits)
        {
            if (hit.gameObject == _owner) continue;
            var target = hit.GetComponent<IDamageable>();
            if (target == null) continue;

            float dist = Vector3.Distance(center, hit.transform.position);
            float falloff = 1f - Mathf.Clamp01(dist / splashRadius);
            target.RegisterHit(hit.ClosestPoint(center));
            target.TakeDamage(damage * falloff, attackerStats);
            NotifyHitMarker(target);
        }
    }

    private static string ShooterLabel(GameObject owner)
    {
        var home = owner.GetComponent<EnemyHome>();
        if (home != null) return home.isDrone ? "DRON DL-3" : "SOLDADO ÉLITE";
        if (owner.GetComponent<EnemyStats>() != null) return "ENEMIGO";
        return "";
    }

    /// <summary>Flash the HUD hit marker when the PLAYER's shot damages something (pink if it killed it).</summary>
    private void NotifyHitMarker(IDamageable target)
    {
        if (_owner == null || HUD.Instance == null) return;
        if (_owner.GetComponent<PlayerController>() == null) return;
        HUD.Instance.ShowHitMarker(!target.IsAlive);
    }
}
}
