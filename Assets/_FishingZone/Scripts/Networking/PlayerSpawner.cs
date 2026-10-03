using System.Collections.Generic;
using FishingZone.Boat;
using FishingZone.Core;
using FishingZone.Player;
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

            int boardingSlot = GetSpawnPose(clientId, out Vector3 position, out Quaternion rotation);
            NetworkObject instance = Instantiate(_playerPrefab, position, rotation);
            instance.SpawnAsPlayerObject(clientId, destroyWithScene: true);

            // Written straight after spawning rather than before: Netcode warns about a variable
            // written before its object is spawned. It goes out in the same tick as the spawn, and
            // the owner waits for it before standing on its own view of the deck.
            if (boardingSlot != BoatBoardingPoints.NoSlot)
            {
                PlayerBoardingPlacement placement = instance.GetComponent<PlayerBoardingPlacement>();
                if (placement != null)
                {
                    placement.AssignSlotOnServer(boardingSlot);
                }
                else
                {
                    GameLog.Error(LogCategory.Network,
                        "The player prefab has no PlayerBoardingPlacement, so a player joining a moving boat may miss the deck.");
                }
            }
            GameLog.Info(LogCategory.Network, boardingSlot == BoatBoardingPoints.NoSlot
                ? $"Spawned player for client {clientId}."
                : $"Spawned player for client {clientId} aboard, at boarding slot {boardingSlot}.");
        }

        /// <summary>
        /// Where a player arrives, and which boarding slot that was, or NoSlot for a fixed point.
        ///
        /// Aboard whenever the scene has a boat. The boat may have sailed since the scene loaded, so
        /// only points that move with it can be trusted to still be on deck; the scene's fixed spawn
        /// points are for places that stay put, which today means the port.
        /// </summary>
        private int GetSpawnPose(ulong clientId, out Vector3 position, out Quaternion rotation)
        {
            BoatBoardingPoints boat = BoatBoardingPoints.Current;
            if (boat != null && boat.Count > 0)
            {
                int slot = ChooseBoardingSlot(boat.Count);
                if (boat.TryGetPose(slot, out position, out rotation))
                {
                    return slot;
                }

                GameLog.Error(LogCategory.Network,
                    $"'{boat.name}' has an empty boarding point at slot {slot}; falling back to the scene's spawn points.");
            }

            GetFixedSpawnPose(clientId, out position, out rotation);
            return BoatBoardingPoints.NoSlot;
        }

        /// <summary>
        /// The boarding point fewest crewmates were given, earliest first on a tie.
        ///
        /// Counted from the players actually spawned rather than from client ids, which are not
        /// reused: a crew whose members have rejoined would otherwise stack two people on one spot
        /// while another stood empty.
        /// </summary>
        private static int ChooseBoardingSlot(int slotCount)
        {
            int[] used = new int[slotCount];

            foreach (NetworkClient client in NetworkManager.Singleton.ConnectedClientsList)
            {
                if (client.PlayerObject == null)
                {
                    continue;
                }

                PlayerBoardingPlacement placement = client.PlayerObject.GetComponent<PlayerBoardingPlacement>();
                if (placement != null && placement.Slot >= 0 && placement.Slot < slotCount)
                {
                    used[placement.Slot]++;
                }
            }

            int best = 0;
            for (int i = 1; i < slotCount; i++)
            {
                if (used[i] < used[best])
                {
                    best = i;
                }
            }

            return best;
        }

        private void GetFixedSpawnPose(ulong clientId, out Vector3 position, out Quaternion rotation)
        {
            IReadOnlyList<PlayerSpawnPoint> points = PlayerSpawnPoint.All;
            if (points.Count > 0)
            {
                PlayerSpawnPoint point = points[(int)(clientId % (ulong)points.Count)];
                position = point.transform.position;

                Vector3 heading = Vector3.ProjectOnPlane(point.transform.forward, Vector3.up);
                rotation = heading.sqrMagnitude > 0.0001f
                    ? Quaternion.LookRotation(heading, Vector3.up)
                    : Quaternion.identity;
                return;
            }

            GameLog.Warn(LogCategory.Network, "No PlayerSpawnPoint in this scene; falling back to world origin.");
            float angle = clientId * 90f * Mathf.Deg2Rad;
            position = new Vector3(Mathf.Sin(angle) * _spawnRingRadius, 1f, Mathf.Cos(angle) * _spawnRingRadius);
            rotation = Quaternion.identity;
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
