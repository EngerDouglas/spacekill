using UnityEngine;

namespace OrbitRush
{

/// <summary>
/// Sticky plasma grenade — attaches to surfaces/players on contact.
/// damage: 60 | radius: 8 | sticky: true
/// </summary>
public class PlasmaGrenade : GrenadeBase
{
    [Header("Plasma")]
    public float explosionDamage = 60f;
    public float explosionRadius = 8f;
    public bool sticky = true;

    private bool _stuck;

    void OnCollisionEnter(Collision col)
    {
        if (!sticky || _stuck) return;
        _stuck = true;

        // Attach to whatever was hit
        transform.SetParent(col.transform);
        _rb.isKinematic = true;
    }

    protected override void OnExplode()
    {
        DealDamageInRadius(explosionRadius, explosionDamage);
    }
}
}
