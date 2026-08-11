using FishingZone.Core;
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

        [SerializeField]
        private bool _logDiagnostics;

        [SerializeField]
        private float _diagnosticHeartbeatSeconds = 2f;

        private float _nextHeartbeatTime;
        private int _clearsSinceLastReport;

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

        public void ResetTracking()
        {
            ClearPlatform();
        }

        private void LateUpdate()
        {
            if (!CanRide())
            {
                ReportClear(_characterController == null || !_characterController.enabled
                    ? "CharacterController disabled (seated at a station?)"
                    : "detached by a jump");
                ClearPlatform();
                return;
            }

            Transform platform = FindPlatform();
            if (platform == null)
            {
                ReportClear("downward probe found nothing on the platform layers");
                ClearPlatform();
                return;
            }

            Matrix4x4 currentPose = Matrix4x4.TRS(platform.position, platform.rotation, Vector3.one);

            if (platform != CurrentPlatform || !_hasSample)
            {
                CurrentPlatform = platform;
                StorePose(platform, currentPose);
                ReportAcquired(platform);
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
            ReportHeartbeat(platform, delta);
        }

        private void ReportAcquired(Transform platform)
        {
            if (!_logDiagnostics)
            {
                return;
            }

            Rigidbody body = platform.GetComponent<Rigidbody>();
            GameLog.Info(LogCategory.Network,
                $"RIDER acquired '{platform.name}' " +
                $"| layer '{LayerMask.LayerToName(platform.gameObject.layer)}' " +
                $"| rigidbody {(body == null ? "NONE" : body.name + (body.isKinematic ? " (kinematic)" : " (dynamic)"))} " +
                $"| platformY {platform.position.y:F3} playerY {transform.position.y:F3} " +
                $"| clears since last acquire: {_clearsSinceLastReport}");

            _clearsSinceLastReport = 0;
        }

        private void ReportClear(string reason)
        {
            if (!_hasSample)
            {
                return;
            }

            _clearsSinceLastReport++;

            if (!_logDiagnostics)
            {
                return;
            }

            Vector3 origin = _characterController != null
                ? transform.TransformPoint(_characterController.center)
                : transform.position;
            float distance = _characterController != null
                ? (_characterController.height * 0.5f) + _groundProbeDistance
                : 0f;

            GameLog.Warn(LogCategory.Network,
                $"RIDER lost the deck: {reason} " +
                $"| probe from {origin} down {distance:F3} " +
                $"| mask {_platformLayers.value} " +
                $"| playerPos {transform.position} " +
                $"| grounded {(_characterController != null && _characterController.enabled && _characterController.isGrounded)}");
        }

        private void ReportHeartbeat(Transform platform, Vector3 delta)
        {
            if (!_logDiagnostics || Time.unscaledTime < _nextHeartbeatTime)
            {
                return;
            }

            _nextHeartbeatTime = Time.unscaledTime + Mathf.Max(_diagnosticHeartbeatSeconds, 0.25f);

            GameLog.Info(LogCategory.Network,
                $"RIDER riding '{platform.name}' " +
                $"| carried delta {delta.magnitude:F4} " +
                $"| platformPos {platform.position} " +
                $"| playerPos {transform.position} " +
                $"| player above deck {(transform.position.y - platform.position.y):F3}");
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
