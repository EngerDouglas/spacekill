using System.Collections;
using UnityEngine;

namespace OrbitRush
{

/// <summary>
/// A weapon lying in the world. Walking over it auto-equips it for whichever
/// player touches it. Attach to a GameObject with a trigger Collider.
/// </summary>
[RequireComponent(typeof(Collider))]
public class WeaponPickup : MonoBehaviour
{
    [Header("Weapon")]
    public GameObject weaponPrefab;    // must have a WeaponBase-derived component
    public string displayName = "Weapon";

    [Header("Respawn")]
    public bool respawns = false;
    public float respawnDelay = 15f;

    private Collider _collider;
    private Renderer[] _renderers;

    void Awake()
    {
        _collider = GetComponent<Collider>();
        _collider.isTrigger = true;
        _renderers = GetComponentsInChildren<Renderer>();
    }

    void OnTriggerEnter(Collider other)
    {
        var inventory = other.GetComponentInParent<WeaponInventory>();
        if (inventory == null) return;

        if (!inventory.TryPickup(this)) return;

        if (respawns) StartCoroutine(RespawnAfterDelay());
        else Destroy(gameObject);
    }

    private IEnumerator RespawnAfterDelay()
    {
        SetVisible(false);
        _collider.enabled = false;

        yield return new WaitForSeconds(respawnDelay);

        SetVisible(true);
        _collider.enabled = true;
    }

    private void SetVisible(bool visible)
    {
        foreach (var r in _renderers) r.enabled = visible;
    }
}
}
