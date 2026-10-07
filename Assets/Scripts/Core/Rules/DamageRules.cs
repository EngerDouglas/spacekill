using UnityEngine;

namespace OrbitRush
{

/// <summary>
/// Damage maths shared by everything that has health (player, AI enemies, support robot), so the
/// rules live in one place instead of being copied into each stats class.
/// </summary>
public static class DamageRules
{
    /// <summary>
    /// Drains <paramref name="shield"/> first. Returns the damage left over for health and tells
    /// whether this hit emptied the shield.
    /// </summary>
    public static float AbsorbWithShield(ref float shield, float amount, out bool shieldBroke)
    {
        shieldBroke = false;
        if (shield <= 0f) return amount;
        float absorbed = Mathf.Min(shield, amount);
        shield -= absorbed;
        shieldBroke = shield <= 0f;
        return amount - absorbed;
    }

    public static float Regen(float value, float max, float ratePerSecond, float dt)
        => Mathf.Min(max, value + ratePerSecond * dt);

    /// <summary>Counts a timer down; true while it is still running (regeneration must wait).</summary>
    public static bool TickDelay(ref float timer, float dt)
    {
        if (timer <= 0f) return false;
        timer -= dt;
        return true;
    }
}
}
