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

        private void HandleClientDisconnected(ulong clientId)
        {
            string who = clientId == _networkManager.LocalClientId ? "Local client" : $"Client {clientId}";
            GameLog.Info(LogCategory.Network, $"{who} disconnected. Connected clients: {_networkManager.ConnectedClientsIds.Count}");
        }
    }
}
