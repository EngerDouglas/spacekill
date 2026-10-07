using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace OrbitRush
{

/// <summary>Main-menu modal panels (maps, armory, settings...).</summary>
public partial class MainMenu
{
    // ══════════════════════════════════════════════════════════════════════
    // Modal panels
    // ══════════════════════════════════════════════════════════════════════

    private void CloseModal()
    {
        if (_modal != null) { UiAnim.FadeOutAndDestroy(_modal, 0.16f); _modal = null; }
    }

    private void OpenModal(string title, System.Action<Transform> fill, System.Action onClose = null)
    {
        CloseModal();

        var dim = NewRect("Modal", _canvasRoot, CC, CC, Vector2.zero, Vector2.zero);
        dim.anchorMin = Vector2.zero; dim.anchorMax = Vector2.one; dim.offsetMin = Vector2.zero; dim.offsetMax = Vector2.zero;
        var dimImg = dim.gameObject.AddComponent<Image>();
        dimImg.color = new Color(0f, 0f, 0.03f, 0.62f);
        dimImg.raycastTarget = true;      // swallow clicks so the home screen behind isn't pressed
        _modal = dim.gameObject;
        UiAnim.Intro(dim, 0f, 0.22f, Vector2.zero, 1f);          // the dimmer fades in

        var panel = Box(dim, "Panel", CC, CC, Vector2.zero, new Vector2(980f, 700f), NavyDeep, Pink, 3f);
        UiAnim.Intro(panel.rectTransform, 0.04f, 0.4f, new Vector2(0f, -50f), 0.9f);
        Label(panel.transform, title, 54, White, TextAnchor.MiddleCenter, TC, TC, new Vector2(0f, -22f), new Vector2(900f, 70f), FontStyle.BoldAndItalic, outline: Pink);

        var close = Box(panel.transform, "Close", TR, TR, new Vector2(-18f, -18f), new Vector2(64f, 56f), Color.Lerp(NavyDeep, Pink, 0.4f), Pink);
        Label(close.transform, "X", 34, White, TextAnchor.MiddleCenter, CC, CC, Vector2.zero, new Vector2(64f, 56f));
        MakeClickable(close, onClose != null ? onClose : CloseModal);

        fill(panel.transform);

        // Title, buttons and rows pop in one after another
        UiAnim.Stagger(panel.transform, 0.045f, 0.4f, 0.16f, 60f);
    }

    private void OpenMaps()
    {
        OpenModal("MODO DE JUEGO", panel =>
        {
            Label(panel, "Elige cómo quieres jugar. Se aplica al pulsar JUGAR.", 22, Muted, TextAnchor.MiddleCenter, TC, TC, new Vector2(0f, -92f), new Vector2(900f, 30f), FontStyle.Normal);
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
                Color accent = selected ? Pink : Cyan;
                var btn = Box(panel, "Mode_" + i, CC, CC, new Vector2(0f, 90f - i * 150f), new Vector2(860f, 126f),
                              selected ? Color.Lerp(NavyDeep, Pink, 0.4f) : Navy, accent);
                Label(btn.transform, (selected ? "► " : "") + ModeName(i).ToUpper(), 32, White, TextAnchor.UpperLeft, TL, TL, new Vector2(28f, -18f), new Vector2(800f, 40f), FontStyle.BoldAndItalic);
                Label(btn.transform, desc[i], 21, Muted, TextAnchor.UpperLeft, TL, TL, new Vector2(28f, -66f), new Vector2(800f, 48f), FontStyle.Normal);
                MakeClickable(btn, () =>
                {
                    _modeIndex = index;
                    if (_mapsSubtitle != null) _mapsSubtitle.text = ModeSubtitle();
                    OpenMaps();     // rebuild with the new selection
                });
            }
        });
    }

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
            };
            Color[] colors = { Yellow, Pink, Cyan, Violet, Pink };
            for (int i = 0; i < 5; i++)
            {
                var row = Box(panel, "Weapon_" + i, CC, CC, new Vector2(0f, 200f - i * 100f), new Vector2(880f, 88f), Navy, colors[i]);
                Label(row.transform, weapons[i, 0], 28, White, TextAnchor.UpperLeft, TL, TL, new Vector2(22f, -10f), new Vector2(780f, 36f), FontStyle.BoldAndItalic);
                Label(row.transform, weapons[i, 1], 19, Muted, TextAnchor.UpperLeft, TL, TL, new Vector2(22f, -48f), new Vector2(840f, 30f), FontStyle.Normal);
            }
            Label(panel, "Recoge armas del suelo para equiparlas (teclas 1 / 2 para cambiar).\nGranadas: Q · Bomba especial: F", 21, Cyan, TextAnchor.MiddleCenter,
                  BC, BC, new Vector2(0f, 28f), new Vector2(900f, 70f), FontStyle.Normal);
        });
    }

    private void OpenSettings()
    {
        // Settings can be opened from the pause menu, which is itself a modal: keep the pause state.
        bool fromPause = _paused;
        OpenModal("AJUSTES", panel =>
        {
            float sens = _player != null ? Mathf.Clamp(PlayerPrefs.GetFloat(PrefSensitivity, _player.lookSensitivity), 0.04f, 0.4f) : 0.15f;
            Label(panel, "Sensibilidad del ratón", 26, White, TextAnchor.MiddleLeft, TL, TL, new Vector2(60f, -130f), new Vector2(500f, 36f));
            MakeSlider(panel, new Vector2(60f, -176f), new Vector2(860f, 36f), 0.04f, 0.4f, sens, v =>
            {
                PlayerPrefs.SetFloat(PrefSensitivity, v);
                ApplySensitivity(v);
            });

            Label(panel, "Volumen", 26, White, TextAnchor.MiddleLeft, TL, TL, new Vector2(60f, -250f), new Vector2(500f, 36f));
            MakeSlider(panel, new Vector2(60f, -296f), new Vector2(860f, 36f), 0f, 1f, AudioListener.volume, v =>
            {
                AudioListener.volume = v;
                PlayerPrefs.SetFloat(PrefVolume, v);
            });

            MenuButton(panel, Screen.fullScreen ? "PANTALLA COMPLETA: SÍ" : "PANTALLA COMPLETA: NO", new Vector2(0f, -40f), Cyan, () =>
            {
                Screen.fullScreenMode = Screen.fullScreen ? FullScreenMode.Windowed : FullScreenMode.FullScreenWindow;
                OpenSettings();
            }, 760f, 78f);
            MenuButton(panel, "SALIR DEL JUEGO", new Vector2(0f, -140f), Yellow, QuitGame, 760f, 78f);
            Label(panel, "Controles: WASD mover · Espacio saltar · Ctrl jetpack · Alt dash · Q/F granadas · Esc pausa",
                  18, Muted, TextAnchor.MiddleCenter, BC, BC, new Vector2(0f, 24f), new Vector2(940f, 28f), FontStyle.Normal);
        }, onClose: fromPause ? (System.Action)Pause : CloseModal);
    }

    private Slider MakeSlider(Transform parent, Vector2 pos, Vector2 size, float min, float max, float value, UnityEngine.Events.UnityAction<float> onChange)
    {
        var rt = NewRect("Slider", parent, TL, TL, pos, size);
        var slider = rt.gameObject.AddComponent<Slider>();

        var back = Box(rt, "Background", CC, CC, Vector2.zero, new Vector2(size.x, 14f), new Color(0.12f, 0.08f, 0.25f, 1f), Cyan, 1.5f);
        back.rectTransform.anchorMin = new Vector2(0f, 0.5f); back.rectTransform.anchorMax = new Vector2(1f, 0.5f);
        back.rectTransform.offsetMin = new Vector2(0f, -7f); back.rectTransform.offsetMax = new Vector2(0f, 7f);

        var fillArea = NewRect("Fill Area", rt, CC, CC, Vector2.zero, Vector2.zero);
        fillArea.anchorMin = new Vector2(0f, 0.5f); fillArea.anchorMax = new Vector2(1f, 0.5f);
        fillArea.offsetMin = new Vector2(0f, -7f); fillArea.offsetMax = new Vector2(0f, 7f);
        var fill = Box(fillArea, "Fill", CC, CC, Vector2.zero, Vector2.zero, Pink);
        fill.rectTransform.anchorMin = new Vector2(0f, 0f); fill.rectTransform.anchorMax = new Vector2(0f, 1f);
        fill.rectTransform.offsetMin = Vector2.zero; fill.rectTransform.offsetMax = new Vector2(8f, 0f);

        var handleArea = NewRect("Handle Slide Area", rt, CC, CC, Vector2.zero, Vector2.zero);
        handleArea.anchorMin = Vector2.zero; handleArea.anchorMax = Vector2.one;
        handleArea.offsetMin = new Vector2(14f, 0f); handleArea.offsetMax = new Vector2(-14f, 0f);
        var handle = Box(handleArea, "Handle", CC, CC, Vector2.zero, new Vector2(28f, 44f), White, Pink);
        handle.rectTransform.anchorMin = new Vector2(0f, 0f); handle.rectTransform.anchorMax = new Vector2(0f, 1f);
        handle.rectTransform.sizeDelta = new Vector2(28f, 0f);
        handle.raycastTarget = true;

        slider.fillRect = fill.rectTransform;
        slider.handleRect = handle.rectTransform;
        slider.targetGraphic = handle;
        slider.direction = Slider.Direction.LeftToRight;
        slider.minValue = min;
        slider.maxValue = max;
        slider.SetValueWithoutNotify(Mathf.Clamp(value, min, max));
        slider.onValueChanged.AddListener(onChange);
        return slider;
    }
}
}
