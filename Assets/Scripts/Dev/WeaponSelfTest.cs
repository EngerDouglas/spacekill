#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace OrbitRush
{

/// <summary>
/// Developer tool: run the game with <c>-nomenu -weapontest</c> and, for every weapon kind, it picks the weapon
/// up, puts a damage-counting target exactly where the crosshair points, fires, and logs whether projectiles
/// spawned, where they went, where they landed and how much damage the target took. Does nothing without the flag.
/// </summary>
public class WeaponSelfTest : MonoBehaviour
{
    /// <summary>A stand-in enemy that only counts the damage it receives.</summary>
    private class Dummy : MonoBehaviour, IDamageable
    {
        public float damageTaken;
        public bool IsAlive => true;
        public Faction Faction => Faction.Enemy;
        public void TakeDamage(float amount, PlayerStats attacker) => damageTaken += amount;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Register()
    {
        foreach (var arg in System.Environment.GetCommandLineArgs())
        {
            if (arg != "-weapontest") continue;
            SceneManager.sceneLoaded += (s, m) =>
            {
                if (FindFirstObjectByType<WeaponSelfTest>() == null && FindFirstObjectByType<WeaponInventory>() != null)
                    new GameObject("WeaponSelfTest").AddComponent<WeaponSelfTest>();
            };
            return;
        }
    }

    IEnumerator Start()
    {
        yield return new WaitForSeconds(5f);
        var inventory = FindFirstObjectByType<WeaponInventory>();
        var player = inventory.transform;

        // One pickup of each kind
        var byKind = new Dictionary<string, WeaponPickup>();
        foreach (var p in FindObjectsByType<WeaponPickup>(FindObjectsSortMode.None))
            if (!byKind.ContainsKey(p.displayName)) byKind[p.displayName] = p;
        Log($"found pickups for: {string.Join(", ", byKind.Keys)}");

        foreach (var kv in byKind.OrderBy(k => k.Key))
        {
            bool picked = inventory.TryPickup(kv.Value);
            yield return new WaitForSeconds(0.4f);   // let the weapon activate (Awake/Start run)

            var w = inventory.ActiveWeapon;
            if (w == null) { Log($"== {kv.Key}: NO ACTIVE WEAPON after pickup (picked={picked})"); continue; }

            Vector3 muzzleLocal = w.muzzle != null ? player.InverseTransformPoint(w.muzzle.position) : Vector3.zero;
            Log($"== {kv.Key} -> '{w.weaponName}' picked={picked} ammo={w.currentAmmo}/{w.maxAmmo} " +
                $"muzzle={(w.muzzle != null ? Fmt(muzzleLocal) : "NULL")} projectilePrefab={(w.projectilePrefab != null)} " +
                $"fireRate={w.fireRate}");

            // Target exactly at the aim point (where shots are meant to land)
            Vector3 aim = PlayerAim.GetAimPoint(player, 400f);
            Vector3 up = player.up;
            float aimDist = Vector3.Distance(player.position, aim);
            var target = new GameObject("TestTarget");
            target.transform.position = aim + up * 0.8f;
            var col = target.AddComponent<SphereCollider>(); col.radius = 1.6f;
            var tb = target.AddComponent<Rigidbody>(); tb.isKinematic = true;
            var dummy = target.AddComponent<Dummy>();
            Log($"   aim point at {aimDist:F1} m, target placed there");

            var before = new HashSet<Projectile>(FindObjectsByType<Projectile>(FindObjectsSortMode.None));
            bool fired = w.TryFire();
            yield return null; yield return null;
            var fresh = FindObjectsByType<Projectile>(FindObjectsSortMode.None).Where(pr => !before.Contains(pr)).ToList();
            Log($"   TryFire={fired}, projectiles spawned={fresh.Count}, ammo now {w.currentAmmo}");

            if (fresh.Count > 0)
            {
                var first = fresh[0];
                var rb = first.GetComponent<Rigidbody>();
                Vector3 vel = rb.linearVelocity;
                Vector3 local = player.InverseTransformDirection(vel.normalized);
                Log($"   first projectile: speed={vel.magnitude:F1} m/s, dir(local right,up,fwd)={Fmt(local)} " +
                    $"damage={first.damage} splash={first.splashRadius} orbital={first.orbital} gravity={first.affectedByGravity}");

                // Follow it until it is gone
                float t = 0f; Vector3 last = first.transform.position; float apex = 0f;
                while (first != null && t < 12f)
                {
                    last = first.transform.position;
                    float h = Vector3.Dot(last - player.position, up);
                    if (h > apex) apex = h;
                    yield return new WaitForSeconds(0.05f);
                    t += 0.05f;
                }
                Vector3 rel = last - player.position;
                Log($"   projectile gone after {t:F2}s; last seen {Fmt(player.InverseTransformPoint(last))} " +
                    $"(distance {rel.magnitude:F1} m, apex height {apex:F1} m)");
            }

            yield return new WaitForSeconds(0.6f);
            Log($"   TARGET damage taken = {dummy.damageTaken:F1}   (target was {aimDist:F1} m away)");
            Destroy(target);
            foreach (var pr in FindObjectsByType<Projectile>(FindObjectsSortMode.None)) Destroy(pr.gameObject);
            yield return new WaitForSeconds(1f);
        }

        // ── Katana ──
        var melee = player.GetComponent<MeleeCombat>();
        if (melee == null) Log("== Katana: MeleeCombat MISSING");
        else
        {
            Log($"== Katana loaded={melee.DebugReady}");
            for (int round = 0; round < 3; round++)
            {
                var mt = new GameObject("MeleeTarget");
                mt.transform.position = player.position + player.forward * 2f;
                var mc = mt.AddComponent<SphereCollider>(); mc.radius = 0.8f;
                var mb = mt.AddComponent<Rigidbody>(); mb.isKinematic = true;
                var md = mt.AddComponent<Dummy>();
                if (round == 0) melee.DebugLight(); else if (round == 1) melee.DebugCharged(1f); else melee.DebugDodge();
                yield return new WaitForSeconds(0.2f);
                Log($"   {(round == 0 ? "light" : round == 1 ? "charged" : "dodge")}: drawn={melee.DebugDrawn} firingBlocked={melee.BlocksFiring} moved={Vector3.Distance(player.position, mt.transform.position):F1} m from target");
                yield return new WaitForSeconds(0.8f);
                Log($"   damage dealt = {md.damageTaken:F1}");
                Destroy(mt);
            }
        }

        Log("=== WEAPON SELF-TEST DONE ===");
        Application.Quit();
    }

    private static string Fmt(Vector3 v) => $"({v.x:F2}, {v.y:F2}, {v.z:F2})";
    private static void Log(string msg) => Debug.Log("[WeaponTest] " + msg);
}

}
#endif
