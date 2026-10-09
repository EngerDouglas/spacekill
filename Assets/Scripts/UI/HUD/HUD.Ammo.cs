using UnityEngine;
using UnityEngine.UI;

namespace OrbitRush
{

/// <summary>
/// Ammo feedback, one piece at a time so each reads clearly:
///   • bullet pips  — one thin bar per round (grouped when the magazine is big); each spent round flashes cyan and fades,
///                    and during a reload they refill one after another from the left
///   • ammo number  — springs up a little on every shot, counts quickly during a reload, shakes when empty, pulses red when low
///   • reload       — the label fades in/out; when it finishes a wave of light runs across the pips and the number pops
///   • weapon swap  — name, number and pips slide in and fade up from the right
/// Everything runs on unscaled time with spring / ease curves instead of linear steps.
/// </summary>
public partial class HUD
{
    private const float PipRowWidth = 300f;
    private const int   MaxPips = 30;
    private static readonly Vector2 AmmoBasePos = new Vector2(-Margin, 124f);
    private static readonly Vector2 NameBasePos = new Vector2(-Margin, 238f);

    private RectTransform _pipRow;
    private CanvasGroup _pipGroup, _reloadCg;
    private Image[] _pips;
    private float[] _pipLevel, _pipFlash;
    private int _pipCount, _pipPer = 1, _pipsFor = -1;

    private WeaponBase _animWeapon;
    private float _shownAmmo, _punch, _punchVel, _switchK = 1f, _emptyK, _finishFlash;
    private bool _animReloading;
    private int _reloadFromAmmo, _lastShownInt = -1, _lastShownMax = -1;
    private float _kickVel;

    // ── Build ─────────────────────────────────────────────────────────────

    void BuildPips()
    {
        _pipRow = NewRect("Pips", transform, BR, BR, new Vector2(-Margin, 86f), new Vector2(PipRowWidth, 12f));
        _pipGroup = _pipRow.gameObject.AddComponent<CanvasGroup>();
        _pipGroup.alpha = 0f;
    }

    /// <summary>One pip per round up to <see cref="MaxPips"/>; bigger magazines share a pip between several rounds.</summary>
    void RebuildPips(int maxAmmo)
    {
        foreach (Transform c in _pipRow) Destroy(c.gameObject);

        _pipPer = maxAmmo > MaxPips ? Mathf.CeilToInt(maxAmmo / (float)MaxPips) : 1;
        _pipCount = Mathf.Max(1, Mathf.CeilToInt(maxAmmo / (float)_pipPer));

        const float gap = 3f;
        float w = Mathf.Clamp((PipRowWidth - gap * (_pipCount - 1)) / _pipCount, 2f, 14f);

        _pips = new Image[_pipCount];
        _pipLevel = new float[_pipCount];
        _pipFlash = new float[_pipCount];
        for (int i = 0; i < _pipCount; i++)
        {
            // Right-aligned: the last pip sits on the right edge, so rounds are spent from the right
            _pips[i] = Box(_pipRow, "Pip" + i, BR, BR, new Vector2(-(_pipCount - 1 - i) * (w + gap), 0f), new Vector2(w, 10f), Track);
            _pipLevel[i] = 0f;                                  // starts empty: a new weapon's pips fill in one after another
        }
        _pipsFor = maxAmmo;
    }

    // ── Events ────────────────────────────────────────────────────────────

    /// <summary>A shot was fired (WeaponBase.AnyFired, raised right after the round is spent).</summary>
    void OnWeaponFired(WeaponBase weapon)
    {
        if (_inventory == null || weapon != _inventory.ActiveWeapon || _pips == null) return;

        _punchVel += 3.2f;          // the ammo number jumps
        _kickVel += 170f;           // the crosshair opens

        int idx = Mathf.Clamp(weapon.currentAmmo / _pipPer, 0, _pipCount - 1);   // the pip that just emptied
        _pipFlash[idx] = 1f;
    }

    // ── Per frame ─────────────────────────────────────────────────────────

    /// <summary>Spring + eased animation of the whole ammo block. Called every frame while a gun (not the katana) is held.</summary>
    void AnimateAmmo(WeaponBase w)
    {
        float dt = Time.unscaledDeltaTime;

        if (w != _animWeapon)            // new weapon: restart the entrance and rebuild the pips
        {
            _animWeapon = w;
            _switchK = 0f;
            _shownAmmo = w.currentAmmo;
            _animReloading = false;
            _punch = _punchVel = 0f;
            _pipsFor = -1;
        }
        if (_pipsFor != w.maxAmmo) RebuildPips(w.maxAmmo);
        _switchK = Mathf.MoveTowards(_switchK, 1f, dt * 3.5f);
        float enter = UiAnim.EaseOutCubic(_switchK);

        // Reload bookkeeping: remember where it started, celebrate when it ends
        bool reloading = w.IsReloading;
        if (reloading && !_animReloading) _reloadFromAmmo = w.currentAmmo;
        if (!reloading && _animReloading) { _finishFlash = 1f; _punchVel += 4.5f; }
        _animReloading = reloading;

        // The number follows the real ammo; during a reload it climbs along the progress curve
        float target = reloading ? Mathf.Lerp(_reloadFromAmmo, w.maxAmmo, UiAnim.EaseOutCubic(w.ReloadProgress)) : w.currentAmmo;
        _shownAmmo = Mathf.MoveTowards(_shownAmmo, target, (reloading ? 220f : 80f) * dt);

        // ── Number ──
        int shown = Mathf.RoundToInt(_shownAmmo);
        if (shown != _lastShownInt || w.maxAmmo != _lastShownMax)
        {
            _lastShownInt = shown; _lastShownMax = w.maxAmmo;
            _ammoText.text = $"{shown}  <size=30><color=#FFFFFF80>| {w.maxAmmo}</color></size>";
        }

        _punchVel += (-_punch * 260f - _punchVel * 17f) * dt;           // damped spring around 0
        _punch += _punchVel * dt;
        _ammoText.rectTransform.localScale = Vector3.one * (1f + Mathf.Clamp(_punch, -0.4f, 1.2f) * 0.08f);

        float ammoPct = w.maxAmmo > 0 ? w.currentAmmo / (float)w.maxAmmo : 0f;
        bool empty = w.currentAmmo <= 0 && !reloading;
        bool low = !reloading && ammoPct <= 0.25f;
        _emptyK = Mathf.MoveTowards(_emptyK, empty ? 1f : 0f, dt * 6f);
        float shake = Mathf.Sin(Time.unscaledTime * 55f) * 3f * _emptyK;

        float pulse = low ? 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 8f) : 0f;
        Color nc = Color.Lerp(White, Alert, low ? Mathf.Lerp(0.55f, 1f, pulse) : 0f);
        if (reloading) nc = Color.Lerp(White, Amber, 0.45f);
        nc.a = enter;
        _ammoText.color = nc;
        _ammoText.rectTransform.anchoredPosition = AmmoBasePos + new Vector2((1f - enter) * 22f + shake, 0f);

        var nameCol = _weaponName.color; nameCol.a = 0.70f * enter; _weaponName.color = nameCol;
        _weaponName.rectTransform.anchoredPosition = NameBasePos + new Vector2((1f - enter) * 22f, 0f);

        // ── Pips ──
        _pipGroup.alpha = Mathf.MoveTowards(_pipGroup.alpha, enter, dt * 8f);
        for (int i = 0; i < _pipCount; i++)
        {
            float fill = Mathf.Clamp01((_shownAmmo - i * _pipPer) / _pipPer);
            float speed = fill < _pipLevel[i] ? 12f : 8f;               // spent pips drop fast, refilling ones rise a bit slower
            _pipLevel[i] = Mathf.MoveTowards(_pipLevel[i], fill, dt * speed);
            _pipFlash[i] = Mathf.MoveTowards(_pipFlash[i], 0f, dt * 3.5f);

            // Wave of light left → right when a reload finishes
            float wave = Mathf.Clamp01(_finishFlash * 1.5f - (i / (float)_pipCount) * 0.9f);

            Color c = Color.Lerp(Track, White, _pipLevel[i]);
            if (low) c = Color.Lerp(c, Alert, pulse * _pipLevel[i]);
            c = Color.Lerp(c, Cyan, Mathf.Max(_pipFlash[i], wave * 0.85f));
            _pips[i].color = c;
            _pips[i].rectTransform.localScale = new Vector3(1f, 1f + _pipFlash[i] * 0.7f + wave * 0.35f, 1f);
        }
        _finishFlash = Mathf.MoveTowards(_finishFlash, 0f, dt * 1.3f);

        // ── Reload label ──
        float labelTarget = reloading ? 0.75f + 0.25f * Mathf.Sin(Time.unscaledTime * 7f) : 0f;
        _reloadCg.alpha = Mathf.MoveTowards(_reloadCg.alpha, labelTarget, dt * 8f);
    }

    /// <summary>Nothing to count (katana drawn, or no weapon): the ammo block settles, and the next gun slides in again.</summary>
    void IdleAmmo(string number)
    {
        float dt = Time.unscaledDeltaTime;
        _animWeapon = null;
        _ammoText.text = number;
        _lastShownInt = -1;
        _ammoText.color = Soft;
        _ammoText.rectTransform.localScale = Vector3.one;
        _ammoText.rectTransform.anchoredPosition = AmmoBasePos;
        _weaponName.rectTransform.anchoredPosition = NameBasePos;
        var nameCol = _weaponName.color; nameCol.a = 0.70f; _weaponName.color = nameCol;
        if (_pipGroup != null) _pipGroup.alpha = Mathf.MoveTowards(_pipGroup.alpha, 0f, dt * 8f);
        if (_reloadCg != null) _reloadCg.alpha = Mathf.MoveTowards(_reloadCg.alpha, 0f, dt * 8f);
    }
}
}
