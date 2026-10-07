using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace OrbitRush
{

/// <summary>HUD per-frame refresh: reads player, weapon and match state into the widgets.</summary>
public partial class HUD
{
    // ══════════════════════════════════════════════════════════════════════
    // REFRESH
    // ══════════════════════════════════════════════════════════════════════

    void FindPlayer()
    {
        var player = GameObject.FindWithTag("Player");
        if (player == null) return;
        _stats      = player.GetComponent<PlayerStats>();
        _inventory  = player.GetComponent<WeaponInventory>();
        _controller = player.GetComponent<PlayerController>();
        _playerBody = player.GetComponent<Rigidbody>();
    }

    void RefreshHUD()
    {
        if (_stats == null) FindPlayer();
        if (_healthText == null) return;      // art panels failed to load (HudArt not imported as Sprites): nothing to refresh

        RefreshVitals();
        RefreshWeapon();
        RefreshSlots();
        RefreshPlanet();
        RefreshMatch();
        RefreshKills();
        RefreshCrosshair();
        RefreshVignette();
        RefreshRadar();
        RefreshBanner();
    }

    // ── Vitals ────────────────────────────────────────────────────────────

    void RefreshVitals()
    {
        if (_stats == null) return;

        SetBar(_health, _stats.HealthPercent);
        SetBar(_shield, _stats.ShieldPercent);
        SetBar(_jet, _stats.JetpackPercent);
        SetBar(_stamina, _stats.StaminaPercent);
        if (_controller != null) SetBar(_dash, 1f - _controller.DashCooldownPercent);
        SetBar(_dodge, _meleeDodge);
        SetBar(_katana, _meleeOut ? Mathf.Max(0.04f, _meleeCharge) : 0f);

        _healthText.text = Mathf.CeilToInt(_stats.health).ToString();
        _shieldText.text = (!_art && _stats.shield > 0.5f) ? Mathf.CeilToInt(_stats.shield).ToString() : "";

        // Health: green → yellow → red as it drops; pulses when critical
        float hp = _stats.HealthPercent;
        Color hc = hp > 0.5f ? Color.Lerp(Yellow, Green, (hp - 0.5f) * 2f) : Color.Lerp(Red, Yellow, hp * 2f);
        if (hp < 0.25f) hc = Color.Lerp(hc, White, 0.35f * (0.5f + 0.5f * Mathf.Sin(Time.time * 9f)));
        _health.fillImage.color = hc;
        _healthText.color = hp < 0.25f ? Red : White;

        // Jetpack bar flashes when almost empty
        Color jetColor = _art ? Pink : Cyan;   // the supplied art draws the jetpack bar in pink
        _jet.fillImage.color = _stats.JetpackPercent < 0.2f
            ? Color.Lerp(jetColor, Muted, Mathf.PingPong(Time.time * 4f, 1f))
            : jetColor;
    }

    void SetBar(Bar b, float value)
    {
        if (b == null) return;
        value = Mathf.Clamp01(value);

        // The fill follows quickly; the pale "ghost" trail lags behind, showing how much was just lost.
        b.shown = Mathf.MoveTowards(b.shown, value, Time.unscaledDeltaTime * 3.5f);
        if (b.ghost != null)
        {
            if (value >= b.ghostShown) b.ghostShown = value;
            else b.ghostShown = Mathf.MoveTowards(b.ghostShown, value, Time.unscaledDeltaTime * 0.35f);
            b.ghost.anchorMax = new Vector2(b.ghostShown, 1f);
        }
        b.fill.anchorMax = new Vector2(b.shown, 1f);
    }

    // ── Weapon ────────────────────────────────────────────────────────────

    void RefreshWeapon()
    {
        if (_inventory == null) return;

        string qKey = _art ? "" : "<color=#FF1A9E>[Q]</color> ", fKey = _art ? "" : "<color=#FFCC1A>[F]</color> ";
        _grenadeQ.text = $"{qKey}{GrenadeLabel(_inventory.equippedGrenade, "Granada")}  <b>x{_inventory.grenadeCount}</b>";
        _grenadeF.text = $"{fKey}{GrenadeLabel(_inventory.specialBomb, "Especial")}  <b>x{_inventory.specialCount}</b>";

        var weapon = _inventory.ActiveWeapon;
        if (weapon == null)
        {
            _weaponName.text = "SIN ARMA";
            _ammoCurrent.text = "—";
            _ammoCurrent.color = Muted;
            _ammoMax.text = "";
            _reloadGroup.SetActive(false);
            _heatGroup.SetActive(false);
            return;
        }

        _weaponName.text = weapon.weaponName.ToUpper();
        _ammoCurrent.text = weapon.currentAmmo.ToString();
        _ammoMax.text = "/ " + weapon.maxAmmo;
        float ammoPct = weapon.maxAmmo > 0 ? (float)weapon.currentAmmo / weapon.maxAmmo : 0f;
        _ammoCurrent.color = ammoPct <= 0.25f ? Color.Lerp(Pink, White, 0.5f + 0.5f * Mathf.Sin(Time.time * 10f)) : White;

        bool reloading = weapon.IsReloading;
        _reloadGroup.SetActive(reloading);
        if (reloading) { _reload.shown = weapon.ReloadProgress; _reload.fill.anchorMax = new Vector2(_reload.shown, 1f); }

        var plasma = weapon as PlasmaRifle;
        _heatGroup.SetActive(plasma != null && !reloading);
        if (plasma != null)
        {
            SetBar(_heat, plasma.HeatPercent);
            _heat.fillImage.color = plasma.IsOverheated ? Color.Lerp(Red, White, Mathf.PingPong(Time.time * 6f, 1f)) : new Color(1f, 0.45f, 0.05f);
        }
    }

    /// <summary>"PlasmaGrenade" → "Plasma Grenade"; the slot's default name if nothing is equipped.</summary>
    static string GrenadeLabel(GrenadeBase grenade, string fallback)
    {
        if (grenade == null) return fallback;

        string n = grenade.GetType().Name;
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < n.Length; i++)
        {
            if (i > 0 && char.IsUpper(n[i])) sb.Append(' ');
            sb.Append(n[i]);
        }
        return sb.ToString();
    }

    void RefreshSlots()
    {
        if (_inventory == null) return;
        if (_builtSlotCount != _inventory.maxWeapons) BuildSlotBoxes(_inventory.maxWeapons);

        var weapons = _inventory.Weapons;
        int active = _inventory.ActiveIndex;
        for (int i = 0; i < _slotBg.Length; i++)
        {
            bool filled = i < weapons.Count;
            bool isActive = filled && i == active;
            _slotName[i].text = filled ? ShortName(weapons[i].weaponName) : "";
            _slotName[i].color = isActive ? White : Muted;
            _slotKey[i].color = isActive ? White : Cyan;

            // Re-tessellate the frame only when its look actually changes (0 = empty, 1 = owned, 2 = active)
            int state = isActive ? 2 : (filled ? 1 : 0);
            if (state == _slotState[i]) continue;
            _slotState[i] = state;

            switch (state)
            {
                case 2:  _slotBg[i].SetColors(Pink, Color.Lerp(NavyDeep, Pink, 0.50f), Color.Lerp(NavyDeep, Pink, 0.18f)); break;
                case 1:  _slotBg[i].SetColors(Cyan, new Color(0.07f, 0.09f, 0.24f, 0.90f), new Color(0.02f, 0.03f, 0.10f, 0.92f)); break;
                default: _slotBg[i].SetColors(new Color(0.35f, 0.40f, 0.60f, 0.55f), new Color(0.05f, 0.06f, 0.16f, 0.45f), new Color(0.02f, 0.03f, 0.10f, 0.45f)); break;
            }
        }
    }

    static string ShortName(string name)
    {
        if (string.IsNullOrEmpty(name)) return "?";
        name = name.ToUpper();
        return name.Length <= 11 ? name : name.Substring(0, 10) + ".";
    }

    // ── Planet / match / kills ────────────────────────────────────────────

    void RefreshPlanet()
    {
        if (_controller == null) return;
        var planet = _controller.CurrentPlanet;
        if (planet == null) return;

        _planetName.text = planet.planetName.ToUpper();
        _gravityText.text = $"GRAVEDAD {planet.gravityStrength:F1}  ·  {planet.biome.ToString().ToUpper()}";
    }

    void RefreshMatch()
    {
        var gm = GameManager.Instance;
        if (gm == null) return;

        float t = Mathf.Max(0f, gm.MatchTimeRemaining);
        _timerText.text = $"{Mathf.FloorToInt(t / 60f):00}:{Mathf.FloorToInt(t % 60f):00}";
        _timerText.color = t < 30f ? Color.Lerp(Red, White, Mathf.PingPong(Time.time * 2f, 1f)) : White;

        switch (gm.gameMode)
        {
            case GameManager.GameMode.TeamDeathmatch: _modeText.text = "EQUIPOS · ROSA vs CIAN"; break;
            case GameManager.GameMode.PlanetCapture:  _modeText.text = "CAPTURA DE PLANETAS"; break;
            default:                                  _modeText.text = "TODOS CONTRA TODOS"; break;
        }

        bool teamMode = gm.IsTeamMode;
        if (_teamGroup.activeSelf != teamMode) _teamGroup.SetActive(teamMode);
        if (teamMode)
        {
            _pinkScore.text = gm.GetTeamScore(Team.Pink).ToString();
            _cyanScore.text = gm.GetTeamScore(Team.Cyan).ToString();
        }
    }

    void RefreshKills()
    {
        var gm = GameManager.Instance;
        if (gm == null || _stats == null) return;
        _killsText.text = gm.GetKills(_stats.PlayerId).ToString();
        _pveText.text = gm.GetPveScore(_stats.PlayerId).ToString();
    }

    // ── Crosshair ─────────────────────────────────────────────────────────

    void RefreshCrosshair()
    {
        // Gap grows with movement speed and with each shot, then eases back.
        float speed = _playerBody != null ? _playerBody.linearVelocity.magnitude : 0f;
        var weapon = _inventory != null ? _inventory.ActiveWeapon : null;
        if (weapon != null)
        {
            if (_lastAmmo >= 0 && weapon.currentAmmo < _lastAmmo) _kick = Mathf.Min(_kick + 9f, 18f);
            _lastAmmo = weapon.currentAmmo;
        }
        _kick = Mathf.MoveTowards(_kick, 0f, Time.unscaledDeltaTime * 40f);

        float targetGap = (9f + Mathf.Clamp(speed, 0f, 14f) * 0.7f) * Mathf.Lerp(1f, 0.45f, AimZoom) + _kick;
        _crossGap = Mathf.Lerp(_crossGap, targetGap, Time.unscaledDeltaTime * 14f);

        _ticks[0].anchoredPosition = new Vector2(0, _crossGap + 6f);
        _ticks[1].anchoredPosition = new Vector2(0, -(_crossGap + 6f));
        _ticks[2].anchoredPosition = new Vector2(-(_crossGap + 6f), 0);
        _ticks[3].anchoredPosition = new Vector2(_crossGap + 6f, 0);

        // Red when an enemy is under the crosshair
        Color c = IsAimingAtEnemy() ? Red : White;
        c.a = 0.92f;
        for (int i = 0; i < 4; i++) _tickImages[i].color = c;
        _centerDot.color = c;

        // Hit marker
        if (_hitTimer > 0f)
        {
            _hitTimer -= Time.unscaledDeltaTime;
            float k = Mathf.Clamp01(_hitTimer / _hitDuration);
            var hc = _hitWasKill ? Pink : White; hc.a = k;
            foreach (var img in _hitImages) img.color = hc;
            _hitMarker.localScale = Vector3.one * (_hitWasKill ? 1.5f : 1f) * (1.25f - 0.25f * k);
            if (_hitTimer <= 0f) _hitMarker.gameObject.SetActive(false);
        }
    }

    private float _aimCheckTimer;
    private bool _aimingAtEnemy;

    bool IsAimingAtEnemy()
    {
        _aimCheckTimer -= Time.unscaledDeltaTime;
        if (_aimCheckTimer > 0f) return _aimingAtEnemy;
        _aimCheckTimer = 0.05f;   // 20 checks per second is plenty

        _aimingAtEnemy = false;
        var cam = Camera.main;
        if (cam == null || _stats == null) return false;

        var ray = cam.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
        var hits = Physics.RaycastAll(ray, 120f);
        float best = float.MaxValue;
        foreach (var h in hits)
        {
            if (h.collider.isTrigger || h.collider.transform.IsChildOf(_stats.transform)) continue;
            if (h.distance < best) { best = h.distance; _aimingAtEnemy = h.collider.GetComponentInParent<EnemyStats>() != null; }
        }
        return _aimingAtEnemy;
    }

    /// <summary>Flashes the hit marker. Called by projectiles when the player damages something.</summary>
    public void ShowHitMarker(bool kill = false)
    {
        _hitWasKill = kill;
        _hitTimer = _hitDuration = kill ? 0.32f : 0.18f;
        _hitMarker.gameObject.SetActive(true);
    }

    // ── Damage vignette ───────────────────────────────────────────────────

    void RefreshVignette()
    {
        if (_stats == null) return;

        if (_lastHealth >= 0f && _stats.health < _lastHealth)
            _damageFlash = Mathf.Clamp01(_damageFlash + (_lastHealth - _stats.health) / 35f + 0.15f);
        _lastHealth = _stats.health;
        _damageFlash = Mathf.MoveTowards(_damageFlash, 0f, Time.unscaledDeltaTime * 1.6f);

        float low = _stats.HealthPercent < 0.3f ? (0.3f - _stats.HealthPercent) / 0.3f : 0f;
        float pulse = low * (0.18f + 0.14f * Mathf.Sin(Time.time * 6f));
        var c = _vignette.color;
        c.a = Mathf.Clamp01(Mathf.Max(_damageFlash * 0.85f, pulse));
        _vignette.color = c;
    }

    // ── Radar ─────────────────────────────────────────────────────────────

    void RefreshRadar()
    {
        if (_stats == null) return;

        _enemyScanTimer -= Time.unscaledDeltaTime;
        if (_enemyScanTimer <= 0f)
        {
            _enemyScanTimer = 0.4f;
            _enemies.Clear();
            _enemies.AddRange(EnemyStats.All);
        }

        Transform p = _stats.transform;
        int used = 0;
        foreach (var e in _enemies)
        {
            if (e == null || !e.IsAlive) continue;

            Vector3 rel = e.transform.position - p.position;
            float x = Vector3.Dot(rel, p.right);
            float y = Vector3.Dot(rel, p.forward);
            Vector2 v = new Vector2(x, y) / RadarRange;
            bool outside = v.magnitude > 1f;
            if (outside) v = v.normalized;
            if (outside && rel.magnitude > RadarRange * 2.2f) continue;   // far away: don't clutter the rim

            var dot = GetRadarDot(used++);
            bool drone = e.GetComponent<EnemyHome>() != null && e.GetComponent<EnemyHome>().isDrone;
            Color col = drone ? Yellow : Red;
            col.a = outside ? 0.45f : 1f;
            dot.color = col;
            dot.rectTransform.anchoredPosition = v * _radarRadius * 0.96f;
            dot.rectTransform.sizeDelta = Vector2.one * (drone ? 12f : 10f);
            dot.gameObject.SetActive(true);
        }
        for (int i = used; i < _radarDots.Count; i++) _radarDots[i].gameObject.SetActive(false);
    }

    Image GetRadarDot(int index)
    {
        while (_radarDots.Count <= index)
        {
            var img = Box(_radar, "Dot" + _radarDots.Count, CC, CC, Vector2.zero, new Vector2(10, 10), Red);
            img.sprite = Circle();
            _radarDots.Add(img);
        }
        return _radarDots[index];
    }

    // ── Banner ────────────────────────────────────────────────────────────

    private float _bannerAge;

    void RefreshBanner()
    {
        if (_bannerTimer <= 0f) return;
        _bannerAge += Time.unscaledDeltaTime;
        // Slam in from large with a little overshoot, then settle
        float k = Mathf.Clamp01(_bannerAge / 0.4f);
        _banner.transform.localScale = Vector3.one * Mathf.LerpUnclamped(1.35f, 1f, UiAnim.EaseOutBack(k));
        _bannerTimer -= Time.unscaledDeltaTime;
        if (_bannerTimer <= 0f) _banner.SetActive(false);
    }

    public void ShowEvent(string message, float duration = 4f)
    {
        _bannerText.text = message;
        _bannerAge = 0f;
        _bannerTimer = duration;
        _banner.SetActive(true);
    }
}
}
