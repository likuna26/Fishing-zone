using System;
using FishingZone.Core;
using Unity.Netcode;
using UnityEngine;

// UnityEngine.Random is the one meant here. Naming it explicitly is required rather than tidy:
// `using System` above brings System.Random into scope, and an unqualified Random would then be
// ambiguous. This is why FisherStation deliberately has no `using System` at all.
using Random = UnityEngine.Random;

namespace FishingZone.Fishing
{
    /// <summary>
    /// Whether the fish are feeding.
    ///
    /// One thing about the water, and deliberately only one: fish are either moving or they are not.
    /// This is NOT weather. There is no wind, no waves, no sky, nothing visual, and nothing here
    /// touches the boat — a hull handles the same in either state. It is a fact about fish, and the
    /// only system that reads it is fishing.
    ///
    /// The server owns it outright. It rolls a state when the crew arrives, runs a clock, and flips
    /// when the clock expires; no client writes it, asks for it, or reports anything about time.
    /// Replicated because it must reach one player who is not the server — the Observer at the
    /// lookout, who is the only person aboard able to see it.
    ///
    /// Placed in the expedition scene rather than on the persistent services object, which is what
    /// scopes it to a voyage: it is created when the crew arrives at the fishing grounds and
    /// destroyed when they sail home, so every voyage draws fresh water and there is nothing to
    /// reset, clear or carry back to port.
    /// </summary>
    public class WaterActivity : NetworkBehaviour
    {
        /// <summary>
        /// How long a quiet spell lasts. Longer than a feeding one on purpose: the good water should
        /// be worth calling out, which it only is if it is the exception.
        /// </summary>
        [SerializeField]
        private float _minQuietSeconds = 20f;

        [SerializeField]
        private float _maxQuietSeconds = 45f;

        [SerializeField]
        private float _minFeedingSeconds = 12f;

        [SerializeField]
        private float _maxFeedingSeconds = 25f;

        /// <summary>
        /// What feeding water does to the wait for a bite. Below one, so a bite comes sooner.
        ///
        /// Kept here rather than on the stations, so the whole boat is tuned in one place and two
        /// stations cannot disagree about what the same water is worth.
        /// </summary>
        [SerializeField]
        private float _feedingBiteMultiplier = 0.4f;

        /// <summary>Above one, so quiet water is a cost rather than merely the absence of a bonus.</summary>
        [SerializeField]
        private float _quietBiteMultiplier = 1.6f;

        /// <summary>
        /// The whole of what travels. One bit, written by the server and read by everyone, which is
        /// all the lookout needs and all anybody is entitled to know.
        /// </summary>
        private readonly NetworkVariable<bool> _isFeeding = new NetworkVariable<bool>(
            false,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        /// <summary>True when the fish are moving. Readable on any peer, once the value has arrived.</summary>
        public bool IsFeeding => _isFeeding.Value;

        /// <summary>
        /// What the current water does to a wait for a bite. Read by the server alone, at the moment
        /// a line goes out, and never afterwards.
        /// </summary>
        public float BiteDelayMultiplier => _isFeeding.Value ? _feedingBiteMultiplier : _quietBiteMultiplier;

        /// <summary>
        /// Raised on every peer when the water turns. Worth listening to rather than reading
        /// <see cref="IsFeeding"/> once, because it changes while somebody is standing still looking
        /// at it, which is the entire point of the lookout.
        /// </summary>
        public event Action<bool> FeedingChanged;

        /// <summary>
        /// How much of this spell is left, counted down by the server alone.
        ///
        /// Not replicated. A Fisher who knew the water was about to turn could simply wait for it,
        /// which would replace the one thing the Observer is for. What everybody may know is what
        /// the water is doing now.
        /// </summary>
        private float _spellCountdown;

        public override void OnNetworkSpawn()
        {
            // Subscribed before the roll below, so the server hears its own first state through the
            // same path a remote client does and there is only one way this is ever reported.
            _isFeeding.OnValueChanged += HandleFeedingChanged;

            if (IsServer)
            {
                // Rolled rather than started quiet, so a crew cannot learn that every voyage opens
                // the same way and stop asking their lookout.
                bool feeding = Random.value < 0.5f;

                _isFeeding.Value = feeding;
                ArmSpellCountdown(feeding);

                GameLog.Info(LogCategory.Fish,
                    $"The crew arrived on water that is {DescribeState(feeding)}, for {_spellCountdown:F1}s.");
            }
        }

        public override void OnNetworkDespawn()
        {
            _isFeeding.OnValueChanged -= HandleFeedingChanged;
        }

        /// <summary>
        /// Turns the water, on the server's clock and nobody else's.
        ///
        /// Guarded on IsSpawned before anything reads or writes the variable, because a
        /// NetworkVariable touched before its object is spawned throws, and a component that runs a
        /// clock every frame is exactly where that would go unnoticed.
        /// </summary>
        private void Update()
        {
            if (!IsSpawned || !IsServer)
            {
                return;
            }

            _spellCountdown -= Time.deltaTime;
            if (_spellCountdown > 0f)
            {
                return;
            }

            bool feeding = !_isFeeding.Value;

            _isFeeding.Value = feeding;
            ArmSpellCountdown(feeding);

            GameLog.Info(LogCategory.Fish,
                $"The water turned: it is now {DescribeState(feeding)}, for {_spellCountdown:F1}s.");
        }

        /// <summary>
        /// Wound on the way into a spell rather than by whoever caused it, so no state can be entered
        /// without a clock. Drawn afresh each time, so a crew cannot count the water in.
        /// </summary>
        private void ArmSpellCountdown(bool feeding)
        {
            _spellCountdown = feeding
                ? Random.Range(_minFeedingSeconds, _maxFeedingSeconds)
                : Random.Range(_minQuietSeconds, _maxQuietSeconds);
        }

        /// <summary>
        /// Runs on every peer, including the server that wrote it. Says nothing about who should
        /// care: whoever is listening decides what a turn in the water means to them.
        /// </summary>
        private void HandleFeedingChanged(bool previous, bool current)
        {
            FeedingChanged?.Invoke(current);
        }

        private static string DescribeState(bool feeding)
        {
            return feeding ? "feeding" : "quiet";
        }
    }
}
