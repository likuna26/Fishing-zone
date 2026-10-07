using FishingZone.Roles;
using FishingZone.World;
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
        /// What the way home says while the boat is not yet alongside. Used only by a station that
        /// returns to port, and only in a scene that has a berth.
        /// </summary>
        [SerializeField]
        private string _awayFromBerthText = "Bring her alongside the quay to end the voyage";

        [SerializeField]
        private string _tooFastAtBerthText = "Ease her to a stop alongside the quay";

        /// <summary>
        /// Time constant of the speed smoothing on clients, matching the dashboard's, so the prompt
        /// and the knots shown at the wheel do not tell two different stories.
        /// </summary>
        [SerializeField]
        private float _speedSmoothingTime = 0.5f;

        private Rigidbody _boatBody;
        private Transform _boat;

        private Vector3 _lastBoatPosition;
        private bool _hasLastBoatPosition;
        private float _smoothedBoatSpeed;

        private bool IsReturnToPort => _destination == GameState.Port;

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

        /// <summary>
        /// The boat this station is aboard, if it is aboard one. A station on a jetty has no body
        /// above it and stays put, which reads as stopped wherever it is.
        /// </summary>
        private void Awake()
        {
            _boatBody = GetComponentInParent<Rigidbody>();
            _boat = _boatBody != null ? _boatBody.transform : transform;
        }

        private void OnEnable()
        {
            // Forgotten on purpose, as the dashboard does: a boat placed by a scene load would
            // otherwise read as one great leap.
            _hasLastBoatPosition = false;
            _smoothedBoatSpeed = 0f;
        }

        /// <summary>
        /// Measures how fast the boat is going, for the prompt on clients.
        ///
        /// Taken from how far it moved, because on a client the boat's body is kinematic and
        /// reports no velocity at all. The server never uses this: it reads its own simulated body.
        /// Late, so the client has already moved the boat to this frame's pose.
        /// </summary>
        private void LateUpdate()
        {
            if (!IsReturnToPort)
            {
                return;
            }

            Vector3 position = _boat.position;
            float deltaTime = Time.deltaTime;

            if (_hasLastBoatPosition && deltaTime > 0f)
            {
                Vector3 moved = position - _lastBoatPosition;
                moved.y = 0f;

                float speed = moved.magnitude / deltaTime;
                float blend = 1f - Mathf.Exp(-deltaTime / Mathf.Max(_speedSmoothingTime, 0.01f));
                _smoothedBoatSpeed = Mathf.Lerp(_smoothedBoatSpeed, speed, blend);
            }

            _lastBoatPosition = position;
            _hasLastBoatPosition = true;
        }

        /// <summary>
        /// Whether the boat lies where a voyage may end.
        ///
        /// Always berthed for a station that sets sail rather than returns, and for a scene with no
        /// berth, which ends a voyage anywhere exactly as before.
        ///
        /// The server reads its own simulated body, so the decision rests on the real boat. Clients
        /// read the measured speed, which only decides the words they see.
        /// </summary>
        private BerthStatus CurrentBerthStatus()
        {
            if (!IsReturnToPort || !HarbourBerth.AnyExist)
            {
                return BerthStatus.Berthed;
            }

            float speed;
            if (IsServer && _boatBody != null && !_boatBody.isKinematic)
            {
                Vector3 velocity = _boatBody.linearVelocity;
                velocity.y = 0f;
                speed = velocity.magnitude;
            }
            else
            {
                speed = _smoothedBoatSpeed;
            }

            return HarbourBerth.StatusOf(_boat.position, speed);
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
            if (!PlayerRoleController.IsAuthorizedFor(interactor, PlayerRole.Navigator))
            {
                return WrongRoleText;
            }

            switch (CurrentBerthStatus())
            {
                case BerthStatus.Away:
                    return _awayFromBerthText;
                case BerthStatus.TooFast:
                    return _tooFastAtBerthText;
                default:
                    return DepartText;
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
            if (!registry.IsAuthorizedFor(senderId, PlayerRole.Navigator))
            {
                GameLog.Info(LogCategory.Flow,
                    $"Refused client {senderId} at '{name}': only the Navigator says where the boat goes, and they are {role}.");
                return;
            }

            // A voyage ends alongside the quay, never out at sea. Asked of the server's own boat, so
            // a client whose prompt read berthed a frame early is still told no.
            BerthStatus berth = CurrentBerthStatus();
            if (berth != BerthStatus.Berthed)
            {
                GameLog.Info(LogCategory.Flow, berth == BerthStatus.TooFast
                    ? $"Refused client {senderId} at '{name}': the boat is at the berth but still moving."
                    : $"Refused client {senderId} at '{name}': the boat is not alongside the quay.");
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
