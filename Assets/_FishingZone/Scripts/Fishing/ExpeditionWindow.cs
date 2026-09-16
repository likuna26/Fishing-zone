using FishingZone.Core;
using Unity.Netcode;
using UnityEngine;

namespace FishingZone.Fishing
{
    /// <summary>
    /// How much of the fishing day is left.
    ///
    /// Written out because it travels over the network as an integer, and letting these shift with a
    /// future reordering would silently change what a crew is being told.
    /// </summary>
    public enum ExpeditionPhase
    {
        Open = 0,
        Fading = 1,
        Closed = 2
    }

    /// <summary>
    /// The fishing day, and how much of it the crew has left.
    ///
    /// A voyage has had no end since voyages existed. Going home costs the crew everything and gains
    /// them nothing, so "shall we head back?" has never been a question anybody had to answer — they
    /// went back when they got bored. This is the first thing that makes staying cost something, and
    /// therefore the first thing that makes leaving a decision.
    ///
    /// It bounds the fishing and not the voyage. Nothing is taken away when the day ends: no catch
    /// is lost, no line breaks, no scene changes underneath anybody, and the Navigator still sails
    /// home on their own say-so at their own pace. It is a closing time rather than a punishment,
    /// which is the only shape a constraint can honestly take in a game that pays nothing for coming
    /// back yet.
    ///
    /// NOTHING IS ENFORCED HERE. This reports the day and only reports it; fishing is unaffected.
    /// The gate that refuses a late cast is a separate change, deliberately made after a crew has
    /// felt this pacing and settled what the day should be worth.
    ///
    /// Three states rather than a clock, because a countdown replicated to everyone is a number on a
    /// screen, and a number on a screen would do the Observer's job for them. What travels is what
    /// the light is doing; how long is left stays the server's business, and reading it off the
    /// water is the Lookout's.
    ///
    /// Placed in the expedition scene, which is what scopes it to a voyage: created when the crew
    /// reaches the grounds, destroyed when they sail home, so every trip opens on a fresh day and
    /// there is nothing to reset, clear or carry back to port.
    /// </summary>
    public class ExpeditionWindow : NetworkBehaviour
    {
        /// <summary>
        /// Standing in for the whole of this scene's day, so anything that wants to ask can, without
        /// a reference nobody remembered to drag and without searching. One per scene: two would be
        /// two answers to a question with one, and it says so rather than picking.
        /// </summary>
        private static ExpeditionWindow _current;

        public static ExpeditionWindow Current => _current;

        /// <summary>
        /// How long the crew may fish. Serialized because the right answer depends on how far apart
        /// the grounds are, which is a thing about a map rather than a thing about code: a crossing
        /// that costs a minute of eight is a wager, and one that costs ten seconds is free.
        /// </summary>
        [SerializeField]
        private float _windowSeconds = 480f;

        /// <summary>
        /// How much must be left for the light to be going. The warning, not the end — it exists so
        /// the crew can decide whether one more crossing is worth it while there is still a decision
        /// to make.
        /// </summary>
        [SerializeField]
        private float _fadingAtSecondsRemaining = 120f;

        /// <summary>
        /// What the light is doing, and nothing about how long it has left.
        ///
        /// An int rather than the enum for the reason every other replicated enum here is: it keeps
        /// the wire to types this project has proven, and the written-out values make it stable.
        /// </summary>
        private readonly NetworkVariable<int> _phase = new NetworkVariable<int>(
            (int)ExpeditionPhase.Open,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        public ExpeditionPhase Phase => (ExpeditionPhase)_phase.Value;

        /// <summary>
        /// How much of the day is left, counted down by the server alone.
        ///
        /// Deliberately not replicated. A crew who could read the seconds would not need anybody to
        /// watch the light, and the Lookout's judgement — is one more crossing worth it? — would
        /// become arithmetic anybody could do.
        /// </summary>
        private float _remaining;

        private void OnEnable()
        {
            if (_current != null && _current != this)
            {
                GameLog.Error(LogCategory.Flow,
                    $"'{name}' is a second Expedition Window in this scene; '{_current.name}' already holds the day. " +
                    "Keep one and remove the other.");
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

        // Static state outlives a play session when domain reload is disabled, exactly as the
        // registries this borrows from do.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetOnPlay()
        {
            _current = null;
        }

        public override void OnNetworkSpawn()
        {
            if (!IsServer)
            {
                return;
            }

            _remaining = _windowSeconds;
            _phase.Value = (int)(_remaining > _fadingAtSecondsRemaining
                ? ExpeditionPhase.Open
                : ExpeditionPhase.Fading);

            GameLog.Info(LogCategory.Flow,
                $"The crew have {_remaining:F0}s of fishing, and the light starts {DescribeState(Phase)}.");
        }

        /// <summary>
        /// Counts the day down, on the server's clock and nobody else's.
        ///
        /// Guarded on IsSpawned before the variable is touched, because a NetworkVariable read or
        /// written before its object is spawned throws, and a component that runs a clock every
        /// frame is exactly where that would go unnoticed.
        ///
        /// Stops counting once the day is over. There is nothing after Closed, so a voyage nobody
        /// ends does not keep subtracting from a number nothing reads.
        /// </summary>
        private void Update()
        {
            if (!IsSpawned || !IsServer)
            {
                return;
            }

            if (Phase == ExpeditionPhase.Closed)
            {
                return;
            }

            _remaining -= Time.deltaTime;

            ExpeditionPhase next = _remaining <= 0f ? ExpeditionPhase.Closed
                : _remaining <= _fadingAtSecondsRemaining ? ExpeditionPhase.Fading
                : ExpeditionPhase.Open;

            if (next == Phase)
            {
                return;
            }

            _phase.Value = (int)next;

            GameLog.Info(LogCategory.Flow, next == ExpeditionPhase.Closed
                ? "The light has gone; the fishing day is over."
                : $"The light is {DescribeState(next)}, with about {Mathf.Max(_remaining, 0f):F0}s of it left.");
        }

        private static string DescribeState(ExpeditionPhase phase)
        {
            switch (phase)
            {
                case ExpeditionPhase.Closed:
                    return "gone";
                case ExpeditionPhase.Fading:
                    return "going";
                default:
                    return "good";
            }
        }
    }
}
