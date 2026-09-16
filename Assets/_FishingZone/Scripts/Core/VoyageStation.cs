using FishingZone.Fishing;
using FishingZone.Player;
using FishingZone.Roles;
using Unity.Netcode;
using UnityEngine;

namespace FishingZone.Core
{
    /// <summary>
    /// The place aboard where the crew sets off, and the place ashore where they come back.
    ///
    /// One component for both, because they are the same act in opposite directions: somebody says
    /// where the boat is going and everybody goes. Which direction this one is comes from the
    /// destination it was given, so a port and an expedition need one script between them rather
    /// than one each.
    ///
    /// The Navigator decides, as they do at the wheel. Where the boat goes is navigation.
    ///
    /// Holds nothing. There is no voyage in progress, no countdown, no vote and no state of any
    /// kind: this asks a question, the server checks who is asking, and the game flow does the rest.
    /// GameFlowManager already refuses a second transition while one is running and already carries
    /// the whole crew across, so there is nothing here for a scene change to leave behind.
    /// </summary>
    public class VoyageStation : NetworkBehaviour, IInteractable
    {
        /// <summary>
        /// Where this one goes. Only somewhere a boat can sail: Boot, the menu and the lobby are
        /// not places, and an unset field defaults to Boot, so leaving this alone is a mistake this
        /// says out loud rather than a voyage to nowhere.
        /// </summary>
        [SerializeField]
        private GameState _destination = GameState.Expedition;

        /// <summary>
        /// Left empty on purpose. Wording that suits the destination is used unless something is
        /// typed here, so the two placements read correctly without either being retyped — and a
        /// station nobody remembered to word does not end up offering to set sail for home.
        /// </summary>
        [SerializeField]
        private string _departText = string.Empty;

        [SerializeField]
        private string _wrongRoleText = string.Empty;

        /// <summary>
        /// Said to the Navigator at the homeward station once the fishing day is over.
        ///
        /// Left empty on purpose, like the wording above it: the derived line suits the only
        /// station this can ever apply to, so a port and an expedition still need one script
        /// between them and neither has to be retyped.
        ///
        /// It states the day rather than commanding the crew. Nothing here forces anybody home and
        /// nothing here is refused — the wheel is still theirs and leaving is still their call. This
        /// only stops the way home reading exactly as it did an hour ago, when there was still
        /// fishing to do.
        /// </summary>
        [SerializeField]
        private string _dayOverText = string.Empty;

        /// <summary>Whether the day was over when this last worked out what to say.</summary>
        private bool _wasDayOver;

        private bool IsDestinationValid =>
            _destination == GameState.Port || _destination == GameState.Expedition;

        private string DepartText => string.IsNullOrEmpty(_departText)
            ? (_destination == GameState.Expedition ? "Set sail" : "Return to port")
            : _departText;

        private string WrongRoleText => string.IsNullOrEmpty(_wrongRoleText)
            ? (_destination == GameState.Expedition
                ? "Only the Navigator may set sail"
                : "Only the Navigator may return to port")
            : _wrongRoleText;

        private string DayOverText => string.IsNullOrEmpty(_dayOverText)
            ? "The light has gone — take her home"
            : _dayOverText;

        /// <summary>
        /// Whether this is the way home and the fishing is finished.
        ///
        /// Both halves matter. The station that sets sail is never the one to mention a day being
        /// over — it lives in port, where there is no day running at all, and saying so at the jetty
        /// would be nonsense. And a scene with no window has a day that never ends, exactly as every
        /// voyage behaved before there was one.
        ///
        /// Reads the replicated phase and nothing else. No countdown is sent and none is read, so
        /// every peer works this out identically from what the server has already said.
        /// </summary>
        private bool IsDayOverHomeward
        {
            get
            {
                if (_destination != GameState.Port)
                {
                    return false;
                }

                ExpeditionWindow window = ExpeditionWindow.Current;

                return window != null && window.Phase == ExpeditionPhase.Closed;
            }
        }

        /// <summary>
        /// Says once, and loudly, that this will never take anybody anywhere. A station given a
        /// destination it cannot sail to would otherwise look exactly like one nobody was allowed
        /// to use, and the two want very different fixing.
        /// </summary>
        public override void OnNetworkSpawn()
        {
            if (!IsDestinationValid)
            {
                GameLog.Error(LogCategory.Flow,
                    $"'{name}' has a Destination of {_destination}, which is not somewhere a boat can sail. " +
                    "Set it to Port or Expedition in the Inspector.");
            }
        }

        /// <summary>
        /// True for everybody, including players who will certainly be refused.
        ///
        /// Returning false would do more than forbid the press: PlayerInteraction drops a target it
        /// cannot interact with, so this would stop being looked at at all and a Fisher would see no
        /// prompt, no refusal and no reason — it would read as scenery.
        /// </summary>
        public bool CanInteract(GameObject interactor)
        {
            return true;
        }

        /// <summary>
        /// The role comes from the copy carried on the player object, which is the one place this
        /// class is allowed to consult it. A determined client could edit that copy in its own
        /// memory, and the worst it would buy them is an offer their own screen makes and the server
        /// then refuses. It decides what a player reads, never what they may do.
        /// </summary>
        public string GetInteractionText(GameObject interactor)
        {
            if (PlayerRoleController.GetRoleOf(interactor) != PlayerRole.Navigator)
            {
                // Unchanged by the day ending. A Fisher reading the way home is being told whose
                // job it is, which is as true at dusk as at noon, and telling them the fishing is
                // over would offer them something they still may not do.
                return WrongRoleText;
            }

            return IsDayOverHomeward ? DayOverText : DepartText;
        }

        /// <summary>
        /// Watches the fishing day end while somebody is already looking at the way home.
        ///
        /// Needed because prompt text is read once, when a player first looks at something. Every
        /// other thing that changes a prompt in this project announces itself by arriving — a
        /// replicated value landing, an occupant changing — and a Navigator standing at this station
        /// as the light goes would otherwise read the ordinary offer until they looked away and
        /// back, at exactly the moment the wording is worth anything.
        ///
        /// An edge and not a poll of the prompt: the answer is compared with the last one and only a
        /// change says anything. The same shape the lookout and the fishing stations already use to
        /// notice the same kind of silent change. The station in port never changes its answer, so
        /// it never asks for a re-read.
        /// </summary>
        private void Update()
        {
            if (!IsSpawned)
            {
                return;
            }

            bool dayOver = IsDayOverHomeward;
            if (dayOver == _wasDayOver)
            {
                return;
            }

            _wasDayOver = dayOver;

            RefreshLocalPrompt();
        }

        /// <summary>
        /// Asks the local player to read its prompt again.
        ///
        /// Refreshing whatever the player happens to be looking at, rather than insisting it is this
        /// station, keeps this from having to know: re-reading another object's prompt produces the
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

        /// <summary>
        /// Asks, whatever the prompt just said. A player the local copy believes is no Navigator
        /// still gets to ask, and gets their answer from the machine entitled to give one; refusing
        /// here would be quicker and would hide the only thing worth proving.
        /// </summary>
        public void Interact(GameObject interactor)
        {
            if (!IsSpawned)
            {
                // Sending before the object is spawned throws. This can happen for an in-scene
                // station in the moments after the scene loads and before Netcode has spawned it.
                return;
            }

            RequestDepartServerRpc();
        }

        /// <summary>
        /// The decision, and the only one that counts.
        ///
        /// Who is asking comes from the transport rather than from anything the caller sent, so one
        /// player cannot set the crew sailing in another's name. What they are comes from the crew
        /// registry, which is server-only and still standing long after the lobby was unloaded.
        ///
        /// Ownership is not the check, which is why it is not required: a jetty belongs to nobody.
        /// The question is what job the asker took.
        ///
        /// It runs on the server, which is also what makes the transition legal — GameFlowManager
        /// refuses a scene change asked for by anyone else during a session.
        /// </summary>
        [ServerRpc(RequireOwnership = false)]
        private void RequestDepartServerRpc(ServerRpcParams parameters = default)
        {
            ulong senderId = parameters.Receive.SenderClientId;

            if (!IsDestinationValid)
            {
                GameLog.Error(LogCategory.Flow,
                    $"Refused client {senderId} at '{name}': its Destination of {_destination} is not somewhere a boat can sail.");
                return;
            }

            CrewRoleRegistry registry = ServiceRegistry.Get<CrewRoleRegistry>();
            if (registry == null)
            {
                // Refused rather than allowed. The wheel lets anyone steer when the registry is
                // missing, because a crew unable to steer is worse than a role going unchecked.
                // Sailing is not like that: taking the whole crew somewhere on nobody's authority
                // is a larger thing to get wrong than nobody being able to leave.
                GameLog.Error(LogCategory.Flow,
                    $"Refused client {senderId} at '{name}': no crew registry, so nobody's job can be confirmed.");
                return;
            }

            PlayerRole role = registry.GetRole(senderId);
            if (role != PlayerRole.Navigator)
            {
                GameLog.Info(LogCategory.Flow,
                    $"Refused client {senderId} at '{name}': only the Navigator says where the boat goes, and they are {role}.");
                return;
            }

            GameFlowManager flow = ServiceRegistry.Get<GameFlowManager>();
            if (flow == null)
            {
                // ServiceRegistry has already said what was missing.
                return;
            }

            if (flow.IsTransitioning)
            {
                GameLog.Info(LogCategory.Flow,
                    $"Ignored client {senderId} at '{name}': the crew is already on its way somewhere.");
                return;
            }

            GameLog.Info(LogCategory.Flow, $"Client {senderId} took the crew to {_destination} from '{name}'.");

            flow.GoTo(_destination);
        }
    }
}
