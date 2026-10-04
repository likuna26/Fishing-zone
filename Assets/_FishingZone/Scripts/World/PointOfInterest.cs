using System;
using FishingZone.Core;
using FishingZone.Fishing;
using Unity.Netcode;
using UnityEngine;

namespace FishingZone.World
{
    /// <summary>
    /// Something at sea that comes and goes: here for a while, then leaving, then gone.
    ///
    /// Spawned by the server's director and owned by the server outright. It carries its own water —
    /// a <see cref="FishingGround"/> on the same object — so everything that already knows how to
    /// fish a ground fishes this one with no idea it is temporary.
    ///
    /// Leaving is one replicated fact, set once. When it turns true, every peer closes the water to
    /// new lines, so the Fisher's prompt and the server's refusal agree, and whatever shows the point
    /// is told to go. The object itself stays until the last line already in its water is out:
    /// taking the water from under a hooked fish would change what it is, which is not this class's
    /// to decide.
    ///
    /// Nothing about the fish is said here. Whether the water is feeding is its own
    /// <see cref="WaterActivity"/>'s business, read at the lookout as for any other ground.
    /// </summary>
    [RequireComponent(typeof(FishingGround))]
    public class PointOfInterest : NetworkBehaviour
    {
        private readonly NetworkVariable<bool> _isLeaving = new NetworkVariable<bool>(
            false,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        /// <summary>Raised on every peer, once, when this point starts to leave.</summary>
        public event Action Leaving;

        public bool IsLeaving => _isLeaving.Value;

        public FishingGround Ground => _ground;

        private FishingGround _ground;

        /// <summary>Server only. Set before spawning; counted down while the point is here.</summary>
        private float _lifetimeRemaining = float.PositiveInfinity;

        private bool _hasAppliedLeaving;

        private bool _hasReportedWaitingForLine;

        private void Awake()
        {
            _ground = GetComponent<FishingGround>();
        }

        /// <summary>How long this point stays before it leaves. Server only, before it is spawned.</summary>
        public void SetLifetimeOnServer(float seconds)
        {
            _lifetimeRemaining = Mathf.Max(0f, seconds);
        }

        /// <summary>
        /// Starts this point leaving now, whatever its lifetime says. Server only; leaving twice is
        /// leaving once.
        /// </summary>
        public void BeginLeavingOnServer(string reason)
        {
            if (!IsServer || !IsSpawned || _isLeaving.Value)
            {
                return;
            }

            _isLeaving.Value = true;

            GameLog.Info(LogCategory.Flow, $"'{name}' is leaving: {reason}.");
        }

        public override void OnNetworkSpawn()
        {
            _isLeaving.OnValueChanged += HandleLeavingChanged;

            // A peer arriving after the point began to leave adopts that rather than waiting for a
            // change that already happened.
            if (_isLeaving.Value)
            {
                ApplyLeaving();
            }
        }

        public override void OnNetworkDespawn()
        {
            _isLeaving.OnValueChanged -= HandleLeavingChanged;
        }

        private void HandleLeavingChanged(bool previous, bool current)
        {
            if (current)
            {
                ApplyLeaving();
            }
        }

        private void ApplyLeaving()
        {
            if (_hasAppliedLeaving)
            {
                return;
            }

            _hasAppliedLeaving = true;

            if (_ground != null)
            {
                _ground.CloseToNewLines();
            }

            Leaving?.Invoke();
        }

        /// <summary>
        /// The server's clock for this point, and the only place it is ever removed.
        ///
        /// Guarded on IsSpawned before the variable is touched, for the same reason as the water: a
        /// NetworkVariable read before its object is spawned throws.
        /// </summary>
        private void Update()
        {
            if (!IsSpawned || !IsServer)
            {
                return;
            }

            if (!_isLeaving.Value)
            {
                _lifetimeRemaining -= Time.deltaTime;
                if (_lifetimeRemaining <= 0f)
                {
                    BeginLeavingOnServer("its time is up");
                }

                return;
            }

            // A line still in this water finishes where it began: the point waits for it.
            if (FisherStation.AnyLineIn(_ground))
            {
                if (!_hasReportedWaitingForLine)
                {
                    _hasReportedWaitingForLine = true;
                    GameLog.Info(LogCategory.Flow, $"'{name}' is waiting for a line still in its water.");
                }

                return;
            }

            GameLog.Info(LogCategory.Flow, $"'{name}' has gone.");
            NetworkObject.Despawn(true);
        }
    }
}
