using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

namespace OrbitRush
{

/// <summary>
/// "Where do you want to land?" flow, shown before every match and after every death. Three steps:
///   1. A planet-select screen (Apex-style cards: live render of each planet + its traits).
///   2. A satellite view of the chosen planet: drag to turn the globe, wheel to zoom, click to mark the drop point.
///   3. A Helldivers-style drop: the ship in orbit launches a pod, the camera follows it through the atmosphere to the impact.
/// Built entirely in code, runs on unscaled time (works while the game is paused in the main menu).
/// The caller gets the final position / rotation through <c>onLanded</c> and places the player there.
/// </summary>
public partial class DropSelector : MonoBehaviour
{
    public static bool Active { get; private set; }

    static readonly Color White  = Color.white;
    static readonly Color Soft   = new Color(1f, 1f, 1f, 0.63f);
    static readonly Color Faint  = new Color(1f, 1f, 1f, 0.28f);
    static readonly Color Cyan   = new Color32(0x4F, 0xD6, 0xFF, 255);
    static readonly Color Danger = new Color32(0xFF, 0x5A, 0x3C, 255);
    static readonly Color Orange = new Color32(0xFF, 0x9A, 0x2E, 255);

    private Transform _player;
    private Action<Vector3, Quaternion> _onLanded;
    private Action _onCancel;
    private bool _lockCursorAtEnd = true;

    private List<PlanetGravity> _planets;
    private PlanetGravity _planet;
    private Camera _cam;
    private Canvas _canvas;
    private Image _fade;
    private Light _light;
    private SunLight _sunLight;
    private Quaternion _lightRot;
    private bool _lightSaved;
    private Vector3 _sunPos;
    private bool _hudHidden;
    private bool _cancelled;
    private Transform _world;            // 3D props of the selection / drop (destroyed with this object)

    private Vector3 _target, _targetUp;
    private bool _hasTarget;

    // ══════════════════════════════════════════════════════════════════════
    // Entry points
    // ══════════════════════════════════════════════════════════════════════

    /// <summary>All planets the player can drop onto (active, sphere-shaped, not tiny).</summary>
    public static List<PlanetGravity> FindPlanets()
    {
        var list = new List<PlanetGravity>();
        foreach (var p in FindObjectsByType<PlanetGravity>(FindObjectsSortMode.None))
            if (p != null && p.isActiveAndEnabled && p.radius > 10f && p.shape == PlanetGravity.GravityShape.Sphere) list.Add(p);
        list.Sort((a, b) => string.CompareOrdinal(a.planetName, b.planetName));
        return list;
    }

    public static bool CanRun => !Active && FindPlanets().Count > 0;

    /// <summary>
    /// Starts the flow. <paramref name="onLanded"/> is called with the spot where the player must stand once the pod has hit the ground.
    /// <paramref name="onCancel"/> (optional) lets ESC on the first screen back out (main menu); without it the player must pick a spot (respawn).
    /// </summary>
    public static void Begin(Transform player, Action<Vector3, Quaternion> onLanded, Action onCancel = null, bool lockCursorAtEnd = true)
    {
        if (Active) return;
        var go = new GameObject("DropSelector");
        var d = go.AddComponent<DropSelector>();
        d._player = player; d._onLanded = onLanded; d._onCancel = onCancel; d._lockCursorAtEnd = lockCursorAtEnd;
        Active = true;
    }

    /// <summary>Moves the player to the landing spot (same handling as a respawn).</summary>
    public static void PlacePlayer(Transform player, Vector3 pos, Quaternion rot)
    {
        if (player == null) return;
        player.SetPositionAndRotation(pos, rot);
        var rb = player.GetComponent<Rigidbody>();
        if (rb != null)
        {
            rb.isKinematic = false;
            rb.position = pos; rb.rotation = rot;
            rb.linearVelocity = Vector3.zero; rb.angularVelocity = Vector3.zero;
        }
        Physics.SyncTransforms();
    }

    void Start() => StartCoroutine(Run());

    void OnDestroy()
    {
        Active = false;
        if (_cam != null) Destroy(_cam.gameObject);
        if (_world != null) Destroy(_world.gameObject);
        foreach (var c in _cards) if (c.tex != null) c.tex.Release();
        if (_sunLight != null) _sunLight.enabled = true;
        if (_hudHidden && HUD.Instance != null) HUD.Instance.SetHidden(false);
    }

    // ══════════════════════════════════════════════════════════════════════
    // Flow
    // ══════════════════════════════════════════════════════════════════════

    private IEnumerator Run()
    {
        _planets = FindPlanets();
        if (_planets.Count == 0) { Cancel(); yield break; }

        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        if (HUD.Instance != null && HUD.Instance.gameObject.activeInHierarchy) { HUD.Instance.SetHidden(true); _hudHidden = true; }

        _sunLight = FindFirstObjectByType<SunLight>();
        _sunPos = _sunLight != null ? _sunLight.sunPosition : Vector3.zero;
        if (_sunLight != null) _sunLight.enabled = false;      // it would turn the light back toward the player while we look at other planets
        _light = RenderSettings.sun;
        if (_light == null)
            foreach (var l in FindObjectsByType<Light>(FindObjectsSortMode.None))
                if (l.type == LightType.Directional) { _light = l; break; }
        if (_light != null) { _lightRot = _light.transform.rotation; _lightSaved = true; }

        _world = new GameObject("DropWorld").transform;
        BuildCanvas();
        BuildCamera();

        _fade.color = Color.black;
        int startIndex = 0;
        while (true)
        {
            // 1. planet cards
            _chosen = null;
            yield return SelectPlanetPhase(startIndex);
            if (_chosen == null) { Cancel(); yield break; }
            _planet = _chosen;
            startIndex = Mathf.Max(0, _planets.IndexOf(_planet));

            // 2. satellite view
            yield return FadeTo(1f, 0.22f);
            _satConfirmed = false;
            yield return SatellitePhase();
            if (_satConfirmed) break;
            yield return FadeTo(1f, 0.15f);
        }

        // 3. the drop
        yield return DropPhase();
        yield return Finish();
    }

    private IEnumerator Finish()
    {
        var pos = _finalPos; var rot = _finalRot;
        // Everything of the sequence goes away, the pod stays standing in the crater for a while
        if (_pod != null) { _pod.transform.SetParent(null, true); Destroy(_pod, 30f); _pod = null; }
        if (_cam != null) Destroy(_cam.gameObject);
        if (_world != null) Destroy(_world.gameObject);
        if (_dropUi != null) Destroy(_dropUi.gameObject);
        if (_satUi != null) Destroy(_satUi.gameObject);

        if (_sunLight != null) _sunLight.enabled = true;
        if (_hudHidden && HUD.Instance != null) { HUD.Instance.SetHidden(false); _hudHidden = false; }
        if (_lockCursorAtEnd) { Cursor.lockState = CursorLockMode.Locked; Cursor.visible = false; }

        try { _onLanded?.Invoke(pos, rot); }
        catch (Exception e) { Debug.LogException(e); }

        yield return null;
        yield return FadeTo(0f, 0.6f);
        Destroy(gameObject);
    }

    private void Cancel()
    {
        _cancelled = true;
        RestoreLight();
        if (_sunLight != null) _sunLight.enabled = true;
        if (_hudHidden && HUD.Instance != null) { HUD.Instance.SetHidden(false); _hudHidden = false; }
        try { _onCancel?.Invoke(); }
        catch (Exception e) { Debug.LogException(e); }
        Destroy(gameObject);
    }

    private void RestoreLight()
    {
        if (_light != null && _lightSaved) _light.transform.rotation = _lightRot;
    }

    // ══════════════════════════════════════════════════════════════════════
    // Canvas + camera
    // ══════════════════════════════════════════════════════════════════════

    private void BuildCanvas()
    {
        _canvas = gameObject.AddComponent<Canvas>();
        _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _canvas.sortingOrder = 60;                       // above HUD (10), scope (15) and the death screen (40)
        var cs = gameObject.AddComponent<CanvasScaler>();
        cs.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        cs.referenceResolution = new Vector2(1920, 1080);
        cs.matchWidthOrHeight = 0.5f;

        // The black layer used for the transitions sits above everything else
        var fadeGo = Stretch("Fade", transform);
        _fade = fadeGo.gameObject.AddComponent<Image>();
        _fade.color = Color.black; _fade.raycastTarget = false;
        fadeGo.SetAsLastSibling();
    }

    private void BuildCamera()
    {
        var go = new GameObject("DropCamera");
        _cam = go.AddComponent<Camera>();
        _cam.depth = 100f;
        _cam.fieldOfView = 35f;
        _cam.nearClipPlane = 0.3f;
        _cam.farClipPlane = 30000f;
        _cam.allowHDR = true;
        _cam.clearFlags = RenderSettings.skybox != null ? CameraClearFlags.Skybox : CameraClearFlags.SolidColor;
        _cam.backgroundColor = new Color(0.01f, 0.015f, 0.04f);
        _cam.cullingMask = ~LayerMask.GetMask("UI");
        var data = _cam.GetUniversalAdditionalCameraData();
        data.renderPostProcessing = false;               // no blur volume from the pause / death screens
        _cam.enabled = false;
    }

    /// <summary>Lights the planet like the Sun does: day side toward the Sun.</summary>
    private Vector3 SunDirection(PlanetGravity p)
    {
        Vector3 d = _sunPos - p.transform.position;
        return d.sqrMagnitude < 1f ? Vector3.up : d.normalized;
    }

    private void LightFor(PlanetGravity p)
    {
        if (_light == null) return;
        _light.transform.rotation = Quaternion.LookRotation(-SunDirection(p));
    }

    private IEnumerator FadeTo(float alpha, float seconds)
    {
        _fade.transform.SetAsLastSibling();
        float from = _fade.color.a, t = 0f;
        while (t < seconds)
        {
            t += Time.unscaledDeltaTime;
            _fade.color = new Color(0f, 0f, 0f, Mathf.Lerp(from, alpha, Mathf.Clamp01(t / seconds)));
            yield return null;
        }
        _fade.color = new Color(0f, 0f, 0f, alpha);
    }

    // ══════════════════════════════════════════════════════════════════════
    // Planet info
    // ══════════════════════════════════════════════════════════════════════

    private struct Info
    {
        public string name, biome, desc, sizeTxt, gravTxt, threatTxt, lootTxt;
        public float size, grav, threat, loot;
        public int enemies;
    }

    private Info Describe(PlanetGravity p)
    {
        var gm = GameManager.Instance;
        float maxR = 1f;
        foreach (var q in _planets) maxR = Mathf.Max(maxR, q.radius);

        int live = 0;
        foreach (var h in FindObjectsByType<EnemyHome>(FindObjectsSortMode.None)) if (h != null && h.planet == p) live++;
        int predicted = gm != null ? (gm.enemiesPerPlanet + gm.dronesPerPlanet + gm.zombiesPerPlanet) * p.EnemyScale : 6 * p.EnemyScale;

        var i = new Info();
        i.name = p.planetName.ToUpperInvariant();
        i.enemies = live > 0 ? live : predicted;
        i.size = Mathf.Clamp01(p.radius / maxR);
        i.sizeTxt = $"{p.radius:0} m";
        i.grav = Mathf.Clamp01(p.gravityStrength / 25f);
        i.gravTxt = $"{p.gravityStrength / 9.81f:0.0} G";
        i.threat = Mathf.Clamp01(i.enemies / 24f);
        i.threatTxt = i.enemies < 8 ? "BAJA" : i.enemies < 14 ? "MEDIA" : i.enemies < 20 ? "ALTA" : "EXTREMA";
        i.loot = Mathf.Clamp01(p.PickupCopies / 6f);
        i.lootTxt = $"x{p.PickupCopies}";

        switch (p.biome)
        {
            case PlanetGravity.BiomeType.Forest: i.biome = "BOSQUE";  i.desc = "Vegetación densa y mucha cobertura. Visibilidad corta, combates a quemarropa."; break;
            case PlanetGravity.BiomeType.Desert: i.biome = "DESIERTO"; i.desc = "Dunas abiertas y chatarra oxidada. Largas líneas de tiro y poca sombra."; break;
            case PlanetGravity.BiomeType.Ruins:  i.biome = "RUINAS";  i.desc = "Ciudad destruida. Combate vertical entre escombros y edificios derrumbados."; break;
            case PlanetGravity.BiomeType.Ice:    i.biome = "HIELO";   i.desc = "Superficie helada y despejada. Cualquier movimiento se ve de lejos."; break;
            case PlanetGravity.BiomeType.Volcanic: i.biome = "VOLCÁNICO"; i.desc = "Complejo industrial sobre roca caliente. Terreno quebrado y torres."; break;
            case PlanetGravity.BiomeType.Alien:  i.biome = "NEÓN";    i.desc = "Distrito iluminado de noche eterna. Calles estrechas y mucho ruido."; break;
            default:                             i.biome = "ROCOSO";  i.desc = "Mundo árido y desconocido. Sin información detallada."; break;
        }
        return i;
    }

    // ══════════════════════════════════════════════════════════════════════
    // Shared input helpers
    // ══════════════════════════════════════════════════════════════════════

    private static Vector2 MousePos()
    {
        var m = Mouse.current;
        return m != null ? m.position.ReadValue() : new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
    }

    private static bool Over(RectTransform rt, Vector2 screen) =>
        rt != null && rt.gameObject.activeInHierarchy && RectTransformUtility.RectangleContainsScreenPoint(rt, screen, null);

    private static bool ConfirmPressed()
    {
        var kb = Keyboard.current; var pad = Gamepad.current;
        return (kb != null && (kb.enterKey.wasPressedThisFrame || kb.spaceKey.wasPressedThisFrame))
            || (pad != null && pad.buttonSouth.wasPressedThisFrame);
    }

    private static bool BackPressed()
    {
        var kb = Keyboard.current; var pad = Gamepad.current;
        return (kb != null && kb.escapeKey.wasPressedThisFrame) || (pad != null && pad.buttonEast.wasPressedThisFrame);
    }
}
}
