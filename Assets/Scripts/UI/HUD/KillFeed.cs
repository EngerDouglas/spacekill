using System;

namespace OrbitRush
{

/// <summary>
/// Global "who killed whom" event for the HUD's kill feed. Gameplay code raises it when a player or an enemy dies;
/// the HUD subscribes and shows the last few lines. Labels are display text (e.g. "TÚ", "JUGADOR 2", "DRON").
/// </summary>
public static class KillFeed
{
    /// <summary>killer, victim, weapon name (empty when unknown or environmental).</summary>
    public static event Action<string, string, string> Killed;

    public static void Raise(string killer, string victim, string weapon)
        => Killed?.Invoke(killer ?? "", victim ?? "", weapon ?? "");
}
}
