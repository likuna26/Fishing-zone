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

        public bool IsOccupyingStation => _currentAnchor != null;

        private Transform _currentAnchor;
        private Transform _originalParent;

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
        }

        public bool TryOccupy(Transform anchor)
        {
            if (anchor == null || IsOccupyingStation || _movement == null || _characterController == null)
            {
                return false;
            }

            _currentAnchor = anchor;
            _originalParent = transform.parent;

            _characterController.enabled = false;
            _movement.enabled = false;

            transform.SetParent(anchor, false);
            transform.localPosition = Vector3.zero;
            transform.localRotation = Quaternion.identity;

            return true;
        }

        public void Release()
        {
            if (!IsOccupyingStation)
            {
                return;
            }

            transform.SetParent(_originalParent, true);
            transform.rotation = Quaternion.Euler(0f, transform.eulerAngles.y, 0f);

            _characterController.enabled = true;
            _movement.enabled = true;

            _currentAnchor = null;
            _originalParent = null;
        }
    }
}
