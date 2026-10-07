using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

namespace OrbitRush
{

/// <summary>
/// Handles the death → respawn loop. PlayerStats raises its Died event when health
/// hits zero. This then:
///   1. lets the death animation play (the body stays visible, controls off, it just falls with gravity),
///   2. shows the DeathScreen with who killed you, your tally and a respawn countdown,
///   3. once the countdown ends waits for a key / click / pad button (or respawns by itself after a while),
///   4. moves the player to a fresh spawn point, restores stats and gives a couple of seconds of spawn protection.
/// </summary>
public class PlayerRespawn : MonoBehaviour
{
    [Header("Respawn")]
    [Tooltip("Seconds of death screen before respawning is allowed.")]
    public float respawnDelay = 4f;
    [Tooltip("If nobody presses anything, respawn automatically after this many extra seconds.")]
    public float autoRespawnAfter = 8f;
    [Tooltip("Invulnerable for this long after respawning.")]
    public float spawnProtection = 2.5f;

    private PlayerStats _stats;
    private PlayerController _controller;
    private WeaponInventory _inventory;
    private Rigidbody _rb;
    private bool _dead;

    void Awake()
    {
        _stats = GetComponent<PlayerStats>();
        _controller = GetComponent<PlayerController>();
        _inventory = GetComponent<WeaponInventory>();
        _rb = GetComponent<Rigidbody>();
    }

    void OnEnable() { if (_stats != null) _stats.Died += OnPlayerDeath; }
    void OnDisable() { if (_stats != null) _stats.Died -= OnPlayerDeath; }

    // Raised by PlayerStats when health hits zero.
    void OnPlayerDeath()
    {
        if (_dead) return;
        _dead = true;
        StartCoroutine(DeathRoutine());
    }

    private IEnumerator DeathRoutine()
    {
        SetAlive(false);

        var gm = GameManager.Instance;
        int kills = gm != null ? gm.GetKills(_stats.PlayerId) : 0;
        int ai = gm != null ? gm.GetPveScore(_stats.PlayerId) : 0;
        var screen = DeathScreen.Get();
        screen.Show(_stats.LastDamageSource, kills, ai);

        float t = 0f;
        while (t < respawnDelay)
        {
            t += Time.unscaledDeltaTime;
            screen.SetCountdown(respawnDelay - t, t / respawnDelay, false);
            yield return null;
        }

        // Ready: wait for the player (or give up waiting)
        float waited = 0f;
        yield return null;       // don't let the key that was mashed during the countdown count
        while (waited < autoRespawnAfter && !RespawnPressed())
        {
            waited += Time.unscaledDeltaTime;
            screen.SetCountdown(0f, 1f, true);
            yield return null;
        }

        screen.Hide();
        Respawn();
    }

    private static bool RespawnPressed()
    {
        var kb = Keyboard.current; var mouse = Mouse.current; var pad = Gamepad.current;
        return (kb != null && (kb.spaceKey.wasPressedThisFrame || kb.enterKey.wasPressedThisFrame))
            || (mouse != null && mouse.leftButton.wasPressedThisFrame)
            || (pad != null && pad.buttonSouth.wasPressedThisFrame);
    }

    private void Respawn()
    {
        var spawn = GameManager.Instance != null ? GameManager.Instance.GetSpawnPoint() : transform;
        _rb.isKinematic = false;
        transform.SetPositionAndRotation(spawn.position, spawn.rotation);
        _rb.position = spawn.position; _rb.rotation = spawn.rotation;
        _rb.linearVelocity = Vector3.zero;
        _rb.angularVelocity = Vector3.zero;

        var melee = GetComponent<MeleeCombat>();
        if (melee != null) melee.ResetState();

        _stats.Respawn();
        _stats.InvulnerableUntil = Time.time + spawnProtection;
        SetAlive(true);
        if (_inventory != null) _inventory.GiveRandomWeapon();      // come back with a random weapon
        _dead = false;
    }

    private void SetAlive(bool alive)
    {
        if (_controller != null) _controller.enabled = alive;
        if (_inventory != null) _inventory.enabled = alive;
        // The body stays visible while dead (the death animation plays); gravity keeps pulling it down.
        if (_rb != null && alive) _rb.isKinematic = false;
        if (!alive && _rb != null) { _rb.linearVelocity *= 0.2f; _rb.angularVelocity = Vector3.zero; }
    }

    void FixedUpdate()
    {
        if (!_dead || _rb == null || _rb.isKinematic) return;
        // Controller is off, so apply planet gravity here and bleed off sideways speed: the body settles where it fell
        Vector3 g = GravitySystem.Instance != null ? GravitySystem.Instance.GetGravityVector(transform.position) : Vector3.zero;
        _rb.AddForce(g, ForceMode.Acceleration);
        Vector3 up = g.sqrMagnitude > 0.001f ? -g.normalized : transform.up;
        Vector3 planar = Vector3.ProjectOnPlane(_rb.linearVelocity, up);
        _rb.AddForce(-planar * 4f, ForceMode.Acceleration);
    }
}
}
