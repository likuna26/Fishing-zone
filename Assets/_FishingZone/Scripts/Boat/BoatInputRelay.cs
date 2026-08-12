using FishingZone.Core;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

namespace FishingZone.Boat
{
    /// <summary>
    /// Carries the driver's intent to the machine that simulates the hull.
    ///
    /// Only whoever currently holds the wheel reads the boat actions, and what they read is a
    /// request rather than a result: the server remains the only thing that turns intent into force,
    /// so no client ever gains authority over where the boat is.
    ///
    /// A host driving skips the round trip entirely and hands its intent straight over, which is why
    /// hosting feels identical to before any of this existed.
    /// </summary>
    public class BoatInputRelay : NetworkBehaviour
    {
        [SerializeField]
        private InputActionReference _throttleAction;

        [SerializeField]
        private InputActionReference _steerAction;

        [SerializeField]
        private InputActionReference _brakeAction;

        [SerializeField]
        private BoatMovement _boatMovement;

        [SerializeField]
        private NavigatorStation _station;

        private bool _isConfigured;

        private void Awake()
        {
            if (_boatMovement == null)
            {
                _boatMovement = GetComponent<BoatMovement>();
            }

            if (_station == null)
            {
                _station = GetComponentInChildren<NavigatorStation>();
            }

            _isConfigured = _throttleAction != null && _steerAction != null && _brakeAction != null
                            && _boatMovement != null && _station != null;

            if (!_isConfigured)
            {
                GameLog.Error(LogCategory.Input, "BoatInputRelay is missing an action reference, the BoatMovement or the NavigatorStation. Assign them in the Inspector.");
            }
        }

        private void FixedUpdate()
        {
            if (!_isConfigured || !IsSpawned || NetworkManager.Singleton == null)
            {
                return;
            }

            // Everyone else, including the server when a client is driving, contributes nothing.
            if (!_station.IsOccupied || _station.OccupantClientId != NetworkManager.Singleton.LocalClientId)
            {
                return;
            }

            float throttle = _throttleAction.action.ReadValue<float>();
            float steer = _steerAction.action.ReadValue<float>();
            bool isBraking = _brakeAction.action.IsPressed();

            if (IsServer)
            {
                _boatMovement.SetInput(throttle, steer, isBraking);
                return;
            }

            SubmitInputServerRpc(throttle, steer, isBraking);
        }

        [ServerRpc(RequireOwnership = false)]
        private void SubmitInputServerRpc(float throttle, float steer, bool isBraking, ServerRpcParams parameters = default)
        {
            // Re-checked on arrival, because the sender may have been released between sending and
            // this running, and because a client's claim about who is driving is not evidence.
            if (_station.OccupantClientId != parameters.Receive.SenderClientId)
            {
                return;
            }

            _boatMovement.SetInput(throttle, steer, isBraking);
        }
    }
}
