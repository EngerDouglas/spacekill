using UnityEngine;

namespace OrbitRush
{

/// <summary>
/// Procedural life for enemies whose model is a single static mesh (no skeleton): a stomping bob and sway while walking,
/// a lean into the run, a recoil kick when firing and a lunge when swinging. Moves only the "Visual" child, never the body.
/// </summary>
public class EnemyModelMotion : MonoBehaviour
{
    public float stepsPerMetre = 0.55f;
    public float bobHeight = 0.07f;
    public float swayDegrees = 4f;
    public float leanDegrees = 7f;
    [Tooltip("Rolls along instead of stepping: no stomping bob, a smooth glide that tilts into acceleration and banks in turns, with a faint engine tremble.")]
    public bool rolling = false;
    [Tooltip("A sphere that rolls: the model turns about its centre as it travels (its pivot must be the centre).")]
    public bool rollBall = false;
    public float ballRadius = 0.8f;

    private Transform _visual;
    private Vector3 _basePos;
    private Quaternion _baseRot;
    private Rigidbody _body;
    private EnemyController _controller;
    private float _phase, _kick, _lunge, _speed, _accelLean, _bank;
    private float _lastSpeed, _lastYaw;
    private Transform _rotorL, _rotorR;
    private float _rotorSpeed = 500f;      // degrees per second

    void Start()
    {
        _visual = transform.Find("Visual");
        _body = GetComponent<Rigidbody>();
        _controller = GetComponent<EnemyController>();
        if (_visual == null) { enabled = false; return; }
        foreach (var t in _visual.GetComponentsInChildren<Transform>(true))
        {
            if (t.name == "Rotor_L") _rotorL = t;
            else if (t.name == "Rotor_R") _rotorR = t;
        }
        _basePos = _visual.localPosition;
        _baseRot = _visual.localRotation;
        var ai = GetComponent<EnemyAI>();
        if (ai != null)
        {
            ai.Fired += () => _kick = 1f;
            ai.Swung += () => _lunge = 1f;
        }
    }

    void LateUpdate()
    {
        if (_visual == null || _body == null) return;
        float dt = Time.deltaTime;
        Vector3 up = _controller != null ? _controller.PlanetUp : transform.up;
        float speed = Vector3.ProjectOnPlane(_body.linearVelocity, up).magnitude;
        _speed = Mathf.Lerp(_speed, speed, 1f - Mathf.Exp(-8f * dt));
        float moving = Mathf.Clamp01(_speed / 1.5f);

        SpinRotors(dt, moving);

        if (rollBall)
        {
            // Roll by the distance travelled: angle = distance / radius, about the axis perpendicular to the motion
            Vector3 v = Vector3.ProjectOnPlane(_body.linearVelocity, up);
            if (v.magnitude > 0.05f)
            {
                float radiusWorld = ballRadius * transform.lossyScale.x;
                Vector3 axis = Vector3.Cross(up, v).normalized;
                _visual.Rotate(axis, v.magnitude * dt / Mathf.Max(0.05f, radiusWorld) * Mathf.Rad2Deg, Space.World);
            }
            _kick = Mathf.MoveTowards(_kick, 0f, dt * 5f);
            return;
        }

        if (rolling)
        {
            // Pitch back when speeding up, forward when braking; bank into turns
            float accel = dt > 0f ? (speed - _lastSpeed) / dt : 0f;
            _lastSpeed = speed;
            _accelLean = Mathf.Lerp(_accelLean, Mathf.Clamp(-accel * 0.8f, -9f, 9f), 1f - Mathf.Exp(-6f * dt));
            float yaw = transform.eulerAngles.y;
            float yawRate = dt > 0f ? Mathf.DeltaAngle(_lastYaw, yaw) / dt : 0f;
            _lastYaw = yaw;
            _bank = Mathf.Lerp(_bank, Mathf.Clamp(-yawRate * 0.06f, -10f, 10f) * moving, 1f - Mathf.Exp(-5f * dt));
            _kick = Mathf.MoveTowards(_kick, 0f, dt * 5f);
            float tremble = Mathf.Sin(Time.time * 47f) * 0.012f * (0.4f + moving);
            _visual.localPosition = _basePos + new Vector3(0f, tremble, -_kick * 0.05f);
            _visual.localRotation = _baseRot * Quaternion.Euler(_accelLean - _kick * 2.5f + leanDegrees * 0.4f * Mathf.Clamp01(_speed / 6f), 0f, _bank);
            return;
        }

        _phase += _speed * stepsPerMetre * dt * Mathf.PI * 2f;
        _kick = Mathf.MoveTowards(_kick, 0f, dt * 5f);
        _lunge = Mathf.MoveTowards(_lunge, 0f, dt * 2.2f);

        float bob = Mathf.Abs(Mathf.Sin(_phase)) * bobHeight * moving;
        float roll = Mathf.Sin(_phase) * swayDegrees * moving;
        float lean = leanDegrees * Mathf.Clamp01(_speed / 6f) + Ease.Smooth(_lunge) * 14f - _kick * 3f;

        _visual.localPosition = _basePos + new Vector3(0f, bob - Ease.Smooth(_lunge) * 0.1f, Ease.Smooth(_lunge) * 0.25f - _kick * 0.05f);
        _visual.localRotation = _baseRot * Quaternion.Euler(lean, 0f, roll);
    }

    /// <summary>Propellers (separate meshes named Rotor_L / Rotor_R) turn about the model's up axis, in opposite directions;
    /// they spin up when the enemy moves and idle slowly otherwise.</summary>
    private void SpinRotors(float dt, float moving)
    {
        if (_rotorL == null && _rotorR == null) return;
        float target = Mathf.Lerp(520f, 1500f, moving) + _kick * 400f;
        _rotorSpeed = Mathf.Lerp(_rotorSpeed, target, 1f - Mathf.Exp(-3f * dt));
        Vector3 axis = _visual.parent != null ? _visual.parent.up : Vector3.up;
        if (_rotorL != null) _rotorL.Rotate(axis, _rotorSpeed * dt, Space.World);
        if (_rotorR != null) _rotorR.Rotate(axis, -_rotorSpeed * dt, Space.World);
    }
}
}
