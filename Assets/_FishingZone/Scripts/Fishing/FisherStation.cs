using FishingZone.Core;
using FishingZone.Player;
using FishingZone.Roles;
using Unity.Netcode;
using UnityEngine;

namespace FishingZone.Fishing
{
    /// <summary>
    /// A place aboard where one Fisher may fish. For now it only decides who has it.
    ///
    /// Nothing is caught, cast, reeled or held here yet: this establishes that the job chosen back
    /// in the lobby still governs what a player may do once the lobby is long gone, that the server
    /// is what decides it, and that a place taken is a place nobody else can take. The fishing
    /// itself is built on top of this.
    ///
    /// One station is one place, and stations know nothing of each other. Two Fishers cannot stand
    /// on the same stretch of rail, so a crew that carries two of them gets two of these rather than
    /// one station keeping two seats. A second place to fish is another object in the scene rather
    /// than a capacity number in this class, and neither object has to be told the other exists.
    ///
    /// Deliberately does nothing to the player. No seat, no anchor, no suspended movement, no
    /// captured focus. Claiming a place is a claim and not a posture; what a Fisher's body does once
    /// they have one is for the mechanics that follow to decide. In particular the wheel's captured
    /// focus is NOT copied here: that works only because its occupant is seated and cannot walk
    /// away, and pinning the focus of a player who is free to move would leave them unable to
    /// interact with anything else on the boat.
    /// </summary>
    public class FisherStation : NetworkBehaviour, IInteractable
    {
        /// <summary>No real client id, so an empty station needs no second variable to describe it.</summary>
        public const ulong NoOccupant = ulong.MaxValue;

        [SerializeField]
        private string _fishText = "Fish here";

        [SerializeField]
        private string _stopFishText = "Stop fishing";

        [SerializeField]
        private string _busyText = "Station in use";

        [SerializeField]
        private string _wrongRoleText = "Only the Fisher may fish here";

        private readonly NetworkVariable<ulong> _occupantClientId = new NetworkVariable<ulong>(
            NoOccupant,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        public ulong OccupantClientId => _occupantClientId.Value;

        public bool IsOccupied => _occupantClientId.Value != NoOccupant;

        private bool IsLocalOccupant =>
            NetworkManager.Singleton != null && _occupantClientId.Value == NetworkManager.Singleton.LocalClientId;

        public override void OnNetworkSpawn()
        {
            _occupantClientId.OnValueChanged += HandleOccupantChanged;

            if (IsServer)
            {
                NetworkManager.Singleton.OnClientDisconnectCallback += HandleClientDisconnected;
            }

            // For the case where this station arrives after a player is already looking at where it
            // will be. Harmless otherwise, and null-safe before any player exists.
            RefreshLocalPrompt();
        }

        public override void OnNetworkDespawn()
        {
            _occupantClientId.OnValueChanged -= HandleOccupantChanged;

            if (IsServer && NetworkManager.Singleton != null)
            {
                NetworkManager.Singleton.OnClientDisconnectCallback -= HandleClientDisconnected;
            }
        }

        /// <summary>
        /// True for everybody, including players who will certainly be refused.
        ///
        /// Returning false here would do more than forbid the press: PlayerInteraction drops a
        /// target that cannot be interacted with, so the station would stop being looked at at all
        /// and a Navigator would see no prompt, no refusal and no reason — it would read as scenery.
        /// Worse, the request would never leave the machine, and a rule the server never gets asked
        /// about is a rule nobody can show is working.
        /// </summary>
        public bool CanInteract(GameObject interactor)
        {
            return true;
        }

        /// <summary>
        /// Reads differently for the four cases a player can be in, so a place that is taken looks
        /// taken rather than merely unresponsive.
        ///
        /// Being the wrong job is reported ahead of the place being busy, matching the order the
        /// server refuses in: it is the firmer of the two reasons and the one that will not change
        /// by waiting.
        ///
        /// The role comes from the copy carried on the player object, which is the one place this
        /// class is allowed to consult it. That value is a replicated mirror a determined client
        /// could edit in its own memory, and the worst that can do is make somebody's own screen
        /// offer them something the server will then refuse. It decides what a player reads. It
        /// never decides what a player may do; see the requests below.
        /// </summary>
        public string GetInteractionText(GameObject interactor)
        {
            if (PlayerRoleController.GetRoleOf(interactor) != PlayerRole.Fisher)
            {
                return _wrongRoleText;
            }

            if (!IsOccupied)
            {
                return _fishText;
            }

            return IsLocalOccupant ? _stopFishText : _busyText;
        }

        /// <summary>
        /// Asks the server, whatever the prompt just said.
        ///
        /// A player the local mirror believes is no Fisher still gets to ask, and a Fisher looking
        /// at somebody else's place still gets to ask, and both get their answer from the machine
        /// entitled to give one. Refusing locally would be quicker and would hide the only thing
        /// worth proving.
        ///
        /// Which of the two requests to send is decided from local state, and that is safe: the
        /// server verifies both independently, so a tampered flag produces a refusal rather than an
        /// exploit.
        /// </summary>
        public void Interact(GameObject interactor)
        {
            if (!IsSpawned)
            {
                // Sending before the object is spawned throws. This can happen for an in-scene
                // station in the moments after the scene loads and before Netcode has spawned it.
                return;
            }

            if (IsLocalOccupant)
            {
                RequestReleaseServerRpc();
                return;
            }

            RequestOccupyServerRpc();
        }

        /// <summary>
        /// The decision, and the only one that counts.
        ///
        /// Who is asking comes from the transport rather than from anything the caller sent, so one
        /// player cannot claim on another's behalf. What they are comes from the crew registry,
        /// which is server-only, written when the lobby accepted their choice, and still standing
        /// long after the lobby was unloaded. PlayerRoleController is not consulted and must never
        /// be: it is a copy that lives on the asking machine.
        ///
        /// Ownership is not the check either, which is why it is not required. A station belongs to
        /// nobody; the question is what job the asker took, not who owns this object.
        /// </summary>
        [ServerRpc(RequireOwnership = false)]
        private void RequestOccupyServerRpc(ServerRpcParams parameters = default)
        {
            ulong senderId = parameters.Receive.SenderClientId;

            CrewRoleRegistry registry = ServiceRegistry.Get<CrewRoleRegistry>();
            if (registry == null)
            {
                // Refused, where the wheel would have allowed it. The wheel reasons that a crew
                // unable to steer is a worse failure than a role going unchecked, and it is right.
                // Here there is nothing to lose by refusing and everything to lose by permitting: a
                // station that quietly let the whole crew fish would look exactly like one that was
                // working, and the missing registry would go unnoticed until it mattered elsewhere.
                GameLog.Error(LogCategory.Fish,
                    $"Refused client {senderId} at '{name}': no crew registry, so nobody's job can be confirmed.");
                return;
            }

            // Checked before availability, because being the wrong job is a firmer refusal than the
            // place merely being taken, and it makes the log say which of the two happened.
            PlayerRole role = registry.GetRole(senderId);
            if (role != PlayerRole.Fisher)
            {
                GameLog.Info(LogCategory.Fish,
                    $"Refused client {senderId} at '{name}': only a Fisher may fish there, and they are {role}.");
                return;
            }

            if (IsOccupied)
            {
                GameLog.Info(LogCategory.Fish,
                    $"Refused client {senderId} at '{name}': client {_occupantClientId.Value} already has it.");
                return;
            }

            _occupantClientId.Value = senderId;

            GameLog.Info(LogCategory.Fish, $"Client {senderId} took the fishing station '{name}'.");
        }

        /// <summary>
        /// Only the holder may give a place up, so a stray request cannot turn somebody else out.
        ///
        /// The role is not checked again. Nobody but a Fisher could have become the occupant, and a
        /// role cannot change during a mission because the lobby is where it is chosen and the lobby
        /// is gone; identity is the whole question here.
        /// </summary>
        [ServerRpc(RequireOwnership = false)]
        private void RequestReleaseServerRpc(ServerRpcParams parameters = default)
        {
            ulong senderId = parameters.Receive.SenderClientId;

            if (_occupantClientId.Value != senderId)
            {
                GameLog.Info(LogCategory.Fish,
                    $"Refused client {senderId} leaving '{name}': they do not have it.");
                return;
            }

            ReleaseOnServer();
        }

        private void ReleaseOnServer()
        {
            ulong previous = _occupantClientId.Value;

            _occupantClientId.Value = NoOccupant;

            GameLog.Info(LogCategory.Fish, $"Client {previous} left the fishing station '{name}'.");
        }

        /// <summary>
        /// A Fisher who vanishes would otherwise hold their place for the rest of the mission.
        ///
        /// Each station listens for itself and frees only what it is holding, so a crewmate leaving
        /// one place cannot disturb the other. That is the same arrangement the roster, the registry
        /// and the wheel already use, and it is why the order these callbacks run in has never
        /// mattered: no listener reads another's state.
        /// </summary>
        private void HandleClientDisconnected(ulong clientId)
        {
            if (IsServer && _occupantClientId.Value == clientId)
            {
                ReleaseOnServer();
            }
        }

        /// <summary>
        /// Runs on every peer. Nobody's body is moved and nothing is seated; the only thing that
        /// changes locally is what this place says when looked at.
        /// </summary>
        private void HandleOccupantChanged(ulong previous, ulong current)
        {
            RefreshLocalPrompt();
        }

        /// <summary>
        /// Asks the local player to read its prompt again.
        ///
        /// Done on every peer rather than only on the occupant's, because a Fisher standing and
        /// watching somebody else take a place needs their own text to stop offering it. The prompt
        /// is otherwise read once, when the player first looks, and would go on saying whatever was
        /// true at that moment.
        ///
        /// Refreshing whatever the player happens to be looking at, rather than insisting it is this
        /// station, keeps this from having to know: re-reading another object's prompt produces the
        /// same words it already had.
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
    }
}
