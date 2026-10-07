using UnityEngine;

namespace OrbitRush
{

/// <summary>Which side a damageable belongs to — drives who shoots through whom, without type-sniffing components.</summary>
public enum Faction { Neutral, Player, Enemy, Companion }

/// <summary>
/// Anything that can take damage and die — implemented by PlayerStats, EnemyStats and SupportRobot. Lets weapons,
/// grenades and melee hit them through the same code path without knowing which one they hit.
/// </summary>
public interface IDamageable
{
    bool IsAlive { get; }

    /// <summary>Side this belongs to (see <see cref="FactionRules"/>).</summary>
    Faction Faction { get; }

    /// <summary>
    /// Applies damage. Pass the PlayerStats responsible so a kill can be
    /// credited correctly (PvP kill or PvE score) — null for environmental
    /// damage (meteors, supernova, black holes with no clear attacker).
    /// </summary>
    void TakeDamage(float amount, PlayerStats attacker);

    /// <summary>A projectile struck at this world point (drives the shield ripple). Optional.</summary>
    void RegisterHit(Vector3 worldPoint) { }

    /// <summary>Remembers who/what hurt it, for the death screen. Optional.</summary>
    void NoteSource(string label) { }
}

public static class FactionRules
{
    /// <summary>True when a shot from <paramref name="shooter"/> simply passes through <paramref name="target"/>.</summary>
    public static bool PassesThrough(Faction shooter, Faction target)
        => (shooter == Faction.Companion && target == Faction.Player)     // the support robot's bolts pass through the player
        || (shooter == Faction.Enemy && target == Faction.Enemy);         // AI enemies don't shoot each other

    public static Faction Of(GameObject go)
    {
        if (go == null) return Faction.Neutral;
        var d = go.GetComponent<IDamageable>();
        return d != null ? d.Faction : Faction.Neutral;
    }
}
}
