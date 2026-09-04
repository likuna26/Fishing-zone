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
        }

        private void OnDestroy()
        {
            if (NetworkManager.Singleton != null)
            {
                NetworkManager.Singleton.OnClientDisconnectCallback -= HandleClientDisconnected;
                NetworkManager.Singleton.OnServerStopped -= HandleServerStopped;
            }

            ServiceRegistry.Unregister<CrewCatchLog>();
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
        /// A crewmate who leaves takes their catch with them. Only theirs: everybody still aboard
        /// keeps what they landed.
        /// </summary>
        private void HandleClientDisconnected(ulong clientId)
        {
            if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer)
            {
                return;
            }

            if (_catchesByClient.Remove(clientId))
            {
                GameLog.Info(LogCategory.Fish, $"Forgot the catches of client {clientId} on disconnect.");
            }
        }

        /// <summary>Ending the session empties the hold, so the next crew never inherits the last one's fish.</summary>
        private void HandleServerStopped(bool wasHost)
        {
            _catchesByClient.Clear();
        }
    }
}
