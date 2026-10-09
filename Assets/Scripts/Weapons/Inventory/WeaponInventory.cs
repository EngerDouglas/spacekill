using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace OrbitRush
{

/// <summary>
/// Manages the player's weapon inventory: picked-up weapons, the active one,
/// switching, dropping, fires on input. Attach to the Player alongside a
/// PlayerInput component.
///
/// Weapons are no longer fixed primary/secondary slots — they're picked up
/// from WeaponPickup objects in the world (walk over one to grab it) and
/// held in a capped list. Drop the active weapon with the Drop action (G).
/// </summary>
public class WeaponInventory : MonoBehaviour
{
    [Header("Slots")]
    public int maxWeapons = 4;
    public Transform weaponMount;                 // where picked-up weapon models attach; defaults to this transform
    public GameObject droppedWeaponPickupPrefab;   // a WeaponPickup prefab used when dropping a weapon

    [Header("Starting Loadout")]
    public GameObject[] startingWeapons;           // optional weapon prefabs granted at spawn

    [Header("Grenade")]
    public GrenadeBase equippedGrenade;
    public int grenadeCount = 2;

    [Header("Special")]
    public GrenadeBase specialBomb;
    public int specialCount = 1;

    private readonly List<WeaponBase> _weapons = new();
    private int _activeIndex = -1;
    private PlayerStats _stats;

    void Start()
    {
        _stats = GetComponent<PlayerStats>();
        EnsureStartingGrenades();

        if (startingWeapons != null)
            foreach (var prefab in startingWeapons)
                if (prefab != null) AddWeapon(prefab, equip: _weapons.Count == 0);

        if (_weapons.Count == 0) StartCoroutine(GiveRandomWeaponWhenReady());
    }

    /// <summary>
    /// Always start armed: once the world's weapon pickups exist (they are spawned a moment after the scene
    /// loads), equip a random one of the weapons they offer.
    /// </summary>
    private System.Collections.IEnumerator GiveRandomWeaponWhenReady()
    {
        for (float waited = 0f; waited < 6f && _weapons.Count == 0; waited += 0.25f)
        {
            if (GiveRandomWeapon()) yield break;
            yield return new WaitForSeconds(0.25f);
        }
    }

    /// <summary>Equips a random weapon from the ones lying in the world. Returns false if there are none yet.</summary>
    public bool GiveRandomWeapon()
    {
        var pickups = FindObjectsByType<WeaponPickup>(FindObjectsSortMode.None);
        var options = new List<WeaponPickup>();
        foreach (var p in pickups)
            if (p != null && p.weaponPrefab != null && !(p.weaponPrefab.GetComponent<WeaponBase>() is { IsMelee: true })) options.Add(p);
        if (options.Count == 0) return false;
        return TryPickup(options[Random.Range(0, options.Count)]);
    }

    // ── Pickup ───────────────────────────────────────────────────────────

    /// <summary>Called by WeaponPickup when the player walks over it. Returns true if it was picked up.</summary>
    public bool TryPickup(WeaponPickup pickup)
    {
        if (pickup == null || pickup.weaponPrefab == null) return false;

        if (_weapons.Count < maxWeapons)
        {
            AddWeapon(pickup.weaponPrefab, equip: true);
            return true;
        }

        // Inventory full — swap out the currently active weapon for the new one.
        if (_activeIndex < 0) return false;
        DropWeaponAt(_activeIndex, pickup.transform.position, pickup.transform.rotation);
        AddWeapon(pickup.weaponPrefab, equip: true);
        return true;
    }

    public enum GiveResult { Failed, Added, Swapped, Refilled }

    /// <summary>
    /// Gives the player a weapon from a template (the vending machine). Already carrying that weapon: its magazine is refilled.
    /// Inventory full: the active weapon is dropped and replaced.
    /// </summary>
    public GiveResult GiveWeapon(GameObject template)
    {
        var wanted = template != null ? template.GetComponent<WeaponBase>() : null;
        if (wanted == null) return GiveResult.Failed;

        foreach (var w in _weapons)
            if (w != null && w.GetType() == wanted.GetType()) { w.currentAmmo = w.maxAmmo; return GiveResult.Refilled; }

        if (_weapons.Count < maxWeapons) { AddWeapon(template, equip: true); return GiveResult.Added; }

        if (_activeIndex < 0) return GiveResult.Failed;
        DropWeaponAt(_activeIndex, transform.position + transform.forward * 1.5f, Quaternion.LookRotation(transform.forward));
        AddWeapon(template, equip: true);
        return GiveResult.Swapped;
    }

    /// <summary>True when the player already carries a weapon of this class.</summary>
    public bool Has(System.Type weaponType)
    {
        foreach (var w in _weapons) if (w != null && w.GetType() == weaponType) return true;
        return false;
    }

    private void AddWeapon(GameObject weaponPrefab, bool equip)
    {
        var mount = weaponMount != null ? weaponMount : transform;
        var instance = Instantiate(weaponPrefab, mount);
        instance.transform.localPosition = Vector3.zero;
        instance.transform.localRotation = Quaternion.identity;

        var weapon = instance.GetComponent<WeaponBase>();
        if (weapon == null)
        {
            Debug.LogWarning($"[WeaponInventory] '{weaponPrefab.name}' has no WeaponBase component — ignoring pickup.");
            Destroy(instance);
            return;
        }

        if (!weapon.hasScope && !weapon.IsMelee && instance.GetComponent<WeaponSight>() == null) instance.AddComponent<WeaponSight>();

        weapon.sourcePrefab = weaponPrefab;
        _weapons.Add(weapon);
        SetActive(weapon, false);

        if (equip) Equip(_weapons.Count - 1);
    }

    // ── Grenade pickup ───────────────────────────────────────────────────

    /// <summary>
    /// Called by GrenadePickup when the player walks over it. Adds a charge to
    /// the regular grenade or special-bomb slot — only if that slot is empty
    /// or already holds the same grenade type (no free swapping by walking
    /// over a different kind while you're still carrying one).
    /// </summary>
    public bool TryPickupGrenade(GrenadePickup pickup)
    {
        if (pickup == null || pickup.grenadePrefab == null) return false;

        var grenade = pickup.grenadePrefab.GetComponent<GrenadeBase>();
        if (grenade == null) return false;

        // NOTE: the pickup (and the template parented under it) is destroyed right after this returns,
        // so the slot must keep its OWN copy. Storing the template itself left a dead reference:
        // the HUD count went up but throwing did nothing.
        if (pickup.isSpecial)
        {
            // A different kind can only replace the current one once the old one is used up.
            if (specialBomb != null && specialCount > 0 && specialBomb.GetType() != grenade.GetType()) return false;
            if (specialBomb == null || specialBomb.GetType() != grenade.GetType())
            {
                if (specialBomb != null) Destroy(specialBomb.gameObject);
                specialBomb = OwnCopyOf(pickup.grenadePrefab);
            }
            specialCount += pickup.pickupAmount;
        }
        else
        {
            if (equippedGrenade != null && grenadeCount > 0 && equippedGrenade.GetType() != grenade.GetType()) return false;
            if (equippedGrenade == null || equippedGrenade.GetType() != grenade.GetType())
            {
                if (equippedGrenade != null) Destroy(equippedGrenade.gameObject);
                equippedGrenade = OwnCopyOf(pickup.grenadePrefab);
            }
            grenadeCount += pickup.pickupAmount;
        }

        return true;
    }

    /// <summary>A persistent, disabled copy of a grenade template, parented to the player.</summary>
    private GrenadeBase OwnCopyOf(GameObject template)
    {
        var copy = Instantiate(template, transform);
        copy.name = template.name;
        copy.SetActive(false);   // stays disabled until thrown (Throw() clones it)
        return copy.GetComponent<GrenadeBase>();
    }

    /// <summary>
    /// Makes sure the starting grenade/special counts are actually usable: with no grenade equipped, the
    /// HUD showed "Grenades 2" but Q/F did nothing until one was picked up.
    /// </summary>
    private void EnsureStartingGrenades()
    {
        if (equippedGrenade == null && grenadeCount > 0)
            equippedGrenade = AdoptTemplate(GrenadeSpawner.BuildGrenadeTemplate(GrenadeSpawner.GrenadeKind.PlasmaGrenade));

        if (specialBomb == null && specialCount > 0)
            specialBomb = AdoptTemplate(GrenadeSpawner.BuildSpecialTemplate());
    }

    private GrenadeBase AdoptTemplate(GameObject template)
    {
        template.transform.SetParent(transform, false);
        return template.GetComponent<GrenadeBase>();
    }

    // ── Drop ─────────────────────────────────────────────────────────────

    public void OnDrop(InputValue value)
    {
        if (!value.isPressed || _activeIndex < 0) return;

        Vector3 dropPos = transform.position + transform.forward * 1.5f;
        DropWeaponAt(_activeIndex, dropPos, Quaternion.LookRotation(transform.forward));

        Equip(_weapons.Count > 0 ? Mathf.Min(_activeIndex, _weapons.Count - 1) : -1);
    }

    private void DropWeaponAt(int index, Vector3 position, Quaternion rotation)
    {
        var weapon = _weapons[index];
        _weapons.RemoveAt(index);

        if (droppedWeaponPickupPrefab != null && weapon.sourcePrefab != null)
        {
            var pickupGO = Instantiate(droppedWeaponPickupPrefab, position, rotation);
            var pickup = pickupGO.GetComponent<WeaponPickup>();
            if (pickup != null)
            {
                pickup.weaponPrefab = weapon.sourcePrefab;
                pickup.displayName = weapon.weaponName;
            }
        }

        Destroy(weapon.gameObject);

        if (index == _activeIndex) _activeIndex = -1;
        else if (index < _activeIndex) _activeIndex--;
    }

    // ── Switching ────────────────────────────────────────────────────────

    public void OnNext(InputValue value)
    {
        if (value.isPressed) CycleWeapon(1);
    }

    public void OnPrevious(InputValue value)
    {
        if (value.isPressed) CycleWeapon(-1);
    }

    private void CycleWeapon(int direction)
    {
        if (_weapons.Count == 0) return;
        int next = _activeIndex < 0 ? 0 : (_activeIndex + direction + _weapons.Count) % _weapons.Count;
        Equip(next);
    }

    /// <summary>Index of the carried weapon with this name (ignores case and spaces), or -1 if it isn't carried.</summary>
    public int IndexOfWeapon(string weaponName)
    {
        string key = NormalizeName(weaponName);
        for (int i = 0; i < _weapons.Count; i++)
            if (_weapons[i] != null && NormalizeName(_weapons[i].weaponName) == key) return i;
        return -1;
    }

    /// <summary>Equips the carried weapon with this name (used by the weapon wheel). False if it isn't carried.</summary>
    public bool TryEquipByName(string weaponName)
    {
        int i = IndexOfWeapon(weaponName);
        if (i < 0) return false;
        if (i != _activeIndex) Equip(i);
        return true;
    }

    private static string NormalizeName(string s) => string.IsNullOrEmpty(s) ? "" : s.Replace(" ", "").ToLowerInvariant();

    private void Equip(int index)
    {
        if (_activeIndex >= 0 && _activeIndex < _weapons.Count)
            SetActive(_weapons[_activeIndex], false);

        _activeIndex = index;

        if (_activeIndex >= 0 && _activeIndex < _weapons.Count)
            SetActive(_weapons[_activeIndex], true);
    }

    // ── Fire / reload ────────────────────────────────────────────────────

    public void OnFire(InputValue value)
    {
        if (value.isPressed && !WeaponWheel.IsOpen && !ShopPanel.IsOpen) ActiveWeapon?.TryFire();
    }

    public void OnReload(InputValue value)
    {
        if (value.isPressed) ActiveWeapon?.StartReload();
    }

    // ── Grenades ─────────────────────────────────────────────────────────

    public void OnThrowGrenade(InputValue value)
    {
        if (!value.isPressed || grenadeCount <= 0 || equippedGrenade == null) return;
        Vector3 dir = GetThrowDirection();
        if (equippedGrenade.Throw(GetThrowOrigin(dir), dir, _stats))
            grenadeCount--;
    }

    public void OnThrowSpecial(InputValue value)
    {
        if (!value.isPressed || specialCount <= 0 || specialBomb == null) return;
        Vector3 dir = GetThrowDirection();
        if (specialBomb.Throw(GetThrowOrigin(dir), dir, _stats))
            specialCount--;
    }

    /// <summary>
    /// Where a grenade appears: a bit in front of the player, outside their capsule, so it doesn't
    /// spawn overlapping (and get shoved around by) the thrower's own collider.
    /// </summary>
    private Vector3 GetThrowOrigin(Vector3 direction)
        => transform.position + direction * 1.0f + transform.up * 0.3f;

    /// <summary>
    /// Aims the throw at wherever the crosshair is actually pointing (same
    /// raycast weapons use — see PlayerAim), with a small lift along the
    /// player's own local "up" for an arc. Uses transform.up rather than
    /// world up so the arc is correct no matter which planet — and which
    /// side of it — the player is standing on.
    /// </summary>
    private Vector3 GetThrowDirection()
    {
        Vector3 aimPoint = PlayerAim.GetAimPoint(transform, maxRange: 60f); // grenades don't need sniper-length range
        Vector3 toAim = aimPoint - transform.position;
        Vector3 dir = toAim.sqrMagnitude > 0.01f ? toAim.normalized : transform.forward;
        return (dir + transform.up * 0.15f).normalized;
    }

    private void SetActive(WeaponBase weapon, bool active = true)
    {
        if (weapon != null) weapon.gameObject.SetActive(active);
    }

    public WeaponBase ActiveWeapon => _activeIndex >= 0 && _activeIndex < _weapons.Count ? _weapons[_activeIndex] : null;
    public IReadOnlyList<WeaponBase> Weapons => _weapons;
    public int ActiveIndex => _activeIndex;
}
}
