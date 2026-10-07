using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace OrbitRush
{

/// <summary>Main-menu home screen.</summary>
public partial class MainMenu
{
    // ══════════════════════════════════════════════════════════════════════
    // Home screen
    // ══════════════════════════════════════════════════════════════════════

    private void BuildHome()
    {
        _home = NewRect("Home", _canvasRoot, CC, CC, Vector2.zero, Vector2.zero).gameObject;
        var rt = (RectTransform)_home.transform;
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
        Transform h = _home.transform;

        // Soft vignette so the neon UI reads over any background.
        Box(h, "ShadeLeft", ML, ML, Vector2.zero, new Vector2(520f, 2200f), new Color(0.01f, 0.01f, 0.06f, 0.35f));

        BuildLogo(h);
        BuildProfile(h);
        BuildTopRight(h);

        // Left navigation
        string[,] nav =
        {
            { "INICIO", "♦" }, { "MAPAS", "▲" }, { "ARMAS", "■" }, { "PERSONAJE", "☺" },
            { "OBJETOS", "♥" }, { "LOGROS", "☼" }, { "CONFIGURACIÓN", "♠" },
        };
        System.Action[] navActions =
        {
            () => { },
            OpenMaps, OpenArmory,
            () => Toast("PERSONAJE: PRÓXIMAMENTE"), () => Toast("OBJETOS: PRÓXIMAMENTE"), () => Toast("LOGROS: PRÓXIMAMENTE"),
            OpenSettings,
        };
        for (int i = 0; i < 7; i++)
            NavItem(h, nav[i, 0], nav[i, 1], -20f + (3 - i) * 86f - 30f, i == 0, navActions[i]);

        // Centre cards
        Card(h, "TORRE CENTRAL", "Misiones y progreso", TC, TC, new Vector2(0f, -150f), Pink, "♠", () => Toast("TORRE CENTRAL: PRÓXIMAMENTE"));
        _mapsSubtitle = Card(h, "MAPAS", ModeSubtitle(), ML, ML, new Vector2(450f, 130f), Cyan, "▲", OpenMaps);
        Card(h, "TIENDA", "Objetos y cosméticos", ML, ML, new Vector2(450f, -120f), Violet, "♥", () => Toast("TIENDA: PRÓXIMAMENTE"));
        Card(h, "ARMERÍA", "Mejora y personaliza", MR, MR, new Vector2(-60f, 150f), Yellow, "■", OpenArmory);
        Card(h, "DESAFÍOS", "Completa y gana recompensas", MR, MR, new Vector2(-60f, -20f), Pink, "♦", () => Toast("DESAFÍOS: PRÓXIMAMENTE"));

        BuildMissionAndPlay(h);
        BuildPromo(h);

        // Everything flies in from its nearest edge, one piece after another
        UiAnim.Stagger(h, 0.035f, 0.5f, 0.1f, 110f);

        // Toast + version
        _toast = Label(h, "", 34, White, TextAnchor.MiddleCenter, BC, BC, new Vector2(0f, 290f), new Vector2(900f, 60f), FontStyle.BoldAndItalic, outline: Color.black);
        _toast.color = new Color(1f, 1f, 1f, 0f);
        Label(h, "ORBIT RUSH  ·  v0.1 prueba", 18, Muted, TextAnchor.LowerCenter, BC, BC, new Vector2(0f, 10f), new Vector2(600f, 30f), FontStyle.Normal);
    }

    private void BuildLogo(Transform h)
    {
        Label(h, "ORBIT", 120, White, TextAnchor.UpperLeft, TL, TL, new Vector2(50f, -22f), new Vector2(600f, 130f), FontStyle.BoldAndItalic, outline: Pink);
        Label(h, "RUSH", 120, Pink, TextAnchor.UpperLeft, TL, TL, new Vector2(120f, -118f), new Vector2(600f, 130f), FontStyle.BoldAndItalic, outline: White);
        Label(h, "SMALL PLANETS, BIG DREAMS", 22, Cyan, TextAnchor.UpperLeft, TL, TL, new Vector2(66f, -236f), new Vector2(560f, 30f), FontStyle.BoldAndItalic);
    }

    private void BuildProfile(Transform h)
    {
        var card = Box(h, "Profile", TL, TL, new Vector2(620f, -34f), new Vector2(530f, 100f), Navy, Pink);
        Tab(card.transform, "PERFIL", Pink);
        var avatar = HexBox(card.transform, "Avatar", ML, ML, new Vector2(14f, -4f), new Vector2(84f, 76f), Yellow);
        Label(avatar.transform, "U", 44, Yellow, TextAnchor.MiddleCenter, CC, CC, Vector2.zero, new Vector2(84f, 76f), FontStyle.BoldAndItalic);
        Label(card.transform, "JUGADOR_01", 28, White, TextAnchor.UpperLeft, TL, TL, new Vector2(112f, -20f), new Vector2(300f, 36f), FontStyle.BoldAndItalic);
        Label(card.transform, "NIV. 12", 18, Muted, TextAnchor.UpperLeft, TL, TL, new Vector2(112f, -62f), new Vector2(90f, 24f), FontStyle.Bold);
        Box(card.transform, "XpBack", TL, TL, new Vector2(204f, -68f), new Vector2(200f, 12f), new Color(0.12f, 0.08f, 0.25f, 1f));
        Box(card.transform, "XpFill", TL, TL, new Vector2(204f, -68f), new Vector2(200f * 2480f / 5000f, 12f), Pink);
        Label(card.transform, "2,480 / 5,000", 17, White, TextAnchor.MiddleLeft, TL, TL, new Vector2(412f, -62f), new Vector2(112f, 24f), FontStyle.Normal);
    }

    private void BuildTopRight(Transform h)
    {
        // Menu (hamburger) → settings / quit
        var menu = Box(h, "MenuBtn", TR, TR, new Vector2(-20f, -34f), new Vector2(76f, 64f), Navy, Pink);
        Label(menu.transform, "≡", 44, Pink, TextAnchor.MiddleCenter, CC, CC, Vector2.zero, new Vector2(76f, 64f));
        MakeClickable(menu, OpenSettings);

        var settings = Box(h, "SettingsBtn", TR, TR, new Vector2(-106f, -34f), new Vector2(76f, 64f), Navy, Cyan);
        Label(settings.transform, "☼", 38, Cyan, TextAnchor.MiddleCenter, CC, CC, Vector2.zero, new Vector2(76f, 64f));
        MakeClickable(settings, OpenSettings);

        var mail = Box(h, "MailBtn", TR, TR, new Vector2(-192f, -34f), new Vector2(76f, 64f), Navy, Cyan);
        Label(mail.transform, "@", 34, Cyan, TextAnchor.MiddleCenter, CC, CC, Vector2.zero, new Vector2(76f, 64f));
        var badge = Box(mail.transform, "Badge", TR, CC, new Vector2(-6f, -4f), new Vector2(28f, 28f), Pink);
        Label(badge.transform, "3", 18, White, TextAnchor.MiddleCenter, CC, CC, Vector2.zero, new Vector2(28f, 28f));
        MakeClickable(mail, () => Toast("MENSAJES: PRÓXIMAMENTE"));

        Chip(h, "♦", "4,320", Pink, -290f);
        Chip(h, "●", "1,250", Cyan, -448f);
        Chip(h, "☼", "36", Yellow, -606f);
    }

    private void Chip(Transform h, string icon, string value, Color accent, float x)
    {
        var chip = Box(h, "Chip_" + value, TR, TR, new Vector2(x, -34f), new Vector2(150f, 64f), Navy, accent);
        Label(chip.transform, icon, 28, accent, TextAnchor.MiddleCenter, ML, ML, new Vector2(6f, 0f), new Vector2(40f, 64f));
        Label(chip.transform, value, 28, White, TextAnchor.MiddleLeft, ML, ML, new Vector2(50f, 0f), new Vector2(96f, 64f), FontStyle.BoldAndItalic);
    }

    private void BuildMissionAndPlay(Transform h)
    {
        var card = Box(h, "Mission", BR, BR, new Vector2(-30f, 150f), new Vector2(620f, 230f), Navy, Cyan);
        Tab(card.transform, "MISIÓN ACTIVA", Cyan);
        Label(card.transform, "EL ORIGEN DE TODO", 34, White, TextAnchor.UpperLeft, TL, TL, new Vector2(26f, -34f), new Vector2(520f, 42f), FontStyle.BoldAndItalic);
        Label(card.transform, "Llega al núcleo del planeta flotante.", 20, Muted, TextAnchor.UpperLeft, TL, TL, new Vector2(26f, -80f), new Vector2(520f, 28f), FontStyle.Normal);

        var play = Box(card.transform, "PlayButton", BC, BC, new Vector2(0f, 18f), new Vector2(576f, 84f), Pink, White, 3f);
        Label(play.transform, "JUGAR", 52, White, TextAnchor.MiddleCenter, CC, CC, Vector2.zero, new Vector2(576f, 84f), FontStyle.BoldAndItalic, outline: new Color(0.45f, 0f, 0.25f));
        MakeClickable(play, () => Play("JUGAR button"));
        _playButton = play.rectTransform;

        var strip = Box(h, "Strip", BR, BR, new Vector2(-30f, 30f), new Vector2(620f, 84f), Navy, Cyan);
        StripButton(strip.transform, "AMIGOS", "☻", 0, () => Toast("AMIGOS: PRÓXIMAMENTE"));
        StripButton(strip.transform, "MENSAJES", "@", 1, () => Toast("MENSAJES: PRÓXIMAMENTE"));
        StripButton(strip.transform, "AJUSTES", "☼", 2, OpenSettings);
    }

    private void StripButton(Transform strip, string label, string icon, int index, System.Action onClick)
    {
        var rt = NewRect("Strip_" + label, strip, ML, ML, new Vector2(index * 206f + 4f, 0f), new Vector2(204f, 84f));
        var img = rt.gameObject.AddComponent<Image>();
        img.color = new Color(1f, 1f, 1f, 0.01f);   // practically invisible, but clickable
        Label(rt, icon, 28, Cyan, TextAnchor.MiddleCenter, ML, ML, new Vector2(8f, 0f), new Vector2(44f, 84f));
        Label(rt, label, 22, White, TextAnchor.MiddleLeft, ML, ML, new Vector2(58f, 0f), new Vector2(140f, 84f), FontStyle.Bold);
        MakeClickable(img, onClick);
    }

    private void BuildPromo(Transform h)
    {
        var promo = Box(h, "Promo", BL, BL, new Vector2(30f, 30f), new Vector2(680f, 150f), Navy, Pink);
        Tab(promo.transform, "OFERTA", Pink);
        var art = Box(promo.transform, "Art", ML, ML, new Vector2(16f, -4f), new Vector2(112f, 112f), Color.Lerp(NavyDeep, Violet, 0.5f), Yellow);
        Label(art.transform, "U", 70, Pink, TextAnchor.MiddleCenter, CC, CC, Vector2.zero, new Vector2(112f, 112f), FontStyle.BoldAndItalic);
        Label(promo.transform, "PAQUETE INICIAL", 30, White, TextAnchor.UpperLeft, TL, TL, new Vector2(150f, -28f), new Vector2(480f, 40f), FontStyle.BoldAndItalic);
        Label(promo.transform, "¡Consigue un impulso extra\nen tu aventura!", 20, Muted, TextAnchor.UpperLeft, TL, TL, new Vector2(150f, -72f), new Vector2(480f, 60f), FontStyle.Normal);
        Label(promo.transform, "►", 36, Pink, TextAnchor.MiddleCenter, MR, MR, new Vector2(-16f, 0f), new Vector2(40f, 60f));
        MakeClickable(promo, () => Toast("PAQUETE INICIAL: PRÓXIMAMENTE"));
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
