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
        private Transform _originalParent;

        private void Awake()
        {
            // Resolved automatically so the prefab cannot be half-wired.
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

        /// <summary>
        /// Snaps to the anchor and parents to it, so the player rides the boat with no platform
        /// maths at all while seated. Returns false if already at a station.
        /// </summary>
        public bool TryOccupy(Transform anchor)
        {
            if (anchor == null || IsOccupyingStation || _movement == null || _characterController == null)
            {
                return false;
            }

            _currentAnchor = anchor;
            _originalParent = transform.parent;

            // The controller must be switched off before the transform is moved, otherwise it
            // fights the assignment and the player is left jittering at the seat.
            _characterController.enabled = false;
            _movement.enabled = false;

            transform.SetParent(anchor, false);
            transform.localPosition = Vector3.zero;
            transform.localRotation = Quaternion.identity;

            // Dropped explicitly rather than left to whichever script order happens to run first:
            // if the rider kept its reference here it would survive the whole seated period.
            if (_platformRider != null)
            {
                _platformRider.ResetTracking();
            }

            return true;
        }

        public void Release()
        {
            if (!IsOccupyingStation)
            {
                return;
            }

            transform.SetParent(_originalParent, true);

            // Drop any roll and pitch inherited from a rocking hull; a CharacterController that is
            // not upright behaves badly.
            transform.rotation = Quaternion.Euler(0f, transform.eulerAngles.y, 0f);

            // Reset before walking resumes, so the first frame back can only re-acquire the deck and
            // return a zero displacement. Without this the first Move of the frame would difference
            // the new walking position against a reference taken while parented, and on a turning
            // hull that difference is large enough to throw the player off the map.
            if (_platformRider != null)
            {
                _platformRider.ResetTracking();
            }

            _characterController.enabled = true;
            _movement.enabled = true;

            _currentAnchor = null;
            _originalParent = null;
        }
    }
}
