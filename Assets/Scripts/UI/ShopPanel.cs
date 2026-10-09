using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace OrbitRush
{

/// <summary>
/// Vending machine shop. Near a machine a prompt says "E · EXPENDEDORA"; E opens a panel (Apex look, like the HUD) listing
/// the weapons with their price in coins. W/S or the mouse pick a row, E / Enter / click buys, Esc closes.
/// Owning a weapon turns its row into an ammo refill for a quarter of the price. The world is blurred behind the panel and
/// player input is switched off while it is open (the game keeps running).
/// </summary>
public class ShopPanel : MonoBehaviour
{
    public static bool IsOpen { get; private set; }
    private static int _closedFrame = -10;
    /// <summary>True while open and on the frame it closed with Esc, so the pause menu doesn't open on the same key press.</summary>
    public static bool BlocksPause => IsOpen || _closedFrame == Time.frameCount;

    private struct Item
    {
        public WeaponSpawner.WeaponKind kind;
        public string name, blurb, definition;
        public int price;
        public System.Type type;
        public Color color;
    }

    private static readonly Item[] Items =
    {
        new Item { kind = WeaponSpawner.WeaponKind.Blaster,         name = "BLASTER",          blurb = "Pistola de energía fiable",            definition = "Blaster",        price = 60,  type = typeof(Blaster),         color = new Color32(0xFF, 0xD1, 0x66, 255) },
        new Item { kind = WeaponSpawner.WeaponKind.SpaceShotgun,    name = "SPACE SHOTGUN",    blurb = "Ráfaga de perdigones a corta distancia", definition = "SpaceShotgun",   price = 150, type = typeof(SpaceShotgun),    color = new Color32(0xFF, 0x5B, 0x5B, 255) },
        new Item { kind = WeaponSpawner.WeaponKind.PlasmaRifle,     name = "PLASMA RIFLE",     blurb = "Fuego automático de plasma",           definition = "PlasmaRifle",    price = 250, type = typeof(PlasmaRifle),     color = new Color32(0x4F, 0xD6, 0xFF, 255) },
        new Item { kind = WeaponSpawner.WeaponKind.GravitySniper,   name = "GRAVITY SNIPER",   blurb = "Un disparo, mucho daño, con mira",     definition = "GravitySniper",  price = 450, type = typeof(GravitySniper),   color = new Color32(0xA8, 0x6B, 0xFF, 255) },
        new Item { kind = WeaponSpawner.WeaponKind.OrbitalLauncher, name = "ORBITAL LAUNCHER", blurb = "Proyectil orbital con explosión",       definition = "OrbitalLauncher", price = 800, type = typeof(OrbitalLauncher), color = new Color32(0x6B, 0xE8, 0x7A, 255) },
    };

    static readonly Color Ink = new Color32(0x0B, 0x12, 0x20, 255);
    static readonly Color Cyan = new Color32(0x4F, 0xD6, 0xFF, 255);
    static readonly Color Amber = new Color32(0xFF, 0xD1, 0x66, 255);
    static readonly Color Alert = new Color32(0xFF, 0x5B, 0x5B, 255);
    static readonly Color Good = new Color32(0x6B, 0xE8, 0x7A, 255);

    private PlayerController _player;
    private WeaponInventory _inventory;
    private PlayerWallet _wallet;
    private PlayerStats _stats;
    private PlayerInput _playerInput;
    private VendingMachine _machine;

    // UI
    private Canvas _canvas;
    private CanvasGroup _group, _promptGroup;
    private Text _balance, _status, _promptText;
    private readonly List<Row> _rows = new List<Row>();
    private int _selected;
    private float _fade, _statusTimer, _shake;
    private Sprite _dot;
    private Font _font, _fontBold, _fontLight;

    private class Row
    {
        public RectTransform rect;
        public Image bg, bar, coin;
        public Text name, blurb, stats, price, tag;
    }

    private static ShopPanel _instance;
    private float _openedAt;
    private bool _mine;        // this instance opened the shop (the static IsOpen is shared)

    void Start()
    {
        // One panel only: a second HUD / rebuild must not run a second copy that would close the first one's shop
        if (_instance != null && _instance != this) { Destroy(this); return; }
        _instance = this;
        BuildUi();
    }

    void OnDestroy() { if (_instance == this) _instance = null; }

    void OnDisable() { if (_mine) Close(false); }

    // ── Per frame ─────────────────────────────────────────────────────────

    void Update()
    {
        if (_canvas == null || _instance != this) return;
        float dt = Time.unscaledDeltaTime;
        var kb = Keyboard.current;

        if (_mine)
        {
            HandleOpenInput(kb);
            _fade = Mathf.MoveTowards(_fade, 1f, dt * 7f);
            _promptGroup.alpha = 0f;
        }
        else
        {
            _fade = Mathf.MoveTowards(_fade, 0f, dt * 9f);
            UpdatePrompt(kb);
        }

        _group.alpha = _fade;
        _canvas.gameObject.SetActive(_fade > 0.001f || _promptGroup.alpha > 0.001f);
        if (_fade > 0.001f) RefreshRows(dt);
    }

    private bool EnsurePlayer()
    {
        if (_player == null)
        {
            _player = FindFirstObjectByType<PlayerController>();
            if (_player == null) return false;
            _inventory = _player.GetComponent<WeaponInventory>();
            _stats = _player.GetComponent<PlayerStats>();
            _playerInput = _player.GetComponent<PlayerInput>();
        }
        if (_wallet == null) _wallet = PlayerWallet.Local;
        return _inventory != null && _wallet != null;
    }

    private void UpdatePrompt(Keyboard kb)
    {
        bool show = false;
        if (!IsOpen && EnsurePlayer() && Time.timeScale > 0f && (_stats == null || _stats.IsAlive) && !WeaponWheel.IsOpen)
        {
            var near = VendingMachine.Nearest(_player.transform.position, VendingMachine.InteractRange);
            if (near != null)
            {
                show = true;
                if (kb != null && kb.eKey.wasPressedThisFrame) { Open(near); return; }
            }
        }
        _promptGroup.alpha = Mathf.MoveTowards(_promptGroup.alpha, show ? 1f : 0f, Time.unscaledDeltaTime * 8f);
    }

    private void HandleOpenInput(Keyboard kb)
    {
        // Leave if the player died or got pushed away
        if (_stats != null && !_stats.IsAlive) { Close(true); return; }
        if (_machine == null || Vector3.Distance(_machine.transform.position, _player.transform.position) > VendingMachine.InteractRange * 2f) { Close(true); return; }
        if (kb == null) return;

        if (kb.escapeKey.wasPressedThisFrame || kb.backspaceKey.wasPressedThisFrame) { _closedFrame = Time.frameCount; Close(true); return; }
        if (kb.sKey.wasPressedThisFrame || kb.downArrowKey.wasPressedThisFrame) _selected = (_selected + 1) % Items.Length;
        if (kb.wKey.wasPressedThisFrame || kb.upArrowKey.wasPressedThisFrame) _selected = (_selected + Items.Length - 1) % Items.Length;
        if (Time.unscaledTime - _openedAt > 0.25f && (kb.eKey.wasPressedThisFrame || kb.enterKey.wasPressedThisFrame)) Buy(_selected);
    }

    // ── Open / close ──────────────────────────────────────────────────────

    private void Open(VendingMachine machine)
    {
        _machine = machine;
        _mine = true;
        _openedAt = Time.unscaledTime;
        IsOpen = true;
        _selected = 0;
        SetStatus("Elige un arma", Color.white);

        PlayerController.LookLocked = true;
        if (_playerInput != null) _playerInput.enabled = false;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        ScreenBlur.Request(this, true);
        HUD.Instance?.SetHidden(true);
        EnsureEventSystem();
    }

    private void Close(bool restore)
    {
        _mine = false;
        IsOpen = false;
        PlayerController.LookLocked = false;
        if (_playerInput != null) _playerInput.enabled = true;
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
        ScreenBlur.Request(this, false);
        HUD.Instance?.SetHidden(false);
        _machine = null;
    }

    // ── Buying ────────────────────────────────────────────────────────────

    private int PriceOf(int i) => _inventory != null && _inventory.Has(Items[i].type) ? RefillPrice(i) : Items[i].price;
    private static int RefillPrice(int i) => Mathf.Max(15, Items[i].price / 4);

    private void Buy(int i)
    {
        if (!EnsurePlayer()) { Debug.LogWarning("[Shop] Buy: no player / inventory / wallet found."); SetStatus("Error: jugador no encontrado", Alert); return; }
        var item = Items[i];
        bool owned = _inventory.Has(item.type);
        int price = owned ? RefillPrice(i) : item.price;

        if (_wallet.Coins < price)
        {
            SetStatus($"Te faltan {price - _wallet.Coins} monedas", Alert);
            _shake = 1f;
            return;
        }

        var template = WeaponSpawner.GetShopTemplate(item.kind);
        if (template == null) { Debug.LogWarning($"[Shop] No template for {item.kind}."); SetStatus("No disponible", Alert); return; }
        if (!_wallet.TrySpend(price)) return;

        var result = _inventory.GiveWeapon(template);
        if (result == WeaponInventory.GiveResult.Failed)
        {
            _wallet.Add(price);          // refund
            SetStatus("No se pudo entregar el arma", Alert);
            return;
        }
        string msg = result == WeaponInventory.GiveResult.Refilled ? $"{item.name}: munición recargada"
                   : result == WeaponInventory.GiveResult.Swapped ? $"{item.name} comprada (soltaste tu arma activa)"
                   : $"{item.name} comprada";
        Debug.Log($"[Shop] {msg} (coins left: {_wallet.Coins})");
        SetStatus(msg, Good);
    }

    private void SetStatus(string text, Color color)
    {
        _status.text = text; _status.color = color; _statusTimer = 3f;
    }

    // ── UI ────────────────────────────────────────────────────────────────

    private void BuildUi()
    {
        _font = UiFactory.LoadFont(); _fontBold = UiFactory.LoadFontSemiBold(); _fontLight = UiFactory.LoadFontLight();
        _dot = UiFactory.RadialSprite(64, d => d <= 1f ? Mathf.Clamp01((1f - d) * 32f) : 0f, "ShopDot");

        var go = new GameObject("ShopCanvas");
        go.transform.SetParent(transform, false);
        _canvas = go.AddComponent<Canvas>();
        _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _canvas.sortingOrder = 14;
        var scaler = go.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;
        go.AddComponent<GraphicRaycaster>();
        // This canvas lives under the HUD object, whose CanvasGroup fades to 0 while the shop is open (HUD hidden): don't inherit it
        var own = go.AddComponent<CanvasGroup>();
        own.ignoreParentGroups = true;

        // Prompt (always built; shown near a machine)
        var promptRoot = UiFactory.NewRect("Prompt", go.transform, new Vector2(0.5f, 0.28f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(460f, 60f));
        _promptGroup = promptRoot.gameObject.AddComponent<CanvasGroup>();
        _promptGroup.alpha = 0f;
        var pbg = promptRoot.gameObject.AddComponent<Image>(); pbg.color = new Color(Ink.r, Ink.g, Ink.b, 0.7f); pbg.raycastTarget = false;
        Box(promptRoot, "KeyBox", new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(18f, 0f), new Vector2(34f, 34f), Cyan);
        Label(promptRoot, "E", 24, Ink, TextAnchor.MiddleCenter, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(18f, 0f), new Vector2(34f, 34f), _fontBold);
        _promptText = Label(promptRoot, "EXPENDEDORA DE ARMAS", 24, Color.white, TextAnchor.MiddleLeft, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(66f, 0f), new Vector2(380f, 40f), _fontBold);

        // Panel
        var panel = UiFactory.NewRect("Panel", go.transform, CC, CC, Vector2.zero, new Vector2(1920f, 1080f));
        _group = panel.gameObject.AddComponent<CanvasGroup>();
        _group.alpha = 0f;
        var dim = panel.gameObject.AddComponent<Image>(); dim.color = new Color(0f, 0f, 0f, 0.25f);

        var card = UiFactory.NewRect("Card", panel, CC, CC, Vector2.zero, new Vector2(980f, 700f));
        var cardBg = card.gameObject.AddComponent<Image>(); cardBg.color = new Color(Ink.r, Ink.g, Ink.b, 0.9f); cardBg.raycastTarget = true;
        Box(card, "TopLine", new Vector2(0f, 1f), new Vector2(0f, 1f), Vector2.zero, new Vector2(980f, 3f), Cyan);

        Label(card, "EXPENDEDORA", 44, Color.white, TextAnchor.UpperLeft, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(48f, -34f), new Vector2(560f, 56f), _fontBold);
        Label(card, "Paga con monedas: Oro 10 · Platino 50 · Elite 200", 20, new Color(1f, 1f, 1f, 0.55f), TextAnchor.UpperLeft, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(50f, -92f), new Vector2(640f, 28f), _fontLight);

        var coin = Box(card, "Coin", new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-48f, -52f), new Vector2(22f, 22f), Amber); coin.sprite = _dot;
        _balance = Label(card, "0", 42, Amber, TextAnchor.UpperRight, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-82f, -36f), new Vector2(260f, 56f), _fontBold);

        for (int i = 0; i < Items.Length; i++) _rows.Add(BuildRow(card, i));

        _status = Label(card, "", 24, Color.white, TextAnchor.MiddleLeft, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(48f, 66f), new Vector2(880f, 36f), _fontBold);
        Label(card, "W / S  ELEGIR      E · ENTER  COMPRAR      ESC  CERRAR", 18, new Color(1f, 1f, 1f, 0.5f), TextAnchor.LowerLeft, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(48f, 26f), new Vector2(880f, 26f), _fontLight);
    }

    private Row BuildRow(RectTransform card, int i)
    {
        var r = new Row();
        r.rect = UiFactory.NewRect("Row" + i, card, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -150f - i * 88f), new Vector2(884f, 80f));
        r.bg = r.rect.gameObject.AddComponent<Image>(); r.bg.color = new Color(1f, 1f, 1f, 0.05f);
        var handler = r.rect.gameObject.AddComponent<ShopRowHandler>();
        handler.index = i; handler.panel = this;

        r.bar = Box(r.rect, "Bar", new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), Vector2.zero, new Vector2(5f, 80f), Items[i].color);
        r.name = Label(r.rect, Items[i].name, 30, Color.white, TextAnchor.UpperLeft, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(28f, -10f), new Vector2(420f, 38f), _fontBold);
        r.blurb = Label(r.rect, Items[i].blurb, 19, new Color(1f, 1f, 1f, 0.6f), TextAnchor.UpperLeft, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(28f, -48f), new Vector2(430f, 26f), _fontLight);
        r.stats = Label(r.rect, StatsLine(Items[i]), 19, Items[i].color, TextAnchor.MiddleLeft, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(60f, 0f), new Vector2(260f, 28f), _font);
        r.coin = Box(r.rect, "Coin", new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-24f, 12f), new Vector2(16f, 16f), Amber); r.coin.sprite = _dot;
        r.price = Label(r.rect, "", 34, Amber, TextAnchor.MiddleRight, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-48f, 12f), new Vector2(160f, 40f), _fontBold);
        r.tag = Label(r.rect, "", 17, new Color(1f, 1f, 1f, 0.6f), TextAnchor.MiddleRight, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-24f, -20f), new Vector2(220f, 24f), _fontLight);
        return r;
    }

    private static string StatsLine(Item item)
    {
        var def = Resources.Load<WeaponDefinition>("Weapons/Definitions/" + item.definition);
        if (def == null) return "";
        return $"DAÑO {def.damage:0}   CARGADOR {def.maxAmmo}";
    }

    private void RefreshRows(float dt)
    {
        if (_wallet != null) _balance.text = _wallet.Coins.ToString();
        _statusTimer -= dt;
        if (_statusTimer < 0.6f) { var c = _status.color; c.a = Mathf.Clamp01(_statusTimer / 0.6f); _status.color = c; }
        _shake = Mathf.MoveTowards(_shake, 0f, dt * 4f);

        for (int i = 0; i < _rows.Count; i++)
        {
            var r = _rows[i];
            bool sel = i == _selected;
            bool owned = _inventory != null && _inventory.Has(Items[i].type);
            int price = owned ? RefillPrice(i) : Items[i].price;
            bool afford = _wallet != null && _wallet.Coins >= price;

            r.bg.color = Color.Lerp(r.bg.color, sel ? new Color(Items[i].color.r, Items[i].color.g, Items[i].color.b, 0.22f) : new Color(1f, 1f, 1f, 0.05f), 1f - Mathf.Exp(-16f * dt));
            r.bar.rectTransform.sizeDelta = new Vector2(Mathf.Lerp(r.bar.rectTransform.sizeDelta.x, sel ? 12f : 5f, 1f - Mathf.Exp(-16f * dt)), 80f);
            r.price.text = price.ToString();
            r.price.color = afford ? Amber : Alert;
            r.tag.text = owned ? "YA LA TIENES · RECARGAR MUNICIÓN" : "";
            float sx = sel ? Mathf.Sin(Time.unscaledTime * 60f) * 6f * _shake : 0f;
            r.rect.anchoredPosition = new Vector2(sx, -150f - i * 88f);
        }
    }

    // Row hover / click
    public void HoverRow(int i) { if (IsOpen) _selected = i; }
    public void ClickRow(int i) { if (IsOpen) { _selected = i; Buy(i); } }

    // ── Helpers ───────────────────────────────────────────────────────────

    private static readonly Vector2 CC = new Vector2(0.5f, 0.5f);

    private Text Label(Transform parent, string text, int size, Color color, TextAnchor align, Vector2 anchor, Vector2 pivot, Vector2 pos, Vector2 box, Font font)
    {
        var rt = UiFactory.NewRect("Text", parent, anchor, pivot, pos, box);
        var t = rt.gameObject.AddComponent<Text>();
        t.font = font; t.text = text; t.fontSize = size; t.color = color; t.alignment = align;
        t.horizontalOverflow = HorizontalWrapMode.Overflow; t.verticalOverflow = VerticalWrapMode.Overflow;
        t.supportRichText = true; t.raycastTarget = false;
        return t;
    }

    private Image Box(Transform parent, string name, Vector2 anchor, Vector2 pivot, Vector2 pos, Vector2 size, Color color)
    {
        var rt = UiFactory.NewRect(name, parent, anchor, pivot, pos, size);
        var img = rt.gameObject.AddComponent<Image>();
        img.color = color; img.raycastTarget = false;
        return img;
    }

    private static void EnsureEventSystem()
    {
        var es = EventSystem.current != null ? EventSystem.current : FindFirstObjectByType<EventSystem>();
        if (es == null) es = new GameObject("EventSystem").AddComponent<EventSystem>();
        if (es.GetComponent<InputSystemUIInputModule>() == null)
        {
            foreach (var legacy in es.GetComponents<StandaloneInputModule>()) Destroy(legacy);
            var module = es.gameObject.AddComponent<InputSystemUIInputModule>();
            module.AssignDefaultActions();
        }
    }
}

/// <summary>Forwards mouse hover / click on a shop row to the panel.</summary>
public class ShopRowHandler : MonoBehaviour, IPointerEnterHandler, IPointerClickHandler
{
    public int index;
    public ShopPanel panel;
    public void OnPointerEnter(PointerEventData e) => panel.HoverRow(index);
    public void OnPointerClick(PointerEventData e) => panel.ClickRow(index);
}
}
