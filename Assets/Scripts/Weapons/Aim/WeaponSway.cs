using UnityEngine;

namespace OrbitRush
{

/// <summary>
/// Procedural "held weapon" feel — bobs and sways the weapon mount based on
/// the player's movement, and idles gently when standing still. No rigging,
/// bones, or animation clips needed; this is the standard trick shooters use
/// for weapon feel independent of the character's own body animation.
///
/// Attach to the WeaponMount transform (the pivot picked-up weapons are
/// parented to under WeaponInventory) — it must be a descendant of the
/// player's Rigidbody so it can read movement speed.
/// </summary>
public class WeaponSway : MonoBehaviour
{
    [Header("Idle")]
    public float idleSwayAmount = 0.015f;
    public float idleSwaySpeed = 1.2f;

    [Header("Movement Bob")]
    public float bobAmount = 0.03f;
    public float bobSpeed = 6f;
    [Tooltip("Player speed (world units/sec) at which bob intensity maxes out.")]
    public float maxSpeedForBob = 12f;

    [Header("Smoothing")]
    public float followSpeed = 10f;

    private Vector3 _restLocalPosition;
    private Quaternion _restLocalRotation;
    private Rigidbody _bodyRb;
    private float _bobTimer;

    void Start()
    {
        _restLocalPosition = transform.localPosition;
        _restLocalRotation = transform.localRotation;
        _bodyRb = GetComponentInParent<Rigidbody>();
    }

    void Update()
    {
        float speed = _bodyRb != null ? _bodyRb.linearVelocity.magnitude : 0f;
        float speedT = Mathf.Clamp01(speed / maxSpeedForBob);

        // Idle sway — gentle drift that fades out as the player picks up speed.
        float idleT = 1f - speedT;
        Vector3 idleOffset = new Vector3(
            Mathf.Sin(Time.time * idleSwaySpeed) * idleSwayAmount,
            Mathf.Sin(Time.time * idleSwaySpeed * 2f) * idleSwayAmount * 0.5f,
            0f) * idleT;

        // Movement bob — a little figure-eight that speeds up and grows with velocity.
        _bobTimer += Time.deltaTime * bobSpeed * (0.4f + speedT);
        Vector3 bobOffset = new Vector3(
            Mathf.Cos(_bobTimer) * bobAmount * speedT,
            Mathf.Abs(Mathf.Sin(_bobTimer)) * bobAmount * speedT,
            0f);

        Vector3 targetPos = _restLocalPosition + idleOffset + bobOffset;
        transform.localPosition = Vector3.Lerp(transform.localPosition, targetPos, followSpeed * Time.deltaTime);
        transform.localRotation = Quaternion.Slerp(transform.localRotation, _restLocalRotation, followSpeed * Time.deltaTime);
    }
}
}
