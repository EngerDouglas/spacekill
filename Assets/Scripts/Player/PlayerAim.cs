using UnityEngine;

namespace OrbitRush
{

/// <summary>
/// Shared "where is the camera actually aimed at" raycast — used by weapons
/// (WeaponBase.GetAimDirection) and grenade throwing (WeaponInventory) so
/// both bullets and thrown grenades go where the crosshair is pointing,
/// not just in the camera's raw forward direction.
/// </summary>
public static class PlayerAim
{
    /// <summary>
    /// Raycasts from the camera through screen-center. Returns the hit point,
    /// or a far point along the camera's forward direction if nothing was hit.
    /// Colliders belonging to <paramref name="owner"/> (and its children) are
    /// ignored, so a third-person camera looking past its own character
    /// doesn't end up aiming at itself.
    /// </summary>
    public static Vector3 GetAimPoint(Transform owner, float maxRange = 500f)
    {
        var cam = Camera.main;
        if (cam == null)
            return owner != null ? owner.position + owner.forward * maxRange : Vector3.forward * maxRange;

        Vector3 camPos = cam.transform.position;
        Vector3 camForward = cam.transform.forward;
        Vector3 aimPoint = camPos + camForward * maxRange;

        var hits = Physics.RaycastAll(camPos, camForward, maxRange);
        float closestDist = float.MaxValue;
        foreach (var hit in hits)
        {
            if (owner != null && hit.collider.transform.IsChildOf(owner)) continue;
            if (hit.collider.isTrigger) continue;   // pickup / pickup-range volumes aren't things to aim at
            if (hit.distance < closestDist)
            {
                closestDist = hit.distance;
                aimPoint = hit.point;
            }
        }

        return aimPoint;
    }
}
}
