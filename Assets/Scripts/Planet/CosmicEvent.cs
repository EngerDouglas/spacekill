using System.Collections;
using UnityEngine;

namespace OrbitRush
{

/// <summary>
/// Manages random cosmic events: meteor storms, supernovas.
/// Attach to the same object as GameManager.
/// </summary>
public class CosmicEvent : MonoBehaviour
{
    [Header("Meteor Storm")]
    public GameObject meteorPrefab;
    public int meteorsPerStorm = 20;
    public float meteorDamage = 25f;

    [Header("Supernova")]
    public float supernovaRadius = 150f;
    public float supernovaPushForce = 80f;

    private PlanetGravity[] _allPlanets;

    void Start()
    {
        _allPlanets = FindObjectsOfType<PlanetGravity>();
    }

    public void TriggerMeteorStorm()
    {
        StartCoroutine(MeteorStorm());
    }

    public void TriggerSupernova(Vector3 epicenter)
    {
        Collider[] hits = Physics.OverlapSphere(epicenter, supernovaRadius);
        foreach (var hit in hits)
        {
            var rb = hit.GetComponent<Rigidbody>();
            if (rb == null) continue;

            Vector3 dir = (hit.transform.position - epicenter).normalized;
            float dist = Vector3.Distance(epicenter, hit.transform.position);
            float strength = Mathf.Lerp(supernovaPushForce, supernovaPushForce * 0.2f, dist / supernovaRadius);
            rb.AddForce(dir * strength, ForceMode.VelocityChange);

            hit.GetComponent<IDamageable>()?.TakeDamage(30f, null);
        }

        Debug.Log("[COSMIC EVENT] Supernova detonated!");
    }

    private IEnumerator MeteorStorm()
    {
        Debug.Log("[COSMIC EVENT] Meteor Storm begins!");

        for (int i = 0; i < meteorsPerStorm; i++)
        {
            if (meteorPrefab == null) break;

            // Pick a random planet to target
            var planet = _allPlanets[Random.Range(0, _allPlanets.Length)];
            Vector3 surfacePoint = planet.transform.position +
                Random.onUnitSphere * planet.radius;

            Vector3 spawnPos = surfacePoint + (surfacePoint - planet.transform.position).normalized * 60f;

            var meteor = Instantiate(meteorPrefab, spawnPos, Quaternion.identity);
            var proj = meteor.GetComponent<Projectile>();
            if (proj != null)
            {
                proj.damage = meteorDamage;
                proj.splashRadius = 5f;
                proj.affectedByGravity = true;
                proj.gravityMultiplier = 2f;
                proj.speed = 20f;
                Vector3 dir = (surfacePoint - spawnPos).normalized;
                proj.Init(dir, null, planet);
            }

            yield return new WaitForSeconds(0.5f);
        }
    }

}
}
