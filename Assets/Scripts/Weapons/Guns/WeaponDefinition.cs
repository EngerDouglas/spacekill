using UnityEngine;

namespace OrbitRush
{

/// <summary>
/// Tunable numbers for one weapon, as an asset (Resources/Weapons/Definitions/&lt;Name&gt;.asset). WeaponBase loads the
/// asset named by its <c>DefinitionName</c>; edit the asset to rebalance without touching code.
/// </summary>
[CreateAssetMenu(menuName = "Orbit Rush/Weapon Definition", fileName = "NewWeapon")]
public class WeaponDefinition : ScriptableObject
{
    public string displayName = "Weapon";
    public WeaponBase.GripType grip = WeaponBase.GripType.Rifle;

    [Header("Fire")]
    public float fireRate = 0.3f;
    public int maxAmmo = 24;
    public float reloadTime = 1.5f;
    public float projectileSpeed = 90f;
    [Range(0f, 15f)] public float spread = 0f;
    [Tooltip("0 = keep the projectile's own damage.")]
    public float damage = 0f;

    [Header("Scope")]
    public bool hasScope = false;
    public float scopeFov = 20f;
    [Range(0.1f, 1f)] public float scopeSensitivityMultiplier = 0.35f;

    public const string Folder = "Weapons/Definitions/";
}
}
