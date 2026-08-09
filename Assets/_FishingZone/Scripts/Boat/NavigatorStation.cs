using FishingZone.Core;
using FishingZone.Core.Input;
using FishingZone.Player;
using UnityEngine;

namespace FishingZone.Boat
{
    public class NavigatorStation : MonoBehaviour, IInteractable
    {
        [SerializeField] private BoatMovement _boatMovement;
        [SerializeField] private Transform _standAnchor;
        [SerializeField] private string _enterText = "Take the wheel";
        [SerializeField] private string _exitText = "Leave the wheel";

        private PlayerStationController _occupant;
        private PlayerInteraction _occupantInteraction;
        private GameInput _gameInput;

        public bool IsOccupied => _occupant != null;

        private void Awake()
        {
            if (_boatMovement == null || _standAnchor == null)
            {
                GameLog.Error(LogCategory.Input, "NavigatorStation is missing a Boat Movement or Stand Anchor reference. Assign both in the Inspector.");
            }
        }

        private void OnDisable()
        {
            if (IsOccupied)
            {
                Release();
            }
        }

        public bool CanInteract(GameObject interactor)
        {
            if (_boatMovement == null || _standAnchor == null)
            {
                return false;
            }

            return !IsOccupied || IsOccupant(interactor);
        }

        public string GetInteractionText(GameObject interactor)
        {
            return IsOccupied && IsOccupant(interactor) ? _exitText : _enterText;
        }

        public void Interact(GameObject interactor)
        {
            if (IsOccupied)
            {
                if (IsOccupant(interactor))
                {
                    Release();
                }
                return;
            }

            Occupy(interactor);
        }

        private void Occupy(GameObject interactor)
        {
            PlayerStationController station = interactor.GetComponent<PlayerStationController>();
            PlayerInteraction interaction = interactor.GetComponent<PlayerInteraction>();

            if (station == null || interaction == null)
            {
                GameLog.Error(LogCategory.Input, $"'{interactor.name}' cannot use a station: PlayerStationController or PlayerInteraction is missing.");
                return;
            }

            if (!station.TryOccupy(_standAnchor))
            {
                return;
            }

            _occupant = station;
            _occupantInteraction = interaction;
            _occupantInteraction.CaptureFocus(this);

            _gameInput = ServiceRegistry.Get<GameInput>();
            if (_gameInput != null)
            {
                _gameInput.EnableMap(InputMap.Boat);
            }

            _boatMovement.SetControlEnabled(true);
            GameLog.Info(LogCategory.Input, $"'{interactor.name}' took the wheel.");
        }

        private void Release()
        {
            _boatMovement.SetControlEnabled(false);

            if (_gameInput != null)
            {
                _gameInput.DisableMap(InputMap.Boat);
                _gameInput = null;
            }

            if (_occupantInteraction != null)
            {
                _occupantInteraction.ReleaseFocus(this);
                _occupantInteraction = null;
            }

            if (_occupant != null)
            {
                _occupant.Release();
                GameLog.Info(LogCategory.Input, $"'{_occupant.name}' left the wheel.");
                _occupant = null;
            }
        }

        private bool IsOccupant(GameObject interactor)
        {
            return _occupant != null && _occupant.gameObject == interactor;
        }
    }
}
