using FishingZone.Core;
using FishingZone.Core.Input;
using FishingZone.Player;
using Unity.Netcode;
using UnityEngine;

namespace FishingZone.Boat
{
    /// <summary>
    /// The boat's wheel. Interacting takes the helm, interacting again gives it up.
    ///
    /// Who holds it is replicated state owned by the server, not a local decision. Two players each
    /// believing they had the wheel is what made the hull behave catastrophically, because both were
    /// feeding it input. A client asks, the server decides, and everyone learns the answer from the
    /// same variable.
    ///
    /// While occupied the station captures the local occupant's interaction focus, so looking at
    /// anything else on deck cannot steal the key that gets them out of the seat. The Player action
    /// map is deliberately left enabled throughout, because Interact lives on it.
    /// </summary>
    public class NavigatorStation : NetworkBehaviour, IInteractable
    {
        /// <summary>No real client id, so it reads as "nobody" without a second variable.</summary>
        public const ulong NoOccupant = ulong.MaxValue;

        [SerializeField]
        private BoatMovement _boatMovement;

        [SerializeField]
        private Transform _standAnchor;

        [SerializeField]
        private string _enterText = "Take the wheel";

        [SerializeField]
        private string _exitText = "Leave the wheel";

        [SerializeField]
        private string _busyText = "Wheel in use";

        private readonly NetworkVariable<ulong> _occupantClientId = new NetworkVariable<ulong>(
            NoOccupant,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        public ulong OccupantClientId => _occupantClientId.Value;

        public bool IsOccupied => _occupantClientId.Value != NoOccupant;

        private PlayerStationController _localSeatedPlayer;
        private PlayerInteraction _localInteraction;
        private GameInput _gameInput;

        private bool IsLocalOccupant =>
            NetworkManager.Singleton != null && _occupantClientId.Value == NetworkManager.Singleton.LocalClientId;

        public override void OnNetworkSpawn()
        {
            if (_boatMovement == null || _standAnchor == null)
            {
                GameLog.Error(LogCategory.Network, "NavigatorStation is missing a Boat Movement or Stand Anchor reference. Assign both in the Inspector.");
                return;
            }

            _occupantClientId.OnValueChanged += HandleOccupantChanged;

            if (IsServer)
            {
                NetworkManager.Singleton.OnClientDisconnectCallback += HandleClientDisconnected;
                _boatMovement.SetControlEnabled(false);
            }

            // Adopted immediately so a player joining a crew that is already under way sees the
            // correct prompt, and so a reconnecting occupant is seated rather than left standing.
            HandleOccupantChanged(NoOccupant, _occupantClientId.Value);
        }

        public override void OnNetworkDespawn()
        {
            _occupantClientId.OnValueChanged -= HandleOccupantChanged;

            if (IsServer && NetworkManager.Singleton != null)
            {
                NetworkManager.Singleton.OnClientDisconnectCallback -= HandleClientDisconnected;
            }

            // Never strand a player seated at a station that is going away.
            ReleaseLocalSeat();
        }

        public bool CanInteract(GameObject interactor)
        {
            return _boatMovement != null && _standAnchor != null;
        }

        /// <summary>
        /// Reads differently for the three cases a player can be in, so an occupied wheel is visibly
        /// occupied rather than simply unresponsive.
        /// </summary>
        public string GetInteractionText(GameObject interactor)
        {
            if (!IsOccupied)
            {
                return _enterText;
            }

            return IsLocalOccupant ? _exitText : _busyText;
        }

        public void Interact(GameObject interactor)
        {
            if (IsOccupied && !IsLocalOccupant)
            {
                // Refused here purely to avoid pointless traffic; the server refusal below is what
                // actually guarantees it, since a client could always ask anyway.
                return;
            }

            if (IsLocalOccupant)
            {
                RequestReleaseServerRpc();
                return;
            }

            RequestOccupyServerRpc();
        }

        [ServerRpc(RequireOwnership = false)]
        private void RequestOccupyServerRpc(ServerRpcParams parameters = default)
        {
            ulong requester = parameters.Receive.SenderClientId;

            if (IsOccupied)
            {
                GameLog.Info(LogCategory.Network, $"Refused the wheel to client {requester}: client {_occupantClientId.Value} already has it.");
                return;
            }

            _occupantClientId.Value = requester;
            _boatMovement.SetControlEnabled(true);

            GameLog.Info(LogCategory.Network, $"Client {requester} took the wheel.");
        }

        [ServerRpc(RequireOwnership = false)]
        private void RequestReleaseServerRpc(ServerRpcParams parameters = default)
        {
            // Only the holder may give it up, so a stray request cannot unseat somebody else.
            if (_occupantClientId.Value != parameters.Receive.SenderClientId)
            {
                return;
            }

            ReleaseOnServer();
        }

        private void ReleaseOnServer()
        {
            ulong previous = _occupantClientId.Value;

            _boatMovement.SetControlEnabled(false);
            _occupantClientId.Value = NoOccupant;

            GameLog.Info(LogCategory.Network, $"Client {previous} left the wheel.");
        }

        /// <summary>
        /// A driver who vanishes mid-turn would otherwise leave the wheel held forever and the boat
        /// unusable for the rest of the crew.
        /// </summary>
        private void HandleClientDisconnected(ulong clientId)
        {
            if (IsServer && _occupantClientId.Value == clientId)
            {
                ReleaseOnServer();
            }
        }

        /// <summary>
        /// Runs on every peer. Each one only acts on its own seat: the occupant sits down or stands
        /// up locally, and everybody else simply ends up with different prompt text.
        /// </summary>
        private void HandleOccupantChanged(ulong previous, ulong current)
        {
            if (NetworkManager.Singleton == null)
            {
                return;
            }

            ulong local = NetworkManager.Singleton.LocalClientId;

            if (current == local)
            {
                TakeLocalSeat();
            }
            else if (previous == local || _localSeatedPlayer != null)
            {
                ReleaseLocalSeat();
            }
        }

        private void TakeLocalSeat()
        {
            if (_localSeatedPlayer != null)
            {
                return;
            }

            NetworkObject playerObject = NetworkManager.Singleton.LocalClient?.PlayerObject;
            if (playerObject == null)
            {
                GameLog.Error(LogCategory.Network, "Took the wheel but the local player object does not exist yet.");
                return;
            }

            PlayerStationController seat = playerObject.GetComponent<PlayerStationController>();
            PlayerInteraction interaction = playerObject.GetComponent<PlayerInteraction>();

            if (seat == null || interaction == null || !seat.TryOccupy(_standAnchor))
            {
                GameLog.Error(LogCategory.Network, "Local player cannot use a station: seating or interaction component missing.");
                return;
            }

            _localSeatedPlayer = seat;
            _localInteraction = interaction;

            // Captured so that looking at another interactable cannot take priority over leaving.
            _localInteraction.CaptureFocus(this);

            _gameInput = ServiceRegistry.Get<GameInput>();
            if (_gameInput != null)
            {
                // Added, not switched to: Interact is on the Player map and must stay live.
                _gameInput.EnableMap(InputMap.Boat);
            }
        }

        private void ReleaseLocalSeat()
        {
            if (_gameInput != null)
            {
                _gameInput.DisableMap(InputMap.Boat);
                _gameInput = null;
            }

            if (_localInteraction != null)
            {
                _localInteraction.ReleaseFocus(this);
                _localInteraction = null;
            }

            if (_localSeatedPlayer != null)
            {
                _localSeatedPlayer.Release();
                _localSeatedPlayer = null;
            }
        }
    }
}
