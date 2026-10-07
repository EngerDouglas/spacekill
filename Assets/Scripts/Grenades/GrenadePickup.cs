using UnityEngine;

namespace OrbitRush
{

/// <summary>
/// A grenade or special bomb lying in the world. Walking over it adds it to
/// the player's grenade or special slot and increases the charge count — but
/// only if that slot is empty or already holds the same grenade type (see
/// WeaponInventory.TryPickupGrenade). Mirrors WeaponPickup's walk-to-collect
/// pattern.
/// </summary>
[RequireComponent(typeof(Collider))]
public class GrenadePickup : MonoBehaviour
{
    [Header("Grenade")]
    public GameObject grenadePrefab;   // must have a GrenadeBase-derived component
    public bool isSpecial = false;     // true = fills the "special" slot instead of the regular grenade slot
    public int pickupAmount = 1;
    public string displayName = "Grenade";

    void Awake()
    {
        GetComponent<Collider>().isTrigger = true;
    }

    void OnTriggerEnter(Collider other)
    {
        var inventory = other.GetComponentInParent<WeaponInventory>();
        if (inventory == null) return;

        if (inventory.TryPickupGrenade(this)) Destroy(gameObject);
    }
}
}
