using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace OrbitRush
{

/// <summary>Menu overlays: pause menu, game-mode picker, armory and settings (Apex look: no panels, hairlines and text).</summary>
public partial class MainMenu
{
    // ══════════════════════════════════════════════════════════════════════
    // Overlays
    // ══════════════════════════════════════════════════════════════════════

    private void CloseModal()
    {
        if (_modal != null) { UiAnim.FadeOutAndDestroy(_modal, 0.16f); _modal = null; }
        _modalItems.Clear();
        SetItems(_inMenu ? _homeItems : null);
    }

    /// <summary>A full-screen dimmed overlay (it swallows clicks so the screen behind isn't pressed). Returns its root.</summary>
    private RectTransform NewOverlay(float dimAlpha)
    {
        CloseModal();
        var dim = NewRect("Modal", _canvasRoot, CC, CC, Vector2.zero, Vector2.zero);
        Stretch(dim);
        var img = dim.gameObject.AddComponent<Image>();
        img.color = new Color(0f, 0f, 0.02f, dimAlpha);
        img.raycastTarget = true;
        _modal = dim.gameObject;
        UiAnim.Intro(dim, 0f, 0.22f, Vector2.zero, 1f, true, true);      // the dimmer fades in
        return dim;
    }

    /// <summary>
    /// A centred content overlay: light title with a short cyan rule under it, a "ESC  CERRAR" entry at the top right, and the
    /// content placed by <paramref name="fill"/> inside a 1000×720 area (positions are relative to its top-centre).
    /// </summary>
    private void OpenModal(string title, System.Action<Transform> fill, System.Action onClose = null)
    {
        var dim = NewOverlay(0.55f);
        _pending.Clear();

        var area = NewRect("Content", dim, CC, CC, Vector2.zero, new Vector2(1000f, 720f));
        Label(area, title, 44, White, TextAnchor.MiddleCenter, TC, TC, new Vector2(0f, -20f), new Vector2(900f, 60f), _fontLight);
        Box(area, "Rule", TC, TC, new Vector2(0f, -88f), new Vector2(56f, 2f), Cyan);

        fill(area);

        var close = MenuItem(dim, "ESC  CERRAR", new Vector2(-300f, -70f), onClose != null ? onClose : CloseModal, 16, false, 260f, TR);
        close.label.alignment = TextAnchor.MiddleRight;

        _modalItems = new List<MenuItemFx>(_pending);
        _pending.Clear();
        SetItems(_modalItems);

        UiAnim.Stagger(area, 0.045f, 0.4f, 0.1f, 40f, gentle: true);
    }

    // ── Pause ─────────────────────────────────────────────────────────────

    private void OpenPauseScreen()
    {
        var dim = NewOverlay(0.30f);
        _pending.Clear();
        BuildColumn(dim, "PAUSA", ModeSubtitle(),
            new[] { "CONTINUAR", "AJUSTES", "MENÚ PRINCIPAL", "SALIR DEL JUEGO" },
            new System.Action[] { Resume, OpenSettings, BackToMainMenu, QuitGame },
            "ESC  continuar      ·      ↑ ↓  moverse      ·      ENTER  elegir");
        _modalItems = new List<MenuItemFx>(_pending);
        _pending.Clear();
        SetItems(_modalItems);
        UiAnim.Stagger(dim, 0.04f, 0.4f, 0.05f, 40f, gentle: true);
    }

    // ── Game mode ─────────────────────────────────────────────────────────

    private void OpenMaps()
    {
        OpenModal("MODO DE JUEGO", panel =>
        {
            Label(panel, "Elige cómo quieres jugar. Se aplica al pulsar JUGAR.", 16, Soft, TextAnchor.MiddleCenter, TC, TC, new Vector2(0f, -112f), new Vector2(900f, 24f));
            string[] desc =
            {
                "Todos contra todos: gana quien más bajas consiga.",
                "Equipos Rosa vs Cian. Sin fuego amistoso. Primero en 30 bajas.",
                "Conquista los planetas: cada uno suma puntos por segundo. Primero en 100.",
            };
            for (int i = 0; i < Modes.Length; i++)
            {
                int index = i;
                bool selected = i == _modeIndex;
                var row = NewRect("Mode_" + i, panel, CC, CC, new Vector2(0f, 70f - i * 130f), new Vector2(860f, 112f));
                var hit = row.gameObject.AddComponent<Image>();
                hit.color = new Color(1f, 1f, 1f, selected ? 0.05f : 0f);

                Box(row, "Marker", ML, ML, new Vector2(0f, 0f), new Vector2(3f, selected ? 56f : 24f), selected ? Cyan : Track);
                Label(row, ModeName(i).ToUpper(), 26, selected ? Cyan : White, TextAnchor.UpperLeft, TL, TL, new Vector2(28f, -22f), new Vector2(780f, 36f), _fontSemi);
                Label(row, desc[i], 17, Soft, TextAnchor.UpperLeft, TL, TL, new Vector2(28f, -62f), new Vector2(800f, 28f));
                Box(row, "Line", BL, BL, Vector2.zero, new Vector2(860f, 1f), Track);

                MakeClickable(hit, () =>
                {
                    _modeIndex = index;
                    if (_mapsSubtitle != null) _mapsSubtitle.text = ModeSubtitle();
                    OpenMaps();     // rebuild with the new selection
                });
            }
        });
    }

    // ── Armory ────────────────────────────────────────────────────────────

    private void OpenArmory()
    {
        OpenModal("ARMERÍA", panel =>
        {
            string[,] weapons =
            {
                { "BLASTER",          "Pistola de iones. Disparo único, fiable y rápido de apuntar." },
                { "SPACE SHOTGUN",    "Escopeta de iones: 8 perdigones por disparo. Letal de cerca." },
                { "PLASMA RIFLE",     "Rifle automático de plasma. Cadencia alta; se sobrecalienta." },
                { "GRAVITY SNIPER",   "Francotirador de precisión. 80 de daño; clic derecho para la mira." },
                { "ORBITAL LAUNCHER", "Lanzador de energía: el proyectil orbita el planeta y cae con 120 de daño." },
                { "KATANA",           "Arma cuerpo a cuerpo. Sin munición: ataque ligero, cargado y en esquiva." },
            };
            for (int i = 0; i < 6; i++)
            {
                var row = NewRect("Weapon_" + i, panel, CC, CC, new Vector2(0f, 190f - i * 86f), new Vector2(880f, 76f));
                Label(row, weapons[i, 0], 22, White, TextAnchor.UpperLeft, TL, TL, new Vector2(8f, -8f), new Vector2(780f, 30f), _fontSemi);
                Label(row, weapons[i, 1], 16, Soft, TextAnchor.UpperLeft, TL, TL, new Vector2(8f, -42f), new Vector2(860f, 24f));
                Box(row, "Line", BL, BL, Vector2.zero, new Vector2(880f, 1f), Track);
            }
            Label(panel, "Recoge armas del suelo para equiparlas. Mantén Tab para abrir la rueda de armas.\nGranadas: Q  ·  Bomba especial: F",
                  16, Cyan, TextAnchor.MiddleCenter, BC, BC, new Vector2(0f, 20f), new Vector2(900f, 56f));
        });
    }

    // ── Settings ──────────────────────────────────────────────────────────

    private void OpenSettings()
    {
        // Settings can be opened from the pause menu, which is itself an overlay: keep the pause state.
        bool fromPause = _paused;
        OpenModal("AJUSTES", panel =>
        {
            float sens = _player != null ? Mathf.Clamp(PlayerPrefs.GetFloat(PrefSensitivity, _player.lookSensitivity), 0.04f, 0.4f) : 0.15f;
            Label(panel, "Sensibilidad del ratón", 20, White, TextAnchor.MiddleLeft, TL, TL, new Vector2(60f, -140f), new Vector2(500f, 30f), _fontSemi);
            MakeSlider(panel, new Vector2(60f, -186f), new Vector2(880f, 36f), 0.04f, 0.4f, sens, v =>
            {
                PlayerPrefs.SetFloat(PrefSensitivity, v);
                ApplySensitivity(v);
            });

            Label(panel, "Volumen", 20, White, TextAnchor.MiddleLeft, TL, TL, new Vector2(60f, -260f), new Vector2(500f, 30f), _fontSemi);
            MakeSlider(panel, new Vector2(60f, -306f), new Vector2(880f, 36f), 0f, 1f, AudioListener.volume, v =>
            {
                AudioListener.volume = v;
                PlayerPrefs.SetFloat(PrefVolume, v);
            });

            MenuItem(panel, Screen.fullScreen ? "Pantalla completa:  SÍ" : "Pantalla completa:  NO", new Vector2(40f, -390f), () =>
            {
                Screen.fullScreenMode = Screen.fullScreen ? FullScreenMode.Windowed : FullScreenMode.FullScreenWindow;
                OpenSettings();
            }, 26, false, 700f, TL);
            MenuItem(panel, "Salir del juego", new Vector2(40f, -462f), QuitGame, 26, false, 700f, TL);

            Label(panel, "WASD mover  ·  Espacio saltar  ·  Alt dash  ·  C esquiva  ·  Q / F granadas  ·  Tab rueda de armas  ·  Esc pausa",
                  14, Soft, TextAnchor.MiddleCenter, BC, BC, new Vector2(0f, 20f), new Vector2(960f, 24f));
        }, onClose: fromPause ? (System.Action)Pause : CloseModal);
    }
}
}
