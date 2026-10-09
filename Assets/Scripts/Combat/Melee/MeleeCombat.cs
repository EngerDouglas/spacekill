using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace OrbitRush
{

/// <summary>
/// The katana (Resources/Weapons/Katana.fbx) and melee combat. Added to the player automatically.
///
///   • The katana is an inventory item (<see cref="Katana"/>), picked up and cycled like the guns. It is only drawn
///     (and only attacks) while it is the active weapon; any other weapon makes it disappear.
///   • Left click / V (or right trigger / right-stick click): TAP = light attack (Great Sword 180 Turn animation),
///     alternating combo.
///   • Held: CHARGES (the blade glows brighter and trembles); release for the charged attack (Great Sword
///     Jump Attack) — a slam that hits everything around you, harder the longer you charged.
///   • C (or B on a pad): DODGE — a spinning burst of speed (Walking Turn 180) with a brief invulnerability window.
///     The dodge works whatever weapon is active.
///
/// The arms are animated by the Mixamo clips (CharacterAnimator via PlayerAnimDriver, which listens to
/// <see cref="AnimRequested"/>); the katana simply follows the hand / chest bone. Without a rigged model it
/// falls back to a fixed pose in front of the player.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(300)]       // after CameraRig / WeaponAim (first-person view-model follows the final camera)
public partial class MeleeCombat : MonoBehaviour
{
    /// <summary>True while the katana is the active weapon (drawn): the animation driver uses the sword stance.</summary>
    public bool BlocksFiring { get; private set; }

    /// <summary>(animation key, duration) — asks the animation driver to play a one-shot.</summary>
    public event Action<string, float> AnimRequested;

    [Header("Katana")]
    public string resourcePath = ResourcePaths.Katana;
    public float katanaScale = 1.15f;

    [Header("Light attack")]
    public float lightDamage = 40f;
    public float lightRange = 3.1f;
    [Range(40f, 200f)] public float lightArc = 130f;
    public float lightTime = 0.5f;
    public float lightHitAt = 0.42f;
    public float lungeSpeed = 5f;

    [Header("Charged attack (hold)")]
    [Tooltip("Holding longer than this turns a tap into a charge.")]
    public float tapThreshold = 0.22f;
    public float minChargeTime = 0.55f;
    public float fullChargeTime = 1.3f;
    public float chargedMinDamage = 70f;
    public float chargedMaxDamage = 160f;
    public float chargedRadius = 4.6f;
    public float chargedTime = 1.1f;
    public float chargedHitAt = 0.58f;

    [Header("Dodge")]
    public float dodgeSpeed = 15f;
    public float dodgeLock = 0.28f;
    public float dodgeAnimTime = 0.55f;
    public float dodgeCooldown = 0.9f;
    public float dodgeStamina = 18f;
    public float dodgeInvulnerability = 0.38f;

    [Header("Draw")]
    public float drawTime = 0.9f;

    private enum State { Stowed, Drawing, Ready, Light, Charging, Charged }
    private State _state = State.Stowed;
    private float _stateTime;

    // Input / combo
    private bool _holding;
    private float _holdTime;
    private bool _queuedLight;
    private float _dodgeReadyAt;
    private float _chargePower;
    private bool _hitDone;

    // Parts
    private Transform _rig, _katana, _tip;
    private TrailRenderer _trail;
    private Material[] _glowMaterials;
    private Color[] _glowBase;
    private PlayerStats _stats;
    private PlayerController _controller;
    private PlayerModel _model;
    private PlayerInput _input;
    private WeaponInventory _inventory;
    private bool _forceEquipped;      // test hooks: behave as if the katana were the active weapon

    // Attachment (computed from the model's rest pose)
    private WeaponAim _aim;
    private float _side = 1f;        // flips on every light attack (first-person slashes alternate sides)
    private Transform _chest, _hand;
    private Vector3 _stowLocalPos, _gripLocalPos;
    private Quaternion _stowLocalRot, _gripLocalRot;
    private bool _attached;

    private readonly HashSet<IDamageable> _alreadyHit = new HashSet<IDamageable>();

    // ══════════════════════════════════════════════════════════════════════
    // Setup
    // ══════════════════════════════════════════════════════════════════════

    // Fallback (rigid model) poses, relative to the player
    private static readonly Vector3 StowPos = new Vector3(0.20f, 0.60f, -0.36f);
    private static readonly Vector3 StowBlade = new Vector3(-0.50f, -0.86f, -0.10f);
    private static readonly Vector3 ReadyPos = new Vector3(0.34f, 0.12f, 0.40f);
    private static readonly Vector3 ReadyBlade = new Vector3(-0.15f, 0.30f, 0.94f);

    private static Quaternion BladeRot(Vector3 blade, float roll)
        => Quaternion.LookRotation(blade.normalized, Vector3.up) * Quaternion.AngleAxis(roll, Vector3.forward);

    void Start()
    {
        _stats = GetComponent<PlayerStats>();
        _controller = GetComponent<PlayerController>();
        _model = GetComponent<PlayerModel>();
        _input = GetComponent<PlayerInput>();
        _inventory = GetComponent<WeaponInventory>();
        _aim = GetComponent<WeaponAim>();

        var prefab = Resources.Load<GameObject>(resourcePath);
        if (prefab == null)
        {
            Debug.LogWarning($"[MeleeCombat] Could not load '{resourcePath}' from Resources — melee disabled.", this);
            enabled = false;
            return;
        }

        _rig = new GameObject("KatanaRig").transform;
        _rig.SetParent(transform, false);

        var instance = Instantiate(prefab, _rig);
        instance.name = "Katana";
        instance.transform.localScale = Vector3.one * katanaScale;
        _katana = instance.transform;

        _tip = WeaponModels.CreateMuzzle(_katana);     // sits at the very end of the blade
        BuildTrail();
        CacheGlowMaterials();
        FindBones();

        UpdateKatana(0f);
        _rig.gameObject.SetActive(false);      // only visible while the katana is the active weapon
    }

    void OnDisable() => BlocksFiring = false;

    /// <summary>Finds the chest and right-hand bones and measures the stowed / gripped poses while the model is still in its rest (T) pose.</summary>
    private void FindBones()
    {
        var animator = _model != null ? _model.ModelAnimator : null;
        if (animator == null || !animator.isHuman) return;

        _chest = animator.GetBoneTransform(HumanBodyBones.UpperChest);
        if (_chest == null) _chest = animator.GetBoneTransform(HumanBodyBones.Chest);
        if (_chest == null) _chest = animator.GetBoneTransform(HumanBodyBones.Spine);
        _hand = animator.GetBoneTransform(HumanBodyBones.RightHand);
        if (_chest == null || _hand == null) return;

        // Stowed: the player-relative backpack pose, expressed in the chest bone's space
        Vector3 sp = transform.TransformPoint(StowPos);
        Quaternion sr = transform.rotation * BladeRot(StowBlade, 90f);
        _stowLocalPos = _chest.InverseTransformPoint(sp);
        _stowLocalRot = Quaternion.Inverse(_chest.rotation) * sr;

        // Gripped: blade straight forward from the fist (palm down in the T-pose), handle in the palm
        Transform palm = null;
        foreach (Transform t in _hand) if (t.name.Contains("Middle1")) { palm = t; break; }
        Vector3 gripPoint = palm != null ? Vector3.Lerp(_hand.position, palm.position, 0.7f) : _hand.position;
        Quaternion gr = transform.rotation * BladeRot(Vector3.forward, -90f);
        Vector3 gp = gripPoint + gr * Vector3.forward * (0.10f * katanaScale * transform.lossyScale.x);
        _gripLocalPos = _hand.InverseTransformPoint(gp);
        _gripLocalRot = Quaternion.Inverse(_hand.rotation) * gr;
        _attached = true;
    }

    private void BuildTrail()
    {
        if (_tip == null) return;

        _trail = _tip.gameObject.AddComponent<TrailRenderer>();
        _trail.time = 0.14f;
        _trail.minVertexDistance = 0.04f;
        _trail.widthMultiplier = 0.07f;
        _trail.widthCurve = new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, 0f));
        _trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        _trail.receiveShadows = false;

        var gradient = new Gradient();
        gradient.SetKeys(
            new[] { new GradientColorKey(new Color(1f, 0.55f, 0.9f), 0f), new GradientColorKey(new Color(1f, 0.1f, 0.62f), 0.5f), new GradientColorKey(new Color(0f, 0.85f, 1f), 1f) },
            new[] { new GradientAlphaKey(0.95f, 0f), new GradientAlphaKey(0.5f, 0.5f), new GradientAlphaKey(0f, 1f) });
        _trail.colorGradient = gradient;

        var shader = Shader.Find("Sprites/Default");     // supports vertex colour + transparency
        if (shader != null) _trail.material = new Material(shader);
        _trail.emitting = false;
    }

    private void CacheGlowMaterials()
    {
        var mats = new List<Material>();
        var bases = new List<Color>();
        foreach (var r in _katana.GetComponentsInChildren<Renderer>(true))
        {
            foreach (var m in r.materials)    // instances, so the shared assets aren't touched
            {
                if (!m.HasProperty("_EmissionColor")) continue;
                Color e = m.GetColor("_EmissionColor");
                if (e.maxColorComponent < 0.05f) continue;      // not a glowing material
                mats.Add(m); bases.Add(e);
            }
        }
        _glowMaterials = mats.ToArray();
        _glowBase = bases.ToArray();
    }

    // ══════════════════════════════════════════════════════════════════════
    // Update
    // ══════════════════════════════════════════════════════════════════════

    void Update()
    {
        if (_katana == null) return;

        bool active = Time.timeScale > 0f && (_input == null || _input.enabled) && _stats != null && _stats.IsAlive;
        float dt = Time.deltaTime;

        // The katana is drawn while it is the active inventory item and put away as soon as anything else is selected.
        bool equipped = _forceEquipped || (_inventory != null && _inventory.ActiveWeapon is Katana);
        if (equipped && _state == State.Stowed) BeginDraw();
        else if (!equipped && _state != State.Stowed)
        {
            _state = State.Stowed; _stateTime = 0f; _holding = false; _queuedLight = false;
        }
        _rig.gameObject.SetActive(_state != State.Stowed);

        if (active) ReadInput(dt);
        else _holding = false;

        if (dt > 0f) TickState(dt);
        UpdateGlowAndTrail();

        BlocksFiring = _state != State.Stowed;
        if (HUD.Instance != null)
            HUD.Instance.SetMelee(_state == State.Charging ? Mathf.Clamp01(_holdTime / fullChargeTime) : 0f,
                                  1f - Mathf.Clamp01((_dodgeReadyAt - Time.time) / Mathf.Max(0.01f, dodgeCooldown)),
                                  _state != State.Stowed);
    }

    // After the Animator has posed the bones
    void LateUpdate()
    {
        if (_katana != null) UpdateKatana(Time.deltaTime);
    }

    private void ReadInput(float dt)
    {
        var kb = Keyboard.current;
        var gp = Gamepad.current;

        var mouse = Mouse.current;

        bool down = (kb != null && kb.vKey.wasPressedThisFrame) || (gp != null && (gp.rightStickButton.wasPressedThisFrame || gp.rightTrigger.wasPressedThisFrame))
                    || (mouse != null && mouse.leftButton.wasPressedThisFrame);
        bool held = (kb != null && kb.vKey.isPressed) || (gp != null && (gp.rightStickButton.isPressed || gp.rightTrigger.isPressed))
                    || (mouse != null && mouse.leftButton.isPressed);
        bool up = (kb != null && kb.vKey.wasReleasedThisFrame) || (gp != null && (gp.rightStickButton.wasReleasedThisFrame || gp.rightTrigger.wasReleasedThisFrame))
                  || (mouse != null && mouse.leftButton.wasReleasedThisFrame);
        bool dodge = (kb != null && kb.cKey.wasPressedThisFrame) || (gp != null && gp.buttonEast.wasPressedThisFrame);

        if (dodge) TryDodge();

        if (down && _state != State.Stowed) PressAttack();
        if (_holding)
        {
            if (held) HoldAttack(dt);
            if (up || !held) ReleaseAttack();
        }
    }

    // ── Attack input ──────────────────────────────────────────────────────

    private void PressAttack()
    {
        if (_state == State.Light || _state == State.Charged)
        {
            _queuedLight = true;       // chain the next slash
            return;
        }

        _holding = true;
        _holdTime = 0f;
    }

    private void HoldAttack(float dt)
    {
        _holdTime += dt;
        if (_state == State.Ready && _holdTime > tapThreshold) EnterState(State.Charging);
    }

    private void ReleaseAttack()
    {
        _holding = false;

        switch (_state)
        {
            case State.Charging:
                if (_holdTime >= minChargeTime) StartCharged(Mathf.InverseLerp(minChargeTime, fullChargeTime, _holdTime));
                else StartLight();
                break;
            case State.Ready:
                StartLight();
                break;
            case State.Drawing:
                _queuedLight = true;   // tapped while drawing: slash as soon as it's out
                break;
        }
    }

    // ── State machine ─────────────────────────────────────────────────────

    private void EnterState(State s) { _state = s; _stateTime = 0f; }

    private void BeginDraw()
    {
        EnterState(State.Drawing);
        AnimRequested?.Invoke(CharacterAnimator.Draw, drawTime);
    }

    private void TickState(float dt)
    {
        _stateTime += dt;

        switch (_state)
        {
            case State.Drawing:
                if (_stateTime >= drawTime)
                {
                    EnterState(State.Ready);
                    if (_queuedLight) { _queuedLight = false; StartLight(); }
                    else if (_holding && _holdTime > tapThreshold) EnterState(State.Charging);
                }
                break;

            case State.Light:
                if (!_hitDone && _stateTime >= lightTime * lightHitAt) { _hitDone = true; Strike(lightRange, lightArc, lightDamage, 9f); }
                if (_stateTime >= lightTime)
                {
                    EnterState(State.Ready);
                    if (_queuedLight) { _queuedLight = false; StartLight(); }
                }
                break;

            case State.Charged:
                if (!_hitDone && _stateTime >= chargedTime * chargedHitAt)
                {
                    _hitDone = true;
                    Strike(chargedRadius, 360f, Mathf.Lerp(chargedMinDamage, chargedMaxDamage, _chargePower), 16f);
                }
                if (_stateTime >= chargedTime)
                {
                    EnterState(State.Ready);
                    if (_queuedLight) { _queuedLight = false; StartLight(); }
                }
                break;
        }
    }

    private void StartLight()
    {
        _side = -_side;
        _hitDone = false;
        _alreadyHit.Clear();
        EnterState(State.Light);
        if (_trail != null) _trail.Clear();

        AnimRequested?.Invoke(CharacterAnimator.SwordLight, lightTime);

        // A short lunge into the slash
        if (_controller != null) _controller.Burst(transform.forward, lungeSpeed, 0.14f);
    }

    private void StartCharged(float power)
    {
        _chargePower = Mathf.Clamp01(power);
        _hitDone = false;
        _alreadyHit.Clear();
        EnterState(State.Charged);
        if (_trail != null) _trail.Clear();

        AnimRequested?.Invoke(CharacterAnimator.SwordCharged, chargedTime);
        if (_controller != null) _controller.Burst(transform.forward, 4f + 4f * _chargePower, 0.3f);
    }

    private void TryDodge()
    {
        if (_controller == null || _stats == null) return;
        if (Time.time < _dodgeReadyAt) return;
        if (!_stats.UseStamina(dodgeStamina)) return;

        Vector2 move = _controller.MoveInput;
        Vector3 dir = transform.right * move.x + transform.forward * move.y;
        if (dir.sqrMagnitude < 0.04f) dir = -transform.forward;            // no input: hop backwards
        dir = Vector3.ProjectOnPlane(dir, transform.up).normalized;

        _controller.Burst(dir, dodgeSpeed, dodgeLock);
        _stats.InvulnerableUntil = Time.time + dodgeInvulnerability;
        _dodgeReadyAt = Time.time + dodgeCooldown;

        if (_model != null && _model.ModelAnimator != null) AnimRequested?.Invoke(CharacterAnimator.Dodge, dodgeAnimTime);
        else if (_model != null)    // rigid model: roll about the axis perpendicular to the travel direction
        {
            Vector3 local = transform.InverseTransformDirection(dir);
            _model.PlayTumble(new Vector3(local.z, 0f, -local.x), dodgeLock + 0.12f, 360f);
        }

        // A dodge cancels a charge in progress
        if (_state == State.Charging) { EnterState(State.Ready); _holding = false; }
    }

    // ── Damage ────────────────────────────────────────────────────────────

    private void Strike(float radius, float arcDegrees, float damage, float knockback)
    {
        Vector3 up = transform.up;
        Vector3 origin = transform.position + up * 0.2f;
        bool any = false, killed = false;

        foreach (var col in Physics.OverlapSphere(origin, radius))
        {
            if (col.isTrigger || col.transform.IsChildOf(transform)) continue;

            var target = col.GetComponentInParent<IDamageable>();
            if (target == null || (object)target == _stats || !_alreadyHit.Add(target)) continue;

            Vector3 to = Vector3.ProjectOnPlane(col.bounds.center - transform.position, up);
            if (arcDegrees < 359f && to.sqrMagnitude > 0.01f && Vector3.Angle(transform.forward, to) > arcDegrees * 0.5f) continue;

            target.TakeDamage(damage, _stats);
            any = true;
            if (!target.IsAlive) killed = true;

            var body = col.attachedRigidbody;
            if (body != null && !body.isKinematic)
                body.AddForce((to.normalized + up * 0.3f) * knockback, ForceMode.VelocityChange);

            Vector3 point = col.ClosestPoint(origin);
            ImpactFx.Play(WeaponModels.Folder + "SpaceShotgun_Impact", point, (origin - point).normalized, 3.2f, 0.3f);
        }

        if (any && HUD.Instance != null) HUD.Instance.ShowHitMarker(killed);
    }
}
}

// partial class: see MeleeCombat.cs / .Katana.cs / .Dev.cs
