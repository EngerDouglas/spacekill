using UnityEngine;

namespace OrbitRush
{

/// <summary>
/// The katana as an inventory item, like the guns: pick it up, cycle to it, drop it. It has no ammo and never fires;
/// while it is the active weapon, MeleeCombat draws the blade and handles the attacks (tap / hold / dodge).
/// </summary>
public class Katana : WeaponBase
{
    public Katana()
    {
        weaponName = "Katana";
        maxAmmo = 0;
    }

    public override bool IsMelee => true;

    protected override void OnFire() { }
}
}
