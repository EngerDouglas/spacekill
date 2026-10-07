using UnityEngine;

namespace OrbitRush
{

/// <summary>
/// Base class for all weapons. Handles fire rate, ammo, and shooting logic.
/// Subclass this to implement specific weapons.
/// </summary>
public abstract class WeaponBase : MonoBehaviour
{
    [Header("Weapon Info")]
    public string weaponName = "Weapon";
    public WeaponSlot slot = WeaponSlot.Primary;
    [Tooltip("How the character holds it (drives the pistol/rifle stance and jump animation).")]
    public GripType grip = GripType.Rifle;

    // Set by WeaponInventory when this instance is picked up, so dropping it
    // can spawn a new WeaponPickup referencing the right prefab.
    [HideInInspector] public GameObject sourcePrefab;

    [Header("Fire")]
    public float fireRate = 0.3f;       // seconds between shots
    public int maxAmmo = 30;
    public int currentAmmo;
    public float reloadTime = 1.5f;

    [Header("Projectile")]
    public GameObject projectilePrefab;
    public Transform muzzle;            // where bullets spawn
    public float projectileSpeed = 90f;

    [Header("Spread")]
    [Range(0f, 15f)] public float spread = 0f;

    [Header("Scope (optional)")]
    [Tooltip("If true, holding the Aim input zooms the camera and shows a scope overlay (see WeaponAim).")]
    public bool hasScope = false;
    public float scopeFov = 20f;
    [Range(0.1f, 1f)] public float scopeSensitivityMultiplier = 0.35f;

    private float _nextFireTime;
    private bool _isReloading;
    private float _reloadTimer;

    protected PlayerController _owner;
    protected PlayerStats _ownerStats;
    private MeleeCombat _ownerMelee;

    public enum WeaponSlot { Primary, Secondary }
    public enum GripType { Rifle, Pistol }

    /// <summary>Name of the WeaponDefinition asset (Resources/Weapons/Definitions) holding this weapon's numbers.</summary>
    protected virtual string DefinitionName => null;

    [Tooltip("Optional explicit definition; otherwise it is loaded by DefinitionName.")]
    public WeaponDefinition definition;

    [Tooltip("Damage override for the projectile (0 = the projectile's own value). Filled from the definition.")]
    public float damage;

    private void ApplyDefinition()
    {
        if (definition == null && !string.IsNullOrEmpty(DefinitionName))
            definition = Resources.Load<WeaponDefinition>(WeaponDefinition.Folder + DefinitionName);
        if (definition == null)
        {
            if (!string.IsNullOrEmpty(DefinitionName)) Debug.LogWarning($"[Weapon] No definition asset '{DefinitionName}' — using field defaults.");
            return;
        }
        weaponName = definition.displayName; grip = definition.grip;
        fireRate = definition.fireRate; maxAmmo = definition.maxAmmo; reloadTime = definition.reloadTime;
        projectileSpeed = definition.projectileSpeed; spread = definition.spread; damage = definition.damage;
        hasScope = definition.hasScope; scopeFov = definition.scopeFov; scopeSensitivityMultiplier = definition.scopeSensitivityMultiplier;
    }

    void Awake()
    {
        ApplyDefinition();
        currentAmmo = maxAmmo;
        _owner = GetComponentInParent<PlayerController>();
        _ownerStats = GetComponentInParent<PlayerStats>();
    }

    protected virtual void Update()
    {
        if (_isReloading)
        {
            _reloadTimer -= Time.deltaTime;
            if (_reloadTimer <= 0f)
            {
                currentAmmo = maxAmmo;
                _isReloading = false;
            }
        }
    }

    /// <summary>Raised whenever any weapon fires (drives the firing animation).</summary>
    public static event System.Action<WeaponBase> AnyFired;

    public bool TryFire()
    {
        if (_ownerMelee == null && _owner != null) _ownerMelee = _owner.GetComponent<MeleeCombat>();
        if (_ownerMelee != null && _ownerMelee.BlocksFiring) return false;   // katana drawn
        if (_isReloading) return false;
        if (currentAmmo <= 0) { StartReload(); return false; }
        if (Time.time < _nextFireTime) return false;

        _nextFireTime = Time.time + fireRate;
        currentAmmo--;

        OnFire();
        AnyFired?.Invoke(this);
        NoiseSystem.Emit(muzzle != null ? muzzle.position : transform.position, Loudness.Shoot, _owner != null ? _owner.gameObject : null);

        if (currentAmmo <= 0) StartReload();
        return true;
    }

    public void StartReload()
    {
        if (_isReloading || currentAmmo == maxAmmo) return;
        _isReloading = true;
        _reloadTimer = reloadTime;
    }

    /// <summary>
    /// Implement specific fire behavior in subclasses.
    /// </summary>
    protected abstract void OnFire();

    /// <summary>
    /// Where the crosshair is actually pointing, not just the muzzle's fixed
    /// orientation. Raycasts from the camera through screen-center; if it hits
    /// something, aims at that point, otherwise aims at a far point along the
    /// camera's forward direction. Own colliders (body, weapon) are ignored so
    /// third-person shots don't aim at yourself.
    /// </summary>
    protected Vector3 GetAimDirection(float maxRange = 500f)
    {
        if (muzzle == null) return transform.forward;

        Vector3 aimPoint = PlayerAim.GetAimPoint(_owner != null ? _owner.transform : null, maxRange);
        return (aimPoint - muzzle.position).normalized;
    }

    protected void SpawnProjectile(Vector3 direction)
    {
        if (projectilePrefab == null || muzzle == null) return;

        // Apply spread (flying with the jetpack is much less accurate)
        float spreadNow = _owner != null && _owner.IsFlying ? Mathf.Max(spread, 2f) * 2.5f : spread;
        if (spreadNow > 0f)
        {
            direction = Quaternion.Euler(
                Random.Range(-spreadNow, spreadNow),
                Random.Range(-spreadNow, spreadNow),
                0f
            ) * direction;
        }

        ProjectileFactory.Spawn(projectilePrefab, muzzle.position, direction, _owner?.gameObject, _owner?.CurrentPlanet,
            p => { p.speed = projectileSpeed; if (damage > 0f) p.damage = damage; });
    }

    public bool IsReloading => _isReloading;
    public float ReloadProgress => _isReloading ? 1f - (_reloadTimer / reloadTime) : 1f;
}
}
