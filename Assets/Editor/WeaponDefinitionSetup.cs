using OrbitRush;
using UnityEditor;
using UnityEngine;

/// <summary>Creates the default WeaponDefinition assets (once) so WeaponBase has something to load.</summary>
public static class WeaponDefinitionSetup
{
    const string Dir = "Assets/Resources/Weapons/Definitions";

    [InitializeOnLoadMethod]
    [MenuItem("Orbit Rush/Create Weapon Definitions")]
    public static void EnsureAll()
    {
        if (!AssetDatabase.IsValidFolder("Assets/Resources/Weapons/Definitions"))
            AssetDatabase.CreateFolder("Assets/Resources/Weapons", "Definitions");

        Make("Blaster", d => { d.displayName = "Blaster"; d.grip = WeaponBase.GripType.Pistol; d.fireRate = 0.3f; d.projectileSpeed = 90f; d.maxAmmo = 24; });
        Make("GravitySniper", d => { d.displayName = "Gravity Sniper"; d.fireRate = 2.5f; d.projectileSpeed = 200f; d.maxAmmo = 6; d.reloadTime = 3f; d.damage = 80f; d.hasScope = true; d.scopeFov = 15f; d.scopeSensitivityMultiplier = 0.3f; });
        Make("OrbitalLauncher", d => { d.displayName = "Orbital Launcher"; d.fireRate = 5f; d.projectileSpeed = 40f; d.maxAmmo = 4; d.reloadTime = 4f; d.damage = 120f; });
        Make("PlasmaRifle", d => { d.displayName = "Plasma Rifle"; d.fireRate = 0.1f; d.projectileSpeed = 110f; d.maxAmmo = 60; d.spread = 1.5f; });
        Make("SpaceShotgun", d => { d.displayName = "Space Shotgun"; d.fireRate = 0.8f; d.projectileSpeed = 60f; d.maxAmmo = 12; d.spread = 12f; d.reloadTime = 2f; });
    }

    static void Make(string name, System.Action<WeaponDefinition> fill)
    {
        string path = $"{Dir}/{name}.asset";
        if (AssetDatabase.LoadAssetAtPath<WeaponDefinition>(path) != null) return;
        var d = ScriptableObject.CreateInstance<WeaponDefinition>();
        fill(d);
        AssetDatabase.CreateAsset(d, path);
        AssetDatabase.SaveAssets();
    }
}
