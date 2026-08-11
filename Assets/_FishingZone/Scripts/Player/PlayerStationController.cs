using FishingZone.Core;
using UnityEngine;

namespace FishingZone.Player
{
    /// <summary>
    /// Seats and releases the player at a station anchor.
    /// The player owns this rather than the station, so every future station type — fishing,
    /// observer — reuses the same seating behaviour instead of reaching into player internals.
    ///
    /// Only movement is suspended. Look and interaction stay live, because the player still needs
    /// to see where they are going and to press the key that gets them out again.
    /// </summary>
    public class PlayerStationController : MonoBehaviour
    {
        [SerializeField]
        private PlayerMovement _movement;

        [SerializeField]
        private CharacterController _characterController;

        [SerializeField]
        private PlayerPlatformRider _platformRider;

        public bool IsOccupyingStation => _currentAnchor != null;

        private Transform _currentAnchor;
        private float _lastAnchorYaw;

        private void Awake()
        {
            if (_movement == null)
            {
                _movement = GetComponent<PlayerMovement>();
            }

            if (_characterController == null)
            {
                _characterController = GetComponent<CharacterController>();
            }

            if (_platformRider == null)
            {
                _platformRider = GetComponent<PlayerPlatformRider>();
            }
        }

        public bool TryOccupy(Transform anchor)
        {
            if (anchor == null || IsOccupyingStation || _movement == null || _characterController == null)
            {
                return false;
            }

            _currentAnchor = anchor;
            _characterController.enabled = false;
            _movement.enabled = false;

            transform.position = anchor.position;
            FaceAnchorHeading(anchor);
            _lastAnchorYaw = anchor.eulerAngles.y;

            if (_platformRider != null)
            {
                _platformRider.ResetTracking();
            }

            return true;
        }

        private void LateUpdate()
        {
            if (!IsOccupyingStation)
            {
                return;
            }

            float anchorYaw = _currentAnchor.eulerAngles.y;
            float yawDelta = Mathf.DeltaAngle(_lastAnchorYaw, anchorYaw);
            if (!Mathf.Approximately(yawDelta, 0f))
            {
                transform.Rotate(0f, yawDelta, 0f, Space.World);
            }

            _lastAnchorYaw = anchorYaw;
            transform.position = _currentAnchor.position;
        }

        private void FaceAnchorHeading(Transform anchor)
        {
            Vector3 heading = Vector3.ProjectOnPlane(anchor.forward, Vector3.up);
            if (heading.sqrMagnitude > 0.0001f)
            {
                transform.rotation = Quaternion.LookRotation(heading, Vector3.up);
            }
        }

        public void Release()
        {
            if (!IsOccupyingStation)
            {
                return;
            }

            RestoreUprightHeading();

            if (TryResolveStandingPosition(out Vector3 standingPosition))
            {
                transform.position = standingPosition;
            }
            else
            {
                GameLog.Warn(LogCategory.Input, $"No deck found below the station on '{name}' release; standing position left unchanged.");
            }

            if (_platformRider != null)
            {
                _platformRider.ResetTracking();
            }

            _characterController.enabled = true;
            _movement.enabled = true;
            _currentAnchor = null;
        }

        private void RestoreUprightHeading()
        {
            Vector3 heading = Vector3.ProjectOnPlane(transform.forward, Vector3.up);

            if (heading.sqrMagnitude < 0.0001f)
            {
                heading = Vector3.ProjectOnPlane(transform.up, Vector3.up);
            }

            if (heading.sqrMagnitude < 0.0001f)
            {
                return;
            }

            transform.rotation = Quaternion.LookRotation(heading, Vector3.up);
        }

        private bool TryResolveStandingPosition(out Vector3 standingPosition)
        {
            standingPosition = transform.position;

            if (_characterController == null || _platformRider == null)
            {
                return false;
            }

            float halfHeight = _characterController.height * 0.5f;
            float radius = _characterController.radius;
            float originToBottomSphere = Mathf.Max(halfHeight - radius, 0f) - _characterController.center.y;

            Vector3 capsuleCentre = transform.TransformPoint(_characterController.center);
            float lift = _characterController.height + radius;
            Vector3 castOrigin = capsuleCentre + (Vector3.up * lift);

            if (!Physics.SphereCast(castOrigin, radius, Vector3.down, out RaycastHit hit, lift * 2f,
                    _platformRider.PlatformLayers, QueryTriggerInteraction.Ignore))
            {
                return false;
            }

            if (hit.distance <= 0f)
            {
                return false;
            }

            float restingSphereCentreY = castOrigin.y - hit.distance;
            standingPosition.y = restingSphereCentreY + originToBottomSphere + _characterController.skinWidth;
            return true;
        }
    }
}
