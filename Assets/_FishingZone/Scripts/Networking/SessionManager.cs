using System.Collections;
using System.Collections.Generic;
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
        /// Total participants a crew may hold, the host included: four people, not four guests of a
        /// host. Enforced here rather than through connection approval so that nothing about the
        /// NetworkManager's configuration has to change: a mis-set approval flag refuses every
        /// connection, which is a far worse failure than a fifth player being turned away a moment late.
        /// </summary>
        [SerializeField]
        private int _maxCrewSize = 4;

        /// <summary>
        /// Who the server has accepted into this crew, the host included. Server-side only; a client
        /// never fills this in and must never be asked about it.
        ///
        /// This exists because <see cref="NetworkManager.ConnectedClientsIds"/> cannot answer the
        /// question the crew limit is really asking. That list is a live view of the transport, and
        /// during a networked scene load it holds clients in mixed states while they resynchronise,
        /// so its count rises and falls for reasons that have nothing to do with anyone joining.
        /// Admission, by contrast, happens exactly once per player and never changes until they
        /// leave, which is the property the limit needs.
        /// </summary>
        private readonly HashSet<ulong> _admittedClientIds = new HashSet<ulong>();

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
                NetworkManager.Singleton.OnServerStarted += AdmitHost;
                NetworkManager.Singleton.OnClientConnectedCallback += EnforceCrewSize;
                NetworkManager.Singleton.OnClientDisconnectCallback += ForgetAdmission;
                NetworkManager.Singleton.OnServerStopped += ForgetEveryAdmission;
            }
        }

        private void OnDestroy()
        {
            if (NetworkManager.Singleton != null)
            {
                NetworkManager.Singleton.OnServerStarted -= AdmitHost;
                NetworkManager.Singleton.OnClientConnectedCallback -= EnforceCrewSize;
                NetworkManager.Singleton.OnClientDisconnectCallback -= ForgetAdmission;
                NetworkManager.Singleton.OnServerStopped -= ForgetEveryAdmission;
            }
        }

        /// <summary>
        /// The host occupies one of the four seats, so it is admitted the moment the session opens
        /// rather than being treated as free overhead on top of the limit.
        ///
        /// A dedicated server is not a participant and takes no seat, which is why this asks for a
        /// host specifically. Adding an id already present costs nothing, so this may be called as
        /// often as it likes.
        /// </summary>
        private void AdmitHost()
        {
            if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsHost)
            {
                return;
            }

            _admittedClientIds.Add(NetworkManager.Singleton.LocalClientId);
        }

        /// <summary>
        /// Turns away anyone arriving beyond the crew limit, and no one else.
        ///
        /// The rule is admission, not counting: a client the server has already accepted is simply
        /// acknowledged again and left alone. That matters because this callback is not raised once
        /// per player. Netcode also raises it as clients resynchronise, which a networked scene load
        /// does to the whole crew at once, and an earlier version of this method read the live
        /// connected-client count on each of those and evicted players who had already arrived,
        /// spawned and started playing.
        ///
        /// A seat freed by someone leaving is immediately available again, because their admission
        /// is dropped the moment they disconnect.
        /// </summary>
        private void EnforceCrewSize(ulong clientId)
        {
            if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer)
            {
                return;
            }

            // Done here as well as on OnServerStarted, because the host's own connection callback
            // can arrive before that event depending on how the session was opened, and a host that
            // had not yet been admitted would otherwise be counted twice or turned away.
            AdmitHost();

            if (_admittedClientIds.Contains(clientId))
            {
                // Already crew. This is a resynchronisation, not an arrival.
                return;
            }

            if (_admittedClientIds.Count >= _maxCrewSize)
            {
                GameLog.Warn(LogCategory.Network, $"Crew is full at {_maxCrewSize}; turning away client {clientId}.");
                NetworkManager.Singleton.DisconnectClient(clientId);
                return;
            }

            _admittedClientIds.Add(clientId);
            GameLog.Info(LogCategory.Network, $"Admitted client {clientId} to the crew ({_admittedClientIds.Count}/{_maxCrewSize}).");
        }

        /// <summary>
        /// Frees a seat. Only a real disconnect reaches here, which is what stops a scene change or
        /// a resynchronisation from quietly shrinking the crew.
        /// </summary>
        private void ForgetAdmission(ulong clientId)
        {
            if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer)
            {
                return;
            }

            if (_admittedClientIds.Remove(clientId))
            {
                GameLog.Info(LogCategory.Network, $"Freed the seat of client {clientId} ({_admittedClientIds.Count}/{_maxCrewSize}).");
            }
        }

        /// <summary>Ending the session empties the crew, so the next one never starts partly full.</summary>
        private void ForgetEveryAdmission(bool wasHost)
        {
            _admittedClientIds.Clear();
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
