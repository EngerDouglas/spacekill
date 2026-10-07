using System.Collections;
using UnityEngine;

namespace OrbitRush
{

/// <summary>
/// Calls down meteor fragments from above the explosion point.
/// meteorCount: 6 | damage: 30 per meteor
/// </summary>
public class MeteorGrenade : GrenadeBase
{
    [Header("Meteors")]
    public int meteorCount = 6;
    public float meteorDamage = 30f;
    public float meteorSplash = 3f;
    public float spawnHeight = 40f;
    public float meteorSpeed = 25f;
    public GameObject meteorPrefab;

    // The effect below runs as a coroutine that destroys this object when it finishes.
    protected override bool SelfManagedLifetime => true;

    protected override void OnExplode()
    {
        StartCoroutine(SpawnMeteors());
    }

    private IEnumerator SpawnMeteors()
    {
        // Find local "up" based on planet gravity
        Vector3 up = -(GravitySystem.Instance?.GetGravityVector(transform.position).normalized ?? Vector3.down);

        for (int i = 0; i < meteorCount; i++)
        {
            // Random offset around explosion point
            Vector3 offset = Random.insideUnitSphere * 8f;
            offset = Vector3.ProjectOnPlane(offset, up);

            Vector3 spawnPos = transform.position + up * spawnHeight + offset;

            if (meteorPrefab != null)
            {
                var meteor = Instantiate(meteorPrefab, spawnPos, Quaternion.identity);
                var proj = meteor.GetComponent<Projectile>();
                if (proj != null)
                {
                    proj.damage = meteorDamage;
                    proj.splashRadius = meteorSplash;
                    proj.affectedByGravity = true;
                    proj.gravityMultiplier = 1.5f;
                    proj.speed = meteorSpeed;
                    proj.Init(-up, _owner != null ? _owner.gameObject : null, null);
                }
            }

            yield return new WaitForSeconds(0.25f);
        }

        Destroy(gameObject);
    }
}
}
