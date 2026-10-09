using System.Collections.Generic;
using UnityEngine;

namespace OrbitRush
{

/// <summary>
/// Planet Capture mode. Each planet gets a capture point on its surface. The team
/// with more living players inside the zone fills the point; contested points
/// freeze. Owned planets award their team 1 point per second. First team to
/// GameManager.captureScoreLimit wins (or highest score when the timer ends).
///
/// Added automatically by GameManager at match start when gameMode is PlanetCapture —
/// no scene setup needed.
/// </summary>
public class PlanetCaptureMode : MonoBehaviour
{
    [Header("Capture")]
    public float zoneRadius = 9f;
    [Tooltip("Progress per second per net player in the zone (progress runs -1 Cyan .. +1 Pink).")]
    public float captureRate = 0.2f;
    public float pointsPerSecond = 1f;

    private class Point
    {
        public PlanetGravity planet;
        public Vector3 position;
        public float progress;          // -1 = Cyan owns, +1 = Pink owns
        public Team owner = Team.None;
        public Renderer beacon;
        public float zone;              // capture radius for this planet
        public float size = 1f;         // 1 on a small planet, up to 4 on a big one
    }

    private readonly List<Point> _points = new();
    private readonly Dictionary<Team, float> _scoreRemainder = new();
    private GameManager _gm;

    /// <summary>Planets with places put the capture point on their lookout ("captura" zone); the others on top.</summary>
    private static Vector3 CaptureDirection(PlanetGravity planet)
    {
        var zone = PlanetZoneMath.ByRole(planet, "captura");
        return zone != null ? zone.dir : Vector3.up;
    }

    void Start()
    {
        _gm = GameManager.Instance;
        foreach (var planet in FindObjectsByType<PlanetGravity>(FindObjectsSortMode.None))
        {
            // The point sits on the planet's real surface at its "north pole", and the zone grows with big planets
            float size = Mathf.Clamp(planet.radius / 40f, 1f, 4f);
            var p = new Point
            {
                planet = planet,
                position = planet.GetSurfacePoint(CaptureDirection(planet)),
                size = size,
                zone = zoneRadius * size,
            };
            p.beacon = CreateBeacon(p);
            _points.Add(p);
        }
        Debug.Log($"[PlanetCapture] {_points.Count} capture points created.");
    }

    void Update()
    {
        if (_gm == null || !_gm.IsMatchActive) return;

        foreach (var point in _points)
        {
            UpdatePoint(point);
            AwardPoints(point);
        }
    }

    private void UpdatePoint(Point point)
    {
        int pink = 0, cyan = 0;
        foreach (var player in _gm.Players)
        {
            if (player == null || !player.IsAlive) continue;
            if ((player.transform.position - point.position).sqrMagnitude > point.zone * point.zone) continue;
            if (player.team == Team.Pink) pink++;
            else if (player.team == Team.Cyan) cyan++;
        }

        int net = pink - cyan;
        if (net == 0) return; // empty or contested: progress holds

        point.progress = Mathf.Clamp(point.progress + Mathf.Sign(net) * captureRate * Mathf.Abs(net) * Time.deltaTime, -1f, 1f);

        Team newOwner = point.owner;
        if (point.progress >= 1f) newOwner = Team.Pink;
        else if (point.progress <= -1f) newOwner = Team.Cyan;
        else if (Mathf.Abs(point.progress) < 0.05f) newOwner = Team.None;

        if (newOwner != point.owner)
        {
            point.owner = newOwner;
            RefreshBeacon(point);
            if (newOwner != Team.None)
                HUD.Instance?.ShowEvent($"{point.planet.planetName.ToUpper()} CAPTURED BY {TeamUtil.GetName(newOwner)}");
        }
    }

    private void AwardPoints(Point point)
    {
        if (point.owner == Team.None) return;

        _scoreRemainder.TryGetValue(point.owner, out float acc);
        acc += pointsPerSecond * Time.deltaTime;
        int whole = Mathf.FloorToInt(acc);
        _scoreRemainder[point.owner] = acc - whole;
        if (whole > 0) _gm.AddTeamScore(point.owner, whole);
    }

    // ── Beacon visual ─────────────────────────────────────────────────────

    private Renderer CreateBeacon(Point point)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        go.name = $"CaptureBeacon_{point.planet.planetName}";

        // Visual only — must not block movement or shots.
        var col = go.GetComponent<Collider>();
        if (col != null) Destroy(col);

        go.transform.SetParent(transform, false);
        Vector3 up = point.planet.GetSurfaceUp(point.position);
        go.transform.position = point.position + up * (3f * point.size);
        go.transform.rotation = Quaternion.FromToRotation(Vector3.up, up);
        go.transform.localScale = new Vector3(0.5f * point.size, 3f * point.size, 0.5f * point.size);

        var rend = go.GetComponent<Renderer>();
        point.beacon = rend;
        RefreshBeacon(point);
        return rend;
    }

    private void RefreshBeacon(Point point)
    {
        if (point.beacon == null) return;
        Color c = point.owner == Team.None ? new Color(0.8f, 0.8f, 0.8f) : TeamUtil.GetColor(point.owner);
        var block = new MaterialPropertyBlock();
        point.beacon.GetPropertyBlock(block);
        block.SetColor("_BaseColor", c);
        block.SetColor("_Color", c);
        point.beacon.SetPropertyBlock(block);
    }
}
}
