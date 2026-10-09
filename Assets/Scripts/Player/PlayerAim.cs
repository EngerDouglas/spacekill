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
    /// <summary>Temporary diagnostic: logs which collider the aim ray hits close by while the camera looks level.</summary>
    public static bool DebugLog = true;

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

        // Start the ray level with the character: anything between the camera and the player (the camera sits
        // behind and above them) is never what the crosshair is pointing at.
        float startOffset = owner != null ? Mathf.Max(0f, Vector3.Dot(owner.position - camPos, camForward)) : 0f;
        Vector3 origin = camPos + camForward * startOffset;
        float range = maxRange - startOffset;

        var hits = Physics.RaycastAll(origin, camForward, range);
        float closestDist = float.MaxValue;
        Collider closest = null;
        foreach (var hit in hits)
        {
            var col = hit.collider;
            if (owner != null && col.transform.IsChildOf(owner)) continue;
            if (col.isTrigger) continue;   // pickup / pickup-range volumes aren't things to aim at
            if (col.GetComponentInParent<Projectile>() != null) continue;      // bolts in flight
            if (col.GetComponentInParent<SupportRobot>() != null) continue;    // the trailing robot
            if (hit.distance < closestDist)
            {
                closestDist = hit.distance;
                aimPoint = hit.point;
                closest = col;
            }
        }

        if (DebugLog && closest != null && closestDist < 40f && Mathf.Abs(Vector3.Dot(camForward, owner != null ? owner.up : Vector3.up)) < 0.2f)
            Debug.Log($"[Aim] near hit with level camera: {closest.name} ({closest.GetType().Name}, layer {LayerMask.LayerToName(closest.gameObject.layer)}) at {closestDist:F1} m | screen {Screen.width}x{Screen.height} camRect {cam.pixelRect} aimPoint px {cam.WorldToScreenPoint(aimPoint)} cam pitch vs up {90f - Vector3.Angle(camForward, owner != null ? owner.up : Vector3.up):F1} deg height above ground {Vector3.Distance(camPos, aimPoint) * 0f + (owner != null ? Vector3.Dot(camPos - owner.position, owner.up) : 0f):F1} m");

        return aimPoint;
    }
}
}
