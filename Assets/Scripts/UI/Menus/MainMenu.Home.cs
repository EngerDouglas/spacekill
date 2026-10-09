using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace OrbitRush
{

/// <summary>Main-menu home screen (Apex look: left-aligned text menu over the blurred world).</summary>
public partial class MainMenu
{
    // ══════════════════════════════════════════════════════════════════════
    // Home screen
    // ══════════════════════════════════════════════════════════════════════

    private void BuildHome()
    {
        _home = NewRect("Home", _canvasRoot, CC, CC, Vector2.zero, Vector2.zero).gameObject;
        Stretch((RectTransform)_home.transform);
        Transform h = _home.transform;

        _pending.Clear();
        _mapsSubtitle = BuildColumn(h, "ORBIT RUSH", ModeSubtitle(),
            new[] { "JUGAR", "MODO DE JUEGO", "ARMERÍA", "AJUSTES", "SALIR" },
            new System.Action[] { () => Play("JUGAR button"), OpenMaps, OpenArmory, OpenSettings, QuitGame },
            "ENTER  jugar      ·      ↑ ↓  moverse      ·      ESC  pausa durante la partida");
        _homeItems = new List<MenuItemFx>(_pending);
        _pending.Clear();

        _toast = Label(h, "", 22, White, TextAnchor.MiddleCenter, BC, BC, new Vector2(0f, 150f), new Vector2(900f, 40f), _fontSemi);
        _toast.color = new Color(1f, 1f, 1f, 0f);
        Label(h, "v0.1 prueba", 13, Soft, TextAnchor.MiddleRight, BR, BR, new Vector2(-56f, 48f), new Vector2(300f, 20f));

        UiAnim.Stagger(h, 0.05f, 0.5f, 0.08f, 40f, gentle: true);
        SetItems(_homeItems);
    }

    private string ModeSubtitle() => "Modo: " + ModeName(_modeIndex);

    private static string ModeName(int i)
    {
        switch (Modes[i])
        {
            case GameManager.GameMode.TeamDeathmatch: return "Equipos (Rosa vs Cian)";
            case GameManager.GameMode.PlanetCapture:  return "Captura de planetas";
            default:                                  return "Todos contra todos";
        }
    }
}
}
