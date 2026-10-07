#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace OrbitRush
{

/// <summary>
/// Developer tool: <c>-aitest robot|drone|zombie</c> (with <c>-nomenu</c>) stages encounters with the nearest enemy of that
/// kind and logs how it perceives and reacts, so the behaviour can be checked without playing.
/// </summary>
public class AISelfTest : MonoBehaviour
{
    private static string _mode;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Register()
    {
        var args = System.Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i] != "-aitest") continue;
            _mode = args[i + 1];
            SceneManager.sceneLoaded += (s, m) =>
            {
                if (FindFirstObjectByType<AISelfTest>() == null && FindFirstObjectByType<PlayerController>() != null)
                    new GameObject("AISelfTest").AddComponent<AISelfTest>();
            };
            return;
        }
    }

    private PlayerController _pc;
    private PlayerStats _ps;
    private Rigidbody _rb;
    private static void Log(string m) => Debug.Log("[AITest] " + m);

    IEnumerator Start()
    {
        yield return new WaitForSeconds(5f);
        _pc = FindFirstObjectByType<PlayerController>();
        _ps = _pc.GetComponent<PlayerStats>();
        _rb = _pc.GetComponent<Rigidbody>();
        _ps.InvulnerableUntil = Time.time + 9999f;
        if (SupportRobot.Instance != null) SupportRobot.Instance.enabled = false;        // the support robot would shoot the enemies under test

        var kind = _mode == "drone" ? EnemyKind.Drone : _mode == "zombie" ? EnemyKind.Zombie : EnemyKind.Robot;
        // the biggest planet gives the most room (small planets have a very close horizon)
        var enemy = EnemyAI.All.Where(e => e != null && e.kind == kind && e.Stats.IsAlive && e.Controller.CurrentPlanet != null).OrderByDescending(e => e.Controller.CurrentPlanet.radius).FirstOrDefault();
        if (enemy == null) { Log($"no {kind} found ({EnemyAI.All.Count} enemies total)"); yield break; }
        Log($"testing {kind} '{enemy.name}' on {enemy.Controller.CurrentPlanet?.planetName}; senses: view {enemy.Senses.viewDistance} m, {enemy.Senses.viewAngle} deg, hearing x{enemy.Senses.hearing}");

        if (kind == EnemyKind.Robot) yield return RobotScenario(enemy);
        else if (kind == EnemyKind.Drone) yield return DroneScenario(enemy);
        else yield return ZombieScenario(enemy);
        Log("=== AI SELF-TEST DONE ===");
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    private void Place(EnemyAI e, float distance, float angle, float height = 0f, bool reset = true)
    {
        Vector3 up = e.Controller.PlanetUp;
        Vector3 fwd = Vector3.ProjectOnPlane(e.transform.forward, up).normalized;
        Vector3 dir = Quaternion.AngleAxis(angle, up) * fwd;
        var planet = e.Controller.CurrentPlanet;
        Vector3 p = e.transform.position + dir * distance;
        if (planet != null) p = planet.GetSurfacePoint((p - planet.transform.position).normalized) + (p - planet.transform.position).normalized * (1.2f + height);
        Vector3 pup = planet != null ? (p - planet.transform.position).normalized : up;
        _rb.position = p; _rb.linearVelocity = Vector3.zero;
        var rot = Quaternion.LookRotation(Vector3.ProjectOnPlane(e.transform.position - p, pup).normalized, pup);
        _rb.rotation = rot; _pc.transform.SetPositionAndRotation(p, rot);
        if (reset) e.DebugReset();
    }

    private IEnumerator Watch(EnemyAI e, float seconds, float step = 0.5f, System.Action extra = null)
    {
        string last = "";
        for (float t = 0f; t < seconds; t += step)
        {
            yield return new WaitForSeconds(step);
            if (e == null) { Log("enemy gone"); yield break; }
            string s = e.StateName;
            float d = Vector3.Distance(e.transform.position, _pc.transform.position);
            string flag = s != last ? " <-- change" : "";
            Log($"t={t + step:F1} state={s} dist={d:F0} suspicion={e.Senses.TopSuspicion:F2} detected={(e.Senses.Target != null)} heard={e.Senses.HeardRecently(1f)} hp={_ps.health:F0}{flag}");
            last = s;
            extra?.Invoke();
        }
    }

    // ── Robot ─────────────────────────────────────────────────────────────

    private IEnumerator RobotScenario(EnemyAI r)
    {
        Log("--- 1) player far outside the cone/range (behind it, 70 m): the robot should stay on patrol ---");
        Place(r, 70f, 180f);
        yield return Watch(r, 3f);

        Log("--- 2) the player makes noise out of sight (a shot fired 25 m behind the robot): suspicion, not combat ---");
        Place(r, 25f, 180f);
        NoiseSystem.Emit(_pc.transform.position, Loudness.Shoot, _pc.gameObject);
        yield return Watch(r, 4f);

        Log("--- 3) the player walks into its view at 30 m (in the cone): suspicion rises then combat; laser warns before bursts ---");
        r.Senses.Clear();
        Place(r, 18f, 0f);
        float hp0 = _ps.health;
        int telegraphs = 0, bursts = 0; string lastPhase = "";
        for (float t = 0f; t < 10f; t += 0.1f)
        {
            yield return new WaitForSeconds(0.1f);
            if (Mathf.Abs(t % 0.5f) < 0.05f && r.StateName != "Combat") Place(r, 18f, 0f, 0f, false);     // keep standing in front of it while it turns
            var fi = typeof(EnemyAI).GetField("_burstPhase", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            string ph = fi != null ? fi.GetValue(r).ToString() : "?";
            if (ph != lastPhase) { if (ph == "Telegraph") telegraphs++; if (ph == "Burst") bursts++; lastPhase = ph; }
            if (Mathf.Abs(t % 1f) < 0.05f) Log($"t={t:F1} state={r.StateName} burst={ph} suspicion={r.Senses.TopSuspicion:F2} hp={_ps.health:F0} | {r.Senses.Explain(r.Senses.Contacts.FirstOrDefault())}");
        }
        Log($"result: telegraphs={telegraphs} bursts={bursts} damage taken={hp0 - _ps.health:F0}");

        Log("--- 4) crouching halves how fast it notices; running doubles it (fill rate comparison at 30 m) ---");
        foreach (var mode in new[] { "walk", "run", "crouch" })
        {
            Place(r, 18f, 0f);
            yield return new WaitForSeconds(0.8f);                 // let the player settle before the stance counts
            r.Senses.Clear(); r.DebugReset();
            _pc.SimulateCrouch(mode == "crouch"); _pc.SimulateSprint(mode == "run");
            float t0 = Time.time; float tDetect = -1f;
            while (Time.time - t0 < 12f)
            {
                yield return new WaitForSeconds(0.1f);
                if (Mathf.Abs((Time.time - t0) % 0.5f) < 0.06f) Place(r, 18f, 0f, 0f, false);
                if (r.Senses.TopSuspicion >= r.Senses.suspiciousAt) { tDetect = Time.time - t0; break; }
            }
            Log($"{mode}: suspicious after {(tDetect < 0 ? "never" : tDetect.ToString("F1") + " s")}");
            _pc.SimulateCrouch(false); _pc.SimulateSprint(false);
            yield return new WaitForSeconds(2f);
        }

        Log("--- 5) weak point: damage from behind vs from the front (10 base damage) ---");
        r.Stats.backWeakness = 2.5f;
        float h = r.Stats.Health;
        Place(r, 6f, 180f); yield return new WaitForSeconds(0.2f);
        r.Stats.TakeDamage(10f, _ps);
        float back = h - r.Stats.Health; h = r.Stats.Health;
        Place(r, 6f, 0f); yield return new WaitForSeconds(0.2f);
        r.Stats.TakeDamage(10f, _ps);
        float front = h - r.Stats.Health;
        Log($"from behind: {back:F1}, from the front: {front:F1}");

        Log("--- 6) disabled: a critical hit knocks it out and it takes extra damage ---");
        r.Stats.Disable(2f);
        yield return Watch(r, 2.5f, 0.5f);
    }

    // ── Drone ─────────────────────────────────────────────────────────────

    private IEnumerator DroneScenario(EnemyAI d)
    {
        Log("--- player 28 m in front of the drone on the ground: it should detect, track and mark ---");
        Place(d, 28f, 0f);
        int emps = 0;
        float tMark = -1f; float t0 = Time.time;
        for (float t = 0f; t < 22f; t += 0.5f)
        {
            yield return new WaitForSeconds(0.5f);
            if (tMark < 0f && RadarMarks.IsMarked(_ps)) tMark = Time.time - t0;
            emps = FindObjectsByType<Projectile>(FindObjectsSortMode.None).Count(p => p.empSeconds > 0f);
            if (d == null) { Log("drone gone (exploded?)"); break; }
            Log($"t={t + 0.5f:F1} state={d.StateName} droneHp={d.Stats.Health:F0}/{d.Stats.maxHealth:F0} shield={d.Stats.Shield:F0} dist={Vector3.Distance(d.transform.position, _pc.transform.position):F0} suspicion={d.Senses.TopSuspicion:F2} vel={d.Controller.Body.linearVelocity.magnitude:F1} free={d.Controller.freeFlight} ctl={d.Controller.controlEnabled} [{d.Senses.Explain(d.Senses.Contacts.FirstOrDefault())}] marked={RadarMarks.IsMarked(_ps)} jetpackLocked={_ps.JetpackLocked} hp={_ps.health:F0} empInFlight={emps}");
        }
        Log($"marked after {(tMark < 0 ? "never" : tMark.ToString("F1") + " s")}");

        Log("--- space chase: the player launches with the jetpack toward another planet ---");
        PlanetGravity target = GravitySystem.Instance.Planets.Where(p => p != _pc.CurrentPlanet).OrderBy(p => Vector3.Distance(p.transform.position, _pc.transform.position)).First();
        var home = _pc.CurrentPlanet;
        Vector3 toT = (target.transform.position - home.transform.position).normalized;
        Vector3 spot = home.GetSurfacePoint(toT) + toT * 1.2f;
        _rb.position = spot; _rb.linearVelocity = Vector3.zero; _rb.rotation = Quaternion.LookRotation(Vector3.ProjectOnPlane(Vector3.up, toT).normalized, toT);
        _ps.jetpackEnergy = 100f;
        yield return new WaitForSeconds(2f);
        // bring the drone close and make it notice the player (it shoots at a drone... we just damage it a little)
        d.Controller.Body.position = _pc.transform.position + _pc.transform.up * 6f - _pc.transform.forward * 14f + _pc.transform.right * 9f;
        d.Controller.Body.linearVelocity = Vector3.zero;
        d.DebugReset(); d.Stats.TakeDamage(0.1f, _ps);
        d.empPrefab = null; _ps.UnlockJetpack(); _ps.jetpackEnergy = 100f;      // no EMP for this part: we want to see the chase
        yield return new WaitForSeconds(2f);
        // The player jumps to another planet (teleport: the drone must notice it left and follow through open space)
        {
            Vector3 dest = target.GetSurfacePoint((home.transform.position - target.transform.position).normalized) + (home.transform.position - target.transform.position).normalized * 1.5f;
            _rb.position = dest; _rb.linearVelocity = Vector3.zero;
            _pc.transform.position = dest;
            Log($"player moved to {target.planetName}");
        }
        for (float t = 0f; t < 22f; t += 1f)
        {
            yield return new WaitForSeconds(1f);
            if (d == null) { Log("drone gone"); break; }
            Log($"t={t + 0.5f:F1} player {_pc.Phase} on {_pc.CurrentPlanet?.planetName} | drone state={d.StateName} on {d.Controller.CurrentPlanet?.planetName} freeFlight={d.Controller.freeFlight} vel={d.Controller.Body.linearVelocity.magnitude:F1} spd={d.Controller.speedScale:F2} hp={d.Stats.Health:F0}/{d.Stats.Shield:F0} target={(d.Senses.Target != null)} kin={d.Controller.Body.isKinematic} ctl={d.Controller.controlEnabled} flying={d.Controller.flying} dist={Vector3.Distance(d.transform.position, _pc.transform.position):F0}");
        }
    }

    // ── Zombie ────────────────────────────────────────────────────────────

    private IEnumerator ZombieScenario(EnemyAI z)
    {
        Log("--- 1) a loud noise 50 m away (a shot): zombies are drawn to it ---");
        Place(z, 50f, 180f);
        NoiseSystem.Emit(_pc.transform.position, Loudness.Shoot, _pc.gameObject);
        yield return Watch(z, 5f);

        Log("--- 2) the player 8 m in front and visible: chase, then melee, grab chance ---");
        z.Senses.Clear();
        Place(z, 8f, 0f);
        float hp0 = _ps.health; bool grabbed = false;
        for (float t = 0f; t < 14f; t += 0.5f)
        {
            yield return new WaitForSeconds(0.5f);
            grabbed |= _pc.IsGrabbed;
            Log($"t={t + 0.5f:F1} state={z.StateName} dist={Vector3.Distance(z.transform.position, _pc.transform.position):F1} hp={_ps.health:F0} grabbed={_pc.IsGrabbed} | {z.Senses.Explain(z.Senses.Contacts.FirstOrDefault())}");
            if (_pc.IsGrabbed)
            {
                _pc.SimulateJetpack(true); yield return new WaitForSeconds(0.3f);
                Log($"jetpack while grabbed: phase={_pc.Phase} (should stay Idle)");
                _pc.SimulateJetpack(false);
                for (int i = 0; i < 6; i++) { _pc.DebugMashJump(); }
            }
            if (z == null || !z.Stats.IsAlive) break;
        }
        Log($"result: damage taken {hp0 - _ps.health:F0}, was grabbed: {grabbed}");

        Log("--- 3) contagion: another zombie nearby joins the chase ---");
        var others = EnemyAI.All.Where(e => e != null && e != z && e.kind == EnemyKind.Zombie && Vector3.Distance(e.transform.position, z.transform.position) < 40f).ToList();
        Log($"{others.Count} other zombie(s) within 40 m; states: {string.Join(", ", others.Select(o => o.StateName))}");
    }
}
}
#endif
