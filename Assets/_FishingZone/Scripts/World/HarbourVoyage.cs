using System;
using FishingZone.Core;
using Unity.Netcode;
using UnityEngine;

namespace FishingZone.World
{
    /// <summary>Whether the crew is lying in harbour or out on a voyage.</summary>
    public enum VoyagePhase : byte
    {
        Berthed = 0,
        UnderWay = 1
    }

    /// <summary>
    /// When a voyage begins and ends, as rules apart from any scene or network, so they can be
    /// tested on their own.
    /// </summary>
    public static class VoyageRules
    {
        /// <summary>
        /// Lying in harbour, and the boat is no longer in the harbour's water. Open sea and any other
        /// region both count as out.
        /// </summary>
        public static bool ShouldDepart(VoyagePhase phase, int harbourRegionId, int boatRegionId)
        {
            return phase == VoyagePhase.Berthed && boatRegionId != harbourRegionId;
        }

        /// <summary>
        /// Under way, and lying alongside the quay slowly enough to count as berthed. Coming back into
        /// the harbour's water is not enough: a voyage ends at the quay, not at the breakwater.
        /// </summary>
        public static bool CanEnd(VoyagePhase phase, BerthStatus berth)
        {
            return phase == VoyagePhase.UnderWay && berth == BerthStatus.Berthed;
        }

        /// <summary>The number a departure gives the voyage it begins: one more than the last.</summary>
        public static int NextVoyageNumber(int lastVoyageNumber)
        {
            return lastVoyageNumber + 1;
        }
    }

    /// <summary>
    /// Where the voyage stands: its phase, and which voyage of the session it is.
    ///
    /// One value rather than two variables, for the reason the clock's anchor is one: a voyage
    /// starting changes both at once, and two variables could be read between their updates and
    /// describe voyage 2 still lying in harbour.
    /// </summary>
    public struct VoyageState : INetworkSerializeByMemcpy, IEquatable<VoyageState>
    {
        public VoyagePhase Phase;

        /// <summary>Counts from 1 with the first departure; nought until the crew has left at all.</summary>
        public int Number;

        public bool Equals(VoyageState other)
        {
            return Phase == other.Phase && Number == other.Number;
        }

        public override bool Equals(object obj)
        {
            return obj is VoyageState other && Equals(other);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine((int)Phase, Number);
        }
    }

    /// <summary>
    /// When a voyage begins and ends, in a world the crew never leaves.
    ///
    /// A voyage used to begin when a scene loaded: pressing "Set sail" in port loaded the sea, and
    /// everything that belonged to one trip started over with it. In a harbour that is part of the
    /// sea, nothing loads, so the beginning has to be something that happens in the world — and the
    /// thing that happens is the boat leaving the harbour. Not leaving the berth: a boat lying at
    /// the quay drifts a metre or two, and that is not a departure.
    ///
    /// The server decides, from its own boat, asking the regions at the moment it decides, as every
    /// other server rule about where the boat is does. What travels is the result, so every peer
    /// agrees on which voyage it is and a crewmate joining mid-voyage reads it with the spawn.
    ///
    /// Raises <see cref="VoyageStarted"/> on every peer that sees a voyage begin. Things that belong
    /// to one trip listen for it; a scene without one of these keeps starting trips the old way, on
    /// arrival, so the fallback scenes behave exactly as they always have.
    ///
    /// A voyage ends when the Navigator says so at the quay, and the server agrees the boat is lying
    /// there; the station asks, and this records it. Nothing loads and nobody moves: the phase goes
    /// back to lying in harbour, keeping its number, and <see cref="VoyageEnded"/> tells everything
    /// that belongs to one trip to put itself away. The next departure is the next voyage.
    /// </summary>
    public class HarbourVoyage : NetworkBehaviour
    {
        /// <summary>The harbour the crew sets out from. Leaving it, by region id, begins a voyage.</summary>
        [SerializeField]
        private RegionDefinition _harbourRegion;

        /// <summary>The boat whose departure counts. Found in the scene if left empty.</summary>
        [SerializeField]
        private Transform _boat;

        /// <summary>
        /// Whether coming alongside ends the voyage here, in this scene. Off, the way home loads port
        /// as it did before the harbour was part of the sea — kept so that can be had back without
        /// undoing anything.
        /// </summary>
        [SerializeField]
        private bool _endVoyageInHarbour = true;

        private readonly NetworkVariable<VoyageState> _state = new NetworkVariable<VoyageState>(
            default,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        /// <summary>An id no region can have, standing in for a harbour nobody assigned.</summary>
        private const int NoHarbour = int.MinValue;

        private static HarbourVoyage _current;

        private bool _hasReportedNoBoat;

        /// <summary>
        /// Raised on every peer when a voyage begins, after the state has changed, with the voyage it
        /// began. A crewmate who joins mid-voyage reads the state rather than hearing this.
        /// </summary>
        public static event Action<HarbourVoyage> VoyageStarted;

        /// <summary>
        /// Raised on every peer when a voyage ends, after the state has changed, with the voyage that
        /// ended. Its number is still the finished voyage's until the next departure.
        /// </summary>
        public static event Action<HarbourVoyage> VoyageEnded;

        /// <summary>The voyage in this scene, or null where voyages still begin on arrival.</summary>
        public static HarbourVoyage Current => _current;

        /// <summary>
        /// Whether this scene starts its voyages by departure. Known from the moment the scene loads,
        /// before Netcode has spawned anything, so nothing reading it on arrival can be early.
        /// </summary>
        public static bool AnyExist => _current != null;

        public VoyagePhase Phase => _state.Value.Phase;

        public int VoyageNumber => _state.Value.Number;

        public bool IsUnderWay => Phase == VoyagePhase.UnderWay;

        public bool EndsVoyageInHarbour => _endVoyageInHarbour;

        // Static state outlives a play session when domain reload is disabled, exactly as the other
        // registries in this project do.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetOnPlay()
        {
            _current = null;
            VoyageStarted = null;
            VoyageEnded = null;
        }

        private void OnEnable()
        {
            if (_current != null && _current != this)
            {
                GameLog.Error(LogCategory.Flow,
                    $"'{name}' is a second harbour voyage; '{_current.name}' already keeps the voyages. This one is ignored.");
                return;
            }

            _current = this;
        }

        private void OnDisable()
        {
            if (_current == this)
            {
                _current = null;
            }
        }

        public override void OnNetworkSpawn()
        {
            // Subscribed first, so the server hears its own departures through the same path a
            // remote client does and there is only one way a voyage is ever announced.
            _state.OnValueChanged += HandleStateChanged;

            if (IsServer && _harbourRegion == null)
            {
                GameLog.Error(LogCategory.Flow,
                    $"'{name}' has no harbour region, so it cannot tell harbour from sea. " +
                    "The first voyage begins at once, as it did when voyages began on arrival.");
            }

            GameLog.Info(LogCategory.Flow,
                $"Harbour voyage reads {DescribeState(_state.Value)} on this peer.");
        }

        public override void OnNetworkDespawn()
        {
            _state.OnValueChanged -= HandleStateChanged;
        }

        /// <summary>
        /// Watches for the boat leaving harbour, on the server and nowhere else. Late, so the boat has
        /// moved for the frame and the answer is about where it is now.
        /// </summary>
        private void LateUpdate()
        {
            if (!IsSpawned || !IsServer || _current != this || IsUnderWay)
            {
                return;
            }

            Transform boat = ResolveBoat();
            if (boat == null)
            {
                return;
            }

            // A harbour nobody named is no harbour at all: every water is outside it, so the voyage
            // begins at once — the old behaviour, rather than a voyage that can never begin.
            int harbourId = _harbourRegion != null ? _harbourRegion.Id : NoHarbour;

            RegionDefinition region = RegionVolume.FindRegion(boat.position);
            int boatRegionId = region != null ? region.Id : RegionDefinition.NoRegion;

            if (!VoyageRules.ShouldDepart(Phase, harbourId, boatRegionId))
            {
                return;
            }

            _state.Value = new VoyageState
            {
                Phase = VoyagePhase.UnderWay,
                Number = VoyageRules.NextVoyageNumber(VoyageNumber)
            };
        }

        private Transform ResolveBoat()
        {
            if (_boat != null)
            {
                return _boat;
            }

            BoatRegionTracker tracker = FindAnyObjectByType<BoatRegionTracker>();
            if (tracker != null)
            {
                _boat = tracker.transform;
                return _boat;
            }

            if (!_hasReportedNoBoat)
            {
                _hasReportedNoBoat = true;
                GameLog.Error(LogCategory.Flow, $"'{name}' found no boat in the scene, so no voyage can begin.");
            }

            return null;
        }

        /// <summary>
        /// Ends the voyage under way, here, with nobody going anywhere. Server only, and only for a
        /// voyage that is under way: the caller has already established that the boat lies at the
        /// quay and that the Navigator asked. Answers whether a voyage ended.
        /// </summary>
        public bool EndVoyageOnServer()
        {
            if (!IsServer || !IsSpawned || !IsUnderWay)
            {
                return false;
            }

            _state.Value = new VoyageState { Phase = VoyagePhase.Berthed, Number = VoyageNumber };
            return true;
        }

        private void HandleStateChanged(VoyageState previous, VoyageState current)
        {
            if (current.Phase == VoyagePhase.UnderWay && previous.Phase != VoyagePhase.UnderWay)
            {
                GameLog.Info(LogCategory.Flow, IsServer
                    ? $"Voyage {current.Number} is under way: the boat has left the harbour."
                    : $"Voyage {current.Number} is under way (heard from the host).");

                VoyageStarted?.Invoke(this);
                return;
            }

            if (current.Phase == VoyagePhase.Berthed && previous.Phase == VoyagePhase.UnderWay)
            {
                GameLog.Info(LogCategory.Flow, IsServer
                    ? $"Voyage {current.Number} has ended alongside the quay."
                    : $"Voyage {current.Number} has ended alongside the quay (heard from the host).");

                VoyageEnded?.Invoke(this);
            }
        }

        private static string DescribeState(VoyageState state)
        {
            return state.Phase == VoyagePhase.UnderWay
                ? $"voyage {state.Number} under way"
                : $"lying in harbour after {state.Number} voyages";
        }
    }
}
