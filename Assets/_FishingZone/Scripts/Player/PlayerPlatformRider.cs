using UnityEngine;

namespace FishingZone.Player
{
    /// <summary>
    /// Lets a walking player inherit the motion of the surface under their feet, because a
    /// CharacterController is moved in world space and knows nothing about what it stands on.
    ///
    /// Rather than tracking the platform's velocity, it remembers where the player stood in the
    /// platform's own local space and asks each frame where that spot has moved to. That handles
    /// translation, turning and the roll of a bobbing hull in one step, and it stays correct even
    /// when the platform is driven by physics at a different rate from the player.
    ///
    /// The player is never parented here. Parenting is only used while seated at a station, which
    /// is a separate path that does not run through this component.
    /// </summary>
    public class PlayerPlatformRider : MonoBehaviour
    {
        [SerializeField]
        private CharacterController _characterController;

        [SerializeField]
        private LayerMask _platformLayers;

        /// <summary>
        /// How far below the feet still counts as standing on the platform. Large enough to survive
        /// a step or a small bounce, small enough that a jump lets go immediately.
        /// </summary>
        [SerializeField]
        private float _groundProbeDistance = 0.35f;

        public Transform CurrentPlatform { get; private set; }

        private Vector3 _localStandPoint;
        private float _lastPlatformYaw;
        private bool _hasSample;

        private void Awake()
        {
            if (_characterController == null)
            {
                _characterController = GetComponent<CharacterController>();
            }
        }

        private void OnDisable()
        {
            ClearPlatform();
        }

        /// <summary>
        /// Returns the world-space displacement the player should inherit this frame, and applies the
        /// platform's turn to their facing. Called by PlayerMovement so the result lands in the same
        /// Move call as the player's own motion: applying it separately would mean two Move calls per
        /// frame fighting each other over collisions.
        ///
        /// This is a displacement, not a velocity. It must not be scaled by delta time.
        ///
        /// <paramref name="isDetached"/> is supplied by the caller rather than derived here, because
        /// the ground probe cannot tell a standing player from one in the first frames of a jump:
        /// both are still within probe range of the deck. Only the mover knows it has jumped.
        /// Every other way of leaving the deck needs no flag, because the probe simply stops finding
        /// a platform.
        /// </summary>
        public Vector3 ConsumePlatformDelta(bool isDetached)
        {
            if (isDetached)
            {
                ClearPlatform();
                return Vector3.zero;
            }

            Transform platform = FindPlatform();

            if (platform == null)
            {
                ClearPlatform();
                return Vector3.zero;
            }

            // Stepping onto a platform, or back onto one after a jump, only establishes a reference.
            // Returning zero here is what stops the player being snapped by the gap they were airborne for.
            if (platform != CurrentPlatform || !_hasSample)
            {
                CurrentPlatform = platform;
                SamplePlatform();
                return Vector3.zero;
            }

            Vector3 targetPosition = platform.TransformPoint(_localStandPoint);
            Vector3 delta = targetPosition - transform.position;

            // Yaw only. Rolling with a pitching deck would tip the CharacterController over, and yaw
            // composes additively with the look component so the two never fight.
            float yawDelta = Mathf.DeltaAngle(_lastPlatformYaw, platform.eulerAngles.y);
            if (!Mathf.Approximately(yawDelta, 0f))
            {
                transform.Rotate(0f, yawDelta, 0f, Space.World);
            }

            return delta;
        }

        /// <summary>
        /// Re-sampled after every Update has run, so the reference point reflects where the player
        /// ended up once their own movement was applied. Sampling earlier would feed their walking
        /// back in as platform motion.
        /// </summary>
        private void LateUpdate()
        {
            if (CurrentPlatform != null)
            {
                SamplePlatform();
            }
        }

        private Transform FindPlatform()
        {
            if (_characterController == null || !_characterController.enabled)
            {
                return null;
            }

            Vector3 origin = transform.TransformPoint(_characterController.center);
            float distance = (_characterController.height * 0.5f) + _groundProbeDistance;

            if (!Physics.Raycast(origin, Vector3.down, out RaycastHit hit, distance, _platformLayers))
            {
                return null;
            }

            // The hull's collider may sit on a child of the moving root, so follow the Rigidbody
            // when there is one and fall back to the collider's own transform otherwise.
            return hit.rigidbody != null ? hit.rigidbody.transform : hit.collider.transform;
        }

        private void SamplePlatform()
        {
            _localStandPoint = CurrentPlatform.InverseTransformPoint(transform.position);
            _lastPlatformYaw = CurrentPlatform.eulerAngles.y;
            _hasSample = true;
        }

        private void ClearPlatform()
        {
            CurrentPlatform = null;
            _hasSample = false;
        }
    }
}
