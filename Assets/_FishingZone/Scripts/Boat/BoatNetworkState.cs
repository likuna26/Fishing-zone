using FishingZone.Core;
using Unity.Netcode;
using UnityEngine;

namespace FishingZone.Boat
{
    /// <summary>
    /// Decides which machine simulates the hull.
    ///
    /// Buoyancy and movement both drive the same Rigidbody, so the way to make the boat
    /// host-authoritative is not to rewrite either of them but to run them in exactly one place.
    /// The server keeps them; every client switches them off and lets the replicated transform say
    /// where the boat is. Nothing on a client is left able to author the hull's position.
    ///
    /// The Rigidbody is made kinematic on clients as well, because a physics body with gravity and
    /// no buoyancy would sink while the network transform kept pulling it back, and the two would
    /// fight every frame.
    /// </summary>
    [RequireComponent(typeof(NetworkObject))]
    public class BoatNetworkState : NetworkBehaviour
    {
        [SerializeField]
        private BoatBuoyancy _buoyancy;

        [SerializeField]
        private BoatMovement _movement;

        [SerializeField]
        private Rigidbody _rigidbody;

        private void Awake()
        {
            // Resolved automatically so the prefab cannot be half-wired.
            if (_buoyancy == null)
            {
                _buoyancy = GetComponent<BoatBuoyancy>();
            }

            if (_movement == null)
            {
                _movement = GetComponent<BoatMovement>();
            }

            if (_rigidbody == null)
            {
                _rigidbody = GetComponent<Rigidbody>();
            }
        }

        public override void OnNetworkSpawn()
        {
            bool isSimulating = IsServer;

            if (_buoyancy != null)
            {
                _buoyancy.enabled = isSimulating;
            }

            if (_movement != null)
            {
                _movement.enabled = isSimulating;
            }

            if (_rigidbody != null)
            {
                _rigidbody.isKinematic = !isSimulating;
            }

            GameLog.Info(LogCategory.Network,
                isSimulating
                    ? "Boat is simulating locally: this instance is the server."
                    : "Boat is following the server: local simulation disabled.");
        }
    }
}
