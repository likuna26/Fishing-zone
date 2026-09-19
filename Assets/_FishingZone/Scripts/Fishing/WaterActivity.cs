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
    /// How much a stretch of water has been worked this voyage.
    ///
    /// Written out because it travels as an integer, and letting these shift with a future
    /// reordering would silently change what a crew is being told.
    /// </summary>
    public enum WaterStock
    {
        Fresh = 0,
        Working = 1,
        Tired = 2
    }

    /// <summary>
    /// How the fish are running at a stretch of water today.
    ///
    /// Not how many, which is stock, and not whether they are biting, which is the water itself.
    /// This is the size of what comes up, and it is settled once when the crew arrive.
    ///
    /// Written out because it travels as an integer.
    /// </summary>
    public enum CatchCondition
    {
        Lean = 0,
        Ordinary = 1,
        Heavy = 2
    }

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
        /// How long the fish stay up when the Lookout calls them.
        ///
        /// Deliberately longer than a natural feeding spell, and deliberately not drawn from that
        /// range: a called window is a known quantity the crew can work to, which is the whole
        /// reason it is worth arranging in advance. It is also why the call does not go through
        /// ArmSpellCountdown — a rolled spell could be shorter than this and would quietly cheat the
        /// crew out of the thing they spent it on.
        /// </summary>
        [SerializeField]
        private float _calledFeedingSeconds = 30f;

        /// <summary>
        /// How long this water refuses to answer again. Long enough that calling is an occasion
        /// rather than a rhythm: quiet water still mostly means wait it out or sail somewhere else,
        /// and the call is the crew's answer to one stretch of it, not to all of them.
        /// </summary>
        [SerializeField]
        private float _callCooldownSeconds = 120f;

        /// <summary>
        /// How many fish must come out of this water before the ones left grow wary, and how many
        /// before it is properly worked over.
        ///
        /// Deliberately small to begin with, so a deliberate test reaches both states inside one
        /// short voyage rather than needing a full day at sea. These are placeholders for measuring
        /// against, not balance: the trip count on the Port board is the instrument, and the right
        /// numbers are some fraction of what a crew actually lands in a voyage.
        /// </summary>
        [SerializeField]
        private int _workingAfterCatches = 3;

        [SerializeField]
        private int _tiredAfterCatches = 6;

        /// <summary>
        /// How often a day at this water runs small or runs big. What is left over is an ordinary
        /// day, so at these odds half of them are, and a crew is not promised a story every trip.
        ///
        /// Rolled per ground and independently, so the two can disagree — which is the whole point.
        /// A voyage where both come up ordinary is a quiet one, and that is allowed.
        /// </summary>
        [SerializeField]
        private float _leanChance = 0.25f;

        [SerializeField]
        private float _heavyChance = 0.25f;

        /// <summary>
        /// What worked water does to the wait for a bite, on top of whatever the water is doing.
        ///
        /// Both above one and both gentle. This is the first thing in the project that takes fishing
        /// away rather than adding to it, and a ground the crew cannot fish is not a decision about
        /// whether to move — it is an eviction. The point is to make crossing worth considering, not
        /// to make staying pointless.
        /// </summary>
        [SerializeField]
        private float _workingBiteMultiplier = 1.25f;

        [SerializeField]
        private float _tiredBiteMultiplier = 1.6f;

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
        /// Whether this water would answer a call right now.
        ///
        /// Replicated because the Lookout must not offer something that would be refused: the
        /// Observer decides when to spend the call, and a decision made against a prompt that lies
        /// is not a decision. It says only yes or no — how long is left is the server's business,
        /// and a number counting down would turn a judgement into an egg timer.
        ///
        /// Starts true, so a crew arriving at a ground has one call in hand. It is scene state like
        /// everything else here, so sailing home and out again brings a fresh one.
        /// </summary>
        private readonly NetworkVariable<bool> _isCallReady = new NetworkVariable<bool>(
            true,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        public bool IsCallReady => _isCallReady.Value;

        /// <summary>
        /// How worked this water is. Coarse and replicated, like the water itself and the light:
        /// what travels is a condition, never a count, so nobody can read a threshold off a screen
        /// and nothing does the Lookout's judging for them.
        ///
        /// One way for the length of a voyage. It is scene state on a scene object, so sailing home
        /// and out again brings water nobody has touched.
        /// </summary>
        private readonly NetworkVariable<int> _stock = new NetworkVariable<int>(
            (int)WaterStock.Fresh,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        public WaterStock Stock => (WaterStock)_stock.Value;

        /// <summary>
        /// How the fish are running here today. Settled once when the crew arrive and unchanged for
        /// the rest of the voyage: it is what kind of day it is, not something the water does.
        ///
        /// Coarse and replicated, like everything else anybody is told about a ground. What travels
        /// is the condition; the arithmetic it implies stays where the fish are weighed.
        /// </summary>
        private readonly NetworkVariable<int> _condition = new NetworkVariable<int>(
            (int)CatchCondition.Ordinary,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        public CatchCondition Condition => (CatchCondition)_condition.Value;

        /// <summary>
        /// What the current water does to a wait for a bite. Read by the server alone, at the moment
        /// a line goes out, and never afterwards.
        ///
        /// The two conditions multiply rather than replace one another, and that is the whole of the
        /// interaction: how the fish are behaving now, times how many of them are left to behave.
        /// Feeding water on a worked ground is still far better than quiet water on a fresh one, so
        /// the Lookout's call keeps every bit of its worth on the very ground that most needs it.
        /// </summary>
        public float BiteDelayMultiplier =>
            (_isFeeding.Value ? _feedingBiteMultiplier : _quietBiteMultiplier) * StockMultiplier;

        private float StockMultiplier
        {
            get
            {
                switch (Stock)
                {
                    case WaterStock.Tired:
                        return _tiredBiteMultiplier;
                    case WaterStock.Working:
                        return _workingBiteMultiplier;
                    default:
                        return 1f;
                }
            }
        }

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

        /// <summary>
        /// How many fish have come out of here this voyage. Server-only and never replicated: what
        /// everyone may know is the condition of the water, not the arithmetic behind it.
        /// </summary>
        private int _caughtHere;

        /// <summary>
        /// How much longer this water refuses to answer. Server-only, like every clock here, and
        /// meaningless while the call is ready: it is wound when one is spent and read only until it
        /// runs out.
        /// </summary>
        private float _callCooldown;

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

        /// <summary>
        /// The water belonging to one ground, or null if that ground has none.
        ///
        /// Asked by name rather than by place, because a catch belongs to the water the hook went
        /// into and the boat may well have sailed since. Where the crew are now is a different
        /// question, and <see cref="Under"/> is the one that answers it.
        /// </summary>
        public static WaterActivity For(FishingGround ground)
        {
            if (ground == null)
            {
                return null;
            }

            for (int i = 0; i < Registered.Count; i++)
            {
                WaterActivity water = Registered[i];
                if (water != null && water.Ground == ground)
                {
                    return water;
                }
            }

            return null;
        }

        /// <summary>
        /// Takes one fish off this water's account.
        ///
        /// Server only, and called once per catch that actually belongs to the trip — not per cast,
        /// per bite, per missed bite or per fish that got away. A crew who worked a ground hard and
        /// landed nothing have not thinned it out.
        ///
        /// Counts up and never down. Water does not recover inside a voyage: a ground that has been
        /// worked stays worked until the crew sail home, which is what makes crossing to the other
        /// one an answer rather than a wait.
        /// </summary>
        public void NoteCatchOnServer()
        {
            if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer)
            {
                return;
            }

            _caughtHere++;

            WaterStock next = _caughtHere >= _tiredAfterCatches ? WaterStock.Tired
                : _caughtHere >= _workingAfterCatches ? WaterStock.Working
                : WaterStock.Fresh;

            if (next == Stock)
            {
                return;
            }

            _stock.Value = (int)next;

            GameLog.Info(LogCategory.Fish,
                $"'{name}' is {DescribeStock(next)} after {_caughtHere} landed here.");
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

                // Settled here and never again. What kind of day it is cannot change under a crew
                // who have already committed to sailing somewhere on the strength of it.
                CatchCondition condition = RollCondition();
                _condition.Value = (int)condition;

                // Named, because a map with several grounds turns one line about "the water" into
                // several that cannot be told apart.
                GameLog.Info(LogCategory.Fish,
                    $"'{name}' opened {DescribeState(feeding)}, for {_spellCountdown:F1}s, " +
                    $"with the fish {DescribeCondition(condition)}.");
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
        ///
        /// The call's cooldown is counted here rather than on a loop of its own, and before the
        /// early return below, because the spell and the cooldown run on different lengths and the
        /// cooldown must keep running through a spell that has not finished.
        /// </summary>
        private void Update()
        {
            if (!IsSpawned || !IsServer)
            {
                return;
            }

            TickCallCooldown();

            _spellCountdown -= Time.deltaTime;
            if (_spellCountdown > 0f)
            {
                return;
            }

            // Whatever ended, natural or called, the water turns to the other thing and takes a
            // fresh spell of it. A called window therefore ends the way any feeding does: it goes
            // quiet and the ordinary cycle carries on from there, with nothing to unwind.
            bool feeding = !_isFeeding.Value;

            _isFeeding.Value = feeding;
            ArmSpellCountdown(feeding);

            GameLog.Info(LogCategory.Fish,
                $"'{name}' turned {DescribeState(feeding)}, for {_spellCountdown:F1}s.");
        }

        /// <summary>
        /// Counts the water back towards answering again. Does nothing at all while it already
        /// would, so a ready call costs a comparison rather than a countdown.
        /// </summary>
        private void TickCallCooldown()
        {
            if (_isCallReady.Value)
            {
                return;
            }

            _callCooldown -= Time.deltaTime;
            if (_callCooldown > 0f)
            {
                return;
            }

            _isCallReady.Value = true;

            GameLog.Info(LogCategory.Fish, $"'{name}' will answer a call again.");
        }

        /// <summary>
        /// Brings the fish up, if this water will answer.
        ///
        /// Server only, and the decision itself: the Lookout asks, this says yes or no, and nothing
        /// a client sent is consulted on the way. Returns whether it happened so the caller can say
        /// so in the log rather than guessing.
        ///
        /// Refused on water that is already feeding, and refused **without spending anything** — the
        /// cooldown is wound only by a call that worked. That is what stops the call being banked
        /// against good water or wasted by a mistimed press, and it is why the Observer has to read
        /// the water before spending it rather than after.
        ///
        /// The window is set directly rather than through ArmSpellCountdown. A rolled feeding spell
        /// can be shorter than the called window, and arming one here would quietly hand the crew
        /// less than they arranged for. Whatever was left of the previous spell is replaced outright,
        /// so a call landing in the last second of a quiet stretch still buys the whole window.
        /// </summary>
        public bool TryCallOnServer()
        {
            if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer)
            {
                return false;
            }

            if (!_isCallReady.Value)
            {
                return false;
            }

            if (_isFeeding.Value)
            {
                return false;
            }

            _isFeeding.Value = true;
            _spellCountdown = _calledFeedingSeconds;

            _isCallReady.Value = false;
            _callCooldown = _callCooldownSeconds;

            GameLog.Info(LogCategory.Fish,
                $"'{name}' answered a call: feeding for {_spellCountdown:F1}s, cold for {_callCooldown:F1}s.");

            return true;
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

        /// <summary>
        /// Draws the kind of day this is. Ordinary takes whatever the two chances leave, so setting
        /// both to nothing gives every ground an ordinary day and the mechanic simply goes quiet.
        /// </summary>
        private CatchCondition RollCondition()
        {
            float roll = Random.value;

            if (roll < _leanChance)
            {
                return CatchCondition.Lean;
            }

            return roll < _leanChance + _heavyChance ? CatchCondition.Heavy : CatchCondition.Ordinary;
        }

        private static string DescribeCondition(CatchCondition condition)
        {
            switch (condition)
            {
                case CatchCondition.Heavy:
                    return "running big";
                case CatchCondition.Lean:
                    return "running small";
                default:
                    return "running ordinary";
            }
        }

        private static string DescribeStock(WaterStock stock)
        {
            switch (stock)
            {
                case WaterStock.Tired:
                    return "worked hard";
                case WaterStock.Working:
                    return "thinning out";
                default:
                    return "untouched";
            }
        }
    }
}
