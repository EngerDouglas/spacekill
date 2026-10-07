using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace OrbitRush
{

/// <summary>
/// Aim-down-sights / scope zoom. Hold the Aim input while a weapon with
/// hasScope=true (currently just GravitySniper) is equipped to zoom the
/// camera in, lower look sensitivity for precision, and show a scope
/// vignette overlay. Weapons without a scope simply ignore the input.
///
/// Attach to the Player alongside WeaponInventory, PlayerController and a
/// PlayerInput component (needs an "Aim" action in the Player action map).
/// </summary>
public class WeaponAim : MonoBehaviour
{
    [Header("Zoom Feel")]
    public float zoomSpeed = 10f;

    [Header("Third-person aim (weapons without a scope)")]
    [Tooltip("Field of view while aiming, as a fraction of the normal one.")]
    public float adsFovMultiplier = 0.74f;
    [Tooltip("Where the camera sits while aiming, relative to its normal position: right, up, forward (metres). " +
             "Moves in close behind the right shoulder so the shot lines up with the crosshair.")]
    public Vector3 adsCameraShift = new Vector3(0.75f, -0.7f, 2.0f);
    [Tooltip("How quickly the camera glides in and out (higher = snappier).")]
    public float adsSpeed = 9f;
    [Range(0.2f, 1f)] public float adsSensitivityMultiplier = 0.7f;

    private WeaponInventory _inventory;
    private PlayerController _controller;
    private Camera _camera;

    private bool _aimHeld;
    private float _defaultFov;
    private float _defaultSensitivity;
    private bool _sensitivityCached;

    private GameObject _scopeOverlay;

    private SniperScope _scope;

    // ── First person (F2) ──
    private const string PrefFirstPerson = "OrbitRush.FirstPerson";
    [Header("First person (F2)")]
    [Tooltip("Field of view in first person (wider than the third-person one, as most shooters do).")]
    public float firstPersonFov = 76f;
    private bool _firstPerson;
    private float _fpT;
    private float _baseNear = -1f;
    private PlayerStats _stats;
    private bool _fpLoaded;
    /// <summary>0 = third person, 1 = first person (CameraRig turns its lag / collision off in first person).</summary>
    public float FirstPersonBlend => _fpT;
    public bool IsFirstPerson => _firstPerson;
    private float _adsSmooth;
    /// <summary>0..1 how far the aim-down-sights is raised (smoothstepped).</summary>
    public float AdsBlend => _adsSmooth;

    /// <summary>Test hook (ScreenshotTool).</summary>
    private MeleeCombat _melee;
    private bool MeleeBlocks()
    {
        if (_melee == null) _melee = GetComponent<MeleeCombat>();
        return _melee != null && _melee.BlocksFiring;
    }

    public void DebugSetFirstPerson(bool on) { _fpLoaded = true; _firstPerson = on; if (_stats == null) _stats = GetComponent<PlayerStats>(); }

    private void ToggleFirstPerson()
    {
        _firstPerson = !_firstPerson;
        PlayerPrefs.SetInt(PrefFirstPerson, _firstPerson ? 1 : 0);
        if (HUD.Instance != null) HUD.Instance.ShowEvent(_firstPerson ? "VISTA: PRIMERA PERSONA  [F2]" : "VISTA: TERCERA PERSONA  [F2]", 1.6f);
    }
    /// <summary>0 = third person, 1 = looking through the scope (CameraRig turns its lag / collision off while scoped).</summary>
    public float ScopeBlend => _scopeT;
    private float _scopeT;              // 0 = normal view, 1 = looking through the scope
    private static readonly Vector3 ScopeCameraPos = new Vector3(0f, 0.02f, 0.1f);

    private float _ads;                 // 0 = normal view, 1 = fully zoomed in
    private Vector3 _baseCameraLocalPos;
    private bool _basePosCached;

    void Start()
    {
        _inventory = GetComponent<WeaponInventory>();
        _controller = GetComponent<PlayerController>();
        _camera = Camera.main;
        if (_camera != null) _defaultFov = _camera.fieldOfView;

    }

    /// <summary>Sets the base mouse sensitivity (from the settings menu). WeaponAim scales it while scoping,
    /// so changing PlayerController.lookSensitivity directly would be overwritten every frame.</summary>
    public void SetBaseSensitivity(float sensitivity)
    {
        _defaultSensitivity = sensitivity;
        _sensitivityCached = true;
        if (_controller != null) _controller.lookSensitivity = sensitivity;
    }

    // Kept so PlayerInput's Send Messages doesn't complain, but the aim state is polled from the
    // devices every frame (see PollAimHeld): a message-driven flag can get stuck on, or never
    // arrive, which left the scope permanently on / never zooming.
    public void OnAim(InputValue value) { }

    // Some controllers (virtual pads such as DualSenseX, or a pad whose trigger isn't calibrated)
    // report a high resting value for the trigger, which counted as "aim held" forever. So the
    // trigger is measured relative to the lowest value it has shown, and must rise clearly above it.
    private float _padTriggerRest = float.MaxValue;
    [Tooltip("Log to the Console which input turns the scope on/off (helps diagnose a stuck scope).")]
    public bool debugLog = false;
    private string _aimSource = "";

    /// <summary>Test hook (ScreenshotTool): behave as if the aim button were held.</summary>
    public static bool DebugForceAim;

    private bool PollAimHeld()
    {
        _aimSource = "";
        if (DebugForceAim) { _aimSource = "debug"; return true; }

        var mouse = Mouse.current;
        if (mouse != null && mouse.rightButton.isPressed) { _aimSource = "mouse right button"; return true; }

        var pad = Gamepad.current;
        if (pad != null)
        {
            float v = pad.leftTrigger.ReadValue();
            if (v < _padTriggerRest) _padTriggerRest = v;
            if (v - _padTriggerRest > 0.45f) { _aimSource = $"gamepad left trigger ({v:F2}, rest {_padTriggerRest:F2})"; return true; }
        }
        return false;
    }

    private bool _loggedScoping;

    void Update()
    {
        if (_camera == null) _camera = Camera.main;
        if (_camera == null) return;
        if (_defaultFov <= 0f) _defaultFov = _camera.fieldOfView;

        _aimHeld = PollAimHeld();

        var weapon = _inventory != null ? _inventory.ActiveWeapon : null;
        bool scoping = _aimHeld && weapon != null && weapon.hasScope;

        if (debugLog && scoping != _loggedScoping)
        {
            _loggedScoping = scoping;
            Debug.Log(scoping ? $"[Scope] ON via {_aimSource} — {weapon.weaponName}" : "[Scope] OFF");
        }

        // F2 swaps between first and third person (not while paused); you are always seen from outside when dead
        if (!_fpLoaded) { _firstPerson = PlayerPrefs.GetInt(PrefFirstPerson, 0) == 1; _fpLoaded = true; _stats = GetComponent<PlayerStats>(); }
        var kb = UnityEngine.InputSystem.Keyboard.current;
        if (kb != null && kb.f2Key.wasPressedThisFrame && Time.timeScale > 0f) ToggleFirstPerson();
        bool fpWanted = _firstPerson && (_stats == null || _stats.IsAlive);
        _fpT = Mathf.Lerp(_fpT, fpWanted ? 1f : 0f, 1f - Mathf.Exp(-10f * Time.unscaledDeltaTime));
        if (_fpT < 0.002f && !fpWanted) _fpT = 0f;
        float fpK = Ease.Smooth(_fpT);
        if (_baseNear < 0f) _baseNear = _camera.nearClipPlane;
        _camera.nearClipPlane = Mathf.Lerp(_baseNear, 0.04f, fpK);         // the gun in your hands sits very close to the lens

        ApplyVisibility(scoping ? 2 : (fpK > 0.6f ? 1 : 0));

        // Third-person aim-down-sights: any weapon without a scope zooms the camera in over the shoulder
        if (!_basePosCached) { _baseCameraLocalPos = _camera.transform.localPosition; _basePosCached = true; }
        bool ads = _aimHeld && weapon != null && !weapon.hasScope && !MeleeBlocks() && Time.timeScale > 0f;
        _ads = Mathf.Lerp(_ads, ads ? 1f : 0f, 1f - Mathf.Exp(-adsSpeed * Time.unscaledDeltaTime));
        if (_ads < 0.001f && !ads) _ads = 0f;
        float adsK = Ease.Smooth(_ads);       // smoothstep for a soft start and finish
        _adsSmooth = adsK;

        // Scope: the camera glides up to eye level and the 3D scope view takes over
        if (_scope == null) { _scope = gameObject.AddComponent<SniperScope>(); _scope.Init(_camera); }
        _scopeT = Mathf.Lerp(_scopeT, scoping ? 1f : 0f, 1f - Mathf.Exp(-9f * Time.unscaledDeltaTime));
        if (_scopeT < 0.002f && !scoping) _scopeT = 0f;
        float scopeK = Ease.Smooth(_scopeT);
        Vector3 thirdPerson = _baseCameraLocalPos + adsK * adsCameraShift;
        Vector3 viewPos = Vector3.Lerp(thirdPerson, ScopeCameraPos, fpK);                   // first person = eye level
        _camera.transform.localPosition = Vector3.Lerp(viewPos, ScopeCameraPos, scopeK);
        ScreenBlur.RequestScope(this, scoping);
        if (HUD.Instance != null) { HUD.Instance.AimZoom = adsK; HUD.Instance.SetCrosshairVisible(scopeK < 0.5f && !(fpK > 0.5f && adsK > 0.55f)); HUD.Instance.SetScopeHidden(scopeK > 0.35f); }
        _scope.SetState(scopeK, weapon != null ? weapon.scopeFov : 15f, HUD.Instance != null && HUD.Instance.AimingAtEnemy);

        // Sprinting / dashing widens the view a little (PlayerController.FovBoost) — but never while scoped or aiming.
        float boost = (!scoping && _controller != null) ? _controller.FovBoost * (1f - adsK) : 0f;
        float viewFov = Mathf.Lerp(_defaultFov, Mathf.Max(_defaultFov, firstPersonFov), fpK);
        float normalFov = viewFov + boost;
        float fpAdsFov = Mathf.Lerp(adsFovMultiplier, 0.84f, fpK);          // first person zooms less: the sight does the aiming
        float targetFov = scoping ? Mathf.Min(_defaultFov, 58f)       // the world around the scope stays at a normal view...
                                  : Mathf.Lerp(normalFov, viewFov * fpAdsFov, adsK);   // ...the zoom happens inside the scope
        _camera.fieldOfView = Mathf.Lerp(_camera.fieldOfView, targetFov, 1f - Mathf.Exp(-Mathf.Max(zoomSpeed, adsSpeed) * Time.unscaledDeltaTime));

        if (_controller != null)
        {
            if (!_sensitivityCached)
            {
                _defaultSensitivity = _controller.lookSensitivity;
                _sensitivityCached = true;
            }
            float mult = scoping ? weapon.scopeSensitivityMultiplier : Mathf.Lerp(1f, adsSensitivityMultiplier, adsK);
            _controller.lookSensitivity = _defaultSensitivity * mult;
        }

    }

    // ── Hide the character while scoping ────────────────────────────────
    // The camera is third-person (5 m behind the player), so a zoomed view looks straight at the
    // character's back and the scope is blocked. Hide the body (and team marker) while zoomed.

    private int _viewState = -1;      // 0 = normal, 1 = first person (body hidden, gun and katana stay), 2 = scoped (everything hidden)

    private void ApplyVisibility(int state)
    {
        if (state == _viewState) return;
        _viewState = state;

        // Body and team marker: invisible in first person and in the scope — but in first person they still cast a shadow
        var bodyMode = state == 0 ? UnityEngine.Rendering.ShadowCastingMode.On : UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly;
        foreach (var name in new[] { "CharacterModel", "TeamMarker" })
        {
            var t = transform.Find(name);
            if (t == null) continue;
            foreach (var r in t.GetComponentsInChildren<Renderer>(true))
            {
                r.enabled = state == 0 || state == 1;
                r.shadowCastingMode = bodyMode;
            }
        }

        // The katana and the held gun stay visible unless scoped (the scope view draws its own rail)
        bool itemsVisible = state != 2;
        var katana = transform.Find("KatanaRig");
        if (katana != null) foreach (var r in katana.GetComponentsInChildren<Renderer>(true)) r.enabled = itemsVisible;
        if (_inventory != null && _inventory.weaponMount != null)
            foreach (var r in _inventory.weaponMount.GetComponentsInChildren<Renderer>(true)) r.enabled = itemsVisible;
    }

    // ── Scope vignette (built once) ─────────────────────────────────────

    void BuildScopeOverlay()
    {
        var canvasGO = new GameObject("ScopeOverlay");
        var canvas = canvasGO.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 15; // above HUD (10) so it draws over everything

        var cs = canvasGO.AddComponent<CanvasScaler>();
        cs.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        cs.referenceResolution = new Vector2(1920, 1080);
        cs.matchWidthOrHeight = 0.5f;

        var maskGO = new GameObject("Mask");
        maskGO.transform.SetParent(canvasGO.transform, false);
        var rt = maskGO.AddComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(2400, 2400); // oversized square so the circle stays round at any aspect ratio
        rt.anchoredPosition = Vector2.zero;

        var img = maskGO.AddComponent<Image>();
        img.sprite = BuildScopeMaskSprite();
        img.color = Color.white; // color is baked into the generated texture's alpha

        _scopeOverlay = canvasGO;
        _scopeOverlay.SetActive(false);
    }

    // Shared across all instances — cheap enough to build once and reuse.
    static Sprite _scopeMaskSprite;

    static Sprite BuildScopeMaskSprite(int size = 512, float holeFraction = 0.22f)
    {
        if (_scopeMaskSprite != null) return _scopeMaskSprite;

        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        float radius = size * 0.5f;
        float holeRadius = radius * holeFraction;
        Vector2 center = new Vector2(radius, radius);
        var pixels = new Color32[size * size];

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dist = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), center);
                // Transparent inside the hole, opaque black outside it, with a
                // few pixels of feather at the edge so it isn't jagged.
                float alpha = Mathf.Clamp01((dist - holeRadius) / 3f);
                pixels[y * size + x] = new Color32(0, 0, 0, (byte)(alpha * 255));
            }
        }

        tex.SetPixels32(pixels);
        tex.Apply();
        tex.name = "ScopeMask";

        _scopeMaskSprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
        return _scopeMaskSprite;
    }
}
}
