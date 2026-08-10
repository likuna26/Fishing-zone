using FishingZone.Core;
using Unity.Netcode;
using UnityEngine;

namespace FishingZone.Networking
{
    public class PlayerSpawner : MonoBehaviour
    {
        [SerializeField]
        private GameFlowManager _gameFlow;

        [SerializeField]
        private NetworkObject _playerPrefab;

        [SerializeField]
        private GameState[] _gameplayStates = { GameState.Port, GameState.Expedition };

        [SerializeField]
        private float _spawnRingRadius = 1.5f;

        private void OnEnable()
        {
            if (_gameFlow != null)
            {
                _gameFlow.StateChanged += HandleStateChanged;
            }
        }

        private void OnDisable()
        {
            if (_gameFlow != null)
            {
                _gameFlow.StateChanged -= HandleStateChanged;
            }
        }

        private void Start()
        {
            if (NetworkManager.Singleton != null)
            {
                NetworkManager.Singleton.OnClientConnectedCallback += HandleClientConnected;
            }
        }

        private void OnDestroy()
        {
            if (NetworkManager.Singleton != null)
            {
                NetworkManager.Singleton.OnClientConnectedCallback -= HandleClientConnected;
            }
        }

        private void HandleStateChanged(GameState state)
        {
            if (!IsServerReady() || !IsGameplayState(state))
            {
                return;
            }

            foreach (ulong clientId in NetworkManager.Singleton.ConnectedClientsIds)
            {
                SpawnPlayerFor(clientId);
            }
        }

        private void HandleClientConnected(ulong clientId)
        {
            if (!IsServerReady() || _gameFlow == null || !IsGameplayState(_gameFlow.CurrentState))
            {
                return;
            }

            SpawnPlayerFor(clientId);
        }

        private void SpawnPlayerFor(ulong clientId)
        {
            if (_playerPrefab == null)
            {
                GameLog.Error(LogCategory.Network, "PlayerSpawner has no player prefab assigned.");
                return;
            }

            if (NetworkManager.Singleton.ConnectedClients.TryGetValue(clientId, out NetworkClient client)
                && client.PlayerObject != null)
            {
                return;
            }

            NetworkObject instance = Instantiate(_playerPrefab, GetSpawnPosition(clientId), Quaternion.identity);
            instance.SpawnAsPlayerObject(clientId, destroyWithScene: true);
            GameLog.Info(LogCategory.Network, $"Spawned player for client {clientId}.");
        }

        private Vector3 GetSpawnPosition(ulong clientId)
        {
            float angle = clientId * 90f * Mathf.Deg2Rad;
            return new Vector3(Mathf.Sin(angle) * _spawnRingRadius, 1f, Mathf.Cos(angle) * _spawnRingRadius);
        }

        private bool IsGameplayState(GameState state)
        {
            if (_gameplayStates == null)
            {
                return false;
            }

            for (int i = 0; i < _gameplayStates.Length; i++)
            {
                if (_gameplayStates[i] == state)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsServerReady()
        {
            return NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer;
        }
    }
}
