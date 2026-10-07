using System.Collections;
using UnityEngine;

namespace OrbitRush
{

/// <summary>
/// Creates a mini black hole — pulls players, bullets, and other grenades.
/// radius: 18 | pullForce: 80 | duration: 5s
/// </summary>
public class BlackHoleBomb : GrenadeBase
{
    [Header("Black Hole")]
    public float eventHorizonRadius = 3f;   // instant kill zone
    public float pullRadius = 18f;
    public float pullForce = 80f;
    public float duration = 5f;
    public float instantKillDamage = 9999f;

    // The effect below runs as a coroutine that destroys this object when it finishes.
    protected override bool SelfManagedLifetime => true;

    protected override void OnExplode()
    {
        StartCoroutine(BlackHoleRoutine());
    }

    private IEnumerator BlackHoleRoutine()
    {
        float timer = 0f;
        while (timer < duration)
        {
            timer += Time.fixedDeltaTime;

            Collider[] hits = Physics.OverlapSphere(transform.position, pullRadius);
            foreach (var hit in hits)
            {
                float dist = Vector3.Distance(transform.position, hit.transform.position);
                Vector3 dir = (transform.position - hit.transform.position).normalized;

                // Instant kill inside event horizon
                if (dist < eventHorizonRadius)
                {
                    hit.GetComponent<IDamageable>()?.TakeDamage(instantKillDamage, _owner);
                    continue;
                }

                // Pull force falls off with distance
                float strength = pullForce * (1f - Mathf.Clamp01(dist / pullRadius));
                var rb = hit.GetComponent<Rigidbody>();
                rb?.AddForce(dir * strength, ForceMode.Acceleration);
            }

            yield return new WaitForFixedUpdate();
        }

        // Micro explosion on collapse
        DealDamageInRadius(eventHorizonRadius * 2f, 40f);
        Destroy(gameObject);
    }
}
}
