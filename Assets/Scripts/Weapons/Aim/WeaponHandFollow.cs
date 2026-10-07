using UnityEngine;

namespace OrbitRush
{

/// <summary>
/// Makes the equipped gun follow the animated body: the weapon mount (the pivot WeaponInventory parents guns to)
/// is glued to the character's right hand every frame, so the gun moves with the arms in every animation.
/// The grip is measured once, while the model is still in its rest (T) pose: barrel along the fingers,
/// the gun's top along the thumb. Replaces WeaponSway (the animation provides the sway).
/// </summary>
[DefaultExecutionOrder(300)]       // after CameraRig / WeaponAim: the camera must already be in its final place
public class WeaponHandFollow : MonoBehaviour
{
    [Tooltip("Slide the gun along its barrel axis relative to the palm (metres).")]
    public float forwardOffset = 0.06f;
    public float upOffset = -0.02f;

    [Header("First person view-model")]
    [Tooltip("Where the gun sits on screen in first person, in camera-local units (right, down, forward).")]
    public Vector3 viewModelOffset = new Vector3(0.30f, -0.27f, 0.82f);
    public float viewModelSway = 16f;
    private WeaponAim _aim;
    [Tooltip("How far in front of the lens the sight's reticle sits when aiming (metres).")]
    public float adsDistance = 0.5f;
    private WeaponInventory _inventory;
    private Vector3 _vmPos; private Quaternion _vmRot = Quaternion.identity; private bool _vmInit;

    private Transform _mount, _hand;
    private Vector3 _gripLocalPos;
    private Quaternion _gripLocalRot;
    private bool _ready;

    public void Init(Animator animator)
    {
        var inv = GetComponent<WeaponInventory>();
        if (inv == null) return;
        if (inv.weaponMount == null || inv.weaponMount == transform)
        {
            // No dedicated mount in the scene: make one (guns already held keep working — it starts at the player)
            var go = new GameObject("WeaponMount");
            go.transform.SetParent(transform, false);
            foreach (var w in GetComponentsInChildren<WeaponBase>(true)) w.transform.SetParent(go.transform, true);
            inv.weaponMount = go.transform;
        }
        _mount = inv.weaponMount;
        _hand = animator != null ? animator.GetBoneTransform(HumanBodyBones.RightHand) : null;
        if (_mount == null || _hand == null) return;

        Transform palm = null;
        foreach (Transform t in _hand) if (t.name.Contains("Middle1")) { palm = t; break; }
        Vector3 fingers = palm != null ? (palm.position - _hand.position).normalized : -transform.right;
        Vector3 palmPos = palm != null ? Vector3.Lerp(_hand.position, palm.position, 0.7f) : _hand.position;

        // Rest pose (palm down): fingers point outward, the thumb points forward → gun top = thumb side
        Quaternion gun = Quaternion.LookRotation(fingers, transform.forward);
        Vector3 pos = palmPos + gun * ((Vector3.forward * forwardOffset + Vector3.up * upOffset) * transform.lossyScale.x);
        _gripLocalPos = _hand.InverseTransformPoint(pos);
        _gripLocalRot = Quaternion.Inverse(_hand.rotation) * gun;

        _aim = GetComponent<WeaponAim>();
        _inventory = GetComponent<WeaponInventory>();
        var sway = _mount.GetComponent<WeaponSway>();
        if (sway != null) sway.enabled = false;
        _ready = true;
    }

    void LateUpdate()
    {
        if (!_ready) return;
        Vector3 pos = _hand.TransformPoint(_gripLocalPos);
        Quaternion rot = _hand.rotation * _gripLocalRot;

        // First person: the gun is held low and to the right of the lens (a view-model), with a soft lag behind the camera
        if (ViewModel.TryGet(_aim, out float fp, out Transform cam))
        {
            Vector3 target = cam.TransformPoint(viewModelOffset);
            Quaternion targetRot = cam.rotation * Quaternion.Euler(-1.5f, -2.5f, 0f);

            // Aiming: slide the gun so its sight's reticle lies exactly on the camera's forward axis
            float ads = _aim != null ? _aim.AdsBlend : 0f;
            var weapon = _inventory != null ? _inventory.ActiveWeapon : null;
            var sight = weapon != null ? weapon.GetComponent<WeaponSight>() : null;
            if (ads > 0.001f && sight != null && sight.ReticleAnchor != null)
            {
                Vector3 anchorOffset = Vector3.Scale(_mount.InverseTransformPoint(sight.ReticleAnchor.position), _mount.lossyScale);
                Quaternion adsRot = cam.rotation;
                Vector3 adsPos = cam.position + cam.forward * adsDistance - adsRot * anchorOffset;
                target = Vector3.Lerp(target, adsPos, ads);
                targetRot = Quaternion.Slerp(targetRot, adsRot, ads);
            }
            // Position is damped in CAMERA-LOCAL space (only the ADS slide eases) so the gun travels rigidly with the
            // body — damping it in world space made it trail behind by speed/sway metres while running.
            Vector3 localTarget = cam.InverseTransformPoint(target);
            if (!_vmInit) { _vmPos = localTarget; _vmRot = targetRot; _vmInit = true; }
            float k = 1f - Mathf.Exp(-Mathf.Lerp(viewModelSway, viewModelSway * 1.6f, _aim != null ? _aim.AdsBlend : 0f) * Time.deltaTime);
            _vmPos = Vector3.Lerp(_vmPos, localTarget, k);
            // Rotation keeps a light lag behind the look direction (weapon sway when turning)
            _vmRot = Quaternion.Slerp(_vmRot, targetRot, k);
            pos = Vector3.Lerp(pos, cam.TransformPoint(_vmPos), fp);
            rot = Quaternion.Slerp(rot, _vmRot, fp);
        }
        else _vmInit = false;

        _mount.SetPositionAndRotation(pos, rot);
    }
}
}
