using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace OrbitRush
{

/// <summary>
/// Orbit Rush start screen + pause menu, built entirely in code (no scene or asset setup).
///
/// At startup it pauses the game, puts a slowly orbiting camera behind the player (so the galaxy
/// sky, the planets and the hero are the backdrop), and shows the neon home screen: profile, nav,
/// feature cards and the big PLAY button. PLAY picks the chosen game mode, unpauses and starts the
/// match. Esc during a match opens the pause menu (continue / settings / main menu / quit).
///
/// Installs itself on every scene load that contains a GameManager. Set <see cref="Enabled"/> to
/// false (or delete this file) to go straight into the match as before.
/// </summary>
public partial class MainMenu : MonoBehaviour
{
    public static bool Enabled = true;

    /// <summary>True while the main menu is up — GameManager waits to start the match.</summary>
    public static bool BlocksStart { get; private set; }

    // ── Palette (matches the Orbit Rush art: navy UI, neon pink / cyan / yellow) ──
    static readonly Color Navy     = new Color(0.03f, 0.04f, 0.13f, 0.88f);
    static readonly Color NavyDeep = new Color(0.02f, 0.02f, 0.08f, 0.96f);
    static readonly Color Pink     = new Color(1.00f, 0.10f, 0.62f);
    static readonly Color Cyan     = new Color(0.00f, 0.85f, 1.00f);
    static readonly Color Yellow   = new Color(1.00f, 0.80f, 0.10f);
    static readonly Color Violet   = new Color(0.60f, 0.30f, 1.00f);
    static readonly Color White    = new Color(0.96f, 0.96f, 1.00f);
    static readonly Color Muted    = new Color(0.72f, 0.76f, 0.92f);

    const string PrefSensitivity = "OrbitRush.Sensitivity";
    const string PrefVolume      = "OrbitRush.Volume";
    const string PrefMode        = "OrbitRush.Mode";

    static readonly GameManager.GameMode[] Modes =
    {
        GameManager.GameMode.FreeForAll,
        GameManager.GameMode.TeamDeathmatch,
        GameManager.GameMode.PlanetCapture,
    };

    private Canvas _canvas;
    private Transform _canvasRoot;
    private GameObject _home;
    private GameObject _modal;
    private Text _toast;
    private float _toastTimer;
    private Font _font;
    private Camera _menuCam;
    private Text _mapsSubtitle;
    private RectTransform _playButton;

    private PlayerController _player;
    private PlayerInput _playerInput;
    private WeaponAim _weaponAim;

    private bool _inMenu;
    private bool _paused;
    private int _modeIndex;
    private float _camTime;

    // ══════════════════════════════════════════════════════════════════════
    // Installation
    // ══════════════════════════════════════════════════════════════════════

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Register()
    {
        // Command-line switch for testing / quick launches: OrbitRush.exe -nomenu
        foreach (var arg in System.Environment.GetCommandLineArgs())
            if (arg == "-nomenu") Enabled = false;

        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (!Enabled) return;
        if (FindFirstObjectByType<GameManager>() == null) return;
        if (FindFirstObjectByType<MainMenu>() != null) return;

        new GameObject("MainMenu").AddComponent<MainMenu>();
    }

    // ══════════════════════════════════════════════════════════════════════
    // Lifecycle
    // ══════════════════════════════════════════════════════════════════════

    void Awake()
    {
        _font = LoadFont();
        _modeIndex = Mathf.Clamp(PlayerPrefs.GetInt(PrefMode, 0), 0, Modes.Length - 1);

        // sceneLoaded fires before any Start(), so GameManager.Start() sees this and waits.
        BlocksStart = true;
        _inMenu = true;
        Debug.Log("[MainMenu] Shown (match waits for PLAY).");
        Time.timeScale = 0f;

        FindPlayer();
        SetPlayerInputEnabled(false);
        ApplySavedSettings();
        SetHudVisible(false);
        EnsureEventSystem();

        BuildCanvas();
        BuildHome();
        BuildMenuCamera();
        ShowCursor(true);
    }

    void Update()
    {
        if (_toast != null && _toastTimer > 0f)
        {
            _toastTimer -= Time.unscaledDeltaTime;
            var c = _toast.color; c.a = Mathf.Clamp01(_toastTimer / 0.4f); _toast.color = c;
        }

        var kb = Keyboard.current;

        if (_inMenu)
        {
            if (_playButton != null)
                _playButton.localScale = Vector3.one * (1f + 0.025f * Mathf.Sin(Time.unscaledTime * 3f));

            if (kb != null)
            {
                if (_modal != null && kb.escapeKey.wasPressedThisFrame) CloseModal();
                else if (_modal == null && kb.enterKey.wasPressedThisFrame) Play("Enter key");   // Enter only: Space is too easy to hit by accident
            }
            return;
        }

        // In a match: Esc toggles the pause menu (not once the match is over — the scoreboard takes over).
        if (kb != null && kb.escapeKey.wasPressedThisFrame)
        {
            if (_paused) Resume();
            else if (GameManager.Instance != null && GameManager.Instance.IsMatchActive) Pause();
        }
    }

    void LateUpdate()
    {
        if (!_inMenu || _menuCam == null) return;
        SnapPlayerToSurface();
        PositionMenuCamera();
    }

    void OnDestroy()
    {
        ScreenBlur.Request(this, false);
        if (_inMenu || _paused) Time.timeScale = 1f;   // never leave the game frozen if this object goes away
        if (_menuCam != null) Destroy(_menuCam.gameObject);
    }

    // ══════════════════════════════════════════════════════════════════════
    // Game flow
    // ══════════════════════════════════════════════════════════════════════

    private void Play(string reason)
    {
        Debug.Log($"[MainMenu] PLAY via {reason} at t={Time.realtimeSinceStartup:F1}s");
        PlayerPrefs.SetInt(PrefMode, _modeIndex);
        PlayerPrefs.Save();

        var gm = GameManager.Instance;
        if (gm != null) gm.SetGameMode(Modes[_modeIndex]);

        CloseModal();
        if (_home != null) Destroy(_home);
        if (_menuCam != null) Destroy(_menuCam.gameObject);

        _inMenu = false;
        BlocksStart = false;
        Time.timeScale = 1f;
        SetPlayerInputEnabled(true);
        SetHudVisible(true);
        ShowCursor(false);

        if (gm != null) gm.StartMatch();
    }

    private void Pause()
    {
        _paused = true;
        Time.timeScale = 0f;
        SetPlayerInputEnabled(false);
        ShowCursor(true);
        ScreenBlur.Request(this, true);              // the world behind goes out of focus...
        if (HUD.Instance != null) HUD.Instance.SetHidden(true);   // ...and the HUD fades away

        OpenModal("PAUSA", panel =>
        {
            MenuButton(panel, "CONTINUAR", new Vector2(0, 150), Pink, Resume);
            MenuButton(panel, "AJUSTES", new Vector2(0, 40), Cyan, OpenSettings);
            MenuButton(panel, "MENÚ PRINCIPAL", new Vector2(0, -70), Violet, BackToMainMenu);
            MenuButton(panel, "SALIR DEL JUEGO", new Vector2(0, -180), Yellow, QuitGame);
        }, onClose: Resume);
    }

    private void Resume()
    {
        if (!_paused) return;
        _paused = false;
        if (_modal != null) { UiAnim.FadeOutAndDestroy(_modal, 0.16f); _modal = null; }
        ScreenBlur.Request(this, false);
        if (HUD.Instance != null) HUD.Instance.SetHidden(false);
        Time.timeScale = 1f;
        SetPlayerInputEnabled(true);
        ShowCursor(false);
    }

    /// <summary>Test hook (ScreenshotTool): open the pause menu mid-match, creating the menu object if the game was started with -nomenu.</summary>
    public static void DebugPause()
    {
        var m = FindFirstObjectByType<MainMenu>();
        if (m == null) m = new GameObject("MainMenu").AddComponent<MainMenu>();
        m._inMenu = false;
        BlocksStart = false;
        if (m._home != null) Destroy(m._home);
        if (m._menuCam != null) Destroy(m._menuCam.gameObject);
        SetHudVisible(true);
        m.Pause();
    }

    private void BackToMainMenu()
    {
        Time.timeScale = 1f;
        SceneManager.LoadScene(SceneManager.GetActiveScene().name);
    }

    private static void QuitGame()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    private void FindPlayer()
    {
        _player = FindFirstObjectByType<PlayerController>();
        if (_player == null) return;
        _playerInput = _player.GetComponent<PlayerInput>();
        _weaponAim = _player.GetComponent<WeaponAim>();
    }

    // Disabling PlayerInput (rather than ignoring input) stops mouse movement over the menu from
    // being queued as camera turns that would all apply the moment the match starts.
    private void SetPlayerInputEnabled(bool enabled)
    {
        if (_playerInput != null) _playerInput.enabled = enabled;
    }

    private static void SetHudVisible(bool visible)
    {
        if (HUD.Instance != null) HUD.Instance.gameObject.SetActive(visible);
    }

    private static void ShowCursor(bool show)
    {
        Cursor.lockState = show ? CursorLockMode.None : CursorLockMode.Locked;
        Cursor.visible = show;
    }

    private void ApplySavedSettings()
    {
        if (PlayerPrefs.HasKey(PrefSensitivity)) ApplySensitivity(PlayerPrefs.GetFloat(PrefSensitivity));
        if (PlayerPrefs.HasKey(PrefVolume)) AudioListener.volume = PlayerPrefs.GetFloat(PrefVolume);
    }

    private void ApplySensitivity(float value)
    {
        if (_weaponAim != null) _weaponAim.SetBaseSensitivity(value);
        else if (_player != null) _player.lookSensitivity = value;
    }

    private static void EnsureEventSystem()
    {
        var es = EventSystem.current != null ? EventSystem.current : FindFirstObjectByType<EventSystem>();
        if (es == null) es = new GameObject("EventSystem").AddComponent<EventSystem>();

        // The project uses the new Input System; the legacy module would throw on every click.
        if (es.GetComponent<InputSystemUIInputModule>() == null)
        {
            foreach (var legacy in es.GetComponents<StandaloneInputModule>()) Destroy(legacy);
            var module = es.gameObject.AddComponent<InputSystemUIInputModule>();
            module.AssignDefaultActions();
        }
    }

    // ══════════════════════════════════════════════════════════════════════
    // Backdrop camera
    // ══════════════════════════════════════════════════════════════════════

    private void BuildMenuCamera()
    {
        var go = new GameObject("MenuCamera");
        _menuCam = go.AddComponent<Camera>();
        _menuCam.clearFlags = CameraClearFlags.Skybox;
        _menuCam.depth = 100f;                 // draws over the game's own camera
        _menuCam.fieldOfView = 50f;
        _menuCam.nearClipPlane = 0.1f;
        _menuCam.farClipPlane = 3000f;
        PositionMenuCamera();
    }

    private bool _snapped;

    /// <summary>
    /// The player starts a few metres above the ground, tilted: with the game paused he would hang in
    /// mid-air at an angle behind the menu. Stand him up on the planet's surface (once) so the backdrop
    /// looks right — and the match then starts with him already standing there.
    /// </summary>
    private void SnapPlayerToSurface()
    {
        if (_snapped || _player == null || GravitySystem.Instance == null) return;

        Vector3 p = _player.transform.position;
        var planet = GravitySystem.Instance.GetDominantPlanet(p);
        if (planet == null) return;          // planets register in their Start(); try again next frame

        Vector3 up = planet.GetSurfaceUp(p);
        Vector3 fwd = Vector3.ProjectOnPlane(_player.transform.forward, up);
        if (fwd.sqrMagnitude < 0.001f) fwd = Vector3.ProjectOnPlane(_player.transform.right, up);

        Vector3 pos = planet.GetSurfacePoint(up) + up * 1.05f;   // capsule is 2 m tall, pivot at its centre
        Quaternion rot = Quaternion.LookRotation(fwd.normalized, up);

        _player.transform.SetPositionAndRotation(pos, rot);
        var rb = _player.GetComponent<Rigidbody>();
        if (rb != null)
        {
            rb.position = pos;
            rb.rotation = rot;
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }
        Physics.SyncTransforms();
        _snapped = true;
    }

    /// <summary>Hero seen from behind, camera swaying slowly left and right over the planet's surface.</summary>
    private void PositionMenuCamera()
    {
        _camTime += Time.unscaledDeltaTime;

        if (_player == null)
        {
            float a = _camTime * 0.08f;
            _menuCam.transform.position = new Vector3(Mathf.Sin(a) * 170f, 45f, 60f + Mathf.Cos(a) * 170f);
            _menuCam.transform.LookAt(new Vector3(0f, 0f, 60f));
            return;
        }

        Vector3 p = _player.transform.position;
        var planet = GravitySystem.Instance != null ? GravitySystem.Instance.GetDominantPlanet(p) : null;
        Vector3 up = planet != null ? planet.GetSurfaceUp(p) : Vector3.up;

        Vector3 fwd = Vector3.ProjectOnPlane(_player.transform.forward, up);
        if (fwd.sqrMagnitude < 0.001f) fwd = Vector3.ProjectOnPlane(_player.transform.right, up);
        fwd.Normalize();

        float sway = Mathf.Sin(_camTime * 0.20f) * 28f;
        Vector3 back = Quaternion.AngleAxis(sway, up) * (-fwd);

        // Always aim at the hero (slightly above his head, so he sits low and centred between the cards)
        // — aiming ahead of him made him slide to one side as the camera swayed.
        // Geometry: 6.5 m back, 1.3 m up, aiming 1.8 m above his centre puts his head ~5° and his feet ~24°
        // below the view axis — inside the 25° half-FOV, so he is fully visible low on the screen.
        Vector3 camPos = p + back * 6.5f + up * 1.3f;
        Vector3 target = p + up * 1.8f;
        _menuCam.transform.SetPositionAndRotation(camPos, Quaternion.LookRotation(target - camPos, up));
    }
}
}
