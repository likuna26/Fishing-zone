using System.Collections;
using FishingZone.Core;
using Unity.Netcode;
using UnityEngine;

namespace FishingZone.Networking
{
    /// <summary>
    /// Owns starting and stopping the network session.
    ///
    /// Everything runs inside a session, including solo play, which is a host of one. Keeping a
    /// single path means gameplay never has to ask whether it is networked, and it removes a whole
    /// second configuration that would otherwise need testing and would quietly rot.
    ///
    /// Buttons and menus call these methods rather than touching NetworkManager, so there is one
    /// place to change when the session gains a relay and a join code.
    /// </summary>
    public class SessionManager : MonoBehaviour
    {
        /// <summary>
        /// Crew size the design locks at one to four. Enforced here rather than through connection
        /// approval so that nothing about the NetworkManager's configuration has to change: a
        /// mis-set approval flag refuses every connection, which is a far worse failure than a
        /// fifth player being turned away a moment late.
        /// </summary>
        [SerializeField]
        private int _maxCrewSize = 4;

        public bool IsSessionActive => NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening;

        /// <summary>
        /// Subscribed in Start because NetworkManager assigns its singleton in Awake, and every
        /// Awake runs before any Start. This lives on the persistent services object, so it is the
        /// only place that can police the crew in every scene; the roster exists only in the lobby.
        /// </summary>
        private void Start()
        {
            if (NetworkManager.Singleton != null)
            {
                NetworkManager.Singleton.OnClientConnectedCallback += EnforceCrewSize;
            }
        }

        private void OnDestroy()
        {
            if (NetworkManager.Singleton != null)
            {
                NetworkManager.Singleton.OnClientConnectedCallback -= EnforceCrewSize;
            }
        }

        /// <summary>
        /// Turns away anyone beyond the crew limit. Only the arrival is considered, so the players
        /// already aboard are never disturbed, and a seat freed by someone leaving is immediately
        /// available again because the count simply drops.
        /// </summary>
        private void EnforceCrewSize(ulong clientId)
        {
            if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer)
            {
                return;
            }

            // The host is a client of its own session and must never be the one shown the door.
            if (clientId == NetworkManager.Singleton.LocalClientId)
            {
                return;
            }

            if (NetworkManager.Singleton.ConnectedClientsIds.Count <= _maxCrewSize)
            {
                return;
            }

            GameLog.Warn(LogCategory.Network, $"Crew is full at {_maxCrewSize}; turning away client {clientId}.");
            NetworkManager.Singleton.DisconnectClient(clientId);
        }

        /// <summary>Set while trading a host session for a client one, so the swap cannot overlap itself.</summary>
        private bool _isSwitchingSession;

        /// <summary>Starts hosting. Solo play uses this too; the crew simply has one member.</summary>
        public bool StartHost()
        {
            if (!TryGetNetworkManager(out NetworkManager networkManager))
            {
                return false;
            }

            if (IsSessionActive)
            {
                GameLog.Warn(LogCategory.Network, "Ignored StartHost: a session is already running.");
                return false;
            }

            if (!networkManager.StartHost())
            {
                GameLog.Error(LogCategory.Network, "Failed to start host. Check the transport settings on NetworkManager.");
                return false;
            }

            return true;
        }

        /// <summary>
        /// Joins a host using the address already configured on the transport. Connecting by crew
        /// code arrives later; this is the same lifecycle either way.
        /// </summary>
        public bool StartClient()
        {
            if (!TryGetNetworkManager(out NetworkManager networkManager))
            {
                return false;
            }

            if (IsSessionActive)
            {
                GameLog.Warn(LogCategory.Network, "Ignored StartClient: a session is already running.");
                return false;
            }

            if (!networkManager.StartClient())
            {
                GameLog.Error(LogCategory.Network, "Failed to start client. Check the transport settings on NetworkManager.");
                return false;
            }

            return true;
        }

        /// <summary>
        /// Leaves whatever session this instance is running and connects to someone else's.
        ///
        /// Needed because every instance opens a host of one at boot, so joining is not "start a
        /// client" but "give up being a host first". Netcode's shutdown does not complete within the
        /// call, so a new session cannot simply be opened on the next line.
        ///
        /// Deliberately does not change scene: the host owns shared transitions, and this client
        /// will be led wherever the crew already is.
        /// </summary>
        public void JoinAsClient()
        {
            if (!TryGetNetworkManager(out NetworkManager networkManager))
            {
                return;
            }

            if (_isSwitchingSession)
            {
                GameLog.Warn(LogCategory.Network, "Ignored Join: already switching session.");
                return;
            }

            if (networkManager.IsClient && !networkManager.IsHost)
            {
                GameLog.Warn(LogCategory.Network, "Ignored Join: already connected to a crew.");
                return;
            }

            StartCoroutine(JoinAsClientRoutine());
        }

        private IEnumerator JoinAsClientRoutine()
        {
            _isSwitchingSession = true;

            NetworkManager networkManager = NetworkManager.Singleton;

            if (networkManager.IsListening)
            {
                GameLog.Info(LogCategory.Network, "Leaving the local session to join a crew.");
                networkManager.Shutdown();
            }

            // Shutdown finishes over the following frames. Opening a client before it completes
            // leaves the transport half torn down and the connection silently fails.
            while (networkManager != null && (networkManager.ShutdownInProgress || networkManager.IsListening))
            {
                yield return null;
            }

            _isSwitchingSession = false;

            if (networkManager == null)
            {
                yield break;
            }

            StartClient();
        }

        public void Shutdown()
        {
            if (!IsSessionActive)
            {
                return;
            }

            NetworkManager.Singleton.Shutdown();
            GameLog.Info(LogCategory.Network, "Session shut down.");
        }

        private static bool TryGetNetworkManager(out NetworkManager networkManager)
        {
            networkManager = NetworkManager.Singleton;
            if (networkManager != null)
            {
                return true;
            }

            GameLog.Error(LogCategory.Network, "No NetworkManager in the scene. Add one to the persistent services object.");
            return false;
        }
    }
}
