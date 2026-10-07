
namespace OrbitRush
{
/// <summary>
/// One place to size the characters relative to the map. Everything attached to the player / an enemy (collider,
/// model, camera pivot, weapon mount, health bar, muzzle) is a child of its root, so scaling the root shrinks them all
/// together. Speeds, jump force and gravity are NOT scaled — the game plays the same, the map just looks bigger.
/// 1 = the original size (a 2 m tall character).
/// </summary>
public static class CharacterScale
{
    public const float Player = 0.6f;
    public const float Enemy = 0.6f;
}
}
