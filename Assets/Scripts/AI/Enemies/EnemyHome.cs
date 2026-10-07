using UnityEngine;

namespace OrbitRush
{

/// <summary>
/// Remembers which planet an AI enemy belongs to and what kind it is, so GameManager can
/// respawn a replacement of the same kind on that same planet after it dies.
/// </summary>
public class EnemyHome : MonoBehaviour
{
    public PlanetGravity planet;
    public bool isDrone;
}
}
