using System;
using System.Collections.Generic;
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
    ///
    /// Belongs to one fishing ground: the one it is attached to. That is what makes the Observer's
    /// report worth acting on rather than merely worth hearing. While there was a single stretch of
    /// water for the whole map, every ground was as good as every other and the crew's only sensible
    /// answer to quiet water was to wait it out. A ground that has its own water gives them a second
    /// answer — go somewhere else — and that is the first thing the Navigator can do with something
    /// the Observer said.
    /// </summary>
    public class WaterActivity : NetworkBehaviour
    {
        private static readonly List<WaterActivity> Registered = new List<WaterActivity>();

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
        /// The water this describes. Taken from the same object rather than dragged in, so a ground
        /// and its water cannot be separated by an Inspector reference nobody re-checked: attaching
        /// this to a ground is the whole of the association.
        /// </summary>
        public FishingGround Ground => _ground;

        private FishingGround _ground;

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

        // The list is static, so it outlives a play session when domain reload is disabled. Same
        // arrangement PlayerSpawnPoint and FishingGround already use, for the same reason: what asks
        // about these has to be able to ask before it has any way of having been handed one.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetOnPlay()
        {
            Registered.Clear();
        }

        /// <summary>
        /// Finds its own ground before anything can ask about it, and says so if there is not one.
        ///
        /// Water attached to nothing is unreachable rather than broken — no position is ever inside
        /// it — so this would otherwise be an object quietly running a clock nobody could read. The
        /// likeliest way it happens is a stretch of water left over from when there was only one.
        /// </summary>
        private void Awake()
        {
            _ground = GetComponent<FishingGround>();

            if (_ground == null)
            {
                GameLog.Error(LogCategory.Fish,
                    $"'{name}' has a Water Activity but no Fishing Ground on the same object, so no " +
                    "boat can ever be over it. Put this component on a Fishing Ground, or remove it.");
            }
        }

        private void OnEnable()
        {
            if (!Registered.Contains(this))
            {
                Registered.Add(this);
            }
        }

        private void OnDisable()
        {
            Registered.Remove(this);
        }

        /// <summary>
        /// The water beneath a point, or null for open sea.
        ///
        /// Answers for wherever the asker is standing, which for everything that asks is the boat:
        /// the lookout and the fishing stations are all bolted to a deck that moves. Null is an
        /// ordinary answer and not a failure — most of an expedition map is water worth nothing.
        ///
        /// Gives the same answer on every machine, because both halves of the question are already
        /// on all of them: grounds come with the scene and the boat's transform is replicated. At
        /// the exact edge two peers can disagree by a frame of interpolation, which decides only
        /// what somebody reads; whether a line may go out is settled on the server.
        ///
        /// A walk of a list that holds one entry per ground, asked when somebody casts and when the
        /// lookout checks its words. Caching it would mean giving the answer up every time the boat
        /// moved, which is most frames.
        /// </summary>
        public static WaterActivity Under(Vector3 worldPosition)
        {
            for (int i = 0; i < Registered.Count; i++)
            {
                WaterActivity water = Registered[i];
                if (water != null && water._ground != null && water._ground.Contains(worldPosition))
                {
                    return water;
                }
            }

            return null;
        }

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

                // Named, because a map with several grounds turns one line about "the water" into
                // several that cannot be told apart.
                GameLog.Info(LogCategory.Fish,
                    $"'{name}' opened {DescribeState(feeding)}, for {_spellCountdown:F1}s.");
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
                $"'{name}' turned {DescribeState(feeding)}, for {_spellCountdown:F1}s.");
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
