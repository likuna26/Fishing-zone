using System.Collections;
using FishingZone.Core;
using Unity.Netcode;
using UnityEngine;

namespace FishingZone.Networking
{
    /// <summary>
    /// Owns starting and stopping the network session.
    /// </summary>
    public class SessionManager : MonoBehaviour
    {
        public bool IsSessionActive => NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening;

        private bool _isSwitchingSession;

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
