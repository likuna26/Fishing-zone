using System.Collections.Generic;
using FishingZone.Core;
using Unity.Netcode;
using UnityEngine;

namespace FishingZone.Fishing
{
    /// <summary>
    /// What each of the crew has landed since they connected.
    ///
    /// Kept here rather than on the players themselves for the reason the crew registry is: a player
    /// object is spawned with each gameplay scene and destroyed with it, so a crew sailing home from
    /// an expedition would arrive in port with empty hands. The persistent services object is the
    /// only thing that outlives a scene change, and a session is the lifetime this is meant to have.
    ///
    /// Server-side and not replicated. Nothing on any client reads a catch yet, and a value nobody
    /// reads is better left unsent than shipped in a shape chosen before its reader exists. It also
    /// settles authority outright: with no variable and no message, a client has nothing to write to
    /// and nothing to write with, so a forged catch is not refused so much as unsayable.
    ///
    /// Entries are removed when their owner disconnects and cleared when the session ends. Nothing
    /// is written to disk: this remembers a trip, not a career.
    /// </summary>
    public class CrewCatchLog : MonoBehaviour
    {
        /// <summary>
        /// One landed fish, as the two numbers the server settled on and nothing else. Not the
        /// definition itself, which is an asset and cannot be a record of anything that happened.
        /// </summary>
        private readonly struct StoredCatch
        {
            public StoredCatch(int fishId, int weightTenths)
            {
                FishId = fishId;
                WeightTenths = weightTenths;
            }

            public int FishId { get; }

            public int WeightTenths { get; }
        }

        private readonly Dictionary<ulong, List<StoredCatch>> _catchesByClient =
            new Dictionary<ulong, List<StoredCatch>>();

        /// <summary>
        /// The same crew, counted over one trip instead of the whole session.
        ///
        /// A count rather than a second set of lists: what a voyage produced is a number, and
        /// keeping the fish twice would be two records of one event with two chances to disagree.
        ///
        /// Kept per client rather than as a single running total for one reason, and it is the
        /// disconnect rule. A crewmate who leaves takes their fish out of the session tally, so
        /// unless they come out of this one too, a trip could end up claiming more than the session
        /// it belongs to. One rule, applied to both, rather than a second rule invented for this.
        /// </summary>
        private readonly Dictionary<ulong, int> _voyageCatchesByClient = new Dictionary<ulong, int>();

        /// <summary>
        /// Watched so the log knows when a trip begins. Held rather than looked up each time, so
        /// there is something to unsubscribe from however this object goes away.
        /// </summary>
        private GameFlowManager _gameFlow;

        private void Awake()
        {
            // Registers itself rather than being published by Bootstrap, so that adding it needs no
            // change to the startup sequence: dropping the component on the services object is enough.
            ServiceRegistry.Register(this);
        }

        // Subscribed in Start because NetworkManager assigns its singleton in Awake, and every Awake
        // runs before any Start. Subscribing before a session exists is fine; nothing fires until one does.
        private void Start()
        {
            if (NetworkManager.Singleton != null)
            {
                NetworkManager.Singleton.OnClientDisconnectCallback += HandleClientDisconnected;
                NetworkManager.Singleton.OnServerStopped += HandleServerStopped;
            }

            // Resolved in Start for the same reason: both register themselves in Awake, and every
            // Awake runs before any Start, so by now the flow is there to be found.
            _gameFlow = ServiceRegistry.Get<GameFlowManager>();
            if (_gameFlow != null)
            {
                _gameFlow.StateChanged += HandleStateChanged;
            }
        }

        private void OnDestroy()
        {
            if (NetworkManager.Singleton != null)
            {
                NetworkManager.Singleton.OnClientDisconnectCallback -= HandleClientDisconnected;
                NetworkManager.Singleton.OnServerStopped -= HandleServerStopped;
            }

            if (_gameFlow != null)
            {
                _gameFlow.StateChanged -= HandleStateChanged;
                _gameFlow = null;
            }

            ServiceRegistry.Unregister<CrewCatchLog>();
        }

        /// <summary>
        /// A trip begins when the crew reaches the grounds, and that is the only thing that starts
        /// one.
        ///
        /// Emphatically not when they arrive home. The board in port is what reads this, and it
        /// reads it after the return has already happened — so clearing on arrival in port would
        /// wipe the very trip it is about to report, and every voyage would read as nothing. The
        /// count therefore stands from the moment the crew comes back until the moment they set off
        /// again, which is exactly the window anybody is in port to read it.
        ///
        /// Server-guarded because this fires on every peer: each machine's own flow raises it when
        /// that machine follows the host into a scene. The count lives only on the server, so only
        /// the server has anything to clear.
        /// </summary>
        private void HandleStateChanged(GameState state)
        {
            if (state != GameState.Expedition)
            {
                return;
            }

            if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer)
            {
                return;
            }

            _voyageCatchesByClient.Clear();

            GameLog.Info(LogCategory.Fish, "A new trip has begun; the crew's trip tally starts at nothing.");
        }

        /// <summary>
        /// Writes down a fish somebody landed.
        ///
        /// Answers nothing. A method that adds something is a poor way to ask how much there is —
        /// the only way to learn the count would be to land another fish — so asking is a separate
        /// question with a separate answer.
        ///
        /// Server only. There is no path here that does not begin on the machine that decided the
        /// catch, and this refuses anyway rather than trusting that to stay true.
        /// </summary>
        public void RecordCatchOnServer(ulong clientId, int fishId, int weightTenths)
        {
            if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer)
            {
                return;
            }

            if (!_catchesByClient.TryGetValue(clientId, out List<StoredCatch> catches))
            {
                catches = new List<StoredCatch>();
                _catchesByClient[clientId] = catches;
            }

            catches.Add(new StoredCatch(fishId, weightTenths));

            // The same fish, counted again against the trip it was landed on. One call site, so a
            // catch cannot reach one tally without reaching the other.
            _voyageCatchesByClient.TryGetValue(clientId, out int voyageCatches);
            _voyageCatchesByClient[clientId] = voyageCatches + 1;
        }

        /// <summary>
        /// How many this client has landed since they connected. None, for somebody who has landed
        /// none, and none on a machine that is not the server: the list exists nowhere else.
        ///
        /// Asking leaves no trace. A client nobody has recorded a catch for stays absent from the
        /// dictionary rather than gaining an empty list, so counting cannot quietly populate the
        /// thing being counted.
        ///
        /// The count and nothing beyond it. Totals by weight, tallies by species and anything else
        /// a crew might eventually want counted belong to whatever system eventually wants them,
        /// designed against what it actually needs rather than guessed at here.
        /// </summary>
        public int GetCatchCount(ulong clientId)
        {
            if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer)
            {
                return 0;
            }

            return _catchesByClient.TryGetValue(clientId, out List<StoredCatch> catches) ? catches.Count : 0;
        }

        /// <summary>
        /// How many the whole crew has landed between them since the session began, and none on a
        /// machine that is not the server.
        ///
        /// A separate question from the one above rather than a sum the caller could have worked out,
        /// because the caller cannot: the dictionary is private, the client ids are not published,
        /// and handing out the keys merely so somebody could add up the values would expose far more
        /// than the total. This is the total and only the total.
        ///
        /// Counted on each call rather than kept as a running tally. A tally would be a second copy
        /// of a number the lists already hold, and the disconnect path would have to remember to
        /// correct it; the lists are few and short, and this is asked when a scene loads.
        ///
        /// Leaves no trace, like the per-client count: reading the values of a dictionary creates
        /// nothing in it.
        /// </summary>
        public int GetCrewCatchCount()
        {
            if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer)
            {
                return 0;
            }

            int total = 0;

            // Dictionary hands back a struct enumerator over its values, so this walks them without
            // allocating and without building any intermediate collection.
            foreach (List<StoredCatch> catches in _catchesByClient.Values)
            {
                total += catches.Count;
            }

            return total;
        }

        /// <summary>
        /// How many the whole crew has landed on this trip, and none on a machine that is not the
        /// server.
        ///
        /// The same question as the one above asked over a shorter window, which is the window the
        /// crew actually plays in: they sail out, they work, they come home, and this is what it
        /// came to. The session total says how the evening is going; this says how the trip went.
        ///
        /// Counted on each call rather than kept as a running total, for the reason the crew count
        /// is: a total would be a second copy of a number these entries already hold, and the
        /// disconnect path would have to remember to correct it.
        /// </summary>
        public int GetVoyageCatchCount()
        {
            if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer)
            {
                return 0;
            }

            int total = 0;

            foreach (int catches in _voyageCatchesByClient.Values)
            {
                total += catches;
            }

            return total;
        }

        /// <summary>
        /// A crewmate who leaves takes their catch with them. Only theirs: everybody still aboard
        /// keeps what they landed.
        ///
        /// Out of the trip as well as out of the session, on the same line and by the same rule.
        /// Forgetting them in one and not the other is how a trip ends up claiming more fish than
        /// the session that contains it.
        /// </summary>
        private void HandleClientDisconnected(ulong clientId)
        {
            if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer)
            {
                return;
            }

            _voyageCatchesByClient.Remove(clientId);

            if (_catchesByClient.Remove(clientId))
            {
                GameLog.Info(LogCategory.Fish, $"Forgot the catches of client {clientId} on disconnect.");
            }
        }

        /// <summary>Ending the session empties the hold, so the next crew never inherits the last one's fish.</summary>
        private void HandleServerStopped(bool wasHost)
        {
            _catchesByClient.Clear();
            _voyageCatchesByClient.Clear();
        }
    }
}
