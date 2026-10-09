using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace OrbitRush
{

/// <summary>Step 2: satellite view of the chosen planet, pick the drop point with the mouse.</summary>
public partial class DropSelector
{
    private RectTransform _satUi, _infoPanel, _confirmRt, _backRt, _reticle, _marker;
    private Text _latTxt, _lonTxt, _lightTxt, _threatTxt, _stateTxt, _confirmTxt;
    private NeonPanel _confirmPanel;
    private Transform _satWorld;
    private LineRenderer _beam;

    private bool _satConfirmed, _satBack, _hover, _hoverValid, _lastValid = true, _dragged, _pressOnUi;
    private Text _reticleLabel;
    private Vector3 _hoverPoint, _hoverNormal;
    private float _denied;
    private Vector2 _pressPos;
    private float _yaw, _pitch, _dist, _curYaw, _curPitch, _curDist, _pulse, _enemyTimer;
    private EnemyHome[] _enemies = new EnemyHome[0];

    private static Vector3 Dir(float yaw, float pitch)
    {
        float y = yaw * Mathf.Deg2Rad, p = pitch * Mathf.Deg2Rad;
        return new Vector3(Mathf.Cos(p) * Mathf.Cos(y), Mathf.Sin(p), Mathf.Cos(p) * Mathf.Sin(y));
    }

    private IEnumerator SatellitePhase()
    {
        DestroySelectUi();
        _hasTarget = false; _hover = false; _satConfirmed = false; _satBack = false; _dragged = false; _pressOnUi = true;       // ignore the release of the click that picked the planet

        Vector3 centre = _planet.transform.position;
        float r = _planet.radius;
        LightFor(_planet);

        Vector3 toSun = SunDirection(_planet);
        _yaw = Mathf.Atan2(toSun.z, toSun.x) * Mathf.Rad2Deg;
        _pitch = Mathf.Clamp(Mathf.Asin(Mathf.Clamp(toSun.y, -1f, 1f)) * Mathf.Rad2Deg, -45f, 45f);
        _dist = r * 3.4f;
        _curYaw = _yaw; _curPitch = _pitch; _curDist = _dist * 1.5f;      // glides in when it opens

        _satWorld = new GameObject("Satellite").transform;
        _satWorld.SetParent(_world, false);
        BuildGrid();
        BuildSatUi();

        _cam.fieldOfView = 35f;
        _cam.nearClipPlane = Mathf.Max(0.5f, r * 0.04f);
        _cam.clearFlags = RenderSettings.skybox != null ? CameraClearFlags.Skybox : CameraClearFlags.SolidColor;
        _cam.enabled = true;
        PlaceSatCamera();
        yield return FadeTo(0f, 0.45f);

        while (!_satConfirmed && !_satBack)
        {
            UpdateSatellite();
            yield return null;
        }

        Cursor.visible = true;
        yield return FadeTo(1f, 0.3f);
        _cam.enabled = false;
        if (_satUi != null) Destroy(_satUi.gameObject);
        if (_satWorld != null) Destroy(_satWorld.gameObject);
        if (_satBack) RestoreLight();
    }

    private void PlaceSatCamera()
    {
        Vector3 dir = Dir(_curYaw, _curPitch);
        _cam.transform.position = _planet.transform.position + dir * _curDist;
        _cam.transform.rotation = Quaternion.LookRotation(-dir, Vector3.up);
    }

    private void UpdateSatellite()
    {
        float dt = Time.unscaledDeltaTime;
        var mouse = Mouse.current; var kb = Keyboard.current; var pad = Gamepad.current;
        Vector2 mp = MousePos();
        float r = _planet.radius;

        float sens = 0.10f + 0.20f * Mathf.Clamp01((_dist - r) / (r * 3f));
        bool overUi = Over(_infoPanel, mp) || Over(_confirmRt, mp) || Over(_backRt, mp);

        if (mouse != null)
        {
            if (mouse.leftButton.wasPressedThisFrame)
            {
                _pressPos = mp; _dragged = false; _pressOnUi = overUi;
                if (Over(_backRt, mp)) _satBack = true;
                else if (Over(_confirmRt, mp) && _hasTarget) _satConfirmed = true;
            }
            bool rightDrag = mouse.rightButton.isPressed || mouse.middleButton.isPressed;
            bool leftDrag = mouse.leftButton.isPressed && !_pressOnUi;
            if (rightDrag || leftDrag)
            {
                if (rightDrag || (mp - _pressPos).sqrMagnitude > 36f) _dragged = true;
                if (_dragged)
                {
                    Vector2 d = mouse.delta.ReadValue();
                    _yaw -= d.x * sens;
                    _pitch -= d.y * sens;
                }
            }
            float scroll = mouse.scroll.ReadValue().y;
            if (Mathf.Abs(scroll) > 0.01f)
            {
                float step = Mathf.Clamp(Mathf.Abs(scroll) > 10f ? scroll / 120f : scroll, -3f, 3f);
                _dist *= Mathf.Pow(0.88f, step);
            }
        }
        if (kb != null)
        {
            float k = 70f * dt;
            if (kb.aKey.isPressed || kb.leftArrowKey.isPressed) _yaw -= k;
            if (kb.dKey.isPressed || kb.rightArrowKey.isPressed) _yaw += k;
            if (kb.wKey.isPressed || kb.upArrowKey.isPressed) _pitch += k;
            if (kb.sKey.isPressed || kb.downArrowKey.isPressed) _pitch -= k;
            if (kb.eKey.isPressed || kb.equalsKey.isPressed) _dist *= 1f - 1.2f * dt;
            if (kb.qKey.isPressed || kb.minusKey.isPressed) _dist *= 1f + 1.2f * dt;
        }
        if (pad != null)
        {
            Vector2 s = pad.rightStick.ReadValue();
            _yaw += s.x * 90f * dt; _pitch += s.y * 90f * dt;
            _dist *= 1f - (pad.rightTrigger.ReadValue() - pad.leftTrigger.ReadValue()) * 1.0f * dt;
        }
        _pitch = Mathf.Clamp(_pitch, -80f, 80f);
        _dist = Mathf.Clamp(_dist, r * 1.45f, r * 5.5f);

        float f = 1f - Mathf.Exp(-14f * dt);
        _curYaw = Mathf.Lerp(_curYaw, _yaw, f);
        _curPitch = Mathf.Lerp(_curPitch, _pitch, f);
        _curDist = Mathf.Lerp(_curDist, _dist, 1f - Mathf.Exp(-9f * dt));
        PlaceSatCamera();

        // What is under the cursor
        _hover = false; _hoverValid = false;
        if (!overUi && mouse != null)
        {
            _hover = Pick(_cam.ScreenPointToRay(mp), out _hoverPoint, out _hoverNormal);
            _hoverValid = _hover && IsFreeGround(_hoverPoint, _hoverNormal);
        }

        if (mouse != null && mouse.leftButton.wasReleasedThisFrame && !_dragged && !_pressOnUi && _hover)
        {
            if (_hoverValid) { _target = _hoverPoint; _targetUp = _hoverNormal; _hasTarget = true; _pulse = 1f; }
            else _denied = 1f;                                      // the spot is taken: flash the warning
        }
        if (ConfirmPressed() && _hasTarget) _satConfirmed = true;
        if (BackPressed()) _satBack = true;

        UpdateSatUi(mp, overUi, dt);
    }

    /// <summary>Ray against the planet: its ground collider when it has one (real relief), else the perfect sphere.</summary>
    private bool Pick(Ray ray, out Vector3 point, out Vector3 normal)
    {
        Vector3 centre = _planet.transform.position;
        var gc = _planet.groundCollider;
        if (gc != null && gc.enabled && gc.Raycast(ray, out var hit, 60000f))
        {
            point = hit.point; normal = (hit.point - centre).normalized;
            return true;
        }
        Vector3 oc = ray.origin - centre;
        float b = Vector3.Dot(oc, ray.direction);
        float c = oc.sqrMagnitude - _planet.radius * _planet.radius;
        float disc = b * b - c;
        if (disc >= 0f)
        {
            float t = -b - Mathf.Sqrt(disc);
            if (t > 0f)
            {
                normal = (ray.origin + ray.direction * t - centre).normalized;
                point = _planet.GetSurfacePoint(normal);
                return true;
            }
        }
        point = Vector3.zero; normal = Vector3.up;
        return false;
    }

    /// <summary>
    /// True when the spot is bare ground: nothing solid above it (roofs, canopies), nothing within the pod's footprint
    /// (buildings, rocks, trees, machines). Streets and open floor are fine.
    /// </summary>
    private bool IsFreeGround(Vector3 p, Vector3 n)
    {
        var up = n.normalized;
        foreach (var h in Physics.RaycastAll(new Ray(p + up * 80f, -up), 81f, ~0, QueryTriggerInteraction.Ignore))
        {
            if (_planet.IsGround(h.collider) || IsPlayerCollider(h.collider)) continue;
            return false;
        }
        foreach (var h in Physics.OverlapSphere(p + up * 1.6f, 2.6f, ~0, QueryTriggerInteraction.Ignore))
        {
            if (_planet.IsGround(h) || IsPlayerCollider(h)) continue;
            return false;
        }
        return true;
    }

    private bool IsPlayerCollider(Collider c) => _player != null && c.transform.IsChildOf(_player);

    // ── 3D: holographic lat / lon grid ────────────────────────────────────

    private void BuildGrid()
    {
        Vector3 c = _planet.transform.position;
        float rg = _planet.radius * 1.006f;
        float w = Mathf.Max(0.25f, _planet.radius * 0.0025f);
        var col = new Color(0.31f, 0.84f, 1f, 0.2f);
        const int seg = 96;

        for (int lat = -60; lat <= 60; lat += 30)
        {
            var lr = NewLine(_satWorld, "Lat" + lat, col, w, seg, true);
            float y = Mathf.Sin(lat * Mathf.Deg2Rad) * rg, rr = Mathf.Cos(lat * Mathf.Deg2Rad) * rg;
            for (int i = 0; i < seg; i++)
            {
                float a = i / (float)seg * Mathf.PI * 2f;
                lr.SetPosition(i, c + new Vector3(Mathf.Cos(a) * rr, y, Mathf.Sin(a) * rr));
            }
        }
        for (int lon = 0; lon < 180; lon += 30)
        {
            var lr = NewLine(_satWorld, "Lon" + lon, col, w, seg, true);
            float l = lon * Mathf.Deg2Rad;
            for (int i = 0; i < seg; i++)
            {
                float a = i / (float)seg * Mathf.PI * 2f;
                lr.SetPosition(i, c + new Vector3(Mathf.Cos(a) * Mathf.Cos(l), Mathf.Sin(a), Mathf.Cos(a) * Mathf.Sin(l)) * rg);
            }
        }

        _beam = NewLine(_satWorld, "Beam", Cyan, w * 2f);
        _beam.startColor = Cyan; _beam.endColor = new Color(Cyan.r, Cyan.g, Cyan.b, 0f);
        _beam.enabled = false;
    }

    // ── UI ────────────────────────────────────────────────────────────────

    private void BuildSatUi()
    {
        var info = Describe(_planet);
        _satUi = Stretch("Satellite", transform);
        _satUi.SetAsFirstSibling();

        var vig = FullImage("Vignette", _satUi, new Color(0f, 0f, 0f, 0.8f));
        vig.sprite = VignetteSprite();

        // Back button
        _backRt = UiFactory.NewRect("Back", _satUi, TL, TL, new Vector2(60f, -44f), new Vector2(260f, 40f));
        Txt(_backRt, "←  VOLVER   [ESC]", 18, Soft, TextAnchor.MiddleLeft, MC, Vector2.zero, new Vector2(260f, 40f));

        // Title
        Txt(_satUi, "//  DESPLIEGUE   ·   FASE 02 / 02   ·   VISTA SATELITAL", 18, Cyan, TextAnchor.UpperLeft, TL, new Vector2(96f, -110f), new Vector2(1000f, 28f));
        Txt(_satUi, info.name, 64, White, TextAnchor.UpperLeft, TL, new Vector2(96f, -140f), new Vector2(1000f, 84f), true, FontStyle.BoldAndItalic);
        Txt(_satUi, info.biome + "   ·   " + info.sizeTxt + " de radio", 20, Soft, TextAnchor.UpperLeft, TL, new Vector2(96f, -222f), new Vector2(1000f, 30f));

        // Telemetry panel
        var panel = Panel(_satUi, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-60f, 70f), new Vector2(430f, 600f), new Color(1f, 1f, 1f, 0.3f), 22f);
        _infoPanel = panel.rectTransform;
        float w = 430f - 56f;
        Txt(_infoPanel, "TELEMETRÍA", 16, Cyan, TextAnchor.UpperLeft, TL, new Vector2(28f, -22f), new Vector2(w, 24f));
        Box(_infoPanel, TL, TL, new Vector2(28f, -52f), new Vector2(w, 2f), new Color(1f, 1f, 1f, 0.14f));
        _latTxt = DataRow(_infoPanel, 70f, "LATITUD", w);
        _lonTxt = DataRow(_infoPanel, 106f, "LONGITUD", w);
        _lightTxt = DataRow(_infoPanel, 142f, "ILUMINACIÓN", w);
        _threatTxt = DataRow(_infoPanel, 178f, "ENEMIGOS CERCA", w);
        _stateTxt = DataRow(_infoPanel, 214f, "OBJETIVO", w);

        Box(_infoPanel, TL, TL, new Vector2(28f, -262f), new Vector2(w, 2f), new Color(1f, 1f, 1f, 0.14f));
        Txt(_infoPanel, "PERFIL DEL PLANETA", 16, Cyan, TextAnchor.UpperLeft, TL, new Vector2(28f, -278f), new Vector2(w, 24f));
        StatRow(_infoPanel, 28f, 316f, w, "TAMAÑO", info.size, info.sizeTxt, Cyan);
        StatRow(_infoPanel, 28f, 346f, w, "GRAVEDAD", info.grav, info.gravTxt, Cyan);
        StatRow(_infoPanel, 28f, 376f, w, "AMENAZA", info.threat, info.threatTxt, Color.Lerp(Cyan, Danger, info.threat));
        StatRow(_infoPanel, 28f, 406f, w, "ARMAMENTO", info.loot, info.lootTxt, Cyan);
        Txt(_infoPanel, info.desc, 15, Soft, TextAnchor.UpperLeft, TL, new Vector2(28f, -452f), new Vector2(w, 110f));

        // Confirm button
        _confirmPanel = Panel(_satUi, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-60f, 60f), new Vector2(430f, 76f), Faint, 18f);
        _confirmRt = _confirmPanel.rectTransform;
        _confirmTxt = Txt(_confirmRt, "MARCA UN PUNTO EN EL PLANETA", 20, Soft, TextAnchor.MiddleCenter, MC, Vector2.zero, new Vector2(400f, 40f), true, FontStyle.BoldAndItalic);

        Txt(_satUi, "ARRASTRA  girar      ·      RUEDA  zoom      ·      CLIC  marcar suelo libre      ·      ENTER  confirmar",
            16, Soft, TextAnchor.MiddleLeft, new Vector2(0f, 0f), new Vector2(96f, 48f), new Vector2(1200f, 26f)).rectTransform.pivot = new Vector2(0f, 0.5f);

        // Cursor reticle + locked marker (positioned from screen coordinates, so anchored bottom-left)
        var bl = Vector2.zero;
        _reticle = UiFactory.NewRect("Reticle", _satUi, bl, MC, Vector2.zero, new Vector2(64f, 64f));
        var rimg = _reticle.gameObject.AddComponent<Image>(); rimg.sprite = RingSprite(); rimg.color = new Color(1f, 1f, 1f, 0.9f); rimg.raycastTarget = false;
        Box(_reticle, MC, MC, Vector2.zero, new Vector2(4f, 4f), White);
        Box(_reticle, MC, MC, new Vector2(0f, 44f), new Vector2(2f, 16f), White);
        Box(_reticle, MC, MC, new Vector2(0f, -44f), new Vector2(2f, 16f), White);
        Box(_reticle, MC, MC, new Vector2(44f, 0f), new Vector2(16f, 2f), White);
        Box(_reticle, MC, MC, new Vector2(-44f, 0f), new Vector2(16f, 2f), White);

        _reticleLabel = Txt(_reticle, "ZONA OCUPADA", 15, Danger, TextAnchor.MiddleCenter, MC, new Vector2(0f, -66f), new Vector2(220f, 22f), true, FontStyle.Bold);

        _marker = UiFactory.NewRect("Marker", _satUi, bl, MC, Vector2.zero, new Vector2(84f, 84f));
        var mimg = _marker.gameObject.AddComponent<Image>(); mimg.sprite = RingSprite(); mimg.color = Cyan; mimg.raycastTarget = false;
        Box(_marker, MC, MC, Vector2.zero, new Vector2(8f, 8f), Cyan);
        Txt(_marker, "PUNTO DE CAÍDA", 16, Cyan, TextAnchor.MiddleCenter, MC, new Vector2(0f, 64f), new Vector2(240f, 24f), true, FontStyle.Bold);
        _marker.gameObject.SetActive(false);
        _reticle.gameObject.SetActive(false);

        _fade.transform.SetAsLastSibling();
    }

    private static Text DataRow(Transform parent, float y, string label, float w)
    {
        Txt(parent, label, 14, Soft, TextAnchor.MiddleLeft, TL, new Vector2(28f, -y), new Vector2(180f, 24f));
        return Txt(parent, "—", 18, White, TextAnchor.MiddleRight, TL, new Vector2(28f + w - 200f, -y), new Vector2(200f, 24f), true, FontStyle.Bold);
    }

    private void UpdateSatUi(Vector2 mp, bool overUi, float dt)
    {
        float scale = _canvas.scaleFactor > 0.01f ? _canvas.scaleFactor : 1f;
        bool showReticle = _hover && !overUi;
        _reticle.gameObject.SetActive(showReticle);
        Cursor.visible = !showReticle;
        if (showReticle)
        {
            _reticle.anchoredPosition = mp / scale;
            if (_hoverValid != _lastValid || _denied > 0f)
            {
                _lastValid = _hoverValid;
                Color rc = _hoverValid ? new Color(1f, 1f, 1f, 0.9f) : Danger;
                foreach (var im in _reticle.GetComponentsInChildren<Image>()) im.color = rc;
            }
            _reticleLabel.gameObject.SetActive(!_hoverValid);
            _reticleLabel.color = new Color(Danger.r, Danger.g, Danger.b, 0.7f + 0.3f * Mathf.Sin(Time.unscaledTime * 14f) * Mathf.Clamp01(_denied * 3f));
            _reticle.localScale = Vector3.one * (1f + 0.25f * _denied);
        }
        _denied = Mathf.MoveTowards(_denied, 0f, dt * 2.5f);

        // Readouts follow the cursor, or the locked point when the cursor is elsewhere
        bool have = _hover || _hasTarget;
        Vector3 n = _hover ? _hoverNormal : _targetUp;
        Vector3 p = _hover ? _hoverPoint : _target;
        if (have)
        {
            float lat = Mathf.Asin(Mathf.Clamp(n.y, -1f, 1f)) * Mathf.Rad2Deg;
            float lon = Mathf.Atan2(n.z, n.x) * Mathf.Rad2Deg;
            _latTxt.text = $"{Mathf.Abs(lat):0.0}° {(lat >= 0f ? "N" : "S")}";
            _lonTxt.text = $"{Mathf.Abs(lon):0.0}° {(lon >= 0f ? "E" : "O")}";
            bool day = Vector3.Dot(n, SunDirection(_planet)) > 0f;
            _lightTxt.text = day ? "DÍA" : "NOCHE";
            _lightTxt.color = day ? White : Cyan;

            _enemyTimer -= dt;
            if (_enemyTimer <= 0f) { _enemyTimer = 0.5f; _enemies = FindObjectsByType<EnemyHome>(FindObjectsSortMode.None); }
            int near = 0;
            foreach (var e in _enemies) if (e != null && e.planet == _planet && (e.transform.position - p).sqrMagnitude < 90f * 90f) near++;
            _threatTxt.text = near.ToString();
            _threatTxt.color = near > 0 ? Danger : White;
        }
        else
        {
            _latTxt.text = _lonTxt.text = _lightTxt.text = _threatTxt.text = "—";
            _lightTxt.color = _threatTxt.color = White;
        }
        if (_hover && !_hoverValid) { _stateTxt.text = "OCUPADO"; _stateTxt.color = Danger; }
        else { _stateTxt.text = _hasTarget ? "FIJADO" : "SIN FIJAR"; _stateTxt.color = _hasTarget ? Cyan : Soft; }

        // Locked marker + beam
        _pulse = Mathf.MoveTowards(_pulse, 0f, dt * 2.5f);
        bool markerVisible = false;
        if (_hasTarget)
        {
            Vector3 toCam = (_cam.transform.position - _target).normalized;
            markerVisible = Vector3.Dot(_targetUp, toCam) > 0.03f;
            if (markerVisible)
            {
                Vector3 sp = _cam.WorldToScreenPoint(_target);
                markerVisible = sp.z > 0f;
                _marker.anchoredPosition = new Vector2(sp.x, sp.y) / scale;
                float s = 1f + 0.12f * Mathf.Sin(Time.unscaledTime * 4f) + 0.6f * _pulse;
                _marker.localScale = Vector3.one * s;
            }
            if (_beam != null)
            {
                float dist = Vector3.Distance(_cam.transform.position, _target);
                float w = dist * 0.003f;
                _beam.startWidth = _beam.endWidth = w;
                _beam.SetPosition(0, _target);
                _beam.SetPosition(1, _target + _targetUp * (_planet.radius * 0.5f));
                _beam.enabled = true;
            }
        }
        _marker.gameObject.SetActive(markerVisible);

        // Confirm button
        Color want = _hasTarget ? Cyan : Faint;
        if (_confirmPanel.border != want) _confirmPanel.SetColors(want, _confirmPanel.fillTop, _confirmPanel.fillBottom);
        _confirmTxt.text = _hasTarget ? "CONFIRMAR DESPLIEGUE   [ENTER]" : "MARCA UN PUNTO EN EL PLANETA";
        _confirmTxt.color = _hasTarget ? White : Soft;
    }
}
}
