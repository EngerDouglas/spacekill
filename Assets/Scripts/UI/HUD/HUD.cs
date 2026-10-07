using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace OrbitRush
{

/// <summary>
/// Orbit Rush in-game HUD, built entirely in code (add it to a Canvas GameObject — no wiring).
/// Same visual language as the main menu: navy glass panels, neon pink / cyan / yellow accents.
///
///   top-left     planet + gravity, enemy radar
///   top-centre   match timer, team scores, mode
///   top-right    kills (players / AI)
///   bottom-left  health (with damage trail), shield, jetpack / energy / dash
///   bottom-right weapon, ammo, reload progress, heat, grenades
///   bottom-centre weapon slots
///   centre       dynamic crosshair (opens with movement and fire, turns red over an enemy), hit marker
///   full-screen  damage flash and low-health pulse
///
/// Public API used elsewhere: <see cref="Instance"/>, <see cref="ShowEvent"/>, <see cref="ShowHitMarker"/>.
/// </summary>
public partial class HUD : MonoBehaviour
{
    public static HUD Instance { get; private set; }

    /// <summary>0..1 aim-down-sights amount (set by WeaponAim): the crosshair tightens as it rises.</summary>
    public float AimZoom { get; set; }

    // ── Palette (shared with the main menu) ───────────────────────────────
    static readonly Color Navy     = new Color(0.025f, 0.03f, 0.10f, 0.86f);
    static readonly Color NavyDeep = new Color(0.02f, 0.024f, 0.10f, 0.94f);
    static readonly Color Pink     = new Color(0.98f, 0.01f, 0.62f);
    static readonly Color Cyan     = new Color(0.00f, 0.78f, 0.99f);
    static readonly Color Yellow   = new Color(0.99f, 0.80f, 0.09f);
    static readonly Color Green    = new Color(0.20f, 1.00f, 0.55f);
    static readonly Color Red      = new Color(1.00f, 0.18f, 0.20f);
    static readonly Color White    = new Color(0.96f, 0.96f, 1.00f);
    static readonly Color Muted    = new Color(0.68f, 0.72f, 0.88f);

    // ── Runtime refs ──────────────────────────────────────────────────────
    private PlayerStats      _stats;
    private WeaponInventory  _inventory;
    private PlayerController _controller;
    private Rigidbody        _playerBody;
    private Font             _font;

    // ── Bars ──────────────────────────────────────────────────────────────
    private class Bar
    {
        public RectTransform fill, ghost;
        public Image fillImage;
        public float shown = 1f, ghostShown = 1f;
    }
    private Bar _health, _shield, _jet, _stamina, _dash, _heat, _reload, _dodge, _katana;
    private float _meleeCharge, _meleeDodge = 1f;
    private bool _meleeOut;

    /// <summary>Katana state from MeleeCombat: charge 0-1, dodge readiness 0-1, drawn or stowed.</summary>
    public void SetMelee(float charge, float dodgeReady, bool drawn) { _meleeCharge = charge; _meleeDodge = dodgeReady; _meleeOut = drawn; }
    private Text _healthText, _shieldText;

    // ── Texts / panels ────────────────────────────────────────────────────
    private Text _weaponName, _ammoCurrent, _ammoMax, _reloadLabel, _grenadeQ, _grenadeF;
    private GameObject _heatGroup, _reloadGroup;
    private Text _planetName, _gravityText;
    private Text _timerText, _modeText, _pinkScore, _cyanScore, _killsText, _pveText;
    private GameObject _teamGroup;

    // ── Banner ────────────────────────────────────────────────────────────
    private GameObject _banner;
    private Text _bannerText;
    private float _bannerTimer;

    // ── Weapon slots ──────────────────────────────────────────────────────
    private Transform _slotsRoot;
    private NeonPanel[] _slotBg;
    private Text[] _slotKey, _slotName;
    private int[] _slotState;
    private int _builtSlotCount = -1;

    // ── Crosshair / feedback ──────────────────────────────────────────────
    private RectTransform[] _ticks;
    private Image[] _tickImages;
    private Image _centerDot;
    private RectTransform _hitMarker;
    private Image[] _hitImages;
    private float _hitTimer, _hitDuration = 0.18f;
    private bool _hitWasKill;
    private float _crossGap = 10f, _kick;
    private int _lastAmmo = -1;
    private Image _vignette;
    private float _damageFlash, _lastHealth = -1f;

    // ── Radar ─────────────────────────────────────────────────────────────
    private const float RadarRange = 60f;
    private const float RadarRadius = 92f;
    private RectTransform _radar;
    private readonly List<Image> _radarDots = new List<Image>();
    private readonly List<EnemyStats> _enemies = new List<EnemyStats>();
    private float _enemyScanTimer;

    // ── Cached sprites ────────────────────────────────────────────────────
    private static Sprite _circle, _ring, _vignetteSprite;

    // ══════════════════════════════════════════════════════════════════════

    private CanvasGroup _group;
    private bool _hidden;
    private GameObject _crosshairRoot;

    /// <summary>The scope draws its own reticle: hide the normal crosshair while scoped.</summary>
    public void SetCrosshairVisible(bool visible)
    {
        if (_crosshairRoot != null && _crosshairRoot.activeSelf != visible) _crosshairRoot.SetActive(visible);
    }

    /// <summary>True while an enemy is under the crosshair (lets the scope reticle turn red too).</summary>
    public bool AimingAtEnemy => _aimingAtEnemy;

    /// <summary>Fades the whole HUD out (pause / death / scoreboard) or back in. Works while the game is paused.</summary>
    public void SetHidden(bool hidden) => _hidden = hidden;

    private bool _scopeHidden;
    /// <summary>Looking through the sniper scope: the HUD fades away completely (independent of the pause / death hiding).</summary>
    public void SetScopeHidden(bool hidden) => _scopeHidden = hidden;

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        _font = LoadFont();
        BuildHUD();
        _group = GetComponent<CanvasGroup>();
        if (_group == null) _group = gameObject.AddComponent<CanvasGroup>();
    }

    void Start()  => FindPlayer();

    void Update()
    {
        RefreshHUD();
        if (_group != null)
            _group.alpha = Mathf.MoveTowards(_group.alpha, (_hidden || _scopeHidden) ? 0f : 1f, Time.unscaledDeltaTime * (_scopeHidden ? 8f : 4f));
    }
}
}
