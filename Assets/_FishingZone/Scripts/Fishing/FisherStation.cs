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

        /// <summary>
        /// Nothing on the scale. A rolled weight is always at least one tenth of a kilogram, so no
        /// real catch can be mistaken for an absent one.
        /// </summary>
        public const int NoWeight = 0;

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

        [SerializeField]
        private string _hookedText = "Hooked! Release to stop, or leave the station";

        [SerializeField]
        private string _busyHookedText = "Someone has one on the line";

        /// <summary>
        /// The one thing a Fisher has to be told, because the answer is the opposite of what they
        /// are already doing. Crewmates keep the ordinary hooked text: a fish running is not
        /// something they can act on, and it is still true that somebody has one on the line.
        /// </summary>
        [SerializeField]
        private string _resistText = "It's running — let go of the reel!";

        [SerializeField]
        private string _caughtText = "You landed a catch!";

        /// <summary>The crew's share of the moment, which is most of what makes it worth having.</summary>
        [SerializeField]
        private string _busyCaughtText = "Someone landed a catch";

        /// <summary>
        /// Said when the catch has a name. The two above remain for when it does not, which happens
        /// if the station was given nothing to catch.
        ///
        /// The name is substituted rather than formatted, so a placeholder edited into something
        /// malformed loses the name instead of throwing in the middle of a prompt.
        /// </summary>
        [SerializeField]
        private string _caughtNamedText = "You landed a {0}!";

        [SerializeField]
        private string _busyCaughtNamedText = "Someone landed a {0}";

        /// <summary>
        /// Said when the fish was weighed as well as named. The pair above remain for a fish whose
        /// range was never configured: better to name it and leave the scale out than to print a
        /// number nobody chose.
        /// </summary>
        [SerializeField]
        private string _caughtWeighedText = "You landed a {0} — {1} kg!";

        [SerializeField]
        private string _busyCaughtWeighedText = "Someone landed a {0} — {1} kg";

        /// <summary>
        /// Said to the Fisher who landed it, once their tally is known. There is no crewmate's
        /// version on purpose: how somebody else's day is going is theirs to mention.
        /// </summary>
        [SerializeField]
        private string _caughtCountedText = "You landed a {0} — {1} kg!  ({2} this session)";

        /// <summary>
        /// What may be caught here, chosen from at random by the server. Empty is a configuration
        /// mistake rather than a kind of fishing: the loop still runs, and says loudly that it had
        /// nothing to choose from.
        /// </summary>
        [SerializeField]
        private FishDefinition[] _fishPool;

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

        /// <summary>
        /// How long a bite stays answerable. Short enough to be a reaction, long enough that a
        /// Fisher on another machine is not beaten by the trip their press has to make: the window
        /// is measured entirely on the server, so a remote player spends some of it in transit.
        /// </summary>
        [SerializeField]
        private float _biteWindow = 2f;

        /// <summary>
        /// How long the line takes to come back aboard, with the reel actually being turned. Held
        /// time rather than elapsed time, so a Fisher who lets go is not brought in by waiting.
        /// </summary>
        [SerializeField]
        private float _reelDuration = 4f;

        /// <summary>
        /// How long a fish is worked before it makes a run for it, drawn afresh each time. A range
        /// rather than a fixed interval, so two Fishers reeling together do not have their fish bolt
        /// in step, and so the Fisher cannot simply count.
        /// </summary>
        [SerializeField]
        private float _minResistDelay = 1.5f;

        [SerializeField]
        private float _maxResistDelay = 3f;

        /// <summary>
        /// How long the catch is shown before the station is ready again. Long enough to be seen by
        /// somebody looking at it, short enough that nobody has to wait to cast.
        /// </summary>
        [SerializeField]
        private float _catchDisplaySeconds = 2f;

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

        /// <summary>
        /// Held rather than pressed, so the reel turns for as long as the Fisher keeps hold of it.
        /// Only the two edges are sent; the turning itself is counted on the server.
        /// </summary>
        [SerializeField]
        private InputActionReference _reelAction;

        private readonly NetworkVariable<ulong> _occupantClientId = new NetworkVariable<ulong>(
            NoOccupant,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        private readonly NetworkVariable<int> _phase = new NetworkVariable<int>(
            (int)FishingPhase.Idle,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        /// <summary>
        /// Whether the fish on this line is running.
        ///
        /// Replicated, unlike the reel's other workings, because it is the one thing the Fisher has
        /// to be told: the answer to a run is to let go, which is the opposite of what they are
        /// doing, and a prompt that never changed would leave them holding on forever. Kept apart
        /// from the phase rather than made one, because the phase winds the reel's clock on entry
        /// and a station that changed phase every time a fish bolted would start its reel over.
        /// </summary>
        private readonly NetworkVariable<bool> _isResisting = new NetworkVariable<bool>(
            false,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        /// <summary>
        /// Which fish was landed, as the number rather than the asset.
        ///
        /// A ScriptableObject reference means nothing on another machine, so the server sends the id
        /// and each peer finds the definition in the same pool it was configured with. That keeps
        /// the wire to an int, which is what everything else replicated here already is.
        ///
        /// Cleared on every phase change, so the fish belongs to the catch that produced it and
        /// cannot be read off a station that has moved on.
        /// </summary>
        private readonly NetworkVariable<int> _caughtFishId = new NetworkVariable<int>(
            FishDefinition.NoFish,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        /// <summary>
        /// What the catch weighed, in tenths of a kilogram.
        ///
        /// Tenths rather than kilograms, because a tenth is exactly the precision the scale is read
        /// to: every peer divides the same whole number and none of them can round it differently,
        /// which a float shared to one decimal place could. It also keeps the wire to an int, as
        /// everything else replicated here is.
        ///
        /// Zero means nothing was weighed — either no fish, or a fish whose range was never
        /// configured. Rolled weights are never zero, so the two cannot be confused.
        /// </summary>
        private readonly NetworkVariable<int> _caughtWeightTenths = new NetworkVariable<int>(
            NoWeight,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        /// <summary>
        /// How many this Fisher had landed the instant this one came aboard.
        ///
        /// A snapshot of a tally, not the tally itself. The count lives in the crew's catch log,
        /// which outlives scenes and stations; this is the number that was true at the moment of
        /// this catch, kept only for as long as the catch is being shown.
        ///
        /// Zero means it is not known — no fish, no weight, or no log to ask — and the prompt then
        /// says what it said before there was a tally at all.
        ///
        /// Replicated because station state is, but only the Fisher who landed it is shown it.
        /// </summary>
        private readonly NetworkVariable<int> _caughtSessionCount = new NetworkVariable<int>(
            0,
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
        /// How much longer the phase this station is in has to run, counted down by the server alone.
        ///
        /// One value for both timed phases rather than one each, because a station is only ever in
        /// one of them: waiting counts towards a bite, and a bite counts towards being missed. It is
        /// wound by <see cref="SetPhaseOnServer"/> on the way in, so no transition can forget to.
        ///
        /// Deliberately not replicated. A client that knew when the bite was coming could act on it
        /// before it arrived, and one that knew how much of the window was left could be certain of
        /// a hook it should have had to judge. Everyone learns of a bite when it happens.
        ///
        /// Nothing has to clear it. It is read only in the phases that use it, so every way a cast
        /// can end already stops it by setting the phase, and there is no scheduled callback left in
        /// flight to arrive at an empty station later.
        /// </summary>
        private float _phaseCountdown;

        /// <summary>
        /// Whether the server believes the reel is being turned. Set by the two edges of the
        /// Fisher's hold and by nothing else, and dropped by every phase change, so a hand that was
        /// on the reel cannot still be counted at a station somebody has left.
        ///
        /// Not replicated: whether a crewmate's hand is on their reel is not something anyone else
        /// needs to know, and a flag that flipped with every press would send traffic in proportion
        /// to how hard somebody was clicking.
        /// </summary>
        private bool _reelHeld;

        /// <summary>
        /// How much longer this fish is worked before it runs. Server-only for the same reason the
        /// reel's own clock is: a Fisher who knew when the fish would bolt would not have to watch
        /// for it. Read only while the reel is being turned and the fish is not already running, so
        /// every way a fight can end already stops it.
        /// </summary>
        private float _resistCountdown;

        /// <summary>What this machine last told the server about its own hold, so it says so once.</summary>
        private bool _reelRequested;

        /// <summary>So a station with no reel to turn says so, rather than quietly refusing to move.</summary>
        private bool _hasWarnedMissingReelAction;

        /// <summary>
        /// The water this station is fishing, found once and kept.
        ///
        /// Safe to cache because the two share exactly one lifetime: both are placed in the
        /// expedition scene and both are destroyed when the crew sails home, so this reference
        /// cannot survive into a voyage it does not belong to. Server-side only — no client ever
        /// looks it up, because no client is told what the water is doing.
        ///
        /// Not fetched from ServiceRegistry, which is for the handful of things that outlive a scene
        /// load. This outlives nothing on purpose.
        /// </summary>
        private WaterActivity _waterActivity;

        /// <summary>
        /// Whether the search above has been made. Kept apart from the reference so a scene with no
        /// water is searched once rather than on every cast, and says so once rather than every time.
        /// </summary>
        private bool _hasSearchedWaterActivity;

        private bool IsLocalOccupant =>
            NetworkManager.Singleton != null && _occupantClientId.Value == NetworkManager.Singleton.LocalClientId;

        public override void OnNetworkSpawn()
        {
            _occupantClientId.OnValueChanged += HandleOccupantChanged;
            _phase.OnValueChanged += HandlePhaseChanged;
            _isResisting.OnValueChanged += HandleResistingChanged;
            _caughtFishId.OnValueChanged += HandleCaughtFishChanged;
            _caughtWeightTenths.OnValueChanged += HandleCaughtWeightChanged;
            _caughtSessionCount.OnValueChanged += HandleCaughtCountChanged;

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
            _isResisting.OnValueChanged -= HandleResistingChanged;
            _caughtFishId.OnValueChanged -= HandleCaughtFishChanged;
            _caughtWeightTenths.OnValueChanged -= HandleCaughtWeightChanged;
            _caughtSessionCount.OnValueChanged -= HandleCaughtCountChanged;

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
                    case FishingPhase.Caught:
                        return DescribeOwnCatch();
                    case FishingPhase.Hooked:
                        return _isResisting.Value ? _resistText : _hookedText;
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
                case FishingPhase.Caught:
                    return DescribeCatch(_busyCaughtWeighedText, _busyCaughtNamedText, _busyCaughtText);
                case FishingPhase.Hooked:
                    return _busyHookedText;
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
        /// Brings the cast to something, once the wait this station drew for itself has run out.
        ///
        /// Server only. The countdown is consulted just here, and only while waiting, so a cast that
        /// was reeled in, walked away from or disconnected out from under simply stops being counted
        /// rather than needing to be called off. That is why there is no cancellation anywhere in
        /// this file: there is nothing pending that could arrive late.
        /// </summary>
        private void TickBite()
        {
            FishingPhase phase = Phase;

            // Hooked runs on the Fisher's arm rather than on its own, so it counts only while the
            // reel is being turned. Letting go stops the clock where it stands instead of losing
            // what has already been brought in.
            // A running fish stops the reel outright: holding on through it achieves nothing, which
            // is the whole of the lesson. Nothing is taken away either — the clock simply stands
            // still until the Fisher gives line.
            bool isTimed = phase == FishingPhase.Waiting
                           || phase == FishingPhase.Bite
                           || phase == FishingPhase.Caught
                           || (phase == FishingPhase.Hooked && _reelHeld && !_isResisting.Value);

            if (!isTimed)
            {
                return;
            }

            if (phase == FishingPhase.Hooked)
            {
                // A fish only bolts against a line being pulled, so this runs where the reeling
                // does. That also guarantees a hold precedes every run, and therefore that a release
                // is always available to answer it.
                _resistCountdown -= Time.deltaTime;
                if (_resistCountdown <= 0f)
                {
                    _isResisting.Value = true;

                    GameLog.Info(LogCategory.Fish,
                        $"The fish at '{name}' is running from client {_occupantClientId.Value}.");
                    return;
                }
            }

            _phaseCountdown -= Time.deltaTime;
            if (_phaseCountdown > 0f)
            {
                return;
            }

            ulong occupant = _occupantClientId.Value;

            if (phase == FishingPhase.Waiting)
            {
                SetPhaseOnServer(FishingPhase.Bite);
                GameLog.Info(LogCategory.Fish, $"Something bit at '{name}' for client {occupant}.");
                return;
            }

            if (phase == FishingPhase.Hooked)
            {
                // Chosen before the phase is set, and applied after, because entering a phase clears
                // the fish: the catch is the one moment that puts one back.
                FishDefinition caught = ChooseCatchOnServer();

                // Rolled here, once, and never again: the phase carries the result for its whole
                // moment, and reading it cannot change it. A prompt refreshing, a crewmate looking
                // over, or a peer receiving the value late all read the same number.
                int weightTenths = RollCatchWeightOnServer(caught);

                SetPhaseOnServer(FishingPhase.Caught);
                _caughtFishId.Value = caught != null ? caught.Id : FishDefinition.NoFish;
                _caughtWeightTenths.Value = weightTenths;

                GameLog.Info(LogCategory.Fish, caught == null
                    ? $"Client {occupant} landed a catch at '{name}'."
                    : weightTenths == NoWeight
                        ? $"Client {occupant} landed a {caught.DisplayName} at '{name}'."
                        : $"Client {occupant} landed a {caught.DisplayName} of {FormatWeight(weightTenths)} kg at '{name}'.");

                StoreCatchOnServer(occupant);
                return;
            }

            if (phase == FishingPhase.Caught)
            {
                // The moment passes on its own. Nothing has to be pressed to clear it, so a Fisher
                // who turned away to look at the water does not come back to a station that appears
                // to have stopped working.
                SetPhaseOnServer(FishingPhase.Idle);
                GameLog.Info(LogCategory.Fish, $"'{name}' is clear and ready to cast again.");
                return;
            }

            // The window ran out with no answer. Back to waiting rather than to nothing: the line is
            // still in the water, and a missed bite costs the time it takes for another to come
            // rather than the cast itself.
            SetPhaseOnServer(FishingPhase.Waiting);
            GameLog.Info(LogCategory.Fish, $"The bite at '{name}' got away from client {occupant}.");
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
                // One key for one gesture: put the line out, or strike when something takes it. Which
                // of the two is asked for is chosen from what this machine believes, and the server
                // verifies both independently, so a wrong belief produces a refusal and not a
                // liberty. Pull would read better for striking and is left alone on purpose: it
                // shares a key with Jump, and a Fisher who leapt every time they set the hook would
                // be a worse answer than a key that means the obvious thing twice.
                if (Phase == FishingPhase.Bite)
                {
                    RequestHookServerRpc();
                }
                else
                {
                    RequestStartFishingServerRpc();
                }
            }

            if (_stopFishingAction != null && _stopFishingAction.action.WasPressedThisFrame())
            {
                RequestStopFishingServerRpc();
            }

            PollReelInput();
        }

        /// <summary>
        /// Tells the server when the hold on the reel begins and when it ends, and nothing in
        /// between. The turning is counted there, so holding for four seconds costs two messages
        /// rather than one per frame, and there is no way to ask faster by clicking harder.
        /// </summary>
        private void PollReelInput()
        {
            if (_reelAction == null)
            {
                WarnMissingReelAction();
                return;
            }

            bool isHeld = _reelAction.action.IsPressed();
            if (isHeld == _reelRequested)
            {
                return;
            }

            _reelRequested = isHeld;
            RequestReelServerRpc(isHeld);
        }

        /// <summary>
        /// Said once, and loudly, because the alternative is a station that simply will not come in.
        /// An unassigned action is swallowed by its own null check, which has twice now looked
        /// exactly like a rule that stopped working.
        /// </summary>
        private void WarnMissingReelAction()
        {
            if (_hasWarnedMissingReelAction)
            {
                return;
            }

            _hasWarnedMissingReelAction = true;

            GameLog.Error(LogCategory.Fish,
                $"'{name}' has no Reel Action assigned; this station cannot be reeled in. " +
                "Assign Fishing/Reel on it in the Inspector.");
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

            SetPhaseOnServer(FishingPhase.Waiting);

            GameLog.Info(LogCategory.Fish, $"Client {senderId} started fishing at '{name}'.");
        }

        /// <summary>
        /// Whether the Fisher answered in time.
        ///
        /// There is no separate check that the window is still open, because the phase is the
        /// window: had it run out, the server would already have put this station back to waiting,
        /// and the check below would refuse on that. The clock consulted is the server's own, and no
        /// timing a client reports is read anywhere.
        ///
        /// The role is asked again rather than assumed from occupancy, for the reason casting asks:
        /// one lookup, and a decision that stands on its own rather than on an invariant established
        /// elsewhere that a later change might quietly break.
        /// </summary>
        [ServerRpc(RequireOwnership = false)]
        private void RequestHookServerRpc(ServerRpcParams parameters = default)
        {
            ulong senderId = parameters.Receive.SenderClientId;

            CrewRoleRegistry registry = ServiceRegistry.Get<CrewRoleRegistry>();
            if (registry == null)
            {
                GameLog.Error(LogCategory.Fish,
                    $"Refused client {senderId} hooking at '{name}': no crew registry, so nobody's job can be confirmed.");
                return;
            }

            PlayerRole role = registry.GetRole(senderId);
            if (role != PlayerRole.Fisher)
            {
                GameLog.Info(LogCategory.Fish,
                    $"Refused client {senderId} hooking at '{name}': only a Fisher may fish there, and they are {role}.");
                return;
            }

            if (_occupantClientId.Value != senderId)
            {
                GameLog.Info(LogCategory.Fish,
                    $"Refused client {senderId} hooking at '{name}': they do not have it.");
                return;
            }

            if (Phase != FishingPhase.Bite)
            {
                GameLog.Info(LogCategory.Fish,
                    $"Refused client {senderId} hooking at '{name}': nothing is biting, it is {Phase}.");
                return;
            }

            SetPhaseOnServer(FishingPhase.Hooked);

            GameLog.Info(LogCategory.Fish, $"Client {senderId} set the hook at '{name}'.");
        }

        /// <summary>
        /// Whether the reel is being turned.
        ///
        /// Carries only which edge this is, never how long it has been held or how far the line has
        /// come: the server owns the clock, so nothing a client reports about time is read. Sent
        /// twice per reel rather than once per frame, which is also why it needs no limiting — there
        /// is nothing to gain by asking again.
        /// </summary>
        [ServerRpc(RequireOwnership = false)]
        private void RequestReelServerRpc(bool isReeling, ServerRpcParams parameters = default)
        {
            ulong senderId = parameters.Receive.SenderClientId;

            CrewRoleRegistry registry = ServiceRegistry.Get<CrewRoleRegistry>();
            if (registry == null)
            {
                GameLog.Error(LogCategory.Fish,
                    $"Refused client {senderId} reeling at '{name}': no crew registry, so nobody's job can be confirmed.");
                return;
            }

            PlayerRole role = registry.GetRole(senderId);
            if (role != PlayerRole.Fisher)
            {
                GameLog.Info(LogCategory.Fish,
                    $"Refused client {senderId} reeling at '{name}': only a Fisher may fish there, and they are {role}.");
                return;
            }

            if (_occupantClientId.Value != senderId)
            {
                GameLog.Info(LogCategory.Fish,
                    $"Refused client {senderId} reeling at '{name}': they do not have it.");
                return;
            }

            if (Phase != FishingPhase.Hooked)
            {
                GameLog.Info(LogCategory.Fish,
                    $"Refused client {senderId} reeling at '{name}': there is nothing on the line, it is {Phase}.");
                return;
            }

            // Letting go is the answer to a run, and the same edge that pauses an ordinary reel is
            // what gives the line. Nothing is added to the message to say so: the server already
            // knows the fish is running, and the client only has to report that its hand came off.
            if (!isReeling && _isResisting.Value)
            {
                _isResisting.Value = false;
                ArmResistCountdown();

                GameLog.Info(LogCategory.Fish,
                    $"Client {senderId} gave line at '{name}' and the run is over.");
            }

            _reelHeld = isReeling;
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

            // Wound on the way in rather than by whoever happens to be making the transition, so a
            // phase that runs on a clock cannot be entered without one. Idle alone keeps no time:
            // a station at rest is waiting for nothing.
            _phaseCountdown = phase == FishingPhase.Waiting ? DrawBiteDelayOnServer()
                : phase == FishingPhase.Bite ? _biteWindow
                : phase == FishingPhase.Hooked ? _reelDuration
                : phase == FishingPhase.Caught ? _catchDisplaySeconds
                : 0f;

            if (phase == FishingPhase.Hooked)
            {
                ArmResistCountdown();
            }

            // Dropped on every change, so leaving, quitting or being disconnected mid-reel cannot
            // leave a hand counted on a reel nobody is holding, nor a fish still running at a
            // station nobody is at. One place rather than a clearing step in each of those paths.
            _reelHeld = false;
            _isResisting.Value = false;

            // A fish belongs to the catch that produced it. Cleared here rather than at each of the
            // ways a cycle can end, so nothing landed earlier can be read off a station that has
            // moved on, and no fish can survive into the next cast. The catch itself sets it again
            // immediately afterwards, which is the one place it is ever anything else.
            _caughtFishId.Value = FishDefinition.NoFish;
            _caughtWeightTenths.Value = NoWeight;
            _caughtSessionCount.Value = 0;
        }

        /// <summary>
        /// How long this cast waits, given what the water is doing.
        ///
        /// Server only. Called from the one place a station enters Waiting, and the conditional
        /// above evaluates it only on that branch, so no other transition asks and no client ever
        /// runs it.
        ///
        /// Read once, here, and never again. The result becomes a plain number in the countdown, so
        /// water that turns a second later cannot lengthen or shorten a wait already under way. That
        /// is what makes the Observer's call worth acting on at the moment it is made rather than
        /// worth acting on at leisure — and it is the same rule every other clock in this file
        /// follows: drawn on the way in, read only by the phase that owns it.
        ///
        /// A scene with no water fishes exactly as it did before any of this existed. Failing open
        /// is the right way round: losing the whole fishing loop to a missing object on deck would
        /// be far worse than losing the Observer's part of it, and the miss is named loudly enough
        /// to be fixed.
        /// </summary>
        private float DrawBiteDelayOnServer()
        {
            float delay = Random.Range(_minBiteDelay, _maxBiteDelay);

            WaterActivity water = FindWaterActivity();

            return water == null ? delay : delay * water.BiteDelayMultiplier;
        }

        /// <summary>
        /// Finds the water once and remembers the answer, including when the answer is nothing.
        ///
        /// Searched rather than assigned in the Inspector so a station is not made unusable by a
        /// reference nobody remembered to drag, and searched once rather than per cast because the
        /// object it looks for shares this one's lifetime exactly.
        /// </summary>
        private WaterActivity FindWaterActivity()
        {
            if (_hasSearchedWaterActivity)
            {
                return _waterActivity;
            }

            _hasSearchedWaterActivity = true;
            _waterActivity = FindFirstObjectByType<WaterActivity>();

            if (_waterActivity == null)
            {
                GameLog.Error(LogCategory.Fish,
                    $"'{name}' found no Water Activity in this scene, so bites will keep their " +
                    "unmodified timing and the Lookout has nothing to report. Add a Water Activity " +
                    "to the expedition scene.");
            }

            return _waterActivity;
        }

        /// <summary>
        /// Picks what came up, from what this station was told it could hold.
        ///
        /// Server only, and the client is never asked: there is no message that carries a fish, so
        /// there is nothing to claim and no field to claim it in. Evenly among the usable entries,
        /// because weighting them is a question about rarity and rarity is a system nobody has yet.
        ///
        /// A station with nothing usable configured returns nothing and says so. The catch still
        /// happens and the loop still runs — a Fisher is not stranded by a mistake in an Inspector —
        /// but no fish is invented to cover it up.
        /// </summary>
        private FishDefinition ChooseCatchOnServer()
        {
            int usable = 0;

            if (_fishPool != null)
            {
                for (int i = 0; i < _fishPool.Length; i++)
                {
                    if (_fishPool[i] != null && _fishPool[i].IsValid)
                    {
                        usable++;
                    }
                }
            }

            if (usable == 0)
            {
                GameLog.Error(LogCategory.Fish,
                    $"'{name}' landed a catch but has no usable fish configured. Add Fish Definitions " +
                    "to its Fish Pool, each with a non-zero Id and a Display Name.");
                return null;
            }

            // Walked rather than collected, so choosing a fish allocates nothing.
            int pick = Random.Range(0, usable);

            for (int i = 0; i < _fishPool.Length; i++)
            {
                if (_fishPool[i] == null || !_fishPool[i].IsValid)
                {
                    continue;
                }

                if (pick == 0)
                {
                    return _fishPool[i];
                }

                pick--;
            }

            return null;
        }

        /// <summary>
        /// Writes the catch down against the Fisher who made it.
        ///
        /// Reads the two values back out of the variables that were just settled, rather than being
        /// handed them, so what is kept is provably the same pair everyone was shown. Nothing is
        /// rolled again and nothing is worked out a second time.
        ///
        /// Called once, from the single transition that lands a fish, which is why one catch cannot
        /// become two: a prompt asked to refresh, a crewmate glancing over, the moment timing out,
        /// or the Fisher quitting during it all run elsewhere and never reach here.
        ///
        /// A catch missing either half is shown but not kept. Half a record is worse than none — it
        /// would read later as a fish that weighed nothing, or a weight belonging to no fish — and
        /// the reason it happened has already been logged where it happened.
        /// </summary>
        private void StoreCatchOnServer(ulong clientId)
        {
            int fishId = _caughtFishId.Value;
            int weightTenths = _caughtWeightTenths.Value;

            if (fishId == FishDefinition.NoFish || weightTenths == NoWeight)
            {
                GameLog.Warn(LogCategory.Fish,
                    $"The catch at '{name}' was shown to client {clientId} but not kept: it has " +
                    $"{(fishId == FishDefinition.NoFish ? "no fish" : "no weight")}. " +
                    "Fix the station's Fish Pool or that fish's weight range.");
                return;
            }

            CrewCatchLog log = ServiceRegistry.Get<CrewCatchLog>();
            if (log == null)
            {
                // ServiceRegistry has already said what was missing. Said again here because a crew
                // fishing all evening into nothing at all is worth more than one line of warning.
                GameLog.Error(LogCategory.Fish,
                    $"No crew catch log, so client {clientId}'s catch at '{name}' was not kept. " +
                    "Add a Crew Catch Log to the services object in Bootstrap.");
                return;
            }

            log.RecordCatchOnServer(clientId, fishId, weightTenths);

            int sessionCatches = log.GetCatchCount(clientId);

            // The same number the line below reports, kept where the prompt can reach it. Taken
            // from the log rather than counted here, so nothing on any machine adds anything up.
            _caughtSessionCount.Value = sessionCatches;

            GameLog.Info(LogCategory.Fish,
                $"Client {clientId} stored catch: fish id {fishId}, {FormatWeight(weightTenths)} kg. " +
                $"Session catches: {sessionCatches}.");
        }

        /// <summary>
        /// Puts the catch on the scale.
        ///
        /// Server only. No message carries a weight, so there is nothing for a client to submit and
        /// no field to submit it in; the number exists only after the server has already decided
        /// which fish it was.
        ///
        /// Drawn in tenths rather than in kilograms and rounded afterwards. Rounding a kilogram
        /// figure could land a hair outside the range a designer typed, and could round a very small
        /// fish down to nothing at all, which is the value that means no fish was weighed.
        ///
        /// A fish whose range was never configured is not given an invented one. It weighs nothing,
        /// which the prompt reads as a fish without a scale rather than as a fish of no weight, and
        /// the mistake is named loudly enough to be fixed.
        /// </summary>
        private int RollCatchWeightOnServer(FishDefinition fish)
        {
            if (fish == null)
            {
                return NoWeight;
            }

            if (!fish.HasValidWeightRange)
            {
                GameLog.Error(LogCategory.Fish,
                    $"'{fish.DisplayName}' (id {fish.Id}) has no usable weight range: Min Weight Kg is " +
                    $"{fish.MinWeightKg} and Max Weight Kg is {fish.MaxWeightKg}. Both must be above zero " +
                    "and the maximum must not be below the minimum. The catch stands, unweighed.");
                return NoWeight;
            }

            // At least one tenth, so the lightest legal fish still registers on the scale rather
            // than reading as nothing caught. Range's integer form excludes its upper bound.
            int minTenths = Mathf.Max(1, Mathf.RoundToInt(fish.MinWeightKg * 10f));
            int maxTenths = Mathf.Max(minTenths, Mathf.RoundToInt(fish.MaxWeightKg * 10f));

            return Random.Range(minTenths, maxTenths + 1);
        }

        /// <summary>
        /// Tenths of a kilogram as a number with one decimal place, built from the whole number
        /// rather than from a float, so every machine writes the same digits and none of them writes
        /// a comma where another writes a point.
        /// </summary>
        private static string FormatWeight(int tenths)
        {
            return $"{tenths / 10}.{tenths % 10}";
        }

        /// <summary>
        /// Turns the number back into the fish, using the same list the server chose from. Every
        /// peer holds it, because it is configured on this station and this station exists on all
        /// of them.
        /// </summary>
        private FishDefinition FindFish(int id)
        {
            if (id == FishDefinition.NoFish || _fishPool == null)
            {
                return null;
            }

            for (int i = 0; i < _fishPool.Length; i++)
            {
                if (_fishPool[i] != null && _fishPool[i].Id == id)
                {
                    return _fishPool[i];
                }
            }

            return null;
        }

        /// <summary>Drawn afresh, so no two fights run to the same rhythm.</summary>
        private void ArmResistCountdown()
        {
            _resistCountdown = Random.Range(_minResistDelay, _maxResistDelay);
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
            // Forgotten here as well as on the server, because the server drops its own copy on
            // every change: a hand still on the button would otherwise have nothing new to report
            // when the next fish is on, and would have to be lifted first.
            _reelRequested = false;

            RefreshLocalPrompt();
        }

        /// <summary>
        /// The prompt has to change the moment a fish runs, because what is being asked for is the
        /// opposite of what the Fisher is already doing. Driven by the replicated value arriving
        /// rather than by anything watching for it, exactly as a phase change is.
        /// </summary>
        private void HandleResistingChanged(bool previous, bool current)
        {
            RefreshLocalPrompt();
        }

        /// <summary>
        /// The fish's number arrives on its own message and may land a moment after the phase does,
        /// so the prompt is asked again when it does. Until then a catch reads as a catch, which is
        /// true and is also what a station with nothing configured says for the whole moment.
        /// </summary>
        private void HandleCaughtFishChanged(int previous, int current)
        {
            RefreshLocalPrompt();
        }

        /// <summary>
        /// The scale travels on its own message and may land a moment after the fish does. Reading
        /// it never changes it — the number was settled on the server when the catch happened — so
        /// this only asks the prompt to say it.
        /// </summary>
        private void HandleCaughtWeightChanged(int previous, int current)
        {
            RefreshLocalPrompt();
        }

        /// <summary>
        /// The tally travels on its own message and may land after the fish and the scale do.
        /// Reading it never changes it — the server settled it when the catch happened — so this
        /// only asks the prompt to say it.
        /// </summary>
        private void HandleCaughtCountChanged(int previous, int current)
        {
            RefreshLocalPrompt();
        }

        /// <summary>
        /// Names the fish if this peer can, and falls back to saying only that something was landed
        /// if it cannot: the number may not have arrived yet, or the station may have had nothing to
        /// choose from. Substituted rather than formatted, so a placeholder edited into something
        /// malformed loses the name rather than throwing.
        /// </summary>
        /// <summary>
        /// What the Fisher who landed it reads: the same account everyone else gets, with their
        /// tally on the end once the server has said what it is.
        ///
        /// A tally of nought means it is not known rather than that nothing was caught — no fish, no
        /// weight, or no log to ask — and in that case this says exactly what it said before there
        /// was a tally at all.
        /// </summary>
        private string DescribeOwnCatch()
        {
            int sessionCount = _caughtSessionCount.Value;
            FishDefinition fish = FindFish(_caughtFishId.Value);
            int tenths = _caughtWeightTenths.Value;

            bool canCount = sessionCount > 0
                            && fish != null
                            && tenths != NoWeight
                            && !string.IsNullOrEmpty(_caughtCountedText);

            if (!canCount)
            {
                return DescribeCatch(_caughtWeighedText, _caughtNamedText, _caughtText);
            }

            return _caughtCountedText
                .Replace("{0}", fish.DisplayName)
                .Replace("{1}", FormatWeight(tenths))
                .Replace("{2}", sessionCount.ToString());
        }

        private string DescribeCatch(string weighedText, string namedText, string fallbackText)
        {
            FishDefinition fish = FindFish(_caughtFishId.Value);
            if (fish == null)
            {
                return fallbackText;
            }

            int tenths = _caughtWeightTenths.Value;
            if (tenths != NoWeight && !string.IsNullOrEmpty(weighedText))
            {
                return weighedText
                    .Replace("{0}", fish.DisplayName)
                    .Replace("{1}", FormatWeight(tenths));
            }

            // Named but unweighed: either the scale has not arrived yet, or this fish was never
            // given a range. Saying what it was beats saying nothing, and beats inventing a number.
            return string.IsNullOrEmpty(namedText)
                ? fallbackText
                : namedText.Replace("{0}", fish.DisplayName);
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
