#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace OrbitRush
{

/// <summary>
/// Developer tool: <c>-jetpacktest</c> (with <c>-nomenu</c>) charges the jetpack aimed at a neighbouring planet,
/// launches, logs the five flight phases and the landing, then checks fuel, cooldown, orbit and air-spin.
/// </summary>
public class JetpackSelfTest : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Register()
    {
        foreach (var arg in System.Environment.GetCommandLineArgs())
        {
            if (arg != "-jetpacktest") continue;
            SceneManager.sceneLoaded += (s, m) =>
            {
                if (FindFirstObjectByType<JetpackSelfTest>() == null && FindFirstObjectByType<PlayerController>() != null)
                    new GameObject("JetpackSelfTest").AddComponent<JetpackSelfTest>();
            };
            return;
        }
    }

    private PlayerController _p;
    private PlayerStats _stats;
    private static void Log(string m) => Debug.Log("[JetpackTest] " + m);

    // Patterns a real player uses: does the jetpack ever get "stuck" afterwards?
    private IEnumerator StickScenario()
    {
        var rb = _p.GetComponent<Rigidbody>();
        _p.HardLanded += (impact, forced, stunned) => Log($"   hard landing impact={impact:F1} forced={forced} stunned={stunned}");
        string[] patterns = { "no-target (aim up), release, wait", "hold Ctrl through the whole flight and the landing", "tap Ctrl quickly on the ground", "target dash, hold Ctrl all the time", "grazing launch: aim along the ground at the planet near the horizon" };
        for (int pat = 0; pat < patterns.Length; pat++)
        {
            Log($"=== pattern {pat + 1}: {patterns[pat]}");
            // stand on the side of the planet facing the nearest other planet
            PlanetGravity target = null; float best = 1e9f;
            foreach (var pl in GravitySystem.Instance.Planets)
            {
                if (pl == _p.CurrentPlanet) continue;
                float d = Vector3.Distance(pl.transform.position, _p.transform.position);
                if (d < best) { best = d; target = pl; }
            }
            var home = _p.CurrentPlanet;
            Vector3 toT = (target.transform.position - home.transform.position).normalized;
            Vector3 spot = home.GetSurfacePoint(toT) + toT * 1.2f;
            rb.position = spot; rb.linearVelocity = Vector3.zero;
            rb.rotation = Quaternion.LookRotation(Vector3.ProjectOnPlane(Vector3.up + Vector3.right * 0.3f, toT).normalized, toT);
            _p.transform.SetPositionAndRotation(spot, rb.rotation);
            _stats.UnlockJetpack(); _stats.jetpackEnergy = 100f;
            yield return new WaitForSeconds(2.5f);

            if (pat == 0) { _p.DebugAimAt(home.transform.position + toT * 500f + _p.transform.up * 400f); }
            else if (pat == 4) { _p.DebugAimAt(_p.transform.position + Vector3.ProjectOnPlane(toT, _p.transform.up).normalized * 300f); }       // flat along the ground
            else _p.DebugAimAt(target.transform.position);
            if (pat == 2) { for (int i = 0; i < 3; i++) { _p.SimulateJetpack(true); yield return new WaitForSeconds(0.12f); _p.SimulateJetpack(false); yield return new WaitForSeconds(0.2f); } }
            else
            {
                _p.SimulateJetpack(true); yield return new WaitForSeconds(1.3f);
                if (pat != 1 && pat != 3) _p.SimulateJetpack(false);
            }
            bool landed = false;
            for (float t = 0f; t < 16f; t += 0.5f)
            {
                yield return new WaitForSeconds(0.5f);
                Log($"   t={t + 0.5f:F1} phase={_p.Phase} sonic={_p.IsSonic} grounded={_p.IsGrounded} stunned={_p.IsStunned} locked={_stats.JetpackLocked} fuel={_stats.jetpackEnergy:F0} speed={rb.linearVelocity.magnitude:F1} planet={_p.CurrentPlanet?.planetName} moveScale?");
                if (_p.IsGrounded && _p.Phase == JetpackPhase.Idle && t > 1.5f) { landed = true; if (pat == 1 || pat == 3) _p.SimulateJetpack(false); break; }
            }
            Log($"   landed={landed}; phase now {_p.Phase}; releasing and trying to move + take off again");
            _p.SimulateJetpack(false);
            yield return new WaitForSeconds(2.5f);
            Vector3 before = _p.transform.position;
            _p.SimulateMove(Vector2.up);
            yield return new WaitForSeconds(1.5f);
            float moved = Vector3.Distance(before, _p.transform.position);
            _p.SimulateMove(Vector2.zero);
            Log($"   walking 1.5 s after the flight moved {moved:F1} m (stuck if ~0)");
            _p.SimulateJetpack(true); yield return new WaitForSeconds(0.15f);
            Log($"   Ctrl held on ground: phase={_p.Phase} charge={_p.ChargeAmount:F2} fuel={_stats.jetpackEnergy:F0} cooldownLeft={_p.LaunchCooldownLeft:F1}");
            _p.SimulateJetpack(false); yield return new WaitForSeconds(0.3f);
        }
        Log("=== JETPACK STICK TEST DONE ===");
    }

    IEnumerator Start()
    {
        yield return new WaitForSeconds(4f);
        _p = FindFirstObjectByType<PlayerController>();
        _stats = _p.GetComponent<PlayerStats>();
        if (System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-jpstick") >= 0) { yield return StickScenario(); yield break; }
        _p.HardLanded += (impact, forced, stunned) => Log($"HARD LANDING impact={impact:F1} forced={forced} stunned={stunned}");
        Log($"start on {_p.CurrentPlanet?.planetName} grounded={_p.IsGrounded} fuel={_stats.jetpackEnergy:F0}");

        // nearest other planet
        PlanetGravity target = null; float best = 1e9f;
        foreach (var pl in GravitySystem.Instance.Planets)
        {
            if (pl == _p.CurrentPlanet) continue;
            float d = Vector3.Distance(pl.transform.position, _p.transform.position);
            if (d < best) { best = d; target = pl; }
        }
        Log($"target {target.planetName} at {best:F0} m (field radius {target.FieldRadius:F0})");
        // stand on the side of the home planet that faces the target (otherwise the planet itself is in the way)
        {
            var home = _p.CurrentPlanet;
            Vector3 toT = (target.transform.position - home.transform.position).normalized;
            Vector3 spot = home.GetSurfacePoint(toT) + toT * 1.2f;
            var rb0 = _p.GetComponent<Rigidbody>();
            rb0.position = spot; rb0.linearVelocity = Vector3.zero;
            rb0.rotation = Quaternion.LookRotation(Vector3.ProjectOnPlane(Vector3.up + Vector3.right * 0.3f, toT).normalized, toT);
            _p.transform.SetPositionAndRotation(spot, rb0.rotation);
            yield return new WaitForSeconds(1.5f);
            Log($"moved to the side facing the target: grounded={_p.IsGrounded}");
        }

        // 1) charge: slow movement + aim assist
        _p.DebugAimAt(target.transform.position);
        _p.SimulateMove(Vector2.up);
        _p.SimulateJetpack(true);
        for (int i = 0; i < 6; i++)
        {
            yield return new WaitForSeconds(0.25f);
            Log($"charging phase={_p.Phase} charge={_p.ChargeAmount:F2} speed={_p.PlanarSpeed:F2} aimTarget={_p.AimTarget?.planetName}");
        }
        _p.SimulateMove(Vector2.zero);

        // 2) release -> launch
        _p.SimulateJetpack(false);
        float t = 0f; JetpackPhase last = _p.Phase; float fuelAtLaunch = _stats.jetpackEnergy;
        Log($"released: fuel={fuelAtLaunch:F0}");
        float minDist = 1e9f;
        while (t < 14f)
        {
            yield return new WaitForSeconds(0.25f); t += 0.25f;
            var ph = _p.Phase;
            float d = Vector3.Distance(_p.transform.position, target.transform.position);
            minDist = Mathf.Min(minDist, d);
            if (ph != last || Mathf.Approximately(t % 1f, 0f))
                Log($"t={t:F2} phase={ph} dist={d:F0} planet={_p.CurrentPlanet?.planetName} grounded={_p.IsGrounded} speed={_p.GetComponent<Rigidbody>().linearVelocity.magnitude:F1} fuel={_stats.jetpackEnergy:F0}");
            last = ph;
            bool forced = System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-jpforced") >= 0;
            if (forced) _p.SimulateCrouch(ph == JetpackPhase.Captured);                      // brace for a forced landing (shockwave)
            else _p.SimulateJetpack(false);                                                  // no input: the free soft-landing assist must make this survivable
            if (ph == JetpackPhase.Idle && _p.IsGrounded && t > 2f) break;
        }
        Log($"flight done: min distance to target {minDist:F0}, now on {_p.CurrentPlanet?.planetName}, grounded={_p.IsGrounded}, stunned={_p.IsStunned}");

        // 3) fuel recharges only on the ground, after a second
        yield return new WaitForSeconds(3f);
        Log($"fuel after 3 s on the ground: {_stats.jetpackEnergy:F0} (was {fuelAtLaunch:F0} just after launch)");
        Log($"cooldown left {_p.LaunchCooldownLeft:F1}s overheated={_stats.JetpackOverheated}");

        // 4) overheat: drain the fuel completely
        _stats.jetpackEnergy = 5f;
        _p.SimulateJump(true); yield return new WaitForSeconds(0.1f); _p.SimulateJump(false);
        yield return new WaitForSeconds(0.2f);
        _p.SimulateJetpack(true);
        yield return new WaitForSeconds(1.0f);
        _p.SimulateJetpack(false);
        Log($"after draining: fuel={_stats.jetpackEnergy:F1} locked={_stats.JetpackLocked} overheated={_stats.JetpackOverheated} lockLeft={_stats.JetpackLockRemaining:F1}");

        // 5) long jump (run + crouch + jump) -> fast low arc; on a small planet it can enter orbit; then an air spin
        yield return new WaitForSeconds(4f);
        Log($"--- long jump: on {_p.CurrentPlanet?.planetName} (radius {_p.CurrentPlanet?.radius:F0}) grounded={_p.IsGrounded} stunned={_p.IsStunned}");
        _p.SimulateMove(Vector2.up); _p.SimulateSprint(true);
        yield return new WaitForSeconds(1.2f);
        _p.SimulateCrouch(true);
        _p.SimulateJump(true); yield return new WaitForSeconds(0.08f); _p.SimulateJump(false);
        bool orbited = false; bool spun = false; float maxAlt = 0f;
        var rb = _p.GetComponent<Rigidbody>();
        for (float tt = 0f; tt < 6f; tt += 0.25f)
        {
            yield return new WaitForSeconds(0.25f);
            _p.SimulateCrouch(false);
            orbited |= _p.IsOrbiting;
            float alt = PlanetBody.Altitude(_p.transform.position, _p.CurrentPlanet);
            maxAlt = Mathf.Max(maxAlt, alt);
            if (!spun && !_p.IsGrounded && tt > 1.2f && Vector3.Dot(rb.linearVelocity, _p.transform.up) < 0f) { _p.SimulateJump(true); spun = true; Log("air spin pressed"); }
            else _p.SimulateJump(false);
            Log($"t={tt + 0.25f:F2} speed={rb.linearVelocity.magnitude:F1} planar={_p.PlanarSpeed:F1} altitude={alt:F1} grounded={_p.IsGrounded} orbiting={_p.IsOrbiting}");
            if (_p.IsGrounded && tt > 1f) break;
        }
        _p.SimulateMove(Vector2.zero); _p.SimulateSprint(false);
        Log($"long jump: max altitude {maxAlt:F1}, entered orbit: {orbited}, air-spun: {spun}");

        Log("=== JETPACK SELF-TEST DONE ===");
    }
}
}
#endif
