using UnityEngine;

namespace OrbitRush
{

/// <summary>
/// Dresses the player in the Orbit Rush character made in Blender
/// (Assets/Resources/Models/OrbitRush_Character.fbx) and gives the (single, rigid) model life with
/// procedural motion:
///   • leans into the direction of travel (forward / back) and rolls when strafing
///   • bounces and sways in time with its stride while running
///   • squashes on landing (more the harder it hits) and stretches on take-off, springing back
///   • tilts forward under jetpack thrust
/// All of it is driven by the PlayerController's real velocity, so it always matches what the player does.
///
/// Added automatically by PlayerStats on Start, so no scene or prefab edits are needed. The model is
/// parented to the player and stood on the bottom of the player's capsule, and the player's own capsule
/// mesh (if any) is hidden. To use a different model, assign <see cref="modelPrefab"/> or change
/// <see cref="resourcePath"/>.
/// </summary>
[DisallowMultipleComponent]
public class PlayerModel : MonoBehaviour
{
    [Tooltip("Optional override. If empty, the model is loaded from Resources/<resourcePath>.")]
    public GameObject modelPrefab;
    public string resourcePath = ResourcePaths.PlayerModel;
    [Tooltip("The Blender model is already authored at ~2 m, matching the player capsule.")]
    public float modelScale = 1f;

    [Header("Procedural Motion")]
    public bool animate = true;
    [Tooltip("Forward/back lean at full speed (degrees).")]
    public float leanDegrees = 14f;
    [Tooltip("Sideways roll when strafing at full speed (degrees).")]
    public float rollDegrees = 9f;
    [Tooltip("Vertical bounce of the stride at full speed (metres).")]
    public float bobHeight = 0.06f;
    [Tooltip("Left/right sway of the stride at full speed (degrees).")]
    public float swayDegrees = 3.5f;
    [Tooltip("How strongly landings squash the body.")]
    public float landSquash = 0.012f;

    private const string ModelObjectName = "CharacterModel";

    private Renderer _rootRenderer;
    private Transform _model;
    private PlayerController _controller;
    private Vector3 _baseLocalPosition;

    /// <summary>The Humanoid animator when the model is rigged (null for a rigid model).</summary>
    public Animator ModelAnimator { get; private set; }
    public CharacterAnimator Anim { get; private set; }

    // Smoothed animation state
    private float _lean, _roll, _bobPhase;
    private float _squash, _squashVelocity;      // +compress / -stretch, driven by a spring back to 0
    private float _tumbleTime, _tumbleDuration;  // dodge roll / spin
    private Vector3 _tumbleAxis = Vector3.right;
    private float _tumbleDegrees = 360f;

    void Start()
    {
        if (transform.Find(ModelObjectName) != null) return; // already dressed

        var prefab = modelPrefab != null ? modelPrefab : Resources.Load<GameObject>(resourcePath);
        if (prefab == null)
        {
            Debug.LogWarning($"[PlayerModel] Could not load '{resourcePath}' from Resources — keeping the default body.", this);
            enabled = false;
            return;
        }

        // Stand the model on the bottom of the capsule (player pivot is the capsule center).
        float feetY = 0f;
        var capsule = GetComponent<CapsuleCollider>();
        if (capsule != null) feetY = capsule.center.y - capsule.height * 0.5f;

        var instance = Instantiate(prefab, transform);
        instance.name = ModelObjectName;
        instance.transform.localPosition = new Vector3(0f, feetY, 0f);
        instance.transform.localRotation = Quaternion.identity;
        instance.transform.localScale = Vector3.one * modelScale;
        _model = instance.transform;
        _baseLocalPosition = _model.localPosition;

        // Visual only: stray colliders would fight the capsule and block shots/grounding.
        foreach (var col in instance.GetComponentsInChildren<Collider>(true)) Destroy(col);

        _rootRenderer = GetComponent<Renderer>();
        HideOldBodies();

        // Rigged model → play the Mixamo clips instead of the procedural motion.
        var animator = instance.GetComponentInChildren<Animator>();
        if (animator != null && animator.avatar != null)
        {
            Anim = instance.AddComponent<CharacterAnimator>();
            if (Anim.Setup(animator))
            {
                ModelAnimator = animator;
                animate = false;
                gameObject.AddComponent<PlayerAnimDriver>().Init(Anim, _model);
                gameObject.AddComponent<WeaponHandFollow>().Init(animator);
                if (GetComponent<PlayerController>() != null) gameObject.AddComponent<JetpackVisual>().Init(animator);
            }
        }

        _controller = GetComponent<PlayerController>();
        if (_controller != null)
        {
            _controller.Landed += OnLanded;
            _controller.Jumped += OnJumped;
        }
    }

    void OnDestroy()
    {
        if (_controller != null)
        {
            _controller.Landed -= OnLanded;
            _controller.Jumped -= OnJumped;
        }
    }

    /// <summary>
    /// The scene's player has the old "space soldier" FBX as a child. Deactivate it (rather
    /// than delete it) so nothing is lost; to remove it for good, delete that child in the
    /// Hierarchy. Deactivated objects also stay hidden when PlayerRespawn re-enables renderers.
    /// </summary>
    private void HideOldBodies()
    {
        foreach (Transform child in transform)
        {
            if (child == _model) continue;
            if (child.name.IndexOf("soldier", System.StringComparison.OrdinalIgnoreCase) >= 0)
                child.gameObject.SetActive(false);
        }
    }

    /// <summary>Rotates the whole body about its centre (in the model's local space): a dodge roll or a spin attack.</summary>
    public void PlayTumble(Vector3 localAxis, float duration, float degrees = 360f)
    {
        _tumbleAxis = localAxis.sqrMagnitude > 0.001f ? localAxis.normalized : Vector3.right;
        _tumbleDuration = Mathf.Max(0.05f, duration);
        _tumbleTime = 0f;
        _tumbleDegrees = degrees;
    }

    private void OnLanded(float impactSpeed) => _squash = Mathf.Clamp(impactSpeed * landSquash, 0.04f, 0.22f);

    private void OnJumped() => _squash = -0.09f;     // a quick stretch as the legs push off

    // PlayerRespawn re-enables every Renderer on respawn, so keep the old body hidden each frame.
    void LateUpdate()
    {
        if (_model != null && _rootRenderer != null && _rootRenderer.enabled)
            _rootRenderer.enabled = false;

        if (animate && _model != null && _controller != null) Animate(Time.deltaTime);
    }

    private void Animate(float dt)
    {
        if (dt <= 0f) return;

        Vector3 v = _controller.LocalPlanarVelocity;                    // x = right, z = forward (m/s)
        float topSpeed = Mathf.Max(0.1f, _controller.moveSpeed);
        float forward = Mathf.Clamp(v.z / topSpeed, -1f, 1f);
        float side = Mathf.Clamp(v.x / topSpeed, -1f, 1f);
        float speedNorm = Mathf.Clamp01(_controller.PlanarSpeed / topSpeed);
        bool grounded = _controller.IsGrounded;
        bool jetting = _controller.IsJetpacking;

        // ── Lean & roll (smoothed so direction changes ease in) ──
        float targetLean = forward * leanDegrees;
        if (jetting) targetLean += 12f;
        if (!grounded && !jetting) targetLean *= 0.5f;
        float targetRoll = -side * rollDegrees;

        float follow = 1f - Mathf.Exp(-9f * dt);
        _lean = Mathf.Lerp(_lean, targetLean, follow);
        _roll = Mathf.Lerp(_roll, targetRoll, follow);

        // ── Stride bounce & sway (only while running on the ground) ──
        float bob = 0f, sway = 0f;
        if (grounded && speedNorm > 0.05f)
        {
            _bobPhase += dt * Mathf.Lerp(7f, 13f, speedNorm);          // quicker steps at higher speed
            bob = Mathf.Abs(Mathf.Sin(_bobPhase)) * bobHeight * speedNorm;
            sway = Mathf.Sin(_bobPhase) * swayDegrees * speedNorm;
        }

        // ── Squash / stretch spring (settles back to zero) ──
        _squashVelocity += (-_squash * 190f - _squashVelocity * 14f) * dt;
        _squash += _squashVelocity * dt;

        // Tumble: ease-in-out spin about the body's centre (not the feet)
        Quaternion tumble = Quaternion.identity;
        Vector3 centre = new Vector3(0f, 1f * modelScale, 0f);
        if (_tumbleDuration > 0f)
        {
            _tumbleTime += dt;
            float k = Mathf.Clamp01(_tumbleTime / _tumbleDuration);
            tumble = Quaternion.AngleAxis(Mathf.SmoothStep(0f, _tumbleDegrees, k), _tumbleAxis);
            if (k >= 1f) _tumbleDuration = 0f;
        }

        float s = modelScale;
        _model.localPosition = _baseLocalPosition + new Vector3(0f, bob, 0f) + centre - tumble * centre;
        _model.localRotation = tumble * Quaternion.Euler(_lean, 0f, _roll + sway);
        _model.localScale = new Vector3(s * (1f + _squash * 0.5f), s * (1f - _squash), s * (1f + _squash * 0.5f));
    }
}
}
