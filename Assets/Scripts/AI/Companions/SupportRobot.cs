using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace OrbitRush
{

/// <summary>
/// "Robot de apoyo" — the player's companion (Resources/Models/OrbitRush_RobotApoyo.fbx).
///   • follows the player around the planet (and rejoins them after a planet hop),
///   • shoots nearby enemies with small energy bolts,
///   • heals the player when their health runs low (or on demand with H / pad Y): it walks up and beams repairs for a few seconds, then recharges,
///   • is fragile (60 health): enemies shoot at it too. When destroyed it is rebuilt after a while.
/// Built entirely in code; installed automatically once the match starts (see <see cref="SupportRobotInstaller"/>).
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class SupportRobot : MonoBehaviour, IDamageable
{
    public static SupportRobot Instance { get; private set; }

    [Header("Health")]
    public float maxHealth = 60f;
    public float rebuildDelay = 35f;

    [Header("Following")]
    public float moveSpeed = 7.5f;
    public float followDistance = 3.4f;
    public float maxFollowDistance = 9f;
    public float teleportDistance = 48f;

    [Header("Shooting")]
    public float shootRange = 24f;
    public float fireInterval = 0.55f;
    public float damage = 9f;
    public float projectileSpeed = 60f;

    [Header("Healing")]
    [Tooltip("Heal automatically when the player's health falls under this fraction.")]
    [Range(0.1f, 0.9f)] public float autoHealBelow = 0.45f;
    public float healPerSecond = 16f;
    public float healDuration = 3f;
    public float healCooldown = 25f;
    public float healRange = 20f;

    public float Health { get; private set; }
    public bool IsAlive => Health > 0f && _active;
    public float HealthPercent => maxHealth > 0f ? Mathf.Clamp01(Health / maxHealth) : 0f;
    public Faction Faction => Faction.Companion;
    public float HealCooldownLeft => Mathf.Max(0f, _healReadyAt - Time.time);

    // Parts
    private Transform _visual, _legL, _legR, _eye;
    private Rigidbody _rb;
    private CapsuleCollider _capsule;
    private PlayerStats _player;
    private PlayerController _playerController;
    private readonly List<Collider> _playerColliders = new List<Collider>();
    private PlanetGravity _planet;
    private Vector3 _up = Vector3.up;

    // State
    private bool _active = true;
    private EnemyStats _target;
    private float _scanTimer, _fireTimer, _phase, _walkAmount, _hitFlash;
    private float _healReadyAt, _healEndAt;
    private bool _healing;
    private Vector3 _faceDir = Vector3.forward;
    private GameObject _projectileTemplate;
    private LineRenderer _beam;
    private ParticleSystem _sparkles;
    private Material[] _lightMats = new Material[0];
    private Transform _barRoot, _barFill, _coolFill;
    private Renderer _barFillR, _coolFillR;
    private MaterialPropertyBlock _block;

    static readonly Color Green = new Color(0.25f, 1f, 0.55f);
    static readonly Color Cyan = new Color(0f, 0.85f, 1f);

    // ══════════════════════════════════════════════════════════════════════
    // Setup
    // ══════════════════════════════════════════════════════════════════════

    public static SupportRobot Spawn(Vector3 position, Quaternion rotation)
    {
        var prefab = Resources.Load<GameObject>(ResourcePaths.RobotModel);
        if (prefab == null) { Debug.LogWarning("[SupportRobot] model not found in Resources/Models."); return null; }

        var go = new GameObject("SupportRobot");
        go.transform.SetPositionAndRotation(position, rotation);
        go.transform.localScale = Vector3.one * CharacterScale.Player;
        var robot = go.AddComponent<SupportRobot>();
        robot.BuildVisual(prefab);
        return robot;
    }

    void Awake()
    {
        Instance = this;
        _rb = GetComponent<Rigidbody>();
        _rb.useGravity = false;
        _rb.angularDamping = 20f;
        _rb.interpolation = RigidbodyInterpolation.Interpolate;
        _rb.constraints = RigidbodyConstraints.None;
        _rb.mass = 2f;

        _capsule = gameObject.AddComponent<CapsuleCollider>();
        _capsule.radius = 0.3f; _capsule.height = 0.95f; _capsule.center = new Vector3(0f, -0.13f, 0f);

        Health = maxHealth;
        _block = new MaterialPropertyBlock();
    }

    void OnDestroy() { if (Instance == this) Instance = null; }

    private void BuildVisual(GameObject prefab)
    {
        _visual = Instantiate(prefab, transform).transform;
        _visual.name = "Visual";
        _visual.localPosition = Vector3.zero; _visual.localRotation = Quaternion.identity;
        foreach (var c in _visual.GetComponentsInChildren<Collider>(true)) Destroy(c);

        foreach (Transform t in _visual.GetComponentsInChildren<Transform>(true))
        {
            if (t.name == "Robot_LegL") _legL = t;
            else if (t.name == "Robot_LegR") _legR = t;
        }
        _eye = new GameObject("Eye").transform;
        _eye.SetParent(transform, false);
        _eye.localPosition = new Vector3(0f, 0.05f, 0.34f);      // the lens on the front of the body

        FixMaterials();
        BuildBeam();
        BuildHealthBar();
        BuildProjectile();
    }

    // The FBX arrives with plain materials: give them the atlas texture, a glassy lens and green running lights.
    private void FixMaterials()
    {
        var color = Resources.Load<Texture2D>(ResourcePaths.RobotColorTexture);
        var metal = Resources.Load<Texture2D>(ResourcePaths.RobotMetalTexture);
        var lights = new List<Material>();
        foreach (var r in _visual.GetComponentsInChildren<Renderer>(true))
        {
            var mats = r.materials;
            for (int i = 0; i < mats.Length; i++)
            {
                var m = mats[i];
                string n = m.name;
                if (n.StartsWith("_GLASS_"))
                {
                    m.SetColor("_BaseColor", new Color(0.2f, 0.6f, 0.8f, 0.45f));
                    m.SetFloat("_Surface", 1f); m.SetFloat("_Blend", 0f);
                    m.SetOverrideTag("RenderType", "Transparent");
                    m.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                    m.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                    m.SetInt("_ZWrite", 0); m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                    m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
                    m.EnableKeyword("_EMISSION"); m.SetColor("_EmissionColor", Cyan * 0.8f);
                    m.SetFloat("_Smoothness", 0.95f);
                    continue;
                }
                if (color != null) { m.SetTexture("_BaseMap", color); m.SetColor("_BaseColor", Color.white); }
                if (metal != null) { m.SetTexture("_MetallicGlossMap", metal); m.EnableKeyword("_METALLICSPECGLOSSMAP"); m.SetFloat("_Metallic", 1f); }
                m.SetFloat("_Smoothness", 0.5f);
                if (n.StartsWith("_MODEL_L2_light")) { m.EnableKeyword("_EMISSION"); m.SetColor("_EmissionColor", Green * 3f); lights.Add(m); }
            }
            r.materials = mats;
        }
        _lightMats = lights.ToArray();
    }

    private void BuildBeam()
    {
        var go = new GameObject("HealBeam");
        go.transform.SetParent(transform, false);
        _beam = go.AddComponent<LineRenderer>();
        _beam.positionCount = 2; _beam.widthMultiplier = 0.09f; _beam.useWorldSpace = true;
        _beam.numCapVertices = 4;
        var shader = Shader.Find("Sprites/Default");
        if (shader != null) _beam.material = new Material(shader);
        _beam.startColor = new Color(0.25f, 1f, 0.6f, 0.9f); _beam.endColor = new Color(0.1f, 0.9f, 1f, 0.5f);
        _beam.enabled = false;

        var sp = new GameObject("HealSparkles");
        sp.transform.SetParent(transform, false);
        _sparkles = sp.AddComponent<ParticleSystem>();
        _sparkles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = _sparkles.main;
        main.loop = true; main.playOnAwake = false; main.startLifetime = 0.9f; main.startSpeed = 0.8f; main.startSize = 0.09f;
        main.startColor = new Color(0.4f, 1f, 0.7f, 0.9f); main.simulationSpace = ParticleSystemSimulationSpace.World; main.gravityModifier = -0.15f;
        var em = _sparkles.emission; em.rateOverTime = 0f;
        var sh = _sparkles.shape; sh.shapeType = ParticleSystemShapeType.Sphere; sh.radius = 0.5f;
        var r = sp.GetComponent<ParticleSystemRenderer>();
        var shader2 = Shader.Find("Sprites/Default");
        if (shader2 != null) r.material = new Material(shader2);
    }

    private void BuildProjectile()
    {
        _projectileTemplate = ProjectileFactory.Create("RobotBolt", new Color(0.3f, 1f, 0.7f), 0.13f, damage, projectileSpeed, 2.5f, false, 1f, 0f);
        _projectileTemplate.transform.SetParent(transform, false);
    }

    // Small floating bars: health (green→red) and the heal recharge (cyan)
    private void BuildHealthBar()
    {
        _barRoot = new GameObject("Bars").transform;
        _barRoot.SetParent(transform, false);
        _barRoot.localPosition = new Vector3(0f, 0.62f, 0f);
        var shader = Shader.Find("Universal Render Pipeline/Unlit");
        Transform Quad(string name, Color c, float w, float h, float y, float z, out Renderer rend)
        {
            var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
            q.name = name; Destroy(q.GetComponent<Collider>());
            q.transform.SetParent(_barRoot, false);
            q.transform.localScale = new Vector3(w, h, 1f);
            q.transform.localPosition = new Vector3(0f, y, z);
            rend = q.GetComponent<Renderer>();
            var mat = new Material(shader); mat.SetColor("_BaseColor", c);
            rend.sharedMaterial = mat;
            rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return q.transform;
        }
        Quad("Back", new Color(0.04f, 0.04f, 0.08f), 0.72f, 0.10f, 0f, 0f, out _);
        _barFill = Quad("HealthFill", Green, 0.68f, 0.07f, 0f, -0.001f, out _barFillR);
        Quad("CoolBack", new Color(0.04f, 0.04f, 0.08f), 0.72f, 0.05f, -0.09f, 0f, out _);
        _coolFill = Quad("CoolFill", Cyan, 0.68f, 0.03f, -0.09f, -0.001f, out _coolFillR);
    }

    // ══════════════════════════════════════════════════════════════════════
    // Update
    // ══════════════════════════════════════════════════════════════════════

    void Update()
    {
        if (!_active) return;
        FindPlayer();
        if (_player == null) return;

        float dt = Time.deltaTime;
        _fireTimer -= dt;
        _scanTimer -= dt;

        UpdateHealing(dt);
        if (_scanTimer <= 0f) { _scanTimer = 0.3f; AcquireTarget(); }
        TryShoot();

        AnimateLegs(dt);
        UpdateLights();
        UpdateBars();
        _hitFlash = Mathf.MoveTowards(_hitFlash, 0f, dt * 4f);
    }

    void LateUpdate()
    {
        if (_barRoot != null && Camera.main != null)
            _barRoot.rotation = Quaternion.LookRotation(_barRoot.position - Camera.main.transform.position, Camera.main.transform.up);
    }

    void FixedUpdate()
    {
        if (!_active || _rb == null) return;

        // Planet gravity and alignment (same approach as the enemies)
        PlanetBody.Track(transform.position, ref _planet, ref _up);
        Vector3 g = GravitySystem.Instance != null ? GravitySystem.Instance.GetGravityVector(transform.position) : Vector3.down * 9.8f;
        _rb.AddForce(g, ForceMode.Acceleration);

        Vector3 move = Vector3.zero;
        if (_player != null) move = DesiredMove();
        Vector3 planarVel = Vector3.ProjectOnPlane(_rb.linearVelocity, _up);
        _rb.AddForce((move * moveSpeed - planarVel) * 8f, ForceMode.Acceleration);
        _walkAmount = Mathf.MoveTowards(_walkAmount, planarVel.magnitude / moveSpeed, Time.fixedDeltaTime * 6f);

        // Facing: the enemy being shot, else where it is walking, else the player
        Vector3 face = _faceDir;
        if (_target != null) face = _target.transform.position - transform.position;
        else if (move.sqrMagnitude > 0.05f) face = move;
        else if (_player != null) face = _player.transform.position - transform.position;
        face = Vector3.ProjectOnPlane(face, _up);
        if (face.sqrMagnitude > 0.01f) _faceDir = face.normalized;
        Quaternion look = Quaternion.LookRotation(_faceDir, _up);
        _rb.MoveRotation(Quaternion.Slerp(_rb.rotation, look, 7f * Time.fixedDeltaTime));
    }

    private Vector3 DesiredMove()
    {
        Vector3 toPlayer = Vector3.ProjectOnPlane(_player.transform.position - transform.position, _up);
        float dist = toPlayer.magnitude;

        // Lost the player (a planet hop, or just far away): jump to them
        if (dist > teleportDistance || (_playerController != null && _playerController.CurrentPlanet != null && _planet != null
                                       && _playerController.CurrentPlanet != _planet && _playerController.IsGrounded && dist > 14f))
        {
            TeleportNearPlayer();
            return Vector3.zero;
        }

        float hold = _healing ? 2.0f : followDistance;
        if (dist > hold + (_healing ? 0.2f : 1.5f)) return toPlayer / Mathf.Max(0.01f, dist);        // catch up
        if (_target != null && dist < maxFollowDistance) return Vector3.zero;                         // stay and shoot
        if (dist < hold * 0.6f) return -toPlayer / Mathf.Max(0.01f, dist) * 0.4f;                     // don't crowd the player
        return Vector3.zero;
    }

    private void FindPlayer()
    {
        if (_player != null) return;
        var go = GameObject.FindWithTag("Player");
        if (go == null) return;
        _player = go.GetComponent<PlayerStats>();
        _playerController = go.GetComponent<PlayerController>();
        _playerColliders.Clear();
        _playerColliders.AddRange(go.GetComponentsInChildren<Collider>(true));
        gameObject.layer = go.layer;                       // enemies' detection mask sees the robot like a player
        foreach (var c in _playerColliders) Physics.IgnoreCollision(_capsule, c);   // never block / push the player
    }

    private void TeleportNearPlayer()
    {
        Vector3 up = _playerController != null ? _player.transform.up : Vector3.up;
        Vector3 pos = _player.transform.position + _player.transform.right * 1.8f - _player.transform.forward * 0.8f + up * 0.6f;
        _rb.position = pos; transform.position = pos;
        _rb.linearVelocity = Vector3.zero;
        DeathBurstFx.Play(pos, Cyan, up);
    }

    // ══════════════════════════════════════════════════════════════════════
    // Healing
    // ══════════════════════════════════════════════════════════════════════

    private void UpdateHealing(float dt)
    {
        bool ready = Time.time >= _healReadyAt;
        bool playerAlive = _player.IsAlive;

        if (!_healing)
        {
            bool needs = playerAlive && _player.HealthPercent < autoHealBelow;
            var kb = Keyboard.current; var pad = Gamepad.current;
            bool asked = (kb != null && kb.hKey.wasPressedThisFrame) || (pad != null && pad.buttonNorth.wasPressedThisFrame);
            bool hurt = playerAlive && _player.health < _player.maxHealth - 1f;
            bool near = Vector3.Distance(transform.position, _player.transform.position) < healRange;

            if (ready && near && ((needs) || (asked && hurt)))
            {
                _healing = true;
                _healEndAt = Time.time + healDuration;
                if (HUD.Instance != null) HUD.Instance.ShowEvent("ROBOT DE APOYO: REPARANDO", 2.5f);
            }
            else if (asked && hurt && !ready && HUD.Instance != null)
                HUD.Instance.ShowEvent($"ROBOT RECARGANDO ({Mathf.CeilToInt(HealCooldownLeft)} s)", 1.6f);
        }

        if (_healing)
        {
            float d = Vector3.Distance(transform.position, _player.transform.position);
            if (!playerAlive || Time.time >= _healEndAt || d > healRange * 1.4f) EndHeal();
            else
            {
                _player.Heal(healPerSecond * dt);
                _player.AddShield(healPerSecond * 0.5f * dt);
                DrawBeam();
            }
        }
        else if (_beam.enabled) { _beam.enabled = false; SetSparkles(false); }
    }

    private void EndHeal()
    {
        _healing = false;
        _healReadyAt = Time.time + healCooldown;
        _beam.enabled = false;
        SetSparkles(false);
    }

    private void DrawBeam()
    {
        _beam.enabled = true;
        Vector3 from = _eye.position;
        Vector3 to = _player.transform.position + _player.transform.up * 0.2f;
        Vector3 mid = (from + to) * 0.5f + _up * (0.1f * Mathf.Sin(Time.time * 12f));
        _beam.positionCount = 3;
        _beam.SetPosition(0, from); _beam.SetPosition(1, mid); _beam.SetPosition(2, to);
        _beam.widthMultiplier = 0.07f + 0.03f * Mathf.Sin(Time.time * 18f);
        _sparkles.transform.position = to;
        SetSparkles(true);
    }

    private void SetSparkles(bool on)
    {
        var em = _sparkles.emission; em.rateOverTime = on ? 28f : 0f;
        if (on && !_sparkles.isPlaying) _sparkles.Play();
    }

    // ══════════════════════════════════════════════════════════════════════
    // Shooting
    // ══════════════════════════════════════════════════════════════════════

    private void AcquireTarget()
    {
        if (_healing) { _target = null; return; }
        EnemyStats best = null; float bd = shootRange;
        foreach (var e in EnemyStats.All)
        {
            if (e == null || !e.IsAlive) continue;
            float d = Vector3.Distance(transform.position, e.transform.position);
            if (d >= bd) continue;
            if (!ClearShot(e)) continue;
            bd = d; best = e;
        }
        _target = best;
    }

    private bool ClearShot(EnemyStats e)
    {
        Vector3 from = _eye.position, to = e.transform.position;
        if (Physics.Raycast(from, (to - from).normalized, out var hit, Vector3.Distance(from, to), ~0, QueryTriggerInteraction.Ignore))
            return hit.collider.GetComponentInParent<EnemyStats>() == e || hit.collider.transform.IsChildOf(transform);
        return true;
    }

    private void TryShoot()
    {
        if (_target == null || !_target.IsAlive || _fireTimer > 0f || _healing || _projectileTemplate == null) return;
        Vector3 dir = (_target.transform.position - _eye.position).normalized;
        if (Vector3.Angle(Vector3.ProjectOnPlane(dir, _up), _faceDir) > 25f) return;      // wait until it has turned toward the enemy

        _fireTimer = fireInterval;
        dir = Quaternion.AngleAxis(Random.Range(0f, 360f), dir) * Quaternion.AngleAxis(Random.Range(0f, 1.8f), Vector3.Cross(dir, Random.onUnitSphere).normalized) * dir;

        var proj = ProjectileFactory.Spawn(_projectileTemplate, _eye.position + dir * 0.25f, dir, gameObject, _planet,
            p => { p.damage = damage; p.speed = projectileSpeed; p.affectedByGravity = false; });
        if (proj != null)
        {
            var col = proj.GetComponent<Collider>();
            if (col != null) foreach (var c in _playerColliders) if (c != null) Physics.IgnoreCollision(col, c);
        }
        _hitFlash = Mathf.Max(_hitFlash, 0f);
    }

    // ══════════════════════════════════════════════════════════════════════
    // Visuals
    // ══════════════════════════════════════════════════════════════════════

    private void AnimateLegs(float dt)
    {
        _phase += dt * Mathf.Lerp(2.5f, 9f, _walkAmount) * (_walkAmount > 0.05f ? 1f : 0f);
        float swing = Mathf.Sin(_phase) * 32f * Mathf.Clamp01(_walkAmount * 1.4f);
        if (_legL != null) _legL.localRotation = Quaternion.Euler(swing, 0f, 0f);
        if (_legR != null) _legR.localRotation = Quaternion.Euler(-swing, 0f, 0f);

        // Body bob and a little sway; a quick shudder when hit
        if (_visual != null)
        {
            float bob = Mathf.Abs(Mathf.Sin(_phase)) * 0.035f * _walkAmount + Mathf.Sin(Time.time * 1.7f) * 0.006f;
            Vector3 shudder = _hitFlash > 0f ? Random.insideUnitSphere * 0.02f * _hitFlash : Vector3.zero;
            _visual.localPosition = new Vector3(0f, bob, 0f) + shudder;
            _visual.localRotation = Quaternion.Euler(Mathf.Sin(_phase) * 2.5f * _walkAmount, 0f, Mathf.Sin(_phase * 0.5f) * 2f * _walkAmount);
        }
    }

    private void UpdateLights()
    {
        Color c = _healing ? Cyan * (2.5f + Mathf.Sin(Time.time * 16f))
                : _hitFlash > 0f ? new Color(1f, 0.15f, 0.1f) * 3f
                : Time.time >= _healReadyAt ? Green * 2.6f : new Color(1f, 0.55f, 0.1f) * 1.6f;
        foreach (var m in _lightMats) if (m != null) m.SetColor("_EmissionColor", c);
    }

    private void UpdateBars()
    {
        float hp = Mathf.Clamp01(Health / maxHealth);
        _barFill.localScale = new Vector3(Mathf.Max(0.001f, 0.68f * hp), 0.07f, 1f);
        _barFill.localPosition = new Vector3(-0.34f * (1f - hp), 0f, -0.001f);
        SetColor(_barFillR, Color.Lerp(new Color(1f, 0.2f, 0.15f), Green, hp));

        float cool = _healing ? 1f - Mathf.Clamp01((Time.time - (_healEndAt - healDuration)) / healDuration)
                  : 1f - Mathf.Clamp01(HealCooldownLeft / healCooldown);
        _coolFill.localScale = new Vector3(Mathf.Max(0.001f, 0.68f * cool), 0.03f, 1f);
        _coolFill.localPosition = new Vector3(-0.34f * (1f - cool), -0.09f, -0.001f);
        SetColor(_coolFillR, _healing ? Green : (cool >= 1f ? Cyan : new Color(0.4f, 0.5f, 0.6f)));
    }

    private void SetColor(Renderer r, Color c)
    {
        r.GetPropertyBlock(_block);
        _block.SetColor("_BaseColor", c);
        r.SetPropertyBlock(_block);
    }

    // ══════════════════════════════════════════════════════════════════════
    // Damage / death
    // ══════════════════════════════════════════════════════════════════════

    public void TakeDamage(float amount) => TakeDamage(amount, null);

    public void TakeDamage(float amount, PlayerStats attacker)
    {
        if (!IsAlive) return;
        Health = Mathf.Max(0f, Health - amount);
        _hitFlash = 1f;
        if (Health <= 0f) StartCoroutine(Destroyed());
    }

    private IEnumerator Destroyed()
    {
        _active = false;
        if (_healing) EndHeal();
        DeathBurstFx.Play(transform.position, Green, _up);
        DeathBurstFx.Play(transform.position + _up * 0.3f, Cyan, _up);
        if (HUD.Instance != null) HUD.Instance.ShowEvent("ROBOT DE APOYO DESTRUIDO", 3f);
        SetVisible(false);

        yield return new WaitForSeconds(rebuildDelay);

        FindPlayer();
        if (_player != null) TeleportNearPlayer();
        Health = maxHealth;
        _active = true;
        _target = null;
        _healReadyAt = Time.time + 5f;
        SetVisible(true);
        if (HUD.Instance != null) HUD.Instance.ShowEvent("ROBOT DE APOYO REPARADO", 2.5f);
    }

    private void SetVisible(bool on)
    {
        foreach (var r in GetComponentsInChildren<Renderer>(true)) r.enabled = on;
        _capsule.enabled = on;
        _rb.isKinematic = !on;
        if (!on) { _beam.enabled = false; SetSparkles(false); }
    }
}

/// <summary>Spawns the support robot next to the player once the match has started.</summary>
public static class SupportRobotInstaller
{
    public static bool Enabled = true;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Register()
    {
        if (!Enabled) return;
        var runner = new GameObject("SupportRobotInstaller");
        Object.DontDestroyOnLoad(runner);
        runner.AddComponent<Runner>();
    }

    private class Runner : MonoBehaviour
    {
        IEnumerator Start()
        {
            while (true)
            {
                if (SupportRobot.Instance == null && GameManager.Instance != null && !MainMenu.BlocksStart)
                {
                    var player = GameObject.FindWithTag("Player");
                    var stats = player != null ? player.GetComponent<PlayerStats>() : null;
                    if (stats != null && stats.IsAlive && player.GetComponent<PlayerController>() != null)
                    {
                        yield return new WaitForSeconds(1.2f);       // let the player settle on the planet first
                        if (SupportRobot.Instance == null && player != null)
                            SupportRobot.Spawn(player.transform.position + player.transform.right * 1.8f - player.transform.forward * 0.8f + player.transform.up * 0.6f,
                                               player.transform.rotation);
                    }
                }
                yield return new WaitForSeconds(1f);
            }
        }
    }
}
}
