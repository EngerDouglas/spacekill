#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections;
using System.Linq;
using UnityEngine;

namespace OrbitRush
{

/// <summary>
/// Test aid: `-shot &lt;file.png&gt; [-shotdelay s] [-shotact walk|run|back|left|right|air|light|charged|dodge|sword|death] [-shotafter s]`
/// optionally triggers a player action, saves the game view `shotafter` seconds later and quits.
/// </summary>
public class ScreenshotTool : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Register()
    {
        string path = null, act = null; float delay = 9f, after = 0.3f;
        var args = System.Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i] == "-shot") path = args[i + 1];
            if (args[i] == "-shotact") act = args[i + 1];
            if (args[i] == "-shotdelay") float.TryParse(args[i + 1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out delay);
            if (args[i] == "-shotafter") float.TryParse(args[i + 1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out after);
        }
        if (path == null) return;
        var go = new GameObject("ScreenshotTool"); DontDestroyOnLoad(go);
        var t = go.AddComponent<ScreenshotTool>();
        t.StartCoroutine(t.Run(path, act, delay, after));
    }

    private IEnumerator Run(string path, string act, float delay, float after)
    {
        yield return new WaitForSecondsRealtime(Mathf.Max(0.1f, delay - after));
        if (act != null) yield return StartCoroutine(Act(act, after));
        else yield return new WaitForSecondsRealtime(after);
        yield return new WaitForEndOfFrame();
        var pc = FindFirstObjectByType<PlayerController>();
        if (pc != null)
        {
            var cp = pc.CurrentPlanet;
            Debug.Log($"[ShotDebug] player={pc.transform.position} planet={(cp != null ? cp.planetName : "none")} dist={(cp != null ? Vector3.Distance(cp.transform.position, pc.transform.position) : -1f):F1} radius={(cp != null ? cp.radius : 0f)} grounded={pc.IsGrounded}");
        }
        ScreenCapture.CaptureScreenshot(path);
        yield return new WaitForSecondsRealtime(1.5f);
        Application.Quit();
    }

    /// <summary>"show_&lt;Key&gt;": both rigged characters side by side in empty space playing one CharacterAnimator state.</summary>
    private IEnumerator Showcase(string key, float after)
    {
        foreach (var other in FindObjectsByType<Camera>(FindObjectsSortMode.None)) other.enabled = false;
        var root = new GameObject("Showcase"); root.transform.position = new Vector3(0f, 4000f, 0f);
        var camGo = new GameObject("ShowCam"); var cam = camGo.AddComponent<Camera>();
        cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0.08f, 0.09f, 0.14f);
        cam.fieldOfView = 40f; camGo.transform.SetParent(root.transform, false);
        bool side = key.EndsWith("_side");
        if (side) key = key.Substring(0, key.Length - 5);
        camGo.transform.localPosition = side ? new Vector3(7f, 1.1f, 0f) : new Vector3(0f, 1.2f, 7f);
        camGo.transform.localRotation = Quaternion.LookRotation(new Vector3(0f, 1.0f, 0f) - camGo.transform.localPosition, Vector3.up);
        var lightGo = new GameObject("ShowLight"); var l = lightGo.AddComponent<Light>(); l.type = LightType.Directional; l.intensity = 1.6f;
        lightGo.transform.SetParent(root.transform, false); lightGo.transform.localRotation = Quaternion.Euler(35f, 150f, 0f);
        string[] models = { "Models/OrbitRush_DroidRig", "Models/OrbitRush_SoldierRig" };
        for (int i = 0; i < models.Length; i++)
        {
            var prefab = Resources.Load<GameObject>(models[i]);
            if (prefab == null) continue;
            var inst = Instantiate(prefab, root.transform);
            inst.transform.localPosition = new Vector3(i == 0 ? -1.3f : 1.3f, 0f, 0f);
            var anim = inst.AddComponent<CharacterAnimator>();
            if (!anim.Setup(inst.GetComponentInChildren<Animator>())) continue;
            anim.ClearBaseTargetsExcept(key);
            anim.SetBase(key, 1f, true);
            anim.SetUpperTarget(key == CharacterAnimator.Idle || key == CharacterAnimator.Walk || key == CharacterAnimator.Run ? 0f : 0f);
        }
        yield return new WaitForSecondsRealtime(after);
    }

    /// <summary>"arms" / "armsammo": the five weapon models with their bolts and impacts (or the sniper's four ammo types) on a shelf.</summary>
    private IEnumerator Arms(bool ammo, float after)
    {
        foreach (var other in FindObjectsByType<Camera>(FindObjectsSortMode.None)) other.enabled = false;
        var root = new GameObject("ArmsShowcase"); root.transform.position = new Vector3(0f, 4000f, 0f);
        var camGo = new GameObject("ArmsCam"); var cam = camGo.AddComponent<Camera>();
        cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0.08f, 0.09f, 0.14f);
        cam.fieldOfView = 38f; cam.farClipPlane = 200f; camGo.transform.SetParent(root.transform, false);
        camGo.transform.localPosition = new Vector3(0f, 0.9f, -7.5f);
        camGo.transform.localRotation = Quaternion.LookRotation(new Vector3(0f, 0.7f, 0f) - camGo.transform.localPosition, Vector3.up);
        var lightGo = new GameObject("ArmsLight"); var l = lightGo.AddComponent<Light>(); l.type = LightType.Directional; l.intensity = 1.6f;
        lightGo.transform.SetParent(root.transform, false); lightGo.transform.localRotation = Quaternion.Euler(35f, 150f, 0f);
        string[] kinds = ammo ? new[] { "GravitySniper", "GravitySniper_ap", "GravitySniper_inc", "GravitySniper_exp" }
                              : new[] { "Blaster", "SpaceShotgun", "PlasmaRifle", "GravitySniper", "OrbitalLauncher" };
        float spacing = 2.4f, x0 = -(kinds.Length - 1) * spacing * 0.5f;
        for (int i = 0; i < kinds.Length; i++)
        {
            string k = kinds[i]; float x = x0 + i * spacing;
            string wk = k.Split('_')[0];
            if (!ammo)
            {
                var w = WeaponModels.Load(k);
                if (w != null) { var g = Instantiate(w, root.transform); g.transform.localPosition = new Vector3(x, 1.6f, 0f); g.transform.localRotation = Quaternion.Euler(0f, 90f, 0f); }
            }
            string suffix = k.Contains("_") ? k.Substring(k.IndexOf('_')) : "";
            var bolt = WeaponModels.Load(wk + "_Bolt" + suffix);
            if (bolt != null) { var g = Instantiate(bolt, root.transform); g.transform.localPosition = new Vector3(x, ammo ? 1.4f : 0.5f, 0f); g.transform.localRotation = Quaternion.Euler(0f, 90f, 0f); g.transform.localScale = Vector3.one * 2f; }
            var imp = WeaponModels.Load(wk + "_Impact" + suffix);
            if (imp != null) { var g = Instantiate(imp, root.transform); g.transform.localPosition = new Vector3(x, ammo ? -0.4f : -0.9f, 0f); g.transform.localRotation = Quaternion.Euler(-20f, 0f, 0f); g.transform.localScale = Vector3.one * 2.2f; }
        }
        yield return new WaitForSecondsRealtime(after);
    }

    private IEnumerator Act(string act, float after)
    {
        if (act.StartsWith("show_")) { yield return StartCoroutine(Showcase(act.Substring(5), after)); yield break; }
        if (act == "arms" || act == "armsammo") { yield return StartCoroutine(Arms(act == "armsammo", after)); yield break; }
        var p = FindFirstObjectByType<PlayerController>();
        var melee = p != null ? p.GetComponent<MeleeCombat>() : null;
        if (p == null) yield break;
        var aimComp = p.GetComponent<WeaponAim>();
        if (aimComp != null) aimComp.DebugSetFirstPerson(act.StartsWith("fp"));      // the saved F2 preference must not change the test view
        switch (act)
        {
            case "walk": p.SimulateMove(Vector2.up); break;
            case "run": p.SimulateMove(Vector2.up); p.SimulateSprint(true); break;
            case "back": p.SimulateMove(Vector2.down); break;
            case "left": p.SimulateMove(Vector2.left); break;
            case "right": p.SimulateMove(Vector2.right); break;
            case "air": p.SimulateJump(true); break;
            case "light": melee?.DebugLight(); break;
            case "charged": melee?.DebugCharged(1f); break;
            case "dodge": melee?.DebugDodge(); break;
            case "sword": melee?.DebugDraw(); break;
            case "enemy":
            case "enemykill":
            {
                EnemyAI best = null; float bd = 1e9f;
                foreach (var e in FindObjectsByType<EnemyAI>(FindObjectsSortMode.None))
                {
                    if (e.GetComponent<EnemyAnimDriver>() == null) continue;
                    float d = Vector3.Distance(e.transform.position, p.transform.position);
                    if (d < bd) { bd = d; best = e; }
                }
                if (best != null)
                {
                    Vector3 up = p.transform.up;
                    Vector3 to = Vector3.ProjectOnPlane(best.transform.position - p.transform.position, up).normalized;
                    var rb = p.GetComponent<Rigidbody>();
                    rb.position = best.transform.position - to * 4.5f + up * 0.3f;
                    rb.rotation = Quaternion.LookRotation(to, up);
                    rb.linearVelocity = Vector3.zero;
                    p.GetComponent<PlayerStats>().shield = 9999f; p.GetComponent<PlayerStats>().health = 9999f;
                    var camGo = new GameObject("ShotCam");
                    var cam = camGo.AddComponent<Camera>();
                    cam.depth = 100; cam.fieldOfView = 50f; cam.nearClipPlane = 0.1f; cam.farClipPlane = 2000f;
                    Vector3 side = Vector3.Cross(up, to).normalized;
                    camGo.transform.position = best.transform.position + side * 3.2f - to * 1.2f + up * 0.2f;
                    camGo.transform.rotation = Quaternion.LookRotation(best.transform.position + up * 0.0f - camGo.transform.position, up);
                    foreach (var other in FindObjectsByType<Camera>(FindObjectsSortMode.None)) if (other != cam) other.enabled = false;
                    if (act == "enemykill") { yield return new WaitForSecondsRealtime(after * 0.4f); best.GetComponent<EnemyStats>().TakeDamage(9999f, null); }
                }
                break;
            }
            case "gun":
            case "gunwalk":
            {
                var inv = p.GetComponent<WeaponInventory>();
                WeaponPickup best = null; float bd = 1e9f;
                foreach (var wp in FindObjectsByType<WeaponPickup>(FindObjectsSortMode.None))
                {
                    float d = Vector3.Distance(wp.transform.position, p.transform.position);
                    if (d < bd) { bd = d; best = wp; }
                }
                if (best != null) inv.TryPickup(best);
                yield return new WaitForSecondsRealtime(0.2f);
                var camGo = new GameObject("ShotCam");
                var cam = camGo.AddComponent<Camera>();
                cam.depth = 100; cam.fieldOfView = 45f; cam.nearClipPlane = 0.1f; cam.farClipPlane = 2000f;
                foreach (var other in FindObjectsByType<Camera>(FindObjectsSortMode.None)) if (other != cam) other.enabled = false;
                camGo.transform.SetParent(p.transform, false);
                camGo.transform.localPosition = new Vector3(2.6f, 1.2f, 1.4f);
                camGo.transform.LookAt(p.transform.position + p.transform.up * 0.3f, p.transform.up);
                if (act == "gunwalk") p.SimulateMove(Vector2.up);
                break;
            }
            case "hudfull":
            {
                if (GameManager.Instance != null) GameManager.Instance.SetGameMode(GameManager.GameMode.TeamDeathmatch);
                if (HUD.Instance != null) HUD.Instance.ShowEvent("TORMENTA DE METEORITOS", 30f);
                break;
            }
            case "drone0":
            case "drone1":
            case "drone2":
            case "drone3":
            case "drone4":
            {
                EnemyAI best = null; float bd = 1e9f;
                foreach (var e in FindObjectsByType<EnemyAI>(FindObjectsSortMode.None))
                {
                    if (e.GetComponent<DroneDamageStages>() == null) continue;
                    float d = Vector3.Distance(e.transform.position, p.transform.position);
                    if (d < bd) { bd = d; best = e; }
                }
                if (best != null)
                {
                    var st = best.GetComponent<EnemyStats>();
                    float[] frac = { 1f, 0.55f, 0.28f, 0.1f, 0f };
                    int idx = act[5] - '0';
                    float target = st.maxHealth * frac[idx];
                    if (idx == 4) st.TakeDamage(1e9f, null);          // kill it: it should crash and explode
                    else
                    {
                        st.TakeDamage(st.Shield + (st.maxHealth - target), null);
                        best.enabled = false; best.GetComponent<EnemyController>().enabled = false;   // hold still for the photo
                        best.GetComponent<Rigidbody>().linearVelocity = Vector3.zero;
                    }
                    Vector3 up = p.transform.up;
                    Vector3 to = Vector3.ProjectOnPlane(best.transform.position - p.transform.position, up).normalized;
                    var rb = p.GetComponent<Rigidbody>();
                    rb.position = best.transform.position - to * 6f; rb.rotation = Quaternion.LookRotation(to, up); rb.linearVelocity = Vector3.zero;
                    p.GetComponent<PlayerStats>().shield = 9999f; p.GetComponent<PlayerStats>().health = 9999f;
                    var camGo = new GameObject("ShotCam");
                    var cam = camGo.AddComponent<Camera>();
                    cam.depth = 100; cam.fieldOfView = 40f; cam.nearClipPlane = 0.05f; cam.farClipPlane = 2000f;
                    Vector3 side = best.transform.right;
                    camGo.transform.position = best.transform.position + side * 4.2f + best.transform.forward * 1.2f + best.transform.up * 0.5f;
                    camGo.transform.rotation = Quaternion.LookRotation(best.transform.position - camGo.transform.position, best.transform.up);
                    foreach (var other in FindObjectsByType<Camera>(FindObjectsSortMode.None)) if (other != cam) other.enabled = false;
                    Debug.Log($"[DroneShot] stage={best.GetComponent<DroneDamageStages>().Current} rootUp={best.transform.up} planetUp={up} visualEuler={best.transform.Find("Visual").localEulerAngles}");
                }
                break;
            }
            case "planet0":
            case "planet1":
            case "planet2":
            case "planet3":
            case "planet4":
            case "planet5":
            {
                string[] order = { "Bosque", "Desierto", "Tanques Industriales", "Neon Downtown", "Sky Gardens", "Nieve" };
                string want = order[act[6] - '0'];
                var gm = GameManager.Instance;
                if (gm != null && gm.spawnPoints != null)
                    foreach (var sp in gm.spawnPoints)
                        if (sp != null && sp.name.StartsWith("SpawnPoint_" + want))
                        {
                            var rb = p.GetComponent<Rigidbody>();
                            rb.position = sp.position; rb.rotation = sp.rotation; rb.linearVelocity = Vector3.zero;
                            p.transform.SetPositionAndRotation(sp.position, sp.rotation);
                            break;
                        }
                break;
            }
            case "hop25":
            case "hop40":
            {
                // Launch from Bosque toward Tanques Industriales (the nearest neighbour) with no thrust
                var gm = GameManager.Instance;
                Transform from = null;
                foreach (var sp in gm.spawnPoints) if (sp != null && sp.name.StartsWith("SpawnPoint_Bosque")) { from = sp; break; }
                PlanetGravity target = null;
                foreach (var pg in FindObjectsByType<PlanetGravity>(FindObjectsSortMode.None)) if (pg.planetName == "Tanques Industriales") target = pg;
                if (from != null && target != null)
                {
                    var rb = p.GetComponent<Rigidbody>();
                    rb.position = from.position + from.up * 2f; rb.rotation = from.rotation; p.transform.SetPositionAndRotation(rb.position, rb.rotation);
                    Vector3 dir = (target.transform.position - from.position).normalized;
                    float v = act == "hop40" ? 40f : 25f;
                    rb.linearVelocity = (dir * 0.8f + from.up * 0.2f).normalized * v;
                    p.GetComponent<PlayerStats>().health = 9999f; p.GetComponent<PlayerStats>().shield = 9999f;
                    Debug.Log($"[Hop] launched {act}");
                }
                break;
            }
            case "shield0":
            case "shield1":
            case "shield2":
            {
                EnemyAI best = null; float bd = 1e9f;
                foreach (var e in FindObjectsByType<EnemyAI>(FindObjectsSortMode.None))
                {
                    if (e.GetComponent<DroneDamageStages>() == null) continue;
                    float d = Vector3.Distance(e.transform.position, p.transform.position);
                    if (d < bd) { bd = d; best = e; }
                }
                if (best != null)
                {
                    best.enabled = false; best.GetComponent<EnemyController>().enabled = false;
                    best.GetComponent<Rigidbody>().linearVelocity = Vector3.zero;
                    p.GetComponent<PlayerStats>().shield = 9999f; p.GetComponent<PlayerStats>().health = 9999f;
                    var camGo = new GameObject("ShotCam");
                    var cam = camGo.AddComponent<Camera>();
                    cam.depth = 100; cam.fieldOfView = 40f; cam.nearClipPlane = 0.05f; cam.farClipPlane = 2000f;
                    Transform bt = best.transform;
                    camGo.transform.position = bt.position + bt.right * 3.4f + bt.forward * 0.6f + bt.up * 0.4f;
                    camGo.transform.rotation = Quaternion.LookRotation(bt.position - camGo.transform.position, bt.up);
                    foreach (var other in FindObjectsByType<Camera>(FindObjectsSortMode.None)) if (other != cam) other.enabled = false;
                    yield return new WaitForSecondsRealtime(0.5f);
                    var bub = best.transform.Find("ShieldBubble");
                    Debug.Log($"[ShieldDebug] rootScale={best.transform.lossyScale} bubble={(bub != null ? bub.lossyScale.ToString() : "none")} bounds={(bub != null ? bub.GetComponent<Renderer>().bounds.size.ToString() : "-")} meshSize={(bub != null ? bub.GetComponent<MeshFilter>().sharedMesh.bounds.size.ToString() : "-")} cam={Vector3.Distance(camGo.transform.position, best.transform.position):F2}");
                    var st = best.GetComponent<EnemyStats>();
                    if (act != "shield0")
                    {
                        st.RegisterHit(bt.position + bt.right * 0.9f + bt.up * 0.35f + bt.forward * 0.2f);
                        st.TakeDamage(4f, null);
                        if (act == "shield2")     // a burst of shots on the same side
                        {
                            yield return new WaitForSecondsRealtime(0.12f);
                            st.RegisterHit(bt.position + bt.right * 0.7f - bt.up * 0.3f + bt.forward * 0.6f); st.TakeDamage(4f, null);
                            yield return new WaitForSecondsRealtime(0.1f);
                            st.RegisterHit(bt.position + bt.right * 0.8f + bt.up * 0.6f - bt.forward * 0.4f); st.TakeDamage(4f, null);
                        }
                    }
                }
                break;
            }
            case "ads":
            case "adsoff":
            {
                var inv = p.GetComponent<WeaponInventory>();
                WeaponPickup best = null; float bd = 1e9f;
                foreach (var wp in FindObjectsByType<WeaponPickup>(FindObjectsSortMode.None))
                {
                    float d = Vector3.Distance(wp.transform.position, p.transform.position);
                    if (d < bd) { bd = d; best = wp; }
                }
                // nearest pickup that is NOT a scoped weapon
                best = null; bd = 1e9f;
                foreach (var wp in FindObjectsByType<WeaponPickup>(FindObjectsSortMode.None))
                {
                    var wb = wp.weaponPrefab != null ? wp.weaponPrefab.GetComponent<WeaponBase>() : null;
                    if (wb is GravitySniper) continue;
                    float d = Vector3.Distance(wp.transform.position, p.transform.position);
                    if (d < bd) { bd = d; best = wp; }
                }
                if (best != null) inv.TryPickup(best);
                WeaponAim.DebugForceAim = act == "ads";
                break;
            }
            case "dronefall":
            {
                EnemyAI best = null; float bd = 1e9f;
                foreach (var e in FindObjectsByType<EnemyAI>(FindObjectsSortMode.None))
                {
                    if (e.GetComponent<DroneDamageStages>() == null) continue;
                    float d = Vector3.Distance(e.transform.position, p.transform.position);
                    if (d < bd) { bd = d; best = e; }
                }
                if (best != null)
                {
                    best.enabled = false; best.GetComponent<EnemyController>().enabled = false;
                    var rb = best.GetComponent<Rigidbody>(); rb.linearVelocity = Vector3.zero;
                    p.GetComponent<PlayerStats>().shield = 9999f; p.GetComponent<PlayerStats>().health = 9999f;
                    Transform bt = best.transform;
                    // lift it a few metres so the fall is visible, then film from the side
                    rb.position = rb.position + bt.up * 3f; bt.position = rb.position;
                    var camGo = new GameObject("ShotCam");
                    var cam = camGo.AddComponent<Camera>();
                    cam.depth = 100; cam.fieldOfView = 55f; cam.nearClipPlane = 0.05f; cam.farClipPlane = 2000f;
                    Vector3 side = bt.right;
                    camGo.transform.position = bt.position + side * 3.6f - bt.up * 0.5f;
                    camGo.transform.rotation = Quaternion.LookRotation((bt.position - bt.up * 1.5f) - camGo.transform.position, bt.up);
                    foreach (var other in FindObjectsByType<Camera>(FindObjectsSortMode.None)) if (other != cam) other.enabled = false;
                    yield return new WaitForSecondsRealtime(0.3f);
                    best.GetComponent<EnemyStats>().TakeDamage(1e9f, null);
                    // follow the falling wreck for `after` seconds from a fixed side offset
                    Vector3 off = side * 3.6f; Vector3 lastPos = bt.position; Vector3 upv = bt.up;
                    float t0 = Time.unscaledTime;
                    while (Time.unscaledTime - t0 < after)
                    {
                        if (bt != null) lastPos = bt.position;
                        camGo.transform.position = lastPos + off;
                        camGo.transform.rotation = Quaternion.LookRotation(lastPos - camGo.transform.position, upv);
                        yield return null;
                    }
                    Debug.Log($"[DroneFall] cam enabled={cam.enabled} pos={camGo.transform.position} target={lastPos} droneAlive={(bt != null)} main.enabled={(Camera.main != null && Camera.main.enabled)}");
                    after = 0f;
                }
                break;
            }
            case "pause":
                MainMenu.DebugPause();
                break;
            case "scope":
            {
                var inv = p.GetComponent<WeaponInventory>();
                WeaponPickup best = null; float bd = 1e9f;
                foreach (var wp in FindObjectsByType<WeaponPickup>(FindObjectsSortMode.None))
                {
                    var wb = wp.weaponPrefab != null ? wp.weaponPrefab.GetComponent<WeaponBase>() : null;
                    if (!(wb is GravitySniper)) continue;
                    float d = Vector3.Distance(wp.transform.position, p.transform.position);
                    if (d < bd) { bd = d; best = wp; }
                }
                if (best != null) inv.TryPickup(best);
                WeaponAim.DebugForceAim = true;
                break;
            }
            case "robot":
            case "robotheal":
            {
                yield return new WaitForSecondsRealtime(1.0f);       // let the installer spawn it
                var robot = SupportRobot.Instance;
                if (robot == null) { Debug.Log("[RobotShot] no robot"); break; }
                if (act == "robotheal") p.GetComponent<PlayerStats>().health = 20f;
                var camGo = new GameObject("ShotCam");
                var cam = camGo.AddComponent<Camera>();
                cam.depth = 100; cam.fieldOfView = 50f; cam.nearClipPlane = 0.05f; cam.farClipPlane = 2000f;
                foreach (var other in FindObjectsByType<Camera>(FindObjectsSortMode.None)) if (other != cam) other.enabled = false;
                float t0 = Time.unscaledTime;
                while (Time.unscaledTime - t0 < after)
                {
                    Transform rt = robot.transform;
                    camGo.transform.position = rt.position + rt.forward * 1.6f + rt.right * 1.3f + rt.up * 0.5f;
                    camGo.transform.rotation = Quaternion.LookRotation(rt.position - camGo.transform.position, rt.up);
                    yield return null;
                }
                Debug.Log($"[RobotShot] robot hp={robot.Health} alive={robot.IsAlive} playerHp={p.GetComponent<PlayerStats>().health} cooldown={robot.HealCooldownLeft:F1}");
                after = 0f;
                break;
            }
            case "fp":
            case "fpads":
            case "fprun":
            case "fpkatana":
            {
                var inv = p.GetComponent<WeaponInventory>();
                WeaponPickup best = null; float bd = 1e9f;
                foreach (var wp in FindObjectsByType<WeaponPickup>(FindObjectsSortMode.None))
                {
                    var wb = wp.weaponPrefab != null ? wp.weaponPrefab.GetComponent<WeaponBase>() : null;
                    if (wb is GravitySniper) continue;
                    float d = Vector3.Distance(wp.transform.position, p.transform.position);
                    if (d < bd) { bd = d; best = wp; }
                }
                if ((act == "fp" || act == "fpads" || act == "fprun") && best != null) inv.TryPickup(best);
                p.GetComponent<WeaponAim>().DebugSetFirstPerson(true);
                if (act == "fpads") WeaponAim.DebugForceAim = true;
                if (act == "fprun") { p.SimulateMove(Vector2.up); p.SimulateSprint(true); }
                if (act == "fpkatana") p.GetComponent<MeleeCombat>()?.DebugDraw();
                break;
            }
            case "death": p.GetComponent<PlayerStats>().TakeDamage(9999f); break;
        }
        yield return new WaitForSecondsRealtime(after);
    }
}

}
#endif
