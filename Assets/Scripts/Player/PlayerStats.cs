using UnityEngine;

namespace OrbitRush
{

/// <summary>
/// Holds all player stats. Handles damage, healing, shield.
/// </summary>
public class PlayerStats : MonoBehaviour, IDamageable
{
    [Header("Health")]
    public float maxHealth = 100f;
    public float health = 100f;

    [Header("Shield")]
    public float maxShield = 50f;
    public float shield = 50f;
    public float shieldRegenRate = 5f;
    public float shieldRegenDelay = 4f;

    [Header("Stamina")]
    public float maxStamina = 100f;
    public float stamina = 100f;
    public float staminaRegenRate = 15f;

    [Header("Jetpack")]
    public float maxJetpackEnergy = 100f;
    public float jetpackEnergy = 100f;
    public float jetpackRegenRate = 20f;      // (the scene may override this; see PlayerStats.RegenerateJetpack)
    public float jetpackRegenDelay = 2f;

    [Header("Resistance")]
    [Range(0f, 2f)] public float gravityResistance = 1.0f;

    [Header("Team")]
    [Tooltip("Assigned automatically by GameManager in Team Deathmatch if left as None.")]
    public Team team = Team.None;

    /// <summary>Damage is ignored until this time (Time.time) — used for dodge i-frames.</summary>
    public float InvulnerableUntil;
    public bool IsInvulnerable => Time.time < InvulnerableUntil;

    /// <summary>Who or what dealt the last damage (shown on the death screen).</summary>
    public string LastDamageSource { get; private set; } = "";
    public void NoteSource(string label) { if (!string.IsNullOrEmpty(label)) LastDamageSource = label; }

    /// <summary>Raised once when health reaches zero (PlayerRespawn listens).</summary>
    public event System.Action Died;

    private float _shieldRegenTimer;
    private float _jetpackRegenTimer;
    private float _jetpackLockUntil;

    /// <summary>Set by PlayerController: the jetpack only recharges while standing on the ground.</summary>
    public bool JetpackOnGround { get; set; } = true;
    /// <summary>True while overheated (fuel ran out), jammed by an EMP, or held by a zombie.</summary>
    public bool JetpackLocked => Time.time < _jetpackLockUntil;
    public float JetpackLockRemaining => Mathf.Max(0f, _jetpackLockUntil - Time.time);
    /// <summary>Jetpack blocked after the fuel ran out completely.</summary>
    public bool JetpackOverheated { get; private set; }
    public float overheatSeconds = 3f;

    /// <summary>Disables the jetpack for a while (drone EMP, zombie grab...). Longest lock wins.</summary>
    public void UnlockJetpack() { _jetpackLockUntil = Time.time; }
    public void LockJetpack(float seconds) => _jetpackLockUntil = Mathf.Max(_jetpackLockUntil, Time.time + seconds);
    private int _lastAttackerId = -1;

    // Simple sequential ID for local kill attribution / scoreboard display.
    // Not networked — fine for the current single-scene prototype.
    private static int _nextPlayerId = 1;
    public int PlayerId { get; private set; }

    public bool IsAlive => health > 0f;
    public Faction Faction => Faction.Player;
    public float HealthPercent => health / maxHealth;
    public float ShieldPercent => shield / maxShield;
    public float StaminaPercent => stamina / maxStamina;
    public float JetpackPercent => jetpackEnergy / maxJetpackEnergy;

    void Awake()
    {
        PlayerId = _nextPlayerId++;
    }

    void Start()
    {
        // Dress the player in the Blender character (players only — enemies use EnemyStats).
        if (GetComponent<PlayerController>() != null && GetComponent<PlayerModel>() == null)
            gameObject.AddComponent<PlayerModel>();
        if (GetComponent<PlayerController>() != null && GetComponent<MeleeCombat>() == null)
            gameObject.AddComponent<MeleeCombat>();
        if (GetComponent<PlayerController>() != null && GetComponent<CameraRig>() == null)
            gameObject.AddComponent<CameraRig>();

        // Start (not Awake) so GameManager.Instance already exists.
        GameManager.Instance?.RegisterPlayer(this);
        TeamVisual.Apply(this);
    }

    /// <summary>Changes team at runtime and refreshes the head marker.</summary>
    public void SetTeam(Team newTeam)
    {
        team = newTeam;
        TeamVisual.Apply(this);
    }

    void Update()
    {
        RegenerateShield();
        RegenerateStamina();
        RegenerateJetpack();
    }

    public void TakeDamage(float amount) => TakeDamage(amount, null);

    /// <summary>
    /// Deals damage and remembers who dealt it, so a kill can be credited
    /// to the right player if this brings health to zero. Always overwrites
    /// the last attacker (including back to "none") so credit goes to
    /// whoever hit us most recently, not a stale earlier attacker.
    /// Pass null for environmental damage (meteors, supernova, etc.) or
    /// damage from an AI enemy — those don't award a PvP kill credit.
    /// </summary>
    public void TakeDamage(float amount, PlayerStats attacker)
    {
        if (IsInvulnerable) return;   // mid-dodge

        // No friendly fire between teammates (self-damage, e.g. own splash, still applies).
        if (attacker != null && attacker != this && TeamUtil.AreAllies(team, attacker.team)
            && !(GameManager.Instance != null && GameManager.Instance.friendlyFire))
            return;

        CameraRig.AddTrauma(Mathf.Clamp01(amount / 60f) * 0.55f);      // a hit rattles the camera a little
        _lastAttackerId = attacker != null ? attacker.PlayerId : -1;
        if (attacker != null && attacker != this) LastDamageSource = $"JUGADOR {attacker.PlayerId}";

        if (shield > 0f)
        {
            amount = DamageRules.AbsorbWithShield(ref shield, amount, out _);
            _shieldRegenTimer = shieldRegenDelay;
        }

        health = Mathf.Max(0f, health - amount);

        if (!IsAlive) OnDeath();
    }

    public void Heal(float amount)
        => health = Mathf.Min(maxHealth, health + amount);

    public void AddShield(float amount)
        => shield = Mathf.Min(maxShield, shield + amount);

    /// <summary>Restores full health/shield/stamina/jetpack and clears regen timers. Called by PlayerRespawn.</summary>
    public void Respawn()
    {
        LastDamageSource = "";
        health = maxHealth;
        shield = maxShield;
        stamina = maxStamina;
        jetpackEnergy = maxJetpackEnergy;
        _shieldRegenTimer = 0f;
        _jetpackRegenTimer = 0f;
        _jetpackLockUntil = 0f; JetpackOverheated = false;
        _lastAttackerId = -1;
    }

    public bool UseStamina(float amount)
    {
        if (stamina < amount) return false;
        stamina -= amount;
        return true;
    }

    public bool UseJetpack(float amount)
    {
        if (JetpackLocked || jetpackEnergy <= 0.001f) return false;
        jetpackEnergy = Mathf.Max(0f, jetpackEnergy - amount);
        _jetpackRegenTimer = Mathf.Min(jetpackRegenDelay, 1f);      // recharge starts one second after the last use
        if (jetpackEnergy <= 0.001f)
        {
            JetpackOverheated = true;                                // drained completely: it overheats and jams
            LockJetpack(overheatSeconds);
        }
        return true;
    }

    private void RegenerateShield()
    {
        if (DamageRules.TickDelay(ref _shieldRegenTimer, Time.deltaTime)) return;
        shield = DamageRules.Regen(shield, maxShield, shieldRegenRate, Time.deltaTime);
    }

    private void RegenerateStamina()
        => stamina = Mathf.Min(maxStamina, stamina + staminaRegenRate * Time.deltaTime);

    private void RegenerateJetpack()
    {
        if (JetpackOverheated && !JetpackLocked) JetpackOverheated = false;
        if (!JetpackOnGround) return;
        if (DamageRules.TickDelay(ref _jetpackRegenTimer, Time.deltaTime)) return;
        jetpackEnergy = DamageRules.Regen(jetpackEnergy, maxJetpackEnergy, Mathf.Max(jetpackRegenRate, 35f), Time.deltaTime);
    }

    private void OnDeath()
    {
        Debug.Log($"{gameObject.name} died.");

        if (_lastAttackerId >= 0)
            GameManager.Instance?.RegisterKill(_lastAttackerId, PlayerId);

        Died?.Invoke();
    }
}
}
