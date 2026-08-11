using UnityEngine;

namespace FishingZone.Player
{
    /// <summary>
    /// Lets a walking player inherit the motion of the surface under their feet, because a
    /// CharacterController is moved in world space and knows nothing about what it stands on.
    ///
    /// It tracks the platform's own pose from frame to frame and re-applies that rigid motion to
    /// wherever the player currently is. Storing the player's position in platform space instead
    /// would only be correct if the measurement always happened before the player's own walking,
    /// which ties the result to script execution order; this does not.
    ///
    /// Everything runs in LateUpdate, after physics has moved a locally simulated hull and after a
    /// replicated one has been interpolated. Measuring earlier meant that on a client, where the
    /// boat is driven by a network transform during Update, the pose had not advanced since the last
    /// reference was taken and the inherited motion came out as zero every frame.
    ///
    /// The player is never parented. Seating at a station is a separate path that holds the player
    /// on the anchor directly, and it does not run through this component.
    /// </summary>
    public class PlayerPlatformRider : MonoBehaviour
    {
        [SerializeField]
        private CharacterController _characterController;

        [SerializeField]
        private PlayerMovement _movement;

        [SerializeField]
        private LayerMask _platformLayers;

        /// <summary>
        /// How far below the feet still counts as standing on the platform. Large enough to survive
        /// a step or a small bounce, small enough that a jump lets go immediately.
        /// </summary>
        [SerializeField]
        private float _groundProbeDistance = 0.35f;

        public Transform CurrentPlatform { get; private set; }

        /// <summary>
        /// Exposed so a station can resolve a standing position against exactly the same surfaces
        /// this component will then track. Two separate masks would silently drift apart.
        /// </summary>
        public LayerMask PlatformLayers => _platformLayers;

        private Matrix4x4 _lastPlatformPose;
        private float _lastPlatformYaw;
        private bool _hasSample;

        private void Awake()
        {
            if (_characterController == null)
            {
                _characterController = GetComponent<CharacterController>();
            }

            if (_movement == null)
            {
                _movement = GetComponent<PlayerMovement>();
            }
        }

        private void OnDisable()
        {
            ClearPlatform();
        }

        /// <summary>
        /// Forgets the current platform, so the next frame can only re-establish a reference and
        /// inherit nothing.
        ///
        /// Stations call this on both sitting down and standing up. While seated the transform is
        /// driven by the seat rather than by walking, so any reference taken during that time
        /// describes a different regime and must never be differenced against a walking position.
        /// </summary>
        public void ResetTracking()
        {
            ClearPlatform();
        }

        private void LateUpdate()
        {
            if (!CanRide())
            {
                ClearPlatform();
                return;
            }

            Transform platform = FindPlatform();
            if (platform == null)
            {
                ClearPlatform();
                return;
            }

            Matrix4x4 currentPose = Matrix4x4.TRS(platform.position, platform.rotation, Vector3.one);

            if (platform != CurrentPlatform || !_hasSample)
            {
                CurrentPlatform = platform;
                StorePose(platform, currentPose);
                return;
            }

            Vector3 carried = currentPose.MultiplyPoint3x4(_lastPlatformPose.inverse.MultiplyPoint3x4(transform.position));
            Vector3 delta = carried - transform.position;

            float yawDelta = Mathf.DeltaAngle(_lastPlatformYaw, platform.eulerAngles.y);
            if (!Mathf.Approximately(yawDelta, 0f))
            {
                transform.Rotate(0f, yawDelta, 0f, Space.World);
            }

            if (delta.sqrMagnitude > 0f)
            {
                _characterController.Move(delta);
            }

            StorePose(platform, currentPose);
        }

        private bool CanRide()
        {
            if (_characterController == null || !_characterController.enabled)
            {
                return false;
            }

            return _movement == null || !_movement.IsPlatformDetached;
        }

        private Transform FindPlatform()
        {
            Vector3 origin = transform.TransformPoint(_characterController.center);
            float distance = (_characterController.height * 0.5f) + _groundProbeDistance;

            if (!Physics.Raycast(origin, Vector3.down, out RaycastHit hit, distance, _platformLayers))
            {
                return null;
            }

            return hit.rigidbody != null ? hit.rigidbody.transform : hit.collider.transform;
        }

        private void StorePose(Transform platform, Matrix4x4 pose)
        {
            _lastPlatformPose = pose;
            _lastPlatformYaw = platform.eulerAngles.y;
            _hasSample = true;
        }

        private void ClearPlatform()
        {
            CurrentPlatform = null;
            _hasSample = false;
        }
    }
}
