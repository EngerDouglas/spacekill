using UnityEngine;

namespace OrbitRush
{

/// <summary>A zombie can seize the player: slow movement until they break free by mashing jump.</summary>
public partial class PlayerController
{
    [Header("Zombie grab")]
    [Tooltip("Jump presses needed to break free.")]
    public int grabEscapePresses = 6;
    [Range(0.05f, 1f)] public float grabMoveScale = 0.35f;

    private EnemyAI _grabber;
    private int _escapePresses;

    public bool IsGrabbed => _grabber != null;
    public bool IsGrabbedBy(EnemyAI zombie) => _grabber == zombie;

    public bool TryGrab(EnemyAI zombie)
    {
        if (_grabber != null || zombie == null) return false;
        _grabber = zombie;
        _escapePresses = 0;
        if (HUD.Instance != null) HUD.Instance.ShowEvent("AGARRADO — PULSA SALTO PARA SOLTARTE", 2.5f);
        return true;
    }

    /// <summary>Called when the zombie lets go (or dies / we break free).</summary>
    public void ReleaseGrab(EnemyAI zombie)
    {
        if (_grabber != zombie) return;
        _grabber = null;
    }

    /// <summary>Test hook: one jump press (as the input callback would register it).</summary>
    public void DebugMashJump() => GrabMash();

    /// <summary>Each jump press while held counts toward breaking free.</summary>
    private void GrabMash()
    {
        if (_grabber == null) return;
        _escapePresses++;
        if (_escapePresses >= grabEscapePresses)
        {
            var z = _grabber;
            _grabber = null;
            z.ReleaseGrab();
            if (HUD.Instance != null) HUD.Instance.ShowEvent("¡TE HAS SOLTADO!", 1.2f);
            // A shove away so the zombie is not instantly on top of us again
            _rb.AddForce(-transform.forward * 6f + _planetUp * 3f, ForceMode.VelocityChange);
        }
    }
}
}
