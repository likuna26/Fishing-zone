using System;
using FishingZone.Core;
using Unity.Netcode;
using UnityEngine;

namespace FishingZone.Roles
{
    /// <summary>
    /// The job this player took, carried on the player object so that every peer can see it.
    ///
    /// THIS IS A MIRROR, NOT AN AUTHORITY. It exists so a client can know and show a role, because
    /// <see cref="CrewRoleRegistry"/> is server-only and a client can therefore ask it nothing. The
    /// registry remains the single source of truth: the server reads a role out of it and writes it
    /// here, and never the other way round.
    ///
    /// A ServerRpc must NEVER decide anything from this component. Server-side authorization reads
    /// ServerRpcParams.Receive.SenderClientId and asks the registry, exactly as NavigatorStation
    /// does when someone reaches for the wheel. The reason is simple: write permission stops a
    /// client changing this value for anybody else, but nothing stops one changing its own copy in
    /// memory, and a value a client can edit must only ever decide what that client draws — never
    /// what it is allowed to do. Trusting this for a permission check would turn a display
    /// convenience into a way to steer a boat you were never given.
    ///
    /// Held as int rather than as the enum for the reason the roster's slots are: it keeps the wire
    /// format to types this project has already proven, and PlayerRole's explicit values make the
    /// conversion stable.
    ///
    /// Runs on every peer. It must not be listed among PlayerNetworkController's owner-only
    /// behaviours, which would leave remote copies switched off and knowing nothing.
    /// </summary>
    public class PlayerRoleController : NetworkBehaviour
    {
        private readonly NetworkVariable<int> _role = new NetworkVariable<int>(
            (int)PlayerRole.None,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        /// <summary>
        /// The job this player holds, or None for somebody who never chose one. A player who joined
        /// after the crew set off is None by design: there was no lobby for them to choose in.
        /// </summary>
        public PlayerRole Role => (PlayerRole)_role.Value;

        /// <summary>
        /// Raised on every peer when this player's job becomes known or changes. Worth listening to
        /// rather than reading <see cref="Role"/> once, because a client may well spawn this object
        /// a frame before the value replicates into it.
        /// </summary>
        public event Action<PlayerRole> RoleChanged;

        public override void OnNetworkSpawn()
        {
            // Subscribed before the server writes below, so the host hears its own assignment
            // through the same path a remote client does and there is only one way this is reported.
            _role.OnValueChanged += HandleRoleChanged;

            if (IsServer)
            {
                AdoptRoleFromRegistry();
            }

            GameLog.Info(LogCategory.Network,
                $"Player object for client {OwnerClientId} spawned on this peer reading {Role}.");
        }

        public override void OnNetworkDespawn()
        {
            _role.OnValueChanged -= HandleRoleChanged;
        }

        /// <summary>
        /// Copies the role the crew registry has been holding since the lobby.
        ///
        /// Server only, and read once at spawn rather than watched, because a role cannot change
        /// during a mission: the lobby is where it is chosen and the lobby is gone.
        ///
        /// A missing registry leaves the value at None rather than failing. That costs a player
        /// their role display, which is a far smaller loss than a player object that will not spawn,
        /// and ServiceRegistry has already said loudly what was not found.
        /// </summary>
        private void AdoptRoleFromRegistry()
        {
            CrewRoleRegistry registry = ServiceRegistry.Get<CrewRoleRegistry>();
            if (registry == null)
            {
                GameLog.Warn(LogCategory.Network,
                    $"No crew registry when spawning client {OwnerClientId}; that player has no role to show.");
                return;
            }

            PlayerRole role = registry.GetRole(OwnerClientId);
            _role.Value = (int)role;

            GameLog.Info(LogCategory.Network, $"Client {OwnerClientId} spawned as {role}.");
        }

        private void HandleRoleChanged(int previous, int current)
        {
            PlayerRole role = (PlayerRole)current;

            // Logged on whichever peer received it, so a four-player test can show the value arriving
            // on remote copies and not merely on the machine that wrote it.
            GameLog.Info(LogCategory.Network, $"Role of client {OwnerClientId} replicated here as {role}.");

            RoleChanged?.Invoke(role);
        }

        /// <summary>
        /// The job of whoever is holding this object, or None if it is not a player or has no role.
        ///
        /// Meant for the interactor an IInteractable is handed: a station can ask what is standing in
        /// front of it without knowing how a player is put together. Searched from the parent chain
        /// for the same reason PlayerInteraction searches it, since the collider met on the way in
        /// may be a child of the object that carries the component.
        ///
        /// For prompts and other things a player sees. Never for deciding what they may do.
        /// </summary>
        public static PlayerRole GetRoleOf(GameObject interactor)
        {
            if (interactor == null)
            {
                return PlayerRole.None;
            }

            PlayerRoleController controller = interactor.GetComponentInParent<PlayerRoleController>();
            return controller != null ? controller.Role : PlayerRole.None;
        }
    }
}
