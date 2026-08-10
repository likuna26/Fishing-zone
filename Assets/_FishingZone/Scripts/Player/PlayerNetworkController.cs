using FishingZone.Core;
using Unity.Netcode;
using UnityEngine;

namespace FishingZone.Player
{
    public class PlayerNetworkController : NetworkBehaviour
    {
        [SerializeField]
        private Behaviour[] _ownerOnlyBehaviours;

        [SerializeField]
        private GameObject[] _ownerOnlyObjects;

        public override void OnNetworkSpawn()
        {
            bool isOwner = IsOwner;

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
