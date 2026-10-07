namespace OrbitRush
{

/// <summary>
/// A custom way for an enemy to die (a drone crashing, a soldier falling over). EnemyStats asks each one in turn;
/// the first that returns true owns the death, otherwise the enemy bursts and disappears. Lets new enemy types
/// plug in their death without EnemyStats knowing about them.
/// </summary>
public interface IDeathBehavior
{
    /// <summary>Starts the death sequence. Returns false to let the next behavior (or the default) handle it.</summary>
    bool TryPlayDeath();
}
}
