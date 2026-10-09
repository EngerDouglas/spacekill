using UnityEngine;
using UnityEngine.UI;

namespace OrbitRush
{

/// <summary>Coin counter (top right, under the match stats) and the "+N" that floats up when a coin is collected.</summary>
public partial class HUD
{
    private Text _coinText, _coinGain;
    private Image _coinDot;
    private PlayerWallet _wallet;
    private float _coinShown, _gainTimer;
    private int _gainSum;
    private Color _gainColor = Color.white;

    void BuildCoins()
    {
        _coinDot = Box(transform, "CoinDot", TR, TR, new Vector2(-Margin, -76f), new Vector2(14f, 14f), Amber);
        _coinDot.sprite = Circle();
        _coinText = Txt(transform, "0", 24, Amber, TextAnchor.MiddleRight, TR, TR, new Vector2(-Margin - 22f, -70f), new Vector2(180f, 28f), UiFactory.LoadFontSemiBold());
        _coinGain = Txt(transform, "", 18, Amber, TextAnchor.MiddleRight, TR, TR, new Vector2(-Margin - 22f, -98f), new Vector2(180f, 24f), UiFactory.LoadFontSemiBold());
    }

    /// <summary>A coin was collected: shows the running "+N" under the counter.</summary>
    public void ShowCoinGain(int value, CoinType type)
    {
        _gainSum = _gainTimer > 0f ? _gainSum + value : value;
        _gainTimer = 1.6f;
        _gainColor = type == CoinType.Oro ? Amber : type == CoinType.Platino ? new Color32(0xE0, 0xEB, 0xFF, 255) : new Color32(0xB8, 0x8C, 0xFF, 255);
    }

    void RefreshCoins()
    {
        if (_coinText == null) return;
        if (_wallet == null) _wallet = PlayerWallet.Local;
        if (_wallet == null) return;

        float dt = Time.unscaledDeltaTime;
        _coinShown = Mathf.MoveTowards(_coinShown, _wallet.Coins, Mathf.Max(60f, Mathf.Abs(_wallet.Coins - _coinShown) * 4f) * dt);
        _coinText.text = Mathf.RoundToInt(_coinShown).ToString();

        _gainTimer -= dt;
        float a = Mathf.Clamp01(_gainTimer / 0.5f);
        _coinGain.text = _gainSum > 0 ? "+" + _gainSum : "";
        var c = _gainColor; c.a = a; _coinGain.color = c;
        _coinGain.rectTransform.anchoredPosition = new Vector2(-Margin - 22f, -98f - (1f - Mathf.Clamp01(_gainTimer / 1.6f)) * 6f);
    }
}
}
