using FishingZone.Core;
using Unity.Netcode;
using UnityEngine;

namespace FishingZone.Player
{
    /// <summary>
    /// Decides which parts of a player exist only for the person controlling it.
    /// Every other player component stays an ordinary MonoBehaviour that knows nothing about
    /// networking; this is the single place ownership is interpreted (Technical Specification
    /// section 10: camera enabled only for the local owner, input read only by the owner).
    ///
    /// The listed behaviours must be left DISABLED in the prefab. They are switched on here for the
    /// owner and simply left off for everyone else, which matters because some of them release
    /// shared state in OnDisable: a remote copy that was enabled and then disabled would turn off
    /// the local player's action map on its way out.
    /// </summary>
    public class PlayerNetworkController : NetworkBehaviour
    {
        /// <summary>Movement, look, interaction and input context: anything that reads local input.</summary>
        [SerializeField]
        private Behaviour[] _ownerOnlyBehaviours;

        /// <summary>Whole objects rather than components, for the camera rig and its audio listener.</summary>
        [SerializeField]
        private GameObject[] _ownerOnlyObjects;

        /// <summary>
        /// Handled explicitly rather than left to the list above, because forgetting it is not a
        /// cosmetic mistake: a remote copy is driven entirely by replication and never needs to
        /// collide with anything, but its capsule still overlaps whatever its owner is standing in.
        /// A player seated at the wheel sits partly inside the hull, and on the server that hull is
        /// a dynamic body, so an enabled remote controller pushes the boat around and destabilises
        /// it for the whole crew.
        /// </summary>
        [SerializeField]
        private CharacterController _characterController;

        private void Awake()
        {
            if (_characterController == null)
            {
                _characterController = GetComponent<CharacterController>();
            }
        }

        public override void OnNetworkSpawn()
        {
            bool isOwner = IsOwner;

            if (_characterController != null)
            {
                _characterController.enabled = isOwner;
            }

            if (_ownerOnlyBehaviours != null)
            {
                for (int i = 0; i < _ownerOnlyBehaviours.Length; i++)
                {
                    if (_ownerOnlyBehaviours[i] != null)
                    {
                        _ownerOnlyBehaviours[i].enabled = isOwner;
                    }
                }
            }

            if (_ownerOnlyObjects != null)
            {
                for (int i = 0; i < _ownerOnlyObjects.Length; i++)
                {
                    if (_ownerOnlyObjects[i] != null)
                    {
                        _ownerOnlyObjects[i].SetActive(isOwner);
                    }
                }
            }

            GameLog.Info(LogCategory.Network,
                isOwner
                    ? $"Local player spawned for client {OwnerClientId}."
                    : $"Remote player spawned for client {OwnerClientId}.");
        }
    }
}
