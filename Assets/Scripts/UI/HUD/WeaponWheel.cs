using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace OrbitRush
{

/// <summary>
/// Radial weapon selector. Hold Tab to open it, move the mouse (or the right stick) toward a weapon, release Tab to equip it.
/// Six fixed sectors, one per weapon kind; weapons the player isn't carrying are dimmed and can't be picked.
/// Built entirely in code under the HUD canvas, in the same Apex look as the HUD (thin arcs, white line art, cyan accent).
/// While open: the camera is frozen (<see cref="PlayerController.LookLocked"/>) and firing is blocked (<see cref="IsOpen"/>).
/// </summary>
public class WeaponWheel : MonoBehaviour
{
    /// <summary>True while the wheel is on screen (WeaponInventory ignores Fire while it is).</summary>
    public static bool IsOpen { get; private set; }

    struct Entry
    {
        public string name, file, sub;
        public Entry(string name, string file, string sub) { this.name = name; this.file = file; this.sub = sub; }
    }

    // Clockwise from the top, same order as the Pencil design.
    static readonly Entry[] Entries =
    {
        new Entry("Blaster",          "Blaster",         "Pistola de energía"),
        new Entry("Plasma Rifle",     "PlasmaRifle",     "Rifle de plasma"),
        new Entry("Space Shotgun",    "SpaceShotgun",    "Escopeta espacial"),
        new Entry("Gravity Sniper",   "GravitySniper",   "Francotirador gravitacional"),
        new Entry("Orbital Launcher", "OrbitalLauncher", "Lanzador orbital"),
        new Entry("Katana",           "Katana",          "Cuerpo a cuerpo"),
    };
    const int Count = 6;
    const float SectorSweep = 52f;     // degrees per segment (60° pitch, 8° gap)

    static readonly Color Cyan = new Color32(0x4F, 0xD6, 0xFF, 255);

    // Label anchors (canvas units from the wheel centre): top, upper right, lower right, bottom, lower left, upper left.
    static readonly Vector2[] LabelPos = { new Vector2(0, 327), new Vector2(260, 161), new Vector2(260, -159),
                                           new Vector2(0, -305), new Vector2(-260, -159), new Vector2(-260, 161) };
    static readonly TextAnchor[] LabelAnchor = { TextAnchor.MiddleCenter, TextAnchor.MiddleLeft, TextAnchor.MiddleLeft,
                                                 TextAnchor.MiddleCenter, TextAnchor.MiddleRight, TextAnchor.MiddleRight };

    Font _regular, _semi, _light;
    GameObject _root;
    CanvasGroup _group;
    HudArc[] _arcs = new HudArc[Count];
    Image[] _icons = new Image[Count];
    Text[] _labels = new Text[Count];
    Text _hub;
    bool[] _owned = new bool[Count];
    int _selected = -1;
    Vector2 _cursor;
    WeaponInventory _inventory;

    void Awake()
    {
        _regular = UiFactory.LoadFont();
        _semi = UiFactory.LoadFontSemiBold();
        _light = UiFactory.LoadFontLight();
        Build();
    }

    void OnDisable()
    {
        if (IsOpen)
        {
            IsOpen = false; PlayerController.LookLocked = false;
            ScreenBlur.Request(this, false); ScreenBlur.RequestStrong(this, false);
            if (HUD.Instance != null) HUD.Instance.SetWheelOpen(false);
        }
    }

    // ── Input ─────────────────────────────────────────────────────────────

    void Update()
    {
        var kb = Keyboard.current;
        if (kb == null) return;

        if (!IsOpen)
        {
            if (kb.tabKey.wasPressedThisFrame && CanOpen()) Open();
        }
        else if (kb.tabKey.wasReleasedThisFrame || Time.timeScale <= 0f)
        {
            Close(Time.timeScale > 0f);
        }
        else
        {
            UpdateSelection();
            RefreshHub();
        }

        float target = IsOpen ? 1f : 0f;
        _group.alpha = Mathf.MoveTowards(_group.alpha, target, Time.unscaledDeltaTime * 10f);
        if (!IsOpen && _group.alpha <= 0f && _root.activeSelf) _root.SetActive(false);
    }

    bool CanOpen() => Time.timeScale > 0f && Cursor.lockState == CursorLockMode.Locked && FindInventory() != null;

    WeaponInventory FindInventory()
    {
        if (_inventory == null)
        {
            var player = GameObject.FindWithTag("Player");
            if (player != null) _inventory = player.GetComponent<WeaponInventory>();
        }
        return _inventory;
    }

    void Open()
    {
        IsOpen = true;
        PlayerController.LookLocked = true;
        ScreenBlur.Request(this, true);          // the world goes out of focus behind the wheel...
        ScreenBlur.RequestStrong(this, true);    // ...a lot
        if (HUD.Instance != null) HUD.Instance.SetWheelOpen(true);   // and the HUD steps aside so it doesn't clutter the wheel
        _root.SetActive(true);
        _cursor = Vector2.zero;

        int active = -1;
        for (int i = 0; i < Count; i++)
        {
            _owned[i] = _inventory.IndexOfWeapon(Entries[i].name) >= 0;
            if (_inventory.ActiveWeapon != null && _inventory.IndexOfWeapon(Entries[i].name) == _inventory.ActiveIndex) active = i;
        }
        _selected = active;
        Refresh();
        RefreshHub();
    }

    void Close(bool equip)
    {
        if (equip && _selected >= 0 && _owned[_selected] && _inventory != null)
            _inventory.TryEquipByName(Entries[_selected].name);
        IsOpen = false;
        PlayerController.LookLocked = false;
        ScreenBlur.Request(this, false);
        ScreenBlur.RequestStrong(this, false);
        if (HUD.Instance != null) HUD.Instance.SetWheelOpen(false);
    }

    void UpdateSelection()
    {
        var mouse = Mouse.current;
        if (mouse != null) _cursor += mouse.delta.ReadValue() * 0.6f;

        var pad = Gamepad.current;
        if (pad != null)
        {
            Vector2 s = pad.rightStick.ReadValue();
            if (s.sqrMagnitude > 0.2f) _cursor = s * 220f;
        }
        _cursor = Vector2.ClampMagnitude(_cursor, 220f);

        if (_cursor.magnitude < 70f) return;       // dead zone: keep the current choice
        float ang = Mathf.Atan2(_cursor.x, _cursor.y) * Mathf.Rad2Deg;   // 0 = up, clockwise
        if (ang < 0f) ang += 360f;
        int idx = Mathf.RoundToInt(ang / 60f) % Count;
        if (_owned[idx] && idx != _selected) { _selected = idx; Refresh(); }
    }

    // ── Look ──────────────────────────────────────────────────────────────

    void Refresh()
    {
        for (int i = 0; i < Count; i++)
        {
            bool own = _owned[i], sel = i == _selected;
            float centre = i * 60f;

            if (sel)
            {
                _arcs[i].Set(280f, 260.4f, centre - SectorSweep * 0.5f, SectorSweep);
                _arcs[i].color = Cyan;
            }
            else
            {
                _arcs[i].Set(260f, 249.6f, centre - SectorSweep * 0.5f, SectorSweep);
                _arcs[i].color = new Color(1f, 1f, 1f, own ? 0.33f : 0.12f);
            }

            _icons[i].color = new Color(1f, 1f, 1f, own ? (sel ? 1f : 0.8f) : 0.22f);
            _labels[i].color = sel ? Cyan : new Color(1f, 1f, 1f, own ? 0.9f : 0.3f);
        }
    }

    void RefreshHub()
    {
        string name = "";
        string ammo = "";
        if (_selected >= 0 && _inventory != null)
        {
            name = Entries[_selected].name.ToUpper();
            int i = _inventory.IndexOfWeapon(Entries[_selected].name);
            var w = i >= 0 ? _inventory.Weapons[i] : null;
            if (w != null)
                ammo = w.IsMelee ? "—" : $"{w.currentAmmo} <size=26><color=#FFFFFF80>| {w.maxAmmo}</color></size>";
        }
        _hub.text = $"<size=14><color=#FFFFFFB3>{name}</color></size>\n<size=96>{ammo}</size>";
    }

    // ── Build ─────────────────────────────────────────────────────────────

    void Build()
    {
        var rootRt = UiFactory.NewRect("WeaponWheel", transform, Vector2.zero, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
        rootRt.anchorMin = Vector2.zero; rootRt.anchorMax = Vector2.one;
        rootRt.offsetMin = Vector2.zero; rootRt.offsetMax = Vector2.zero;
        _root = rootRt.gameObject;
        var dim = _root.AddComponent<Image>();
        dim.color = new Color(0f, 0f, 0f, 0.35f);
        dim.raycastTarget = false;
        // Own sorting layer above the HUD, and it ignores the HUD's CanvasGroup: the HUD fades out while the wheel is open
        var canvas = _root.AddComponent<Canvas>();
        canvas.overrideSorting = true;
        canvas.sortingOrder = 12;
        _group = _root.AddComponent<CanvasGroup>();
        _group.ignoreParentGroups = true;
        _group.alpha = 0f;
        _group.blocksRaycasts = false;

        var wheel = UiFactory.NewRect("Wheel", rootRt, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(860f, 860f));

        // Thin outer ring and inner guide ring
        MakeArc(wheel, "OuterRing", 292f, 291f, new Color(1f, 1f, 1f, 0.12f)).Set(292f, 291f, 0f, 360f);
        MakeArc(wheel, "InnerRing", 190f, 189f, new Color(1f, 1f, 1f, 0.12f)).Set(190f, 189f, 0f, 360f);

        for (int i = 0; i < Count; i++)
        {
            _arcs[i] = MakeArc(wheel, "Sector_" + Entries[i].name, 260f, 249.6f, Color.white);

            float a = i * 60f * Mathf.Deg2Rad;
            Vector2 iconPos = new Vector2(Mathf.Sin(a), Mathf.Cos(a)) * 200f;
            var icon = UiFactory.NewRect("Icon_" + Entries[i].name, wheel, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), iconPos, new Vector2(120f, 59f));
            var img = icon.gameObject.AddComponent<Image>();
            img.sprite = Resources.Load<Sprite>(ResourcePaths.HudArtFolder + "Weapons/" + Entries[i].file);
            img.preserveAspect = true;
            img.raycastTarget = false;
            _icons[i] = img;

            var label = UiFactory.NewRect("Label_" + Entries[i].name, wheel, new Vector2(0.5f, 0.5f), PivotFor(LabelAnchor[i]), LabelPos[i], new Vector2(220f, 44f));
            var t = label.gameObject.AddComponent<Text>();
            t.font = _semi; t.fontSize = 15; t.alignment = LabelAnchor[i];
            t.horizontalOverflow = HorizontalWrapMode.Overflow; t.verticalOverflow = VerticalWrapMode.Overflow;
            t.supportRichText = true; t.raycastTarget = false;
            t.text = $"{Entries[i].name.ToUpper()}\n<size=11><color=#FFFFFF80>{Entries[i].sub}</color></size>";
            _labels[i] = t;
        }

        // Centre: weapon name + ammo
        var hubRt = UiFactory.NewRect("Hub", wheel, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(300f, 300f));
        _hub = hubRt.gameObject.AddComponent<Text>();
        _hub.font = _light; _hub.fontSize = 14; _hub.alignment = TextAnchor.MiddleCenter;
        _hub.horizontalOverflow = HorizontalWrapMode.Overflow; _hub.verticalOverflow = VerticalWrapMode.Overflow;
        _hub.supportRichText = true; _hub.raycastTarget = false; _hub.color = Color.white;

        // Hint line at the bottom of the screen
        var hintRt = UiFactory.NewRect("Hint", rootRt, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 70f), new Vector2(900f, 24f));
        var hint = hintRt.gameObject.AddComponent<Text>();
        hint.font = _regular; hint.fontSize = 12; hint.alignment = TextAnchor.MiddleCenter;
        hint.horizontalOverflow = HorizontalWrapMode.Overflow; hint.raycastTarget = false;
        hint.color = new Color(1f, 1f, 1f, 0.63f);
        hint.text = "Mueve el ratón para elegir   ·   Suelta Tab para equipar";

        _root.SetActive(false);
    }

    static Vector2 PivotFor(TextAnchor a)
        => a == TextAnchor.MiddleLeft ? new Vector2(0f, 0.5f) : a == TextAnchor.MiddleRight ? new Vector2(1f, 0.5f) : new Vector2(0.5f, 0.5f);

    static HudArc MakeArc(RectTransform parent, string name, float outer, float inner, Color color)
    {
        var rt = UiFactory.NewRect(name, parent, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(860f, 860f));
        var arc = rt.gameObject.AddComponent<HudArc>();
        arc.raycastTarget = false;
        arc.color = color;
        arc.Set(outer, inner, -26f, 52f);
        return arc;
    }
}
}
