
#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace OrbitRush
{

/// <summary>
/// Developer tool: run the game with the command-line flag <c>-movetest</c> (together with <c>-nomenu</c>)
/// and it drives the player with scripted input, logging speed, jump height and air momentum to the log
/// so the movement feel can be checked without playing. Does nothing unless the flag is present.
/// </summary>
public class MovementSelfTest : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Register()
    {
        foreach (var arg in System.Environment.GetCommandLineArgs())
        {
            if (arg != "-movetest") continue;
            SceneManager.sceneLoaded += (s, m) =>
            {
                if (FindFirstObjectByType<MovementSelfTest>() == null && FindFirstObjectByType<PlayerController>() != null)
                    new GameObject("MovementSelfTest").AddComponent<MovementSelfTest>();
            };
            return;
        }
    }

    private PlayerController _p;
    private Rigidbody _rb;

    IEnumerator Start()
    {
        yield return new WaitForSeconds(4f);
        _p = FindFirstObjectByType<PlayerController>();
        _rb = _p.GetComponent<Rigidbody>();
        Log($"start: grounded={_p.IsGrounded} planet={(_p.CurrentPlanet != null ? _p.CurrentPlanet.planetName : "none")}");

        // 1) accelerate
        Log("--- RUN forward 1.6s (expect a progressive ramp to ~8 m/s, not instant) ---");
        _p.SimulateMove(Vector2.up);
        for (int i = 0; i < 8; i++) { yield return new WaitForSeconds(0.2f); Log($"t={0.2f * (i + 1):F1}s speed={_p.PlanarSpeed:F2}"); }

        // 2) decelerate
        Log("--- RELEASE (expect a progressive stop, not instant) ---");
        _p.SimulateMove(Vector2.zero);
        for (int i = 0; i < 6; i++) { yield return new WaitForSeconds(0.1f); Log($"t={0.1f * (i + 1):F1}s speed={_p.PlanarSpeed:F2}"); }
        yield return new WaitForSeconds(1f);

        // 3) short hop (tap) vs full jump (hold)
        yield return StartCoroutine(JumpTest("TAP (short hop)", 0.06f));
        yield return new WaitForSeconds(1.5f);
        yield return StartCoroutine(JumpTest("HOLD (full jump)", 1.5f));
        yield return new WaitForSeconds(1.5f);

        // 4) momentum in the air
        Log("--- RUN + JUMP, then release input in the air (expect speed to be KEPT) ---");
        _p.SimulateMove(Vector2.up);
        yield return new WaitForSeconds(1.2f);
        _p.SimulateJump(true);
        yield return new WaitForSeconds(0.1f);
        _p.SimulateJump(false);
        _p.SimulateMove(Vector2.zero);
        float v0 = _p.PlanarSpeed;
        for (int i = 0; i < 4; i++) { yield return new WaitForSeconds(0.15f); Log($"air t={0.15f * (i + 1):F2}s speed={_p.PlanarSpeed:F2} (took off at {v0:F2}) grounded={_p.IsGrounded}"); }
        yield return new WaitForSeconds(2f);

        // 5) jump buffer: press just before landing
        Log("--- JUMP BUFFER: tap jump while still in the air, expect an automatic jump on landing ---");
        _p.SimulateMove(Vector2.zero);
        _p.SimulateJump(true); yield return new WaitForSeconds(0.05f); _p.SimulateJump(false);
        float wait = 0f;
        while (!_p.IsGrounded && wait < 3f) { yield return null; wait += Time.deltaTime; }
        // press again right as it would land (buffered), then watch whether it leaves the ground
        _p.SimulateJump(true); yield return new WaitForSeconds(0.05f); _p.SimulateJump(false);
        yield return new WaitForSeconds(0.25f);
        Log($"after buffered press grounded={_p.IsGrounded} (false = it jumped)");

        Log("=== MOVEMENT SELF-TEST DONE ===");
        Application.Quit();
    }

    private IEnumerator JumpTest(string label, float holdSeconds)
    {
        while (!_p.IsGrounded) yield return null;
        yield return new WaitForSeconds(0.3f);
        Vector3 center = _p.CurrentPlanet != null ? _p.CurrentPlanet.transform.position : Vector3.zero;
        float ground = Vector3.Distance(_p.transform.position, center);
        float apex = ground;

        _p.SimulateJump(true);
        float t = 0f;
        bool released = false;
        float airTime = 0f;
        while (t < 3f)
        {
            yield return null;
            t += Time.deltaTime;
            if (!released && t >= holdSeconds) { _p.SimulateJump(false); released = true; }
            float d = Vector3.Distance(_p.transform.position, center);
            if (d > apex) apex = d;
            if (!_p.IsGrounded) airTime += Time.deltaTime;
            if (t > 0.3f && _p.IsGrounded) break;
        }
        _p.SimulateJump(false);
        Log($"{label}: apex height = {apex - ground:F2} m, air time = {airTime:F2} s");
    }

    private static void Log(string msg) => Debug.Log("[MoveTest] " + msg);
}

}
#endif
