using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace OrbitRush
{

/// <summary>
/// Orbit Rush in-game HUD, built entirely in code (add it to a Canvas GameObject — no wiring).
/// "Apex" look: no panels, thin white line art, one cyan accent, Inter type. Layout (1920×1080 reference):
///
///   top-left       radar, planet name + gravity
///   top-centre     match timer, mode (team scores in team modes)
///   top-right      kills / AI kills, kill feed
///   bottom-left    energy and jetpack bars
///   bottom-centre  shield (6 segments), health, dash / dodge rings
///   bottom-right   weapon, ammo, reload / heat, grenades
///   centre         dynamic crosshair, hit marker, damage-direction arcs, "shield low" warning
///   full-screen    damage flash and low-health pulse
///
/// The weapon wheel (hold Tab) lives in <see cref="WeaponWheel"/>.
/// Public API used elsewhere: <see cref="Instance"/>, <see cref="ShowEvent"/>, <see cref="ShowHitMarker"/>, <see cref="SetMelee"/>,
/// <see cref="AimZoom"/>, <see cref="SetCrosshairVisible"/>, <see cref="SetScopeHidden"/>, <see cref="AimingAtEnemy"/>, <see cref="SetHidden"/>.
/// </summary>
public partial class HUD : MonoBehaviour
{
    public static HUD Instance { get; private set; }

    /// <summary>0..1 aim-down-sights amount (set by WeaponAim): the crosshair tightens as it rises.</summary>
    public float AimZoom { get; set; }

    // ── Palette ───────────────────────────────────────────────────────────
    static readonly Color White   = Color.white;
    static readonly Color Soft    = new Color(1f, 1f, 1f, 0.63f);
    static readonly Color Cyan    = new Color32(0x4F, 0xD6, 0xFF, 255);
    static readonly Color Amber   = new Color32(0xFF, 0xD1, 0x66, 255);
    static readonly Color Alert   = new Color32(0xFF, 0x5B, 0x5B, 255);
    static readonly Color Track   = new Color(1f, 1f, 1f, 0.12f);
    static readonly Color Ink     = new Color32(0x0B, 0x12, 0x20, 255);

    // ── Runtime refs ──────────────────────────────────────────────────────
    private PlayerStats      _stats;
    private WeaponInventory  _inventory;
    private PlayerController _controller;
    private Rigidbody        _playerBody;
    private Font             _font, _fontLight, _fontSemi;

    // ── Bars ──────────────────────────────────────────────────────────────
    private class Bar
    {
        public RectTransform fill, ghost;
        public Image fillImage;
        public float shown = 1f, ghostShown = 1f;
    }
    private Bar _health, _energy, _jetpack, _heat;
    private RectTransform[] _shieldFill;
    private Image[] _shieldFillImg;
    private Image _dashRing, _dodgeRing;
    private Text _dashKey, _dodgeKey;
    private Text _vidaText, _escudoText, _energyValue, _jetpackValue;
    private float _meleeCharge, _meleeDodge = 1f;
    private bool _meleeOut;

    /// <summary>Katana state from MeleeCombat: charge 0-1, dodge readiness 0-1, drawn or stowed. The HUD only shows the dodge readiness.</summary>
    public void SetMelee(float charge, float dodgeReady, bool drawn) { _meleeCharge = charge; _meleeDodge = dodgeReady; _meleeOut = drawn; }

    // ── Texts / groups ────────────────────────────────────────────────────
    private Text _weaponName, _ammoText, _reloadLabel, _grenadeQ, _grenadeF;   // ammo animation: HUD.Ammo.cs
    private GameObject _heatGroup, _reloadGroup;
    private Text _planetName, _gravityText;
    private Text _timerText, _modeText, _teamText, _statsText;

    // ── Kill feed ─────────────────────────────────────────────────────────
    private class FeedLine { public string text; public bool mine; public float age; }
    private const int   FeedLines = 3;
    private const float FeedLife  = 6f;
    private readonly List<FeedLine> _feed = new List<FeedLine>();
    private Text[] _feedTexts;

    // ── Damage direction / shield warning ─────────────────────────────────
    private class DamageArc { public RectTransform pivot; public HudArc arc; public float life; public float angle; }
    private DamageArc[] _damageArcs;
    private GameObject _shieldWarn;
    private Image _shieldWarnIcon;
    private Text _shieldWarnText;

    // ── Banner ────────────────────────────────────────────────────────────
    private GameObject _banner;
    private Text _bannerText;
    private float _bannerTimer;

    // ── Crosshair / feedback ──────────────────────────────────────────────
    private RectTransform[] _ticks;
    private Image[] _tickImages;
    private Image _centerDot;
    private RectTransform _hitMarker;
    private Image[] _hitImages;
    private float _hitTimer, _hitDuration = 0.18f;
    private bool _hitWasKill;
    private float _crossGap = 10f, _kick;
    private Image _vignette;
    private float _damageFlash, _lastHealth = -1f;

    // ── Radar ─────────────────────────────────────────────────────────────
    private const float RadarRange = 60f;
    private const float RadarRadius = 100f;
    private RectTransform _radar;
    private float _radarRadius = RadarRadius;
    private readonly List<Image> _radarDots = new List<Image>();
    private readonly List<EnemyStats> _enemies = new List<EnemyStats>();
    private float _enemyScanTimer;

    // ── Cached sprites ────────────────────────────────────────────────────
    private static Sprite _circle, _vignetteSprite, _triangle, _pill;
    private static readonly Dictionary<string, Sprite> _rings = new Dictionary<string, Sprite>();
    private static readonly Dictionary<string, Sprite> _gradPills = new Dictionary<string, Sprite>();

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

    private bool _wheelOpen;
    /// <summary>The weapon wheel is open: the HUD fades away so it doesn't overlap the wheel.</summary>
    public void SetWheelOpen(bool open) => _wheelOpen = open;

    private bool _scopeHidden;
    /// <summary>Looking through the sniper scope: the HUD fades away completely (independent of the pause / death hiding).</summary>
    public void SetScopeHidden(bool hidden) => _scopeHidden = hidden;

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        _font = LoadFont();
        _fontLight = UiFactory.LoadFontLight();
        _fontSemi = UiFactory.LoadFontSemiBold();
        BuildHUD();
        _group = GetComponent<CanvasGroup>();
        if (_group == null) _group = gameObject.AddComponent<CanvasGroup>();
        KillFeed.Killed += OnKill;
        WeaponBase.AnyFired += OnWeaponFired;
    }

    void OnDestroy()
    {
        KillFeed.Killed -= OnKill;
        WeaponBase.AnyFired -= OnWeaponFired;
        if (_stats != null) _stats.HitFrom -= OnPlayerHit;
        if (Instance == this) Instance = null;
    }

    void Start()  => FindPlayer();

    void Update()
    {
        RefreshHUD();
        if (_group != null)
            _group.alpha = Mathf.MoveTowards(_group.alpha, (_hidden || _scopeHidden || _wheelOpen) ? 0f : 1f, Time.unscaledDeltaTime * (_scopeHidden || _wheelOpen ? 8f : 4f));
    }
}
}
