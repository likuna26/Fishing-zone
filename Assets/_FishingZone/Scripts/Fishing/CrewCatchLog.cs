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
        /// Writes down a fish somebody landed, and answers how many they have now.
        ///
        /// The count comes back rather than being available to ask for, because the one thing that
        /// needs it is the line printed the moment a catch is stored. Totals, tallies and anything
        /// else a crew might eventually want counted belong to whatever system eventually wants
        /// them, not to the thing that keeps the list.
        ///
        /// Server only. There is no path here that does not begin on the machine that decided the
        /// catch, and this refuses anyway rather than trusting that to stay true.
        /// </summary>
        public int RecordCatchOnServer(ulong clientId, int fishId, int weightTenths)
        {
            if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer)
            {
                return 0;
            }

            if (!_catchesByClient.TryGetValue(clientId, out List<StoredCatch> catches))
            {
                catches = new List<StoredCatch>();
                _catchesByClient[clientId] = catches;
            }

            catches.Add(new StoredCatch(fishId, weightTenths));

            return catches.Count;
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
