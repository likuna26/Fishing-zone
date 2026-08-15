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
