using FishingZone.Core;
using UnityEngine;
using UnityEngine.InputSystem;

namespace FishingZone.Boat
{
    /// <summary>
    /// Throttle, steering and braking for the hull. Not a simulation: the target is a boat that
    /// feels slightly heavy and carries its momentum through turns.
    ///
    /// Input is turned into intent, and only then into forces. When the boat becomes host-driven,
    /// the intent can arrive from the network instead of from the local actions with no change to
    /// the physics below.
    ///
    /// Damping is deliberately never written here: BoatBuoyancy owns linear and angular damping and
    /// rewrites both every FixedUpdate, so braking is applied as a counter-force instead.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class BoatMovement : MonoBehaviour
    {
        [SerializeField]
        private InputActionReference _throttleAction;

        [SerializeField]
        private InputActionReference _steerAction;

        [SerializeField]
        private InputActionReference _brakeAction;

        [SerializeField]
        private float _acceleration = 8f;

        [SerializeField]
        private float _maxSpeed = 12f;

        /// <summary>Reverse is deliberately weaker than forward, as on a real boat.</summary>
        [SerializeField]
        private float _reverseMultiplier = 0.4f;

        [SerializeField]
        private float _turnTorque = 25f;

        /// <summary>
        /// Speed at which steering reaches full authority. Below it the boat turns progressively
        /// less, so it cannot spin on the spot like a turret.
        /// </summary>
        [SerializeField]
        private float _fullTurnAuthoritySpeed = 2f;

        /// <summary>Resistance to sliding sideways. Without it the hull skates through turns.</summary>
        [SerializeField]
        private float _lateralFriction = 4f;

        [SerializeField]
        private float _brakeStrength = 6f;

        public bool IsControlled { get; private set; }

        private Rigidbody _rigidbody;
        private bool _isConfigured;

        private void Awake()
        {
            _rigidbody = GetComponent<Rigidbody>();

            _isConfigured = _throttleAction != null && _steerAction != null && _brakeAction != null;
            if (!_isConfigured)
            {
                GameLog.Error(LogCategory.Input, "BoatMovement is missing a Throttle, Steer or Brake action reference. Assign all three in the Inspector.");
            }
        }

        public void SetControlEnabled(bool isControlled)
        {
            IsControlled = isControlled && _isConfigured;
        }

        private void FixedUpdate()
        {
            ApplyLateralFriction();

            if (!IsControlled)
            {
                return;
            }

            float throttle = _throttleAction.action.ReadValue<float>();
            float steer = _steerAction.action.ReadValue<float>();
            bool isBraking = _brakeAction.action.IsPressed();

            ApplyThrottle(throttle);
            ApplySteering(steer);

            if (isBraking)
            {
                ApplyBrake();
            }
        }

        private void ApplyThrottle(float throttle)
        {
            if (Mathf.Approximately(throttle, 0f))
            {
                return;
            }

            float forwardSpeed = Vector3.Dot(_rigidbody.linearVelocity, transform.forward);
            if (throttle > 0f && forwardSpeed >= _maxSpeed)
            {
                return;
            }

            if (throttle < 0f && forwardSpeed <= -_maxSpeed * _reverseMultiplier)
            {
                return;
            }

            float power = throttle > 0f ? _acceleration : _acceleration * _reverseMultiplier;
            _rigidbody.AddForce(transform.forward * (throttle * power), ForceMode.Acceleration);
        }

        private void ApplySteering(float steer)
        {
            if (Mathf.Approximately(steer, 0f))
            {
                return;
            }

            float speed = HorizontalVelocity.magnitude;
            float authority = Mathf.Clamp01(speed / Mathf.Max(_fullTurnAuthoritySpeed, 0.01f));
            if (authority <= 0f)
            {
                return;
            }

            _rigidbody.AddTorque(Vector3.up * (steer * _turnTorque * authority), ForceMode.Acceleration);
        }

        private void ApplyLateralFriction()
        {
            Vector3 sideways = Vector3.Project(HorizontalVelocity, transform.right);
            _rigidbody.AddForce(-sideways * _lateralFriction, ForceMode.Acceleration);
        }

        private void ApplyBrake()
        {
            Vector3 horizontal = HorizontalVelocity;
            if (horizontal.sqrMagnitude < 0.01f)
            {
                return;
            }

            _rigidbody.AddForce(-horizontal.normalized * _brakeStrength, ForceMode.Acceleration);
        }

        private Vector3 HorizontalVelocity
        {
            get
            {
                Vector3 velocity = _rigidbody.linearVelocity;
                velocity.y = 0f;
                return velocity;
            }
        }
    }
}
