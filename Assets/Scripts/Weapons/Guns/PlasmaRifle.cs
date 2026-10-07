using UnityEngine;

namespace OrbitRush
{

/// <summary>
/// Automatic plasma rifle with overheat system.
/// damage: 9 | fireRate: 0.1s | overheats after 30 shots
/// </summary>
public class PlasmaRifle : WeaponBase
{
    [Header("Heat")]
    public float heatPerShot = 3.5f;
    public float maxHeat = 100f;
    public float cooldownRate = 20f;
    public float overheatPenalty = 3f;  // seconds locked when maxed

    private float _currentHeat;
    private bool _overheated;
    private float _overheatTimer;

    protected override string DefinitionName => "PlasmaRifle";

    protected override void Update()
    {
        base.Update();

        if (_overheated)
        {
            _overheatTimer -= Time.deltaTime;
            if (_overheatTimer <= 0f)
            {
                _overheated = false;
                _currentHeat = 0f;
            }
            return;
        }

        // Cool down when not firing
        _currentHeat = Mathf.Max(0f, _currentHeat - cooldownRate * Time.deltaTime);
    }

    protected override void OnFire()
    {
        if (_overheated) return;

        SpawnProjectile(GetAimDirection());

        _currentHeat += heatPerShot;
        if (_currentHeat >= maxHeat)
        {
            _overheated = true;
            _overheatTimer = overheatPenalty;
            Debug.Log("Plasma Rifle overheated!");
        }
    }

    public float HeatPercent => _currentHeat / maxHeat;
    public bool IsOverheated => _overheated;
    public float OverheatCooldown => _overheatTimer / overheatPenalty;
}
}
