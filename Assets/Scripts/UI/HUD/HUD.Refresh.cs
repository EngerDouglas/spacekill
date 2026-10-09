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

        var stats = player.GetComponent<PlayerStats>();
        if (stats != _stats)
        {
            if (_stats != null) _stats.HitFrom -= OnPlayerHit;
            _stats = stats;
            if (_stats != null) _stats.HitFrom += OnPlayerHit;
        }
        _inventory  = player.GetComponent<WeaponInventory>();
        _controller = player.GetComponent<PlayerController>();
        _playerBody = player.GetComponent<Rigidbody>();
    }

    void RefreshHUD()
    {
        if (_stats == null) FindPlayer();

        RefreshVitals();
        RefreshWeapon();
        RefreshPlanet();
        RefreshMatch();
        RefreshKills();
        RefreshCoins();
        RefreshFeed();
        RefreshCrosshair();
        RefreshVignette();
        RefreshDamageArcs();
        RefreshShieldWarning();
        RefreshRadar();
        RefreshBanner();
    }

    // ── Vitals ────────────────────────────────────────────────────────────

    void RefreshVitals()
    {
        if (_stats == null) return;

        // Shield: six segments; the whole row turns red when it is low
        float sp = _stats.ShieldPercent;
        bool low = sp <= 0.33f;
        Color sc = low ? Alert : Cyan;
        for (int i = 0; i < _shieldFill.Length; i++)
        {
            _shieldFill[i].anchorMax = new Vector2(Mathf.Clamp01(sp * 6f - i), 1f);
            _shieldFillImg[i].color = sc;
        }
        _escudoText.text = $"ESCUDO  {Mathf.RoundToInt(sp * 100f)}%";
        _escudoText.color = low ? Alert : Cyan;

        // Health: white, pulsing red when critical
        float hp = _stats.HealthPercent;
        SetBar(_health, hp);
        _health.fillImage.color = hp < 0.30f ? Color.Lerp(Alert, White, 0.5f + 0.5f * Mathf.Sin(Time.time * 9f)) : White;
        _vidaText.text = $"VIDA  {Mathf.CeilToInt(_stats.health)}";
        _vidaText.color = hp < 0.30f ? Alert : White;

        // Energy (stamina). The jetpack row is a placeholder: no jetpack exists in the game yet.
        float st = _stats.StaminaPercent;
        SetBar(_energy, st);
        _energyValue.text = Mathf.RoundToInt(st * 100f).ToString();
        SetBar(_jetpack, 0f);
        _jetpackValue.text = "—";

        // Dash / dodge: the ring refills while the ability recharges
        if (_controller != null) SetAbility(_dashRing, _dashKey, 1f - _controller.DashCooldownPercent);
        SetAbility(_dodgeRing, _dodgeKey, _meleeDodge);
    }

    static void SetAbility(Image ring, Text key, float ready)
    {
        ready = Mathf.Clamp01(ready);
        ring.fillAmount = ready;
        key.color = ready >= 0.999f ? Cyan : new Color(1f, 1f, 1f, 0.40f);
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

        _grenadeQ.text = $"<color=#4FD6FF>Q</color>  {GrenadeLabel(_inventory.equippedGrenade, "Granada")}  ×{_inventory.grenadeCount}";
        _grenadeF.text = $"<color=#4FD6FF>F</color>  {GrenadeLabel(_inventory.specialBomb, "Especial")}  ×{_inventory.specialCount}";

        var weapon = _inventory.ActiveWeapon;
        if (weapon == null || weapon.IsMelee)
        {
            _weaponName.text = weapon == null ? "SIN ARMA" : weapon.weaponName.ToUpper();
            IdleAmmo("—");
            _heatGroup.SetActive(false);
            return;
        }

        if (_weaponName.text != weapon.weaponName.ToUpper()) _weaponName.text = weapon.weaponName.ToUpper();
        AnimateAmmo(weapon);          // number, pips, reload label (HUD.Ammo.cs)

        var plasma = weapon as PlasmaRifle;
        _heatGroup.SetActive(plasma != null && !weapon.IsReloading);
        if (plasma != null)
        {
            SetBar(_heat, plasma.HeatPercent);
            _heat.fillImage.color = plasma.IsOverheated ? Color.Lerp(Alert, White, Mathf.PingPong(Time.time * 6f, 1f)) : White;
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

    // ── Planet / match / kills ────────────────────────────────────────────

    void RefreshPlanet()
    {
        if (_controller == null) return;
        var planet = _controller.CurrentPlanet;
        if (planet == null) return;

        _planetName.text = planet.planetName.ToUpper();
        _gravityText.text = $"Gravedad {planet.gravityStrength:F1}  ·  {planet.biome}";
    }

    void RefreshMatch()
    {
        var gm = GameManager.Instance;
        if (gm == null) return;

        float t = Mathf.Max(0f, gm.MatchTimeRemaining);
        _timerText.text = $"{Mathf.FloorToInt(t / 60f):00}:{Mathf.FloorToInt(t % 60f):00}";
        _timerText.color = t < 30f ? Color.Lerp(Alert, White, Mathf.PingPong(Time.time * 2f, 1f)) : White;

        switch (gm.gameMode)
        {
            case GameManager.GameMode.TeamDeathmatch: _modeText.text = "EQUIPOS  ·  ROSA vs CIAN"; break;
            case GameManager.GameMode.PlanetCapture:  _modeText.text = "CAPTURA DE PLANETAS"; break;
            default:                                  _modeText.text = "TODOS CONTRA TODOS"; break;
        }

        bool teamMode = gm.IsTeamMode;
        if (_teamText.gameObject.activeSelf != teamMode) _teamText.gameObject.SetActive(teamMode);
        if (teamMode)
            _teamText.text = $"<color=#FF2D9A>ROSA {gm.GetTeamScore(Team.Pink)}</color>   ·   <color=#4FD6FF>CIAN {gm.GetTeamScore(Team.Cyan)}</color>";
    }

    void RefreshKills()
    {
        var gm = GameManager.Instance;
        if (gm == null || _stats == null) return;
        _statsText.text = $"{gm.GetKills(_stats.PlayerId)} bajas   ·   {gm.GetPveScore(_stats.PlayerId)} enemigos";
    }

    // ── Kill feed ─────────────────────────────────────────────────────────

    void OnKill(string killer, string victim, string weapon)
    {
        string line = string.IsNullOrEmpty(weapon) ? $"{killer}  →  {victim}" : $"{killer}  →  {victim}      {weapon}";
        _feed.Insert(0, new FeedLine { text = line, mine = killer == "TÚ", age = 0f });
        while (_feed.Count > FeedLines) _feed.RemoveAt(_feed.Count - 1);
    }

    void RefreshFeed()
    {
        float dt = Time.unscaledDeltaTime;
        foreach (var f in _feed) f.age += dt;
        _feed.RemoveAll(f => f.age > FeedLife);

        for (int i = 0; i < _feedTexts.Length; i++)
        {
            if (i >= _feed.Count) { _feedTexts[i].text = ""; continue; }
            var f = _feed[i];
            Color c = f.mine ? White : Soft;
            c.a *= Mathf.Clamp01((FeedLife - f.age) / 1.5f);      // fade out over the last 1.5 s
            _feedTexts[i].text = f.text;
            _feedTexts[i].color = c;
        }
    }

    // ── Crosshair ─────────────────────────────────────────────────────────

    void RefreshCrosshair()
    {
        // Gap grows with movement speed and with each shot, then eases back.
        float speed = _playerBody != null ? _playerBody.linearVelocity.magnitude : 0f;
        // Each shot kicks the crosshair open (impulse from OnWeaponFired); a damped spring brings it back with a soft rebound
        float kdt = Time.unscaledDeltaTime;
        _kickVel += (-_kick * 220f - _kickVel * 16f) * kdt;
        _kick = Mathf.Clamp(_kick + _kickVel * kdt, 0f, 20f);

        float targetGap = (9f + Mathf.Clamp(speed, 0f, 14f) * 0.7f) * Mathf.Lerp(1f, 0.45f, AimZoom) + _kick;
        _crossGap = Mathf.Lerp(_crossGap, targetGap, Time.unscaledDeltaTime * 14f);

        _ticks[0].anchoredPosition = new Vector2(0, _crossGap + 6f);
        _ticks[1].anchoredPosition = new Vector2(0, -(_crossGap + 6f));
        _ticks[2].anchoredPosition = new Vector2(-(_crossGap + 6f), 0);
        _ticks[3].anchoredPosition = new Vector2(_crossGap + 6f, 0);

        // Red when an enemy is under the crosshair
        bool enemy = IsAimingAtEnemy();
        Color c = enemy ? Alert : White;
        c.a = 0.92f;
        for (int i = 0; i < 4; i++) _tickImages[i].color = c;
        _centerDot.color = enemy ? Alert : Cyan;

        // Hit marker
        if (_hitTimer > 0f)
        {
            _hitTimer -= Time.unscaledDeltaTime;
            float k = Mathf.Clamp01(_hitTimer / _hitDuration);
            var hc = _hitWasKill ? Alert : White; hc.a = k;
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

    // ── Damage direction + shield warning ─────────────────────────────────

    /// <summary>The player was hit at (or from) this world point: light a red arc around the crosshair pointing that way.</summary>
    void OnPlayerHit(Vector3 worldPoint)
    {
        if (_stats == null || _damageArcs == null) return;

        Vector3 rel = worldPoint - _stats.transform.position;
        float x = Vector3.Dot(rel, _stats.transform.right);
        float y = Vector3.Dot(rel, _stats.transform.forward);
        float ang = (x * x + y * y) < 1e-4f ? 0f : Mathf.Atan2(x, y) * Mathf.Rad2Deg;   // 0 = straight ahead, clockwise

        // Reuse an arc already pointing this way; otherwise take the one that has faded the most.
        DamageArc use = null;
        foreach (var a in _damageArcs)
            if (a.life > 0f && Mathf.Abs(Mathf.DeltaAngle(a.angle, ang)) < 25f) { use = a; break; }
        if (use == null)
        {
            float least = float.MaxValue;
            foreach (var a in _damageArcs) if (a.life < least) { least = a.life; use = a; }
        }
        use.angle = ang;
        use.life = 1f;
        use.pivot.localRotation = Quaternion.Euler(0f, 0f, -ang);
    }

    void RefreshDamageArcs()
    {
        if (_damageArcs == null) return;
        foreach (var a in _damageArcs)
        {
            if (a.life <= 0f) continue;
            a.life = Mathf.MoveTowards(a.life, 0f, Time.unscaledDeltaTime / 1.2f);
            a.arc.color = new Color(Alert.r, Alert.g, Alert.b, a.life * a.life * 0.95f);
        }
    }

    void RefreshShieldWarning()
    {
        bool show = _stats != null && _stats.IsAlive && _stats.ShieldPercent <= 0.33f;
        if (_shieldWarn.activeSelf != show) _shieldWarn.SetActive(show);
        if (!show) return;

        _shieldWarnText.text = _stats.ShieldPercent <= 0.001f ? "ESCUDO ROTO" : "ESCUDO BAJO";
        Color c = Alert; c.a = 0.65f + 0.35f * Mathf.Sin(Time.time * 8f);
        _shieldWarnText.color = c;
        _shieldWarnIcon.color = c;
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
            var home = e.GetComponent<EnemyHome>();
            bool drone = home != null && home.isDrone;
            Color col = drone ? Amber : Alert;
            col.a = outside ? 0.45f : 1f;
            dot.color = col;
            dot.rectTransform.anchoredPosition = v * _radarRadius * 0.96f;
            dot.rectTransform.sizeDelta = Vector2.one * (drone ? 9f : 8f);
            dot.gameObject.SetActive(true);
        }
        for (int i = used; i < _radarDots.Count; i++) _radarDots[i].gameObject.SetActive(false);
    }

    Image GetRadarDot(int index)
    {
        while (_radarDots.Count <= index)
        {
            var img = Box(_radar, "Dot" + _radarDots.Count, CC, CC, Vector2.zero, new Vector2(8, 8), Alert);
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
