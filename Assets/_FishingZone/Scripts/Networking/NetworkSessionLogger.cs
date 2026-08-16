using FishingZone.Core;
using Unity.Netcode;
using UnityEngine;

namespace FishingZone.Networking
{
    /// <summary>
    /// Reports session lifecycle events to the console in the project's tagged log format.
    ///
    /// It exists because connection problems are otherwise invisible: a client that fails to reach
    /// the host looks exactly like one that connected and did nothing. Read-only by design — it
    /// starts nothing, stops nothing and holds no session state, so it can stay in place unchanged
    /// once real session management arrives.
    /// </summary>
    [RequireComponent(typeof(NetworkManager))]
    public class NetworkSessionLogger : MonoBehaviour
    {
        private NetworkManager _networkManager;

        private void Awake()
        {
            _networkManager = GetComponent<NetworkManager>();
        }

        private void OnEnable()
        {
            if (_networkManager == null)
            {
                return;
            }

            _networkManager.OnServerStarted += HandleServerStarted;
            _networkManager.OnClientConnectedCallback += HandleClientConnected;
            _networkManager.OnClientDisconnectCallback += HandleClientDisconnected;
        }

        private void OnDisable()
        {
            if (_networkManager == null)
            {
                return;
            }

            _networkManager.OnServerStarted -= HandleServerStarted;
            _networkManager.OnClientConnectedCallback -= HandleClientConnected;
            _networkManager.OnClientDisconnectCallback -= HandleClientDisconnected;
        }

        private void HandleServerStarted()
        {
            string role = _networkManager.IsHost ? "Host" : "Server";
            GameLog.Info(LogCategory.Network, $"{role} started (max players configured in NetworkManager).");
        }

        private void HandleClientConnected(ulong clientId)
        {
            string who = clientId == _networkManager.LocalClientId ? "Local client" : $"Client {clientId}";
            GameLog.Info(LogCategory.Network, $"{who} connected. Connected clients: {_networkManager.ConnectedClientsIds.Count}");
        }

        /// <summary>
        /// Deliberately reports no client count.
        ///
        /// The count used to be printed here and was worse than useless: this runs before the
        /// departing client has been removed from the list, and on a client that is itself
        /// disconnecting the list is a mirror of server state that is no longer being maintained.
        /// The number it produced was therefore always at least one too high, and a disconnect that
        /// cheerfully reported four connected clients sent a whole investigation down the wrong path.
        ///
        /// Which peer is speaking is logged instead, because "the server saw someone leave" and
        /// "we were thrown out" read identically otherwise and mean entirely different things.
        /// </summary>
        private void HandleClientDisconnected(ulong clientId)
        {
            string who = clientId == _networkManager.LocalClientId ? "Local client" : $"Client {clientId}";
            string peer = _networkManager.IsServer ? "server" : "client";
            GameLog.Info(LogCategory.Network, $"{who} disconnected, as seen by the {peer}.");
        }
    }
}
