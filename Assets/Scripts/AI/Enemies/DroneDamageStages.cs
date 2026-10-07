using System.Collections.Generic;
using UnityEngine;

namespace OrbitRush
{

/// <summary>
/// Progressive damage for the flying enemy drone (Resources/Models/OrbitRush_DroneDL3). As its health drops it
/// goes through four stages, each worse for flying and shooting:
///
///   0 Intact    shield up / above 66 % — steady, green lights
///   1 Damaged   under 66 %, no shield — sparks, lights flicker orange, slight wobble
///   2 Critical  under 35 % — grey smoke, a rotor sputters, it lurches and drifts, aim gets sloppy
///   3 Failing   under 15 % — thick black smoke and fire, sinking, tumbling, red strobing lights, barely able to shoot
///
/// When it finally dies it doesn't vanish: it tumbles out of the sky on gravity and explodes on impact.
/// Pure code (particles, materials, transforms) — no assets besides the model.
/// </summary>
public class DroneDamageStages : MonoBehaviour, IDeathBehavior
{
    public enum Stage { Intact, Damaged, Critical, Failing }

    public Stage Current { get; private set; } = Stage.Intact;

    private static readonly float[] Instability = { 0f, 0.35f, 0.9f, 1.8f };
    private static readonly float[] SpeedScale  = { 1f, 0.92f, 0.7f, 0.45f };
    private static readonly float[] HoverScale  = { 1f, 1f, 0.92f, 0.5f };
    private static readonly float[] Inaccuracy  = { 0f, 1.5f, 5f, 11f };
    private static readonly float[] FireScale   = { 1f, 1.1f, 1.5f, 2.2f };
    private static readonly float[] WobbleDeg   = { 1.2f, 4f, 11f, 24f };
    private static readonly float[] SmokeRate   = { 0f, 0f, 16f, 38f };

    private EnemyStats _stats;
    private EnemyController _controller;
    private EnemyAI _ai;
    private Rigidbody _body;
    private Transform _visual, _rotL, _rotR;
    private float _baseHover, _baseSpeed, _baseFireInterval;

    private ParticleSystem _smoke, _fire, _sparks;
    private ParticleSystem.EmissionModule _smokeEm, _fireEm;
    private readonly List<Material> _lightMats = new List<Material>();
    private readonly List<Color> _lightBase = new List<Color>();

    private float _sparkTimer, _seed, _crashTimer;
    private bool _crashing, _exploded;
    private float _crashSide = 1f;       // +1 / -1: which wing it drops on
    private float _crashAge;
    private float _crashYaw;
    private static Material _softMaterial;

    void Start()
    {
        _stats = GetComponent<EnemyStats>();
        _controller = GetComponent<EnemyController>();
        _ai = GetComponent<EnemyAI>();
        _body = GetComponent<Rigidbody>();
        _seed = Random.value * 100f;

        _visual = transform.Find("Visual");
        if (_visual != null)
        {
            foreach (var t in _visual.GetComponentsInChildren<Transform>(true))
            {
                if (t.name == "Rotor_L") _rotL = t;
                else if (t.name == "Rotor_R") _rotR = t;
            }
            foreach (var r in _visual.GetComponentsInChildren<Renderer>(true))
                foreach (var m in r.materials)       // instances: each drone flickers on its own
                    if (m.name.StartsWith("emision") && m.HasProperty("_EmissionColor"))
                    {
                        _lightMats.Add(m);
                        _lightBase.Add(m.GetColor("_EmissionColor"));
                    }
        }

        if (_controller != null) { _baseHover = _controller.hoverHeight; _baseSpeed = _controller.moveSpeed; }
        if (_ai != null) _baseFireInterval = _ai.fireInterval;

        _smoke = MakeSystem("Smoke", new Color(0.25f, 0.25f, 0.27f, 0.6f), 0.45f, 1.6f, 0.4f, -0.08f, 1.0f, 2.6f);
        _fire = MakeSystem("Fire", new Color(1f, 0.55f, 0.12f, 0.9f), 0.22f, 0.5f, 0.6f, -0.05f, 1.0f, 0.2f);
        _sparks = MakeSystem("Sparks", new Color(1f, 0.9f, 0.5f, 1f), 0.06f, 0.4f, 3.2f, 0.6f, 1f, 0.4f);
        _smokeEm = _smoke.emission; _fireEm = _fire.emission;
    }

    // ══════════════════════════════════════════════════════════════════════

    void Update()
    {
        if (_stats == null) return;
        float dt = Time.deltaTime;

        if (!_crashing) UpdateStage();
        int s = (int)Current;

        // Flight handling (the controller reads these every physics tick)
        if (_controller != null && !_crashing)
        {
            _controller.instability = Mathf.MoveTowards(_controller.instability, Instability[s], 2f * dt);
            _controller.speedScale = Mathf.MoveTowards(_controller.speedScale, SpeedScale[s], 1.5f * dt);
            float hoverTarget = _baseHover * HoverScale[s];
            if (Current == Stage.Failing) hoverTarget *= 0.75f + 0.25f * Mathf.Sin(Time.time * 0.8f + _seed);   // slowly losing height
            _controller.hoverHeight = Mathf.MoveTowards(_controller.hoverHeight, hoverTarget, 1.2f * dt);
        }
        if (_ai != null)
        {
            _ai.inaccuracyDegrees = Mathf.MoveTowards(_ai.inaccuracyDegrees, Inaccuracy[s], 20f * dt);
            _ai.fireIntervalScale = Mathf.MoveTowards(_ai.fireIntervalScale, FireScale[s], 2f * dt);
        }

        Wobble(s, dt);
        Lights(s);
        Particles(s, dt);
    }

    private void UpdateStage()
    {
        float hp = _stats.HealthPercent;
        Stage target;
        if (hp <= 0.15f) target = Stage.Failing;
        else if (hp <= 0.35f) target = Stage.Critical;
        else if (hp <= 0.66f && _stats.Shield <= 0.5f) target = Stage.Damaged;
        else target = Stage.Intact;

        if (target > Current)      // stages only get worse
        {
            Current = target;
            _sparks.transform.localPosition = Vector3.zero;
            _sparks.Emit(18 + 8 * (int)target);              // a burst of sparks marks the new stage
        }
    }

    // ── Body shake and rotor behaviour ────────────────────────────────────

    private void Wobble(int s, float dt)
    {
        if (_visual == null) return;
        float t = Time.time + _seed;
        float amp = WobbleDeg[s];
        float roll = (Mathf.PerlinNoise(t * 0.9f, 0.3f) - 0.5f) * 2f * amp;
        float pitch = (Mathf.PerlinNoise(0.7f, t * 0.8f) - 0.5f) * 2f * amp * 0.7f;
        float yaw = Current == Stage.Failing ? (Mathf.PerlinNoise(t * 0.5f, 9f) - 0.5f) * 2f * 28f : 0f;
        if (_crashing) { CrashPose(dt); return; }
        _visual.localRotation = Quaternion.Slerp(_visual.localRotation, Quaternion.Euler(pitch, yaw, roll), 1f - Mathf.Exp(-10f * dt));

        // Rotors: a gentle hover sway when healthy; sputtering and flopping once damaged
        float swayL = Mathf.Sin(t * 1.7f) * 1.2f, swayR = Mathf.Sin(t * 1.7f + 1.3f) * 1.2f;
        float lA = swayL, rA = swayR;
        if (Current >= Stage.Critical)
        {
            float sputter = Mathf.Clamp01((Mathf.PerlinNoise(t * 2.2f, 4f) - 0.55f) * 4f);       // intermittent
            lA += sputter * 22f * (Current == Stage.Failing ? 1.4f : 1f);
            if (Current == Stage.Failing) rA -= Mathf.Clamp01((Mathf.PerlinNoise(t * 2.6f, 7f) - 0.5f) * 3f) * 28f;
        }
        else if (Current == Stage.Damaged) lA += Mathf.Sin(t * 9f) * 2f;
        if (_rotL != null) _rotL.localRotation = Quaternion.Slerp(_rotL.localRotation, Quaternion.Euler(0f, 0f, lA), 1f - Mathf.Exp(-14f * dt));
        if (_rotR != null) _rotR.localRotation = Quaternion.Slerp(_rotR.localRotation, Quaternion.Euler(0f, 0f, rA), 1f - Mathf.Exp(-14f * dt));
    }

    // ── Indicator lights ──────────────────────────────────────────────────

    private void Lights(int s)
    {
        for (int i = 0; i < _lightMats.Count; i++)
        {
            if (_lightMats[i] == null) continue;
            Color c = _lightBase[i];
            float k = 1f;
            switch (Current)
            {
                case Stage.Damaged:   // flickers between its own colour and amber
                    if (Mathf.PerlinNoise(Time.time * 7f + _seed, i) > 0.62f) c = new Color(1f, 0.55f, 0.05f) * c.maxColorComponent;
                    break;
                case Stage.Critical:  // amber/red blinking
                    c = new Color(1f, 0.25f, 0.02f) * Mathf.Max(0.5f, c.maxColorComponent);
                    k = Mathf.Sin(Time.time * 7f + i) > -0.2f ? 1f : 0.1f;
                    break;
                case Stage.Failing:   // frantic red strobe with dead moments
                    c = new Color(1f, 0.05f, 0.02f) * Mathf.Max(0.6f, c.maxColorComponent);
                    k = Mathf.PerlinNoise(Time.time * 14f + _seed, i * 3f) > 0.45f ? 1.3f : 0f;
                    break;
            }
            _lightMats[i].SetColor("_EmissionColor", c * k);
        }
    }

    // ── Smoke, fire, sparks ───────────────────────────────────────────────

    private void Particles(int s, float dt)
    {
        _smokeEm.rateOverTime = _crashing ? 60f : SmokeRate[s];
        _fireEm.rateOverTime = (Current == Stage.Failing || _crashing) ? 22f : 0f;
        var main = _smoke.main;
        main.startColor = Current == Stage.Failing || _crashing ? new Color(0.06f, 0.06f, 0.07f, 0.85f) : new Color(0.3f, 0.3f, 0.32f, 0.55f);

        if (s >= 1)
        {
            _sparkTimer -= dt;
            if (_sparkTimer <= 0f)
            {
                _sparkTimer = Random.Range(0.6f, 1.8f) / s;
                _sparks.transform.localPosition = Random.insideUnitSphere * 0.5f;
                _sparks.Emit(Random.Range(5, 10));
            }
        }
    }

    // ══════════════════════════════════════════════════════════════════════
    // Death: crash instead of vanishing
    // ══════════════════════════════════════════════════════════════════════

    /// <summary>Called by EnemyStats on death. Returns true if the drone takes over its own destruction.</summary>
    public bool TryPlayDeath() => PlayCrash();

    public bool PlayCrash()
    {
        if (_crashing || _body == null) return false;
        _crashing = true;
        Current = Stage.Failing;
        _crashTimer = 4f;
        _crashSide = Random.value < 0.5f ? -1f : 1f;
        _crashAge = 0f;

        if (_ai != null) _ai.enabled = false;
        if (_controller != null) _controller.enabled = false;
        // These components live on the drone's root: switch them off and hide their visuals (never deactivate the root!)
        foreach (var sh in GetComponentsInChildren<EnemyShield>(true)) { sh.enabled = false; HideChild("ShieldBubble"); }
        foreach (var hb in GetComponentsInChildren<EnemyHealthBar>(true)) { hb.enabled = false; HideChild("HealthBar"); }

        // The body stays upright (so it falls straight onto its landing spot); the model rolls onto its side.
        _body.isKinematic = false;
        _body.useGravity = false;
        _body.constraints = RigidbodyConstraints.FreezeRotation;
        _body.linearVelocity *= 0.4f;
        // A sideways slide toward the wing that drops, plus a little of the forward speed it had
        _body.AddForce(transform.right * (_crashSide * 3.5f), ForceMode.VelocityChange);
        _sparks.Emit(40);
        return true;
    }

    private void HideChild(string name)
    {
        var t = transform.Find(name);
        if (t != null) t.gameObject.SetActive(false);
    }

    void FixedUpdate()
    {
        if (!_crashing || _body == null) return;
        Vector3 g = GravitySystem.Instance != null ? GravitySystem.Instance.GetGravityVector(transform.position) : Vector3.down * 9.8f;
        _body.AddForce(g * 1.8f, ForceMode.Acceleration);
    }

    void LateUpdate()
    {
        if (!_crashing || _exploded) return;
        _crashTimer -= Time.deltaTime;
        if (_crashTimer <= 0f) Explode();
    }

    void OnCollisionEnter(Collision c)
    {
        if (_crashing && !_exploded && _crashAge > 0.2f) Explode();    // blows up the moment it hits the floor (or anything)
    }

    /// <summary>The model banks over onto one wing as it falls, nose dipping, rotors flopping, spinning slowly.</summary>
    private void CrashPose(float dt)
    {
        _crashAge += dt;
        float k = 1f - Mathf.Exp(-2.4f * _crashAge);                    // fast at first, settling
        float roll = _crashSide * Mathf.Lerp(0f, 105f, k);              // rolls past vertical: lying on its side
        float pitch = Mathf.Lerp(0f, 28f, 1f - Mathf.Exp(-1.6f * _crashAge));   // nose dips
        _crashYaw += _crashSide * (60f + 160f * k) * dt;               // slow spin about the vertical axis
        _visual.localRotation = Quaternion.Euler(pitch, _crashYaw, roll);

        float flop = Mathf.Sin(_crashAge * 14f) * 12f;
        if (_rotL != null) _rotL.localRotation = Quaternion.Euler(0f, 0f, _crashSide * 38f * k + flop);
        if (_rotR != null) _rotR.localRotation = Quaternion.Euler(0f, 0f, -_crashSide * 22f * k - flop);
    }

    private void Explode()
    {
        _exploded = true;
        Vector3 up = _controller != null ? _controller.PlanetUp : transform.up;
        DeathBurstFx.Play(transform.position, new Color(1f, 0.45f, 0.08f), up);
        DeathBurstFx.Play(transform.position + up * 0.3f, new Color(1f, 0.9f, 0.4f), up);
        _sparks.Emit(70);
        _fire.Emit(45);
        _smoke.Emit(30);
        FlashLight(transform.position + up * 0.5f);
        // Leave the smoke behind: detach the systems so they fade out where the wreck fell
        foreach (var ps in new[] { _smoke, _fire, _sparks })
        {
            if (ps == null) continue;
            var em = ps.emission; em.rateOverTime = 0f;
            ps.transform.SetParent(null, true);
            Destroy(ps.gameObject, 3f);
        }
        Destroy(gameObject);
    }

    /// <summary>A quick orange flash of light where it blew up.</summary>
    private static void FlashLight(Vector3 position)
    {
        var go = new GameObject("DroneExplosionLight");
        go.transform.position = position;
        var l = go.AddComponent<Light>();
        l.type = LightType.Point; l.color = new Color(1f, 0.6f, 0.2f); l.range = 14f; l.intensity = 8f;
        go.AddComponent<FadeLight>();
        Object.Destroy(go, 0.6f);
    }

    private class FadeLight : MonoBehaviour
    {
        private Light _l; private float _t;
        void Start() => _l = GetComponent<Light>();
        void Update() { _t += Time.deltaTime; if (_l != null) _l.intensity = Mathf.Lerp(8f, 0f, _t / 0.5f); }
    }

    // ── Particle helper ───────────────────────────────────────────────────

    private ParticleSystem MakeSystem(string name, Color color, float size, float life, float speed, float gravity, float sizeStart, float sizeEnd)
    {
        var go = new GameObject(name);
        go.transform.SetParent(transform, false);
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        var main = ps.main;
        main.loop = true; main.playOnAwake = false;
        main.startLifetime = life; main.startSpeed = speed; main.startSize = size; main.startColor = color;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.gravityModifier = gravity; main.maxParticles = 300;

        var em = ps.emission; em.rateOverTime = 0f;
        var shape = ps.shape; shape.shapeType = ParticleSystemShapeType.Sphere; shape.radius = 0.12f;

        var col = ps.colorOverLifetime; col.enabled = true;
        var g = new Gradient();
        g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                  new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.15f), new GradientAlphaKey(0f, 1f) });
        col.color = g;

        var sz = ps.sizeOverLifetime; sz.enabled = true;
        sz.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, sizeStart * 0.5f), new Keyframe(1f, Mathf.Max(0.05f, sizeEnd))));

        var r = go.GetComponent<ParticleSystemRenderer>();
        r.material = SoftMaterial();
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.receiveShadows = false;
        ps.Play();
        return ps;
    }

    private static Material SoftMaterial()
    {
        if (_softMaterial != null) return _softMaterial;
        const int n = 64;
        var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        var px = new Color32[n * n];
        float c = (n - 1) * 0.5f;
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float d = Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c)) / c;
                float a = Mathf.Clamp01(1f - d); a *= a;
                px[y * n + x] = new Color32(255, 255, 255, (byte)(a * 255f));
            }
        tex.SetPixels32(px); tex.Apply();
        _softMaterial = new Material(Shader.Find("Sprites/Default")) { mainTexture = tex };
        return _softMaterial;
    }
}
}
