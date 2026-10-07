using UnityEngine;

namespace OrbitRush
{

/// <summary>
/// Pushes everything outward — can send players flying into space.
/// force: 60 | radius: 20
/// </summary>
public class AntiGravityBomb : GrenadeBase
{
    [Header("Anti-Gravity")]
    public float pushRadius = 20f;
    public float pushForce = 60f;

    protected override void OnExplode()
    {
        Collider[] hits = Physics.OverlapSphere(transform.position, pushRadius);
        foreach (var hit in hits)
        {
            var rb = hit.GetComponent<Rigidbody>();
            if (rb == null) continue;

            Vector3 dir = (hit.transform.position - transform.position).normalized;
            float dist = Vector3.Distance(transform.position, hit.transform.position);
            float strength = Mathf.Lerp(pushForce, pushForce * 0.3f, dist / pushRadius);

            rb.AddForce(dir * strength, ForceMode.VelocityChange);
        }
    }
}
}
