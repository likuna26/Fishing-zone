using FishingZone.Boat;
using FishingZone.Core;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;

namespace FishingZone.Player
{
    /// <summary>
    /// Puts a player who was spawned aboard onto the deck as their own machine sees it.
    ///
    /// The server spawns a player where a boarding point is on the server's boat. A joining client
    /// does not see that boat: it sees one drawn a little in the past, because the hull's position
    /// arrives over the network and is interpolated. Under way, that difference is metres, which is
    /// the difference between the deck and the sea. So the server sends which point, and the owner
    /// stands on that point of the boat it is actually looking at.
    ///
    /// The owner is the right machine to decide this, not merely a convenient one: player transforms
    /// are owner-authoritative, so where the owner puts itself is where everyone else will see it.
    ///
    /// Runs once, shortly after spawning, and never again. Being on the deck afterwards is the
    /// platform rider's job; this only gets the player there.
    ///
    /// Runs on every peer so the slot is known everywhere, but only the owner acts on it. It must not
    /// be listed among PlayerNetworkController's owner-only behaviours, or the server could not read
    /// which slots are taken.
    /// </summary>
    public class PlayerBoardingPlacement : NetworkBehaviour
    {
        /// <summary>
        /// How long the owner keeps looking for the boat before giving up. In-scene objects are
        /// synchronized before the player is, so this is a safety margin rather than a wait anybody
        /// should ever see.
        /// </summary>
        private const float PlacementTimeoutSeconds = 5f;

        private readonly NetworkVariable<int> _slot = new NetworkVariable<int>(
            BoatBoardingPoints.NoSlot,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        /// <summary>Which boarding point this player was given, or NoSlot if they were not placed aboard.</summary>
        public int Slot => _slot.Value;

        private CharacterController _characterController;
        private PlayerPlatformRider _platformRider;
        private NetworkTransform _networkTransform;

        private bool _isPlaced;
        private float _placementDeadline;

        private void Awake()
        {
            _characterController = GetComponent<CharacterController>();
            _platformRider = GetComponent<PlayerPlatformRider>();
            _networkTransform = GetComponent<NetworkTransform>();
        }

        /// <summary>
        /// Server only, and only once the player is spawned: Netcode warns about a variable written
        /// before then. The value follows the spawn within the same tick, and the owner waits for it.
        /// </summary>
        public void AssignSlotOnServer(int slot)
        {
            _slot.Value = slot;
        }

        public override void OnNetworkSpawn()
        {
            // Nothing to do for anybody but the owner, and nothing to do at all for a player who was
            // not spawned aboard — the port's fixed spawn points are already where they belong.
            _isPlaced = !IsOwner;
            _placementDeadline = Time.unscaledTime + PlacementTimeoutSeconds;
        }

        /// <summary>
        /// In Update rather than OnNetworkSpawn on purpose: the network transform has to be spawned
        /// before the owner may teleport it, and sibling behaviours spawn in no guaranteed order.
        /// </summary>
        private void Update()
        {
            if (_isPlaced || !IsSpawned)
            {
                return;
            }

            if (_slot.Value == BoatBoardingPoints.NoSlot)
            {
                // A slot written after spawning is still on its way; one never written means this
                // player was spawned somewhere fixed. Either way, stop asking once the window is up.
                if (Time.unscaledTime > _placementDeadline)
                {
                    _isPlaced = true;
                }

                return;
            }

            BoatBoardingPoints boat = BoatBoardingPoints.Current;
            if (boat == null || (_networkTransform != null && !_networkTransform.IsSpawned))
            {
                if (Time.unscaledTime > _placementDeadline)
                {
                    _isPlaced = true;
                    GameLog.Error(LogCategory.Network,
                        $"Player was given boarding slot {_slot.Value} but no boat with boarding points appeared " +
                        $"within {PlacementTimeoutSeconds:F0}s; left where the server spawned them.");
                }

                return;
            }

            _isPlaced = true;

            if (!boat.TryGetPose(_slot.Value, out Vector3 position, out Quaternion rotation))
            {
                GameLog.Error(LogCategory.Network,
                    $"Player was given boarding slot {_slot.Value}, which '{boat.name}' does not have.");
                return;
            }

            PlaceAt(position, rotation);

            GameLog.Info(LogCategory.Network, $"Client {OwnerClientId} boarded at slot {_slot.Value}.");
        }

        /// <summary>
        /// The controller is switched off around the move for the reason the stations switch it off:
        /// an enabled CharacterController keeps its own idea of where it is and overwrites a
        /// transform set underneath it.
        ///
        /// Teleported rather than moved, so crewmates see the player appear on deck instead of
        /// gliding to it from where the server first put them.
        /// </summary>
        private void PlaceAt(Vector3 position, Quaternion rotation)
        {
            bool controllerWasEnabled = _characterController != null && _characterController.enabled;
            if (controllerWasEnabled)
            {
                _characterController.enabled = false;
            }

            transform.SetPositionAndRotation(position, rotation);

            if (_networkTransform != null && _networkTransform.CanCommitToTransform)
            {
                _networkTransform.Teleport(position, rotation, transform.localScale);
            }

            if (_platformRider != null)
            {
                _platformRider.ResetTracking();
            }

            if (controllerWasEnabled)
            {
                _characterController.enabled = true;
            }
        }
    }
}
