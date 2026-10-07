using UnityEngine;

namespace OrbitRush
{

/// <summary>Shared first-person view-model lookup: the camera to hold things in front of, and how far into first person we are.</summary>
public static class ViewModel
{
    /// <summary>True when the first-person view is (partly) active; gives the eased 0..1 blend and the camera transform.</summary>
    public static bool TryGet(WeaponAim aim, out float blend, out Transform cam)
    {
        float fp = aim != null ? aim.FirstPersonBlend : 0f;
        var c = Camera.main;
        cam = c != null ? c.transform : null;
        blend = Ease.Smooth(fp);
        return fp > 0.001f && cam != null;
    }
}
}
