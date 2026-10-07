using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace OrbitRush
{

/// <summary>
/// End-of-match scoreboard overlay. Hidden by default; GameManager.EndMatch()
/// calls Show() with players ranked by kills. Add this to a Canvas GameObject
/// in the scene (can be the same one as HUD, or its own).
/// </summary>
public class Scoreboard : MonoBehaviour
{
    public static Scoreboard Instance { get; private set; }

    [Header("Layout")]
    public int maxRowsShown = 10;

    private GameObject _root;
    private Transform  _panel;
    private readonly List<GameObject> _rowObjects = new();

    static readonly Color ColBg    = new Color(0f, 0f, 0f, 0.75f);
    static readonly Color ColPanel = new Color(0.06f, 0.08f, 0.12f, 0.95f);
    static readonly Color ColText  = new Color(0.95f, 0.95f, 0.95f);
    static readonly Color ColGold  = new Color(1f, 0.85f, 0.2f);
    static readonly Color ColMuted = new Color(0.65f, 0.65f, 0.65f);

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        Build();
    }

    // ══════════════════════════════════════════════════════════════════════
    // BUILD
    // ══════════════════════════════════════════════════════════════════════

    void Build()
    {
        var canvas = GetComponent<Canvas>();
        if (canvas == null) canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 20; // above HUD

        var cs = gameObject.GetComponent<CanvasScaler>() ?? gameObject.AddComponent<CanvasScaler>();
        cs.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        cs.referenceResolution = new Vector2(1920, 1080);
        cs.matchWidthOrHeight = 0.5f;

        if (gameObject.GetComponent<GraphicRaycaster>() == null)
            gameObject.AddComponent<GraphicRaycaster>();

        _root = new GameObject("ScoreboardRoot");
        _root.transform.SetParent(transform, false);
        var bgRt = _root.AddComponent<RectTransform>();
        bgRt.anchorMin = Vector2.zero; bgRt.anchorMax = Vector2.one;
        bgRt.sizeDelta = Vector2.zero;
        _root.AddComponent<Image>().color = ColBg;

        var panelGO = new GameObject("Panel");
        panelGO.transform.SetParent(_root.transform, false);
        var pRt = panelGO.AddComponent<RectTransform>();
        pRt.anchorMin = pRt.anchorMax = new Vector2(0.5f, 0.5f);
        pRt.sizeDelta = new Vector2(480, 480);
        pRt.anchoredPosition = Vector2.zero;
        panelGO.AddComponent<Image>().color = ColPanel;
        _panel = panelGO.transform;

        var title = MakeText(_panel, "Title", "MATCH OVER",
            new Vector2(0, 200), new Vector2(440, 40), 30, TextAnchor.MiddleCenter);
        title.fontStyle = FontStyle.Bold;
        title.color = ColGold;

        var btnGO = new GameObject("PlayAgainButton");
        btnGO.transform.SetParent(_panel, false);
        var btnRt = btnGO.AddComponent<RectTransform>();
        btnRt.anchorMin = btnRt.anchorMax = new Vector2(0.5f, 0f);
        btnRt.pivot = new Vector2(0.5f, 0f);
        btnRt.anchoredPosition = new Vector2(0, 30);
        btnRt.sizeDelta = new Vector2(220, 50);
        var btnImg = btnGO.AddComponent<Image>();
        btnImg.color = new Color(0.15f, 0.55f, 0.95f);
        var btn = btnGO.AddComponent<Button>();
        btn.targetGraphic = btnImg;
        btn.onClick.AddListener(RestartMatch);
        var btnLabel = MakeText(btnGO.transform, "Label", "PLAY AGAIN",
            Vector2.zero, new Vector2(210, 44), 18, TextAnchor.MiddleCenter);
        btnLabel.fontStyle = FontStyle.Bold;

        _root.SetActive(false);
    }

    // ══════════════════════════════════════════════════════════════════════
    // PUBLIC
    // ══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Shows the scoreboard with players ranked by PvP kill count (highest first).
    /// pveScore is optional and shown per-row as a separate stat — it does NOT
    /// affect ranking, since PvE progress and the PvP leaderboard are tracked apart.
    /// </summary>
    public void Show(List<KeyValuePair<int, int>> ranking, IReadOnlyDictionary<int, int> pveScore = null)
    {
        foreach (var go in _rowObjects) Destroy(go);
        _rowObjects.Clear();

        if (ranking.Count == 0)
        {
            var empty = MakeText(_panel, "NoKills", "No kills registered.",
                new Vector2(0, 140), new Vector2(420, 30), 16, TextAnchor.MiddleCenter);
            empty.color = ColMuted;
            _rowObjects.Add(empty.gameObject);
        }
        else
        {
            int shown = Mathf.Min(ranking.Count, maxRowsShown);
            float y = 140f;
            for (int i = 0; i < shown; i++)
            {
                var entry = ranking[i];
                int pve = pveScore != null && pveScore.TryGetValue(entry.Key, out var s) ? s : 0;
                var row = MakeText(_panel, $"Row{i}",
                    $"{i + 1}.  Player {entry.Key}   —   {entry.Value} kills   <color=#9fd8ff>|  {pve} PvE</color>",
                    new Vector2(0, y), new Vector2(420, 28), 18, TextAnchor.MiddleLeft);
                if (i == 0) row.color = ColGold;
                _rowObjects.Add(row.gameObject);
                y -= 32f;
            }

            if (ranking.Count > shown)
            {
                var more = MakeText(_panel, "More", $"+ {ranking.Count - shown} more",
                    new Vector2(0, y), new Vector2(420, 24), 14, TextAnchor.MiddleCenter);
                more.color = ColMuted;
                _rowObjects.Add(more.gameObject);
            }
        }

        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        _root.SetActive(true);
        ScreenBlur.Request(this, true);
        if (HUD.Instance != null) HUD.Instance.SetHidden(true);
        UiAnim.Intro((RectTransform)_root.transform, 0f, 0.3f, Vector2.zero, 1f);
        UiAnim.Intro((RectTransform)_panel, 0.08f, 0.45f, new Vector2(0f, -60f), 0.88f);
    }

    public void Hide()
    {
        _root.SetActive(false);
        ScreenBlur.Request(this, false);
        if (HUD.Instance != null) HUD.Instance.SetHidden(false);
    }

    private void RestartMatch()
    {
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }

    // ══════════════════════════════════════════════════════════════════════
    // HELPERS
    // ══════════════════════════════════════════════════════════════════════

    Text MakeText(Transform parent, string name, string content,
                  Vector2 pos, Vector2 size, int fontSize, TextAnchor anchor)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;
        var t = go.AddComponent<Text>();
        t.text = content;
        t.font = GetDefaultFont();
        t.fontSize = fontSize;
        t.color = ColText;
        t.alignment = anchor;
        t.supportRichText = true;
        return t;
    }

    Font _cachedFont;
    Font GetDefaultFont()
    {
        if (_cachedFont != null) return _cachedFont;

        _cachedFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (_cachedFont != null) return _cachedFont;

        _cachedFont = Resources.GetBuiltinResource<Font>("Arial.ttf");
        if (_cachedFont != null) return _cachedFont;

        _cachedFont = Font.CreateDynamicFontFromOSFont(
            new[] { "Arial", "Helvetica", "Verdana", "sans-serif" }, 14);

        if (_cachedFont == null)
            Debug.LogError("[Scoreboard] No font found — text will be invisible.");

        return _cachedFont;
    }
}
}
