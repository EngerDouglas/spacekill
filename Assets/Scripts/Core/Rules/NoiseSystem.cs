using System.Collections.Generic;
using UnityEngine;

namespace OrbitRush
{

/// <summary>How loud an action is: the distance (metres) at which AI with normal hearing can notice it.</summary>
public static class Loudness
{
    public const float Walk = 7f;
    public const float Run = 16f;
    public const float Shoot = 55f;
    public const float Explosion = 130f;       // forced landings and explosions: the maximum
}

/// <summary>
/// Everything the player does that makes noise reports here. AI (robots, drones, zombies) listen through
/// <see cref="Heard"/> or poll <see cref="Recent"/>; each listener applies its own hearing multiplier.
/// </summary>
public static class NoiseSystem
{
    public struct Noise
    {
        public Vector3 position;
        public float loudness;          // metres at hearing 1.0
        public GameObject source;       // who made it (can be null)
        public float time;
    }

    /// <summary>Raised for every noise as it happens.</summary>
    public static event System.Action<Noise> Heard;

    private static readonly List<Noise> _recent = new List<Noise>();
    private const float Memory = 6f;

    public static IReadOnlyList<Noise> Recent
    {
        get { Prune(); return _recent; }
    }

    public static void Emit(Vector3 position, float loudness, GameObject source = null)
    {
        var n = new Noise { position = position, loudness = loudness, source = source, time = Time.time };
        Prune();
        _recent.Add(n);
        if (_recent.Count > 64) _recent.RemoveAt(0);
        Heard?.Invoke(n);
    }

    /// <summary>Strongest recent noise a listener at <paramref name="listener"/> with the given hearing multiplier can hear.</summary>
    public static bool TryHear(Vector3 listener, float hearing, float maxAge, out Noise best, GameObject ignore = null)
    {
        best = default;
        float bestScore = 0f;
        foreach (var n in Recent)
        {
            if (Time.time - n.time > maxAge || (ignore != null && n.source == ignore)) continue;
            float reach = n.loudness * hearing;
            float d = Vector3.Distance(listener, n.position);
            if (d > reach) continue;
            float score = (1f - d / reach) * n.loudness;
            if (score > bestScore) { bestScore = score; best = n; }
        }
        return bestScore > 0f;
    }

    private static void Prune()
    {
        for (int i = _recent.Count - 1; i >= 0; i--)
            if (Time.time - _recent[i].time > Memory) _recent.RemoveAt(i);
    }

    /// <summary>Test / reset helper.</summary>
    public static void Clear() { _recent.Clear(); }
}
}
