using UnityEngine;

namespace OrbitRush
{

/// <summary>Enemies drop coins when they die: tougher ones drop more, and a chance of Platino or Elite.</summary>
public static class CoinDrops
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Init()
    {
        EnemyStats.Killed -= OnKilled;
        EnemyStats.Killed += OnKilled;
    }

    private static void OnKilled(EnemyStats enemy, PlayerStats attacker)
    {
        if (enemy == null) return;
        float toughness = enemy.maxHealth + enemy.maxShield;

        var controller = enemy.GetComponent<EnemyController>();
        Vector3 up = controller != null ? controller.PlanetUp : enemy.transform.up;

        // The ground below the enemy (flyers die high up)
        Vector3 origin = enemy.transform.position;
        Vector3 ground = origin;
        if (Physics.Raycast(origin + up * 0.5f, -up, out var hit, 60f, ~0, QueryTriggerInteraction.Ignore)) ground = hit.point;

        int gold = 1 + Mathf.FloorToInt(toughness / 70f);
        for (int i = 0; i < gold; i++) Drop(CoinType.Oro, ground, up);
        if (Random.value < Mathf.Clamp(toughness / 600f, 0.05f, 0.5f)) Drop(CoinType.Platino, ground, up);
        if (Random.value < (toughness >= 150f ? 0.12f : 0.02f)) Drop(CoinType.Elite, ground, up);
    }

    private static void Drop(CoinType type, Vector3 ground, Vector3 up)
    {
        Vector3 scatter = Vector3.ProjectOnPlane(Random.insideUnitSphere, up) * 1.6f;
        Coin.Spawn(type, ground + scatter + up * 0.2f, up, null, lifetime: 120f);
    }
}
}
