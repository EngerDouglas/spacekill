using System.Collections;
using UnityEngine;

namespace OrbitRush
{

/// <summary>
/// Base class for all grenades and special bombs.
/// Subclass to implement specific explosion effects.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public abstract class GrenadeBase : MonoBehaviour
{
    [Header("Grenade")]
    public float fuseTime = 3f;
    public float throwForce = 15f;
    public GameObject explosionVFXPrefab;

    protected Rigidbody _rb;
    protected PlayerStats _owner;
    private bool _thrown;

    void Awake()
    {
        _rb = GetComponent<Rigidbody>();
        _rb.useGravity = false;

        // The pickup's template stays disabled until thrown. A thrown clone must NOT do this: its Awake
        // runs when Throw() activates it, and switching itself off again made thrown grenades invisible
        // and unable to explode (the fuse coroutine couldn't even start on an inactive object).
        if (!_thrown) gameObject.SetActive(false);
    }

    /// <summary>
    /// Spawns and throws a clone of this grenade prefab.
    /// Returns true if successful. Pass the thrower's PlayerStats so any
    /// kills this grenade scores can be credited to them.
    /// </summary>
    public bool Throw(Vector3 origin, Vector3 direction, PlayerStats owner = null)
    {
        if (_thrown) return false;

        // The template is inactive, so the clone starts inactive: mark it as thrown BEFORE activating,
        // so its Awake (which runs on activation) knows not to switch it back off.
        var clone = Instantiate(gameObject, origin, Quaternion.identity);

        var grenade = clone.GetComponent<GrenadeBase>();
        grenade._thrown = true;
        grenade._owner = owner;

        clone.SetActive(true);

        var rb = clone.GetComponent<Rigidbody>();
        rb.useGravity = false;
        rb.linearVelocity = direction.normalized * grenade.throwForce;

        grenade.StartCoroutine(grenade.FuseRoutine());
        return true;
    }

    void FixedUpdate()
    {
        if (!_thrown || _rb.isKinematic) return;

        // Grenades are affected by planetary gravity
        Vector3 gravity = PlanetBody.GravityAt(transform.position);
        _rb.AddForce(gravity, ForceMode.Acceleration);
    }

    private IEnumerator FuseRoutine()
    {
        yield return new WaitForSeconds(fuseTime);
        Explode();
    }

    protected virtual void Explode()
    {
        if (explosionVFXPrefab != null)
            Instantiate(explosionVFXPrefab, transform.position, Quaternion.identity);
        else
            ImpactFx.Play(WeaponModels.Folder + "Grenade_Explosion", transform.position, Vector3.up, 7f, 0.8f);

        OnExplode();

        if (SelfManagedLifetime)
        {
            // The effect (gravity well, black hole, meteor shower) runs as a coroutine on this object and
            // destroys it when finished — destroying it here would kill the effect on the spot.
            // Just hide the grenade body and stop it in place.
            foreach (var r in GetComponentsInChildren<Renderer>(true)) r.enabled = false;
            var col = GetComponent<Collider>();
            if (col != null) col.enabled = false;
            if (_rb != null) { _rb.linearVelocity = Vector3.zero; _rb.isKinematic = true; }
        }
        else
        {
            Destroy(gameObject);
        }
    }

    /// <summary>
    /// True for grenades whose OnExplode starts a long-running effect that destroys the object itself
    /// when it ends. The base class then keeps the object alive instead of destroying it immediately.
    /// </summary>
    protected virtual bool SelfManagedLifetime => false;

    protected abstract void OnExplode();

    protected void DealDamageInRadius(float radius, float damage, bool falloff = true)
    {
        Collider[] hits = Physics.OverlapSphere(transform.position, radius);
        foreach (var hit in hits)
        {
            var target = hit.GetComponent<IDamageable>();
            if (target == null) continue;

            float dist = Vector3.Distance(transform.position, hit.transform.position);
            float mult = falloff ? 1f - Mathf.Clamp01(dist / radius) : 1f;
            target.TakeDamage(damage * mult, _owner);
        }
    }
}
}
