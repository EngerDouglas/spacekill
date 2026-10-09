using UnityEngine;

namespace OrbitRush
{

/// <summary>
/// Health, optional energy shield and death handling for AI enemies.
///
/// This used to be a Netcode NetworkBehaviour, but Netcode currently fails to start in this
/// project (its IL post-processing doesn't run: "Allowed types is not equal to the number of
/// message type indices"), which left every enemy inert. Enemies are plain MonoBehaviours now,
/// running locally. When networking is fixed they can be made server-authoritative again.
/// </summary>
public class EnemyStats : MonoBehaviour, IDamageable
{
    [Header("Health")]
    public float maxHealth = 80f;

    [Header("Energy Shield (0 = none)")]
    public float maxShield = 0f;
    [Tooltip("Seconds without taking damage before the shield starts recharging.")]
    public float shieldRegenDelay = 6f;
    public float shieldRegenRate = 8f;

    public float Health { get; private set; }
    public float Shield { get; private set; }
    public float HealthPercent => maxHealth > 0f ? Mathf.Clamp01(Health / maxHealth) : 0f;
    public float ShieldPercent => maxShield > 0f ? Mathf.Clamp01(Shield / maxShield) : 0f;
    public bool IsAlive => Health > 0f;
    public Faction Faction => Faction.Enemy;

    /// <summary>Time.time of the last hit — lets visuals (the shield bubble) flash on impact.</summary>
    public float LastHitTime { get; private set; } = -100f;

    private bool _dead;

    [Header("Weak points / disabling")]
    [Tooltip("Damage multiplier when hit from behind (the robot's battery is on its back).")]
    public float backWeakness = 1f;
    [Tooltip("A single hit of this fraction of max health (or an EMP) knocks the enemy out for a few seconds.")]
    public bool canBeDisabled = false;
    [Range(0.05f, 1f)] public float criticalHitFraction = 0.3f;
    public float disabledDamageMultiplier = 1.5f;

    private float _disabledUntil;
    private readonly System.Collections.Generic.Dictionary<PlayerStats, float> _damageBy = new System.Collections.Generic.Dictionary<PlayerStats, float>();

    public bool IsDisabled => Time.time < _disabledUntil;
    public float DisabledRemaining => Mathf.Max(0f, _disabledUntil - Time.time);
    /// <summary>Raised on every hit with the attacker (null for the environment) — the AI turns on whoever hurt it.</summary>
    public event System.Action<PlayerStats> Damaged;
    public event System.Action<float> DisabledFor;

    /// <summary>Total damage this player has dealt to this enemy (used when picking a target).</summary>
    public float DamageFrom(PlayerStats attacker) => attacker != null && _damageBy.TryGetValue(attacker, out var v) ? v : 0f;

    /// <summary>EMP / critical hit: sparks and does nothing for a moment, taking extra damage.</summary>
    public void Disable(float seconds)
    {
        _disabledUntil = Mathf.Max(_disabledUntil, Time.time + seconds);
        DisabledFor?.Invoke(seconds);
    }

    /// <summary>Every live enemy — lets the radar, the support robot, etc. avoid scanning the whole scene.</summary>
    public static readonly System.Collections.Generic.List<EnemyStats> All = new System.Collections.Generic.List<EnemyStats>();
    void OnEnable() { if (!All.Contains(this)) All.Add(this); }
    void OnDisable() => All.Remove(this);

    /// <summary>Raised by projectiles with the world position where they struck (drives the shield ripple).</summary>
    public event System.Action<Vector3> HitAt;
    public void RegisterHit(Vector3 worldPoint) => HitAt?.Invoke(worldPoint);

    void Awake()
    {
        Health = maxHealth;
        Shield = maxShield;
    }

    void Update()
    {
        if (maxShield > 0f && Shield < maxShield && Time.time - LastHitTime > shieldRegenDelay)
            Shield = DamageRules.Regen(Shield, maxShield, shieldRegenRate, Time.deltaTime);
    }

    /// <summary>
    /// Applies damage (shield first, then health). Pass the PlayerStats responsible so the kill is
    /// credited to them — null for environmental damage (meteors, black holes with no clear attacker).
    /// </summary>
    public void TakeDamage(float amount, PlayerStats attacker)
    {
        if (_dead || !IsAlive) return;

        LastHitTime = Time.time;

        // Weak spot (back) and the extra damage while disabled
        if (backWeakness > 1.001f && attacker != null)
        {
            Vector3 toAttacker = (attacker.transform.position - transform.position).normalized;
            if (Vector3.Dot(transform.forward, toAttacker) < -0.35f) amount *= backWeakness;
        }
        if (IsDisabled) amount *= disabledDamageMultiplier;
        if (attacker != null) _damageBy[attacker] = DamageFrom(attacker) + amount;
        bool critical = canBeDisabled && maxHealth > 0f && amount >= maxHealth * criticalHitFraction;
        Damaged?.Invoke(attacker);
        if (critical && !IsDisabled) Disable(3f);

        if (Shield > 0f)
        {
            float shield = Shield;
            amount = DamageRules.AbsorbWithShield(ref shield, amount, out bool broke);
            Shield = shield;
            if (broke) DeathBurstFx.Play(transform.position, new Color(0f, 0.85f, 1f), transform.up); // shield pops
        }

        if (amount <= 0f) return;

        Health = Mathf.Max(0f, Health - amount);
        if (Health <= 0f) Die(attacker);
    }

    /// <summary>Short name for the HUD kill feed: DRON, ZOMBI, ROBOT, or ENEMIGO.</summary>
    private string FeedLabel()
    {
        var home = GetComponent<EnemyHome>();
        if (home != null && home.isDrone) return "DRON";
        string n = gameObject.name.ToLowerInvariant();
        if (n.Contains("zomb")) return "ZOMBI";
        if (n.Contains("robot") || n.Contains("droid")) return "ROBOT";
        return "ENEMIGO";
    }

    /// <summary>Raised once when an enemy dies (killer is null for environment kills). Coin drops listen to it.</summary>
    public static event System.Action<EnemyStats, PlayerStats> Killed;

    private void Die(PlayerStats attacker)
    {
        if (_dead) return;
        _dead = true;

        Debug.Log($"{gameObject.name} destroyed" + (attacker != null ? $" by Player {attacker.PlayerId}" : ""));

        if (attacker != null)
            GameManager.Instance?.RegisterEnemyKill(attacker.PlayerId);

        KillFeed.Raise(attacker != null ? attacker.DisplayLabel : "ENTORNO", FeedLabel(), PlayerStats.WeaponLabel(attacker));

        GameManager.Instance?.NotifyEnemyKilled(GetComponent<EnemyHome>());
        Killed?.Invoke(this, attacker);

        // Drones crash and explode on impact (DroneDamageStages) before anything else happens
        var drone = GetComponent<DroneDamageStages>();
        if (drone != null && drone.TryPlayDeath()) return;

        DeathBurstFx.Play(transform.position, new Color(1f, 0.25f, 0.1f), transform.up);

        // Other behaviors (humanoids fall over — Falling Back Death) before the enemy disappears
        foreach (var behavior in GetComponents<IDeathBehavior>())
            if (!(behavior is DroneDamageStages) && behavior.TryPlayDeath()) return;

        Destroy(gameObject);
    }
}
}
