using System.Collections;
using UnityEngine;

namespace OrbitRush
{

/// <summary>
/// Creates a gravity well that pulls players and projectiles.
/// radius: 15 | pullForce: 40 | duration: 4s
/// </summary>
public class GravityGrenade : GrenadeBase
{
    [Header("Gravity Well")]
    public float pullRadius = 15f;
    public float pullForce = 40f;
    public float duration = 4f;

    // The effect below runs as a coroutine that destroys this object when it finishes.
    protected override bool SelfManagedLifetime => true;

    protected override void OnExplode()
    {
        StartCoroutine(GravityWell());
    }

    private IEnumerator GravityWell()
    {
        float timer = 0f;
        while (timer < duration)
        {
            timer += Time.fixedDeltaTime;

            // Pull all rigidbodies in range toward this point
            Collider[] hits = Physics.OverlapSphere(transform.position, pullRadius);
            foreach (var hit in hits)
            {
                var rb = hit.GetComponent<Rigidbody>();
                if (rb == null) continue;

                Vector3 dir = (transform.position - hit.transform.position).normalized;
                float dist = Vector3.Distance(transform.position, hit.transform.position);
                float strength = Mathf.Lerp(pullForce, 0f, dist / pullRadius);

                rb.AddForce(dir * strength, ForceMode.Acceleration);
            }

            yield return new WaitForFixedUpdate();
        }

        Destroy(gameObject);
    }
}
}
