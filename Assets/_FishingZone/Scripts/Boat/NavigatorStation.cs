using FishingZone.Core;
using FishingZone.Core.Input;
using FishingZone.Player;
using FishingZone.Roles;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

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
    ///
    /// The station also relays the local driver's Boat input to the server. Keeping that relay here
    /// guarantees the component already exists on every networked boat and avoids relying on a
    /// separately wired NetworkBehaviour in the scene.
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

        /// <summary>
        /// Said to anyone who could not steer if they tried. The wheel refuses them either way; this
        /// is only the difference between being told and finding out by pressing.
        /// </summary>
        [SerializeField]
        private string _wrongRoleText = "Only the Navigator may steer";

        private readonly NetworkVariable<ulong> _occupantClientId = new NetworkVariable<ulong>(
            NoOccupant,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        public ulong OccupantClientId => _occupantClientId.Value;

        public bool IsOccupied => _occupantClientId.Value != NoOccupant;

        private PlayerStationController _localSeatedPlayer;
        private PlayerInteraction _localInteraction;
        private GameInput _gameInput;
        private InputAction _throttleAction;
        private InputAction _steerAction;
        private InputAction _brakeAction;

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

        private void FixedUpdate()
        {
            if (!IsSpawned || !IsLocalOccupant || _boatMovement == null)
            {
                return;
            }

            if (!ResolveBoatActions())
            {
                return;
            }

            float throttle = _throttleAction.ReadValue<float>();
            float steer = _steerAction.ReadValue<float>();
            bool isBraking = _brakeAction.IsPressed();

            if (IsServer)
            {
                _boatMovement.SetInput(throttle, steer, isBraking);
            }
            else
            {
                SubmitInputServerRpc(throttle, steer, isBraking);
            }
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
            // Holding it comes before being allowed it, which is the reverse of how the fishing
            // stations read and is deliberate. Those refuse when the crew registry is missing; this
            // one allows, on the grounds that a crew unable to steer is worse than a role going
            // unchecked. So somebody can be at this wheel whom the copy on their player object says
            // should not be, and telling a seated driver they may not steer would be absurd. What
            // they can plainly see comes first.
            if (IsLocalOccupant)
            {
                return _exitText;
            }

            // Being the wrong job is reported ahead of the wheel merely being taken: it is the
            // firmer of the two reasons and the one that will not change by waiting.
            //
            // The role comes from the copy carried on the player object, which is the one place this
            // class is allowed to consult it. A determined client could edit that copy in its own
            // memory, and the worst it would buy them is a wheel their own screen offers and the
            // server then refuses. It decides what a player reads, never what they may do.
            if (PlayerRoleController.GetRoleOf(interactor) != PlayerRole.Navigator)
            {
                return _wrongRoleText;
            }

            if (IsOccupied)
            {
                return _busyText;
            }

            return _enterText;
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

            // Checked before availability, because being the wrong job is a firmer refusal than the
            // wheel merely being busy, and it makes the log say which of the two happened.
            if (!IsNavigator(requester))
            {
                GameLog.Info(LogCategory.Network, $"Refused the wheel to client {requester}: only the Navigator steers.");
                return;
            }

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

        [ServerRpc(RequireOwnership = false)]
        private void SubmitInputServerRpc(float throttle, float steer, bool isBraking, ServerRpcParams parameters = default)
        {
            // Never trust a client merely because it can reach this RPC. The current replicated
            // occupant is the only client whose driving intent may reach the authoritative hull.
            if (_occupantClientId.Value != parameters.Receive.SenderClientId)
            {
                return;
            }

            _boatMovement.SetInput(throttle, steer, isBraking);
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

            // Everyone, not only the two players the branches above concern. A crewmate standing and
            // watching the wheel is in neither case, and their prompt would otherwise go on offering
            // a wheel somebody else had just taken until they looked away and back.
            RefreshLocalPrompt();
        }

        /// <summary>
        /// Asks the local player to read this prompt again.
        ///
        /// Occupancy is the only thing the wheel's text depends on that can change while somebody
        /// stands still looking at it, and the text is read once when a target is first looked at.
        /// So the moment it changes, everybody is asked to read it again.
        ///
        /// Refreshing whatever the player happens to be looking at, rather than insisting it is this
        /// wheel, keeps this from having to know: re-reading another object's prompt produces the
        /// same words it already had. It re-raises an event with the value already held, so it can
        /// disturb nothing, and it is safe before any player exists.
        /// </summary>
        private static void RefreshLocalPrompt()
        {
            NetworkObject playerObject = NetworkManager.Singleton?.LocalClient?.PlayerObject;
            if (playerObject == null)
            {
                return;
            }

            PlayerInteraction interaction = playerObject.GetComponent<PlayerInteraction>();
            if (interaction != null)
            {
                interaction.RefreshFocus();
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
                ResolveBoatActions();
            }
        }

        private bool ResolveBoatActions()
        {
            if (_throttleAction != null && _steerAction != null && _brakeAction != null)
            {
                return true;
            }

            if (_gameInput == null)
            {
                _gameInput = ServiceRegistry.Get<GameInput>();
            }

            if (_gameInput?.Actions == null)
            {
                return false;
            }

            _throttleAction = _gameInput.Actions.FindAction("Boat/Throttle", throwIfNotFound: false);
            _steerAction = _gameInput.Actions.FindAction("Boat/Steer", throwIfNotFound: false);
            _brakeAction = _gameInput.Actions.FindAction("Boat/Brake", throwIfNotFound: false);

            if (_throttleAction == null || _steerAction == null || _brakeAction == null)
            {
                GameLog.Error(LogCategory.Input, "NavigatorStation could not resolve Boat/Throttle, Boat/Steer or Boat/Brake from GameInput.");
                return false;
            }

            return true;
        }

        private void ReleaseLocalSeat()
        {
            if (_gameInput != null)
            {
                _gameInput.DisableMap(InputMap.Boat);
                _gameInput = null;
            }

            _throttleAction = null;
            _steerAction = null;
            _brakeAction = null;

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

        /// <summary>
        /// Roles come from the persistent registry rather than the lobby roster, which no longer
        /// exists once a mission is under way.
        ///
        /// A missing registry is treated as permission rather than refusal. Losing the ability to
        /// steer because a component was never attached would be a far worse failure than a role
        /// going unchecked, and ServiceRegistry logs the miss loudly either way.
        /// </summary>
        private static bool IsNavigator(ulong clientId)
        {
            CrewRoleRegistry registry = ServiceRegistry.Get<CrewRoleRegistry>();
            if (registry == null)
            {
                return true;
            }

            return registry.GetRole(clientId) == PlayerRole.Navigator;
        }
    }
}
