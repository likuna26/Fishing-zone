using FishingZone.Core;
using Unity.Netcode;
using UnityEngine;

namespace FishingZone.World
{
    /// <summary>
    /// Starts the world's clock when a session starts.
    ///
    /// Lives on the persistent services object, which outlives every scene but is not somewhere
    /// Netcode can be relied on to spawn an in-scene object (the roster's reason for living in the
    /// lobby). So the clock is a prefab the server spawns once per session and keeps out of every
    /// scene load; Netcode carries an object spawned that way across scene changes and hands it to
    /// players who join later.
    ///
    /// Nothing to do on session end: Netcode destroys what the server spawned when it shuts down, so
    /// the next session opens on a fresh day.
    /// </summary>
    public class WorldClockSpawner : MonoBehaviour
    {
        [SerializeField]
        private NetworkObject _clockPrefab;

        /// <summary>
        /// Subscribed in Start because NetworkManager assigns its singleton in Awake, and every Awake
        /// runs before any Start.
        /// </summary>
        private void Start()
        {
            if (NetworkManager.Singleton != null)
            {
                NetworkManager.Singleton.OnServerStarted += SpawnClock;
            }
        }

        private void OnDestroy()
        {
            if (NetworkManager.Singleton != null)
            {
                NetworkManager.Singleton.OnServerStarted -= SpawnClock;
            }
        }

        private void SpawnClock()
        {
            if (!NetworkManager.Singleton.IsServer || WorldClock.Current != null)
            {
                return;
            }

            if (_clockPrefab == null)
            {
                GameLog.Error(LogCategory.Flow,
                    "WorldClockSpawner has no clock prefab assigned, so this session has no time of day.");
                return;
            }

            NetworkObject clock = Instantiate(_clockPrefab);

            // Kept out of the scene it was made in before it is spawned, so no scene unload can take it.
            DontDestroyOnLoad(clock.gameObject);
            clock.Spawn(destroyWithScene: false);
        }
    }
}
