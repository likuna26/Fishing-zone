using FishingZone.Core;
using FishingZone.Core.Input;
using FishingZone.Player;
using FishingZone.Roles;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

namespace FishingZone.Fishing
{
    /// <summary>
    /// A place aboard where one Fisher may fish, and what is happening there.
    ///
    /// Two separate questions, deliberately kept separate. Interact settles who has the place; Cast
    /// and Release settle whether they are fishing at it. Overloading one key with both would give
    /// it three meanings depending on state nobody can see, and would leave nowhere for reeling to
    /// go. Nothing is caught, cast, reeled or hooked yet: this establishes a state everyone can
    /// trust, and the mechanics are built on top of it.
    ///
    /// One station is one place, and a Fisher may hold one place. Stations hold no reference to each
    /// other; the server simply asks, once, at the moment somebody claims one, whether they already
    /// have another. That question is asked of the scene rather than of a manager, so nothing has to
    /// be kept in step and nothing outlives the objects it describes.
    ///
    /// Deliberately does nothing to the player. No seat, no anchor, no suspended movement, no
    /// captured focus. Claiming a place is a claim and not a posture; what a Fisher's body does once
    /// they have one is for the mechanics that follow. In particular the wheel's captured focus is
    /// NOT copied here: that works only because its occupant is seated and cannot walk away, and
    /// pinning the focus of a player free to move would leave them unable to interact with anything
    /// else aboard.
    /// </summary>
    public class FisherStation : NetworkBehaviour, IInteractable
    {
        /// <summary>No real client id, so an empty station needs no second variable to describe it.</summary>
        public const ulong NoOccupant = ulong.MaxValue;

        [SerializeField]
        private string _fishText = "Fish here";

        /// <summary>Shown to the holder while they are not yet fishing, so both of their options read.</summary>
        [SerializeField]
        private string _occupiedText = "Cast to fish, or leave the station";

        [SerializeField]
        private string _stopFishText = "Waiting for a bite — release to stop, or leave the station";

        [SerializeField]
        private string _busyText = "Station in use";

        /// <summary>Distinct from busy, because a place being worked reads differently from one merely taken.</summary>
        [SerializeField]
        private string _busyFishingText = "Someone has a line out here";

        /// <summary>
        /// Keeps the way out in view. A Fisher who cannot yet answer a bite would otherwise be told
        /// something is happening and not how to stop it.
        /// </summary>
        [SerializeField]
        private string _biteText = "Something is biting! Release to stop, or leave the station";

        /// <summary>What makes a bite visible to the rest of the crew rather than only to its Fisher.</summary>
        [SerializeField]
        private string _busyBiteText = "Someone has a bite here";

        /// <summary>
        /// How long a cast waits before something takes an interest, drawn afresh each time.
        ///
        /// A range rather than a fixed wait, because two Fishers casting together would otherwise
        /// bite in step, which looks like shared state whether or not it is. Serialized so the feel
        /// can be tuned without a recompile, as the hull's handling already is.
        /// </summary>
        [SerializeField]
        private float _minBiteDelay = 3f;

        [SerializeField]
        private float _maxBiteDelay = 10f;

        [SerializeField]
        private string _wrongRoleText = "Only the Fisher may fish here";

        /// <summary>
        /// From the Fishing map, which already exists and is already bound. Read only on the peer
        /// holding this station, and only while the map is enabled, which happens for exactly as
        /// long as they hold it.
        /// </summary>
        [SerializeField]
        private InputActionReference _castAction;

        [SerializeField]
        private InputActionReference _stopFishingAction;

        private readonly NetworkVariable<ulong> _occupantClientId = new NetworkVariable<ulong>(
            NoOccupant,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        private readonly NetworkVariable<int> _phase = new NetworkVariable<int>(
            (int)FishingPhase.Idle,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        public ulong OccupantClientId => _occupantClientId.Value;

        public bool IsOccupied => _occupantClientId.Value != NoOccupant;

        public FishingPhase Phase => (FishingPhase)_phase.Value;

        /// <summary>
        /// Whether this station is the one that turned the Fishing map on. Kept so a station can
        /// never switch off input it did not switch on.
        /// </summary>
        private bool _ownsLocalFishingInput;

        /// <summary>
        /// How much longer this station's cast waits, counted down by the server alone.
        ///
        /// Deliberately not replicated. A client that knew when the bite was coming could act on it
        /// before it arrived, which is exactly what answering a bite will one day have to be
        /// protected from. Everyone learns of a bite when it happens and not a frame sooner.
        ///
        /// Nothing has to clear it. It is read only while this station is waiting, so every way a
        /// cast can end already stops it by setting the phase, and there is no scheduled callback
        /// left in flight to arrive at an empty station later.
        /// </summary>
        private float _biteCountdown;

        /// <summary>
        /// Diagnostic only. A cast is accepted and the phase reaches every peer, so the server both
        /// armed the wait and holds the right phase; what cannot be seen from outside is whether the
        /// server ever looks at either again. Cleared whenever the phase leaves waiting, so each
        /// cast reports for itself.
        /// </summary>
        private bool _hasLoggedServerTick;

        private bool IsLocalOccupant =>
            NetworkManager.Singleton != null && _occupantClientId.Value == NetworkManager.Singleton.LocalClientId;

        public override void OnNetworkSpawn()
        {
            _occupantClientId.OnValueChanged += HandleOccupantChanged;
            _phase.OnValueChanged += HandlePhaseChanged;

            if (IsServer)
            {
                NetworkManager.Singleton.OnClientDisconnectCallback += HandleClientDisconnected;
            }

            // Adopted rather than waited for, so a station arriving after the player who is looking
            // at it still reads correctly. Null-safe before any player exists.
            SetLocalFishingInput(IsLocalOccupant);
            RefreshLocalPrompt();
        }

        public override void OnNetworkDespawn()
        {
            _occupantClientId.OnValueChanged -= HandleOccupantChanged;
            _phase.OnValueChanged -= HandlePhaseChanged;

            if (IsServer && NetworkManager.Singleton != null)
            {
                NetworkManager.Singleton.OnClientDisconnectCallback -= HandleClientDisconnected;
            }

            // Never leave a departing station holding the local player's input. A crew that changed
            // scene mid-cast would otherwise keep the Fishing map live for the rest of the session.
            SetLocalFishingInput(false);
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
        /// Being the wrong job is reported ahead of anything about the place itself, matching the
        /// order the server refuses in: it is the firmer of the reasons and the one that will not
        /// change by waiting.
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

            FishingPhase phase = Phase;

            if (IsLocalOccupant)
            {
                switch (phase)
                {
                    case FishingPhase.Bite:
                        return _biteText;
                    case FishingPhase.Waiting:
                        return _stopFishText;
                    default:
                        return _occupiedText;
                }
            }

            switch (phase)
            {
                case FishingPhase.Bite:
                    return _busyBiteText;
                case FishingPhase.Waiting:
                    return _busyFishingText;
                default:
                    return _busyText;
            }
        }

        /// <summary>
        /// Interact settles the place, and nothing else. Leaving while fishing is allowed and ends
        /// both at once, because everything that empties a station goes through one path and that
        /// path cannot strand anybody mid-cast.
        ///
        /// Asks the server whatever the prompt just said. A player the local mirror believes is no
        /// Fisher still gets to ask, and a Fisher looking at somebody else's place still gets to
        /// ask, and both get their answer from the machine entitled to give one.
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
        /// Two unrelated jobs on two unrelated conditions.
        ///
        /// The server watches every station, whoever is standing at it, because a bite is its
        /// decision to make and the Fisher waiting for one is usually on another machine. Reading
        /// the controls is the opposite: only the peer holding this station does that, so the three
        /// other copies of this component read nothing at all.
        ///
        /// On the host both are true at once and both run, which is correct: it is the server, and
        /// its player may also be the one holding the rod.
        /// </summary>
        private void Update()
        {
            // Diagnostic, and deliberately above every guard below it: whether those guards are
            // passing is the question, so a report from underneath them would answer nothing.
            LogServerTickOnce();

            if (!IsSpawned)
            {
                return;
            }

            if (IsServer)
            {
                TickBite();
            }

            if (IsLocalOccupant)
            {
                PollFishingInput();
            }
        }

        /// <summary>
        /// Says, once per cast on whichever peer manages to run it, that this method was reached
        /// while a line was out, and what this machine believed at that moment.
        ///
        /// Diagnostic, and temporary. A bite that never comes has only a few possible explanations
        /// and they are indistinguishable from outside: Unity not calling Update on this copy at
        /// all, an object this peer does not consider spawned, a peer that is not the server, or a
        /// phase that is not what the prompt suggests. Silence from the host is itself the answer to
        /// the first; the rest are named on the line.
        /// </summary>
        private void LogServerTickOnce()
        {
            if (_hasLoggedServerTick || Phase != FishingPhase.Waiting)
            {
                return;
            }

            _hasLoggedServerTick = true;

            GameLog.Info(LogCategory.Fish,
                $"'{name}' Update reached while waiting. spawned: {IsSpawned}, server: {IsServer}, " +
                $"phase: {Phase}, countdown: {_biteCountdown:F2}.");
        }

        /// <summary>
        /// Brings the cast to something, once the wait this station drew for itself has run out.
        ///
        /// Server only. The countdown is consulted just here, and only while waiting, so a cast that
        /// was reeled in, walked away from or disconnected out from under simply stops being counted
        /// rather than needing to be called off. That is why there is no cancellation anywhere in
        /// this file: there is nothing pending that could arrive late.
        /// </summary>
        private void TickBite()
        {
            if (Phase != FishingPhase.Waiting)
            {
                return;
            }

            _biteCountdown -= Time.deltaTime;
            if (_biteCountdown > 0f)
            {
                return;
            }

            SetPhaseOnServer(FishingPhase.Bite);

            GameLog.Info(LogCategory.Fish,
                $"Something bit at '{name}' for client {_occupantClientId.Value}.");
        }

        /// <summary>
        /// Cast begins, Release ends.
        ///
        /// Neither press is filtered by the phase this machine believes it is in. Asking to start
        /// while a line is already out is refused by the server and logged there, which is worth
        /// more than the message it saves.
        /// </summary>
        private void PollFishingInput()
        {
            if (_castAction != null && _castAction.action.WasPressedThisFrame())
            {
                RequestStartFishingServerRpc();
            }

            if (_stopFishingAction != null && _stopFishingAction.action.WasPressedThisFrame())
            {
                RequestStopFishingServerRpc();
            }
        }

        /// <summary>
        /// Who may have this place.
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

            // Asked last, because it is the only one that has to look past this object. A Fisher
            // holding two places could fish at neither: the Fishing map belongs to whichever station
            // switched it on, so letting go of one would take the other's controls with it.
            if (TryFindStationHeldBy(senderId, out FisherStation held))
            {
                GameLog.Info(LogCategory.Fish,
                    $"Refused client {senderId} at '{name}': they already have '{held.name}'.");
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

        /// <summary>
        /// Whether fishing may begin here.
        ///
        /// The role is asked again rather than assumed from occupancy. It costs one lookup, and it
        /// means this decision stands on its own rather than on an invariant established somewhere
        /// else that a later change might quietly break.
        /// </summary>
        [ServerRpc(RequireOwnership = false)]
        private void RequestStartFishingServerRpc(ServerRpcParams parameters = default)
        {
            ulong senderId = parameters.Receive.SenderClientId;

            CrewRoleRegistry registry = ServiceRegistry.Get<CrewRoleRegistry>();
            if (registry == null)
            {
                GameLog.Error(LogCategory.Fish,
                    $"Refused client {senderId} fishing at '{name}': no crew registry, so nobody's job can be confirmed.");
                return;
            }

            PlayerRole role = registry.GetRole(senderId);
            if (role != PlayerRole.Fisher)
            {
                GameLog.Info(LogCategory.Fish,
                    $"Refused client {senderId} fishing at '{name}': only a Fisher may fish there, and they are {role}.");
                return;
            }

            if (_occupantClientId.Value != senderId)
            {
                GameLog.Info(LogCategory.Fish,
                    $"Refused client {senderId} fishing at '{name}': they do not have it.");
                return;
            }

            if (Phase != FishingPhase.Idle)
            {
                GameLog.Info(LogCategory.Fish,
                    $"Refused client {senderId} fishing at '{name}': already {Phase}.");
                return;
            }

            // Drawn here rather than kept, so each cast waits its own length and an abandoned one
            // leaves nothing behind for the next to inherit.
            _biteCountdown = Random.Range(_minBiteDelay, _maxBiteDelay);

            // Diagnostic, and temporary. Proves the server got this far and says what it drew, so a
            // wait that never ends can be told apart from bounds that were never what they look like
            // in the Inspector.
            GameLog.Info(LogCategory.Fish,
                $"'{name}' armed a bite in {_biteCountdown:F2}s for client {senderId}. " +
                $"(min {_minBiteDelay}, max {_maxBiteDelay})");

            SetPhaseOnServer(FishingPhase.Waiting);

            GameLog.Info(LogCategory.Fish, $"Client {senderId} started fishing at '{name}'.");
        }

        /// <summary>
        /// Stopping asks only who is asking. Giving up is not a privilege, so there is nothing to
        /// check beyond it being theirs to give up.
        /// </summary>
        [ServerRpc(RequireOwnership = false)]
        private void RequestStopFishingServerRpc(ServerRpcParams parameters = default)
        {
            ulong senderId = parameters.Receive.SenderClientId;

            if (_occupantClientId.Value != senderId)
            {
                GameLog.Info(LogCategory.Fish,
                    $"Refused client {senderId} stopping at '{name}': they do not have it.");
                return;
            }

            if (Phase == FishingPhase.Idle)
            {
                return;
            }

            SetPhaseOnServer(FishingPhase.Idle);

            GameLog.Info(LogCategory.Fish, $"Client {senderId} stopped fishing at '{name}'.");
        }

        /// <summary>
        /// The one way a station empties, whether its holder let go, was disconnected, or is being
        /// turned out by anything added later.
        ///
        /// The phase is cleared first and the occupant second, so no peer ever observes a station
        /// that is fishing with nobody at it. The two variables replicate independently, and in the
        /// other order that gap would be visible.
        /// </summary>
        private void ReleaseOnServer()
        {
            ulong previous = _occupantClientId.Value;

            SetPhaseOnServer(FishingPhase.Idle);
            _occupantClientId.Value = NoOccupant;

            GameLog.Info(LogCategory.Fish, $"Client {previous} left the fishing station '{name}'.");
        }

        /// <summary>
        /// The one place the phase is written. Server only, since the variable refuses anything else.
        ///
        /// A single site rather than three assignments, because the phases are about to outnumber
        /// the ways of reaching them: with a bite and the playing of a fish to come, every new path
        /// that moves a station along should have exactly one thing to call.
        /// </summary>
        private void SetPhaseOnServer(FishingPhase phase)
        {
            _phase.Value = (int)phase;
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
        /// Finds a station this client already holds, if any.
        ///
        /// A search of the scene rather than a list kept up to date, because a list would be a
        /// second copy of something the stations already know, and keeping two copies agreeing is
        /// how stale ownership happens. Searching also covers a station wherever it sits, which a
        /// walk of the boat's own children would not: a place to fish may yet be built on a dock.
        ///
        /// Server only, and only when somebody presses to claim a place, so how thorough it is
        /// costs nothing.
        /// </summary>
        private bool TryFindStationHeldBy(ulong clientId, out FisherStation held)
        {
            FisherStation[] stations = FindObjectsByType<FisherStation>(FindObjectsSortMode.None);

            for (int i = 0; i < stations.Length; i++)
            {
                if (stations[i] != this && stations[i].OccupantClientId == clientId)
                {
                    held = stations[i];
                    return true;
                }
            }

            held = null;
            return false;
        }

        /// <summary>
        /// Runs on every peer. Nobody's body is moved and nothing is seated; what changes locally is
        /// what this place says when looked at, and whether this machine is listening for a cast.
        /// </summary>
        private void HandleOccupantChanged(ulong previous, ulong current)
        {
            SetLocalFishingInput(IsLocalOccupant);
            RefreshLocalPrompt();
        }

        private void HandlePhaseChanged(int previous, int current)
        {
            if ((FishingPhase)current != FishingPhase.Waiting)
            {
                // Armed again, so the next cast reports for itself rather than staying quiet
                // because an earlier one already spoke.
                _hasLoggedServerTick = false;
            }

            RefreshLocalPrompt();
        }

        /// <summary>
        /// Turns the Fishing controls on for whoever is holding this place, and off again when they
        /// are not.
        ///
        /// Added to what is already live rather than replacing it, because Interact lives on the
        /// Player map and leaving is done with Interact. The flag means a station only ever switches
        /// off input it switched on, which together with a Fisher holding one place at a time keeps
        /// two stations from ever arguing over it.
        /// </summary>
        private void SetLocalFishingInput(bool isHolding)
        {
            if (_ownsLocalFishingInput == isHolding)
            {
                return;
            }

            GameInput input = ServiceRegistry.Get<GameInput>();
            if (input == null)
            {
                // ServiceRegistry has already logged the miss.
                return;
            }

            if (isHolding)
            {
                input.EnableMap(InputMap.Fishing);
            }
            else
            {
                input.DisableMap(InputMap.Fishing);
            }

            _ownsLocalFishingInput = isHolding;
        }

        /// <summary>
        /// Asks the local player to read its prompt again.
        ///
        /// Done on every peer rather than only on the occupant's, because a Fisher standing and
        /// watching somebody else take a place, or start fishing at one, needs their own text to
        /// keep up. The prompt is otherwise read once, when the player first looks, and would go on
        /// saying whatever was true at that moment.
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
