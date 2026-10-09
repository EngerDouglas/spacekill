using UnityEngine;

namespace OrbitRush
{

/// <summary>The player's coin balance. Lives on the player object; created on demand by <see cref="Local"/>.</summary>
public class PlayerWallet : MonoBehaviour
{
    public const int StartingCoins = 40;

    public int Coins { get; private set; } = StartingCoins;

    /// <summary>(new total, change): positive when coins are collected, negative when spent.</summary>
    public event System.Action<int, int> Changed;

    public void Add(int amount)
    {
        if (amount <= 0) return;
        Coins += amount;
        Changed?.Invoke(Coins, amount);
    }

    public bool TrySpend(int amount)
    {
        if (amount <= 0 || Coins < amount) return false;
        Coins -= amount;
        Changed?.Invoke(Coins, -amount);
        return true;
    }

    private static PlayerWallet _local;

    /// <summary>The human player's wallet (null until the player exists).</summary>
    public static PlayerWallet Local
    {
        get
        {
            if (_local != null) return _local;
            var controller = FindFirstObjectByType<PlayerController>();
            if (controller == null) return null;
            _local = controller.GetComponent<PlayerWallet>();
            if (_local == null) _local = controller.gameObject.AddComponent<PlayerWallet>();
            return _local;
        }
    }
}
}
