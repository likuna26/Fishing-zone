using System.Collections.Generic;
using FishingZone.Core;
using Unity.Netcode;
using UnityEngine;

namespace FishingZone.Roles
{
    /// <summary>
    /// Remembers which job each player took, for as long as they are connected.
    ///
    /// The lobby's roster cannot answer this once a mission is under way: it lives on an object in
    /// the Lobby scene, and that scene is unloaded the moment the crew sets off. Player objects are
    /// no better, being respawned with each gameplay scene. The persistent services object is the
    /// only thing that outlives a scene change, so the answer is kept here.
    ///
    /// Server-authoritative. Clients neither read nor write it; they ask for a role through the
    /// roster, and the server consults this when deciding what they may do.
    ///
    /// Entries are removed on disconnect and on nothing else. In particular no scene unloading and
    /// no roster teardown removes anything, which is what keeps a chosen Navigator's role alive
    /// across the transition out of the lobby.
    /// </summary>
    public class CrewRoleRegistry : MonoBehaviour
    {
        private readonly Dictionary<ulong, PlayerRole> _roles = new Dictionary<ulong, PlayerRole>();

        private void Awake()
        {
            // Registers itself rather than being published by Bootstrap, so that adding it needs no
            // change to the startup sequence: dropping the component on the services object is enough.
            ServiceRegistry.Register(this);
        }

        // Subscribed in Start because NetworkManager assigns its singleton in Awake, and every Awake
        // runs before any Start. Subscribing before a session exists is fine; nothing fires until one does.
        private void Start()
        {
            if (NetworkManager.Singleton != null)
            {
                NetworkManager.Singleton.OnClientDisconnectCallback += HandleClientDisconnected;
                NetworkManager.Singleton.OnServerStopped += HandleServerStopped;
            }
        }

        private void OnDestroy()
        {
            if (NetworkManager.Singleton != null)
            {
                NetworkManager.Singleton.OnClientDisconnectCallback -= HandleClientDisconnected;
                NetworkManager.Singleton.OnServerStopped -= HandleServerStopped;
            }

            ServiceRegistry.Unregister<CrewRoleRegistry>();
        }

        /// <summary>Returns None for anyone who has not chosen, including a player who has rejoined.</summary>
        public PlayerRole GetRole(ulong clientId)
        {
            return _roles.TryGetValue(clientId, out PlayerRole role) ? role : PlayerRole.None;
        }

        /// <summary>
        /// Whether this client may do a job, which is not the same question as whether it is theirs.
        ///
        /// A job you took is yours. A job nobody took is everybody's — and that is not a loophole
        /// but the point of the rule a crew of four never notices. Exclusivity exists so that one
        /// player has to rely on another; an empty seat creates reliance on nobody, so enforcing it
        /// there is a locked door with no one behind it. With every job filled this reduces to the
        /// comparison it replaced, and a full crew plays exactly as it did.
        ///
        /// Somebody with no job at all is refused either way. The fallback is for a crewmate
        /// covering an empty post, not for a player who arrived after the lobby and was never given
        /// anything to cover it from — they are not short-handed, they are unassigned.
        ///
        /// Server-side truth. The prompts ask the same question of the replicated copy so a player
        /// is not offered what this would refuse, but only this decides.
        /// </summary>
        public bool IsAuthorizedFor(ulong clientId, PlayerRole required)
        {
            if (required == PlayerRole.None)
            {
                // Not a job, so there is nothing to be authorized for. No gate asks this; it is
                // refused here so that none can start by accident.
                return false;
            }

            PlayerRole held = GetRole(clientId);
            if (held == required)
            {
                return true;
            }

            if (held == PlayerRole.None)
            {
                return false;
            }

            return !IsRoleHeld(required);
        }

        /// <summary>Whether anybody aboard took this job.</summary>
        private bool IsRoleHeld(PlayerRole role)
        {
            foreach (PlayerRole held in _roles.Values)
            {
                if (held == role)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Records a role the server has already accepted. Called by the roster once it has
        /// established whose request it was; this does no validation of its own beyond refusing to
        /// run anywhere but the server.
        /// </summary>
        public void SetRole(ulong clientId, PlayerRole role)
        {
            if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer)
            {
                return;
            }

            _roles[clientId] = role;
        }

        /// <summary>
        /// The only thing that forgets a role. A player who leaves and comes back mid-mission
        /// therefore returns with none, because there is no lobby to choose in.
        /// </summary>
        private void HandleClientDisconnected(ulong clientId)
        {
            if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer)
            {
                return;
            }

            if (_roles.Remove(clientId))
            {
                GameLog.Info(LogCategory.Network, $"Forgot the role of client {clientId} on disconnect.");
            }
        }

        /// <summary>Ending the session clears the crew, so a new one never inherits the last one's jobs.</summary>
        private void HandleServerStopped(bool wasHost)
        {
            _roles.Clear();
        }
    }
}
