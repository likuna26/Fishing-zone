using System;
using FishingZone.Core;
using FishingZone.Roles;
using Unity.Netcode;
using UnityEngine;

namespace FishingZone.Networking
{
    /// <summary>
    /// Who is in the crew and who is ready, owned by the server.
    ///
    /// Deliberately built from plain ulong and bool network variables rather than a list of a custom
    /// struct. The crew is one to four people by design, so a fixed set of slots costs nothing, and
    /// primitives avoid depending on serialization and equality rules that differ between Netcode
    /// versions. The same two mechanisms already carry the wheel's occupancy.
    ///
    /// Lives on an object in the Lobby scene. It cannot live on the persistent services object:
    /// that is made DontDestroyOnLoad from a scene loaded before any session exists, which is not
    /// somewhere Netcode can be relied on to spawn an in-scene object.
    ///
    /// The four slots describe the shape of the crew, not a connection limit. Nothing here refuses
    /// a connection.
    /// </summary>
    public class CrewRoster : NetworkBehaviour
    {
        /// <summary>No real client id, so an empty slot needs no second variable to describe it.</summary>
        public const ulong EmptySlot = ulong.MaxValue;

        public const int SlotCount = 4;

        private readonly NetworkVariable<ulong> _memberA = NewMemberSlot();
        private readonly NetworkVariable<ulong> _memberB = NewMemberSlot();
        private readonly NetworkVariable<ulong> _memberC = NewMemberSlot();
        private readonly NetworkVariable<ulong> _memberD = NewMemberSlot();

        private readonly NetworkVariable<bool> _readyA = NewReadySlot();
        private readonly NetworkVariable<bool> _readyB = NewReadySlot();
        private readonly NetworkVariable<bool> _readyC = NewReadySlot();
        private readonly NetworkVariable<bool> _readyD = NewReadySlot();

        // Held as int rather than as the enum, for the same reason the slots above are primitives:
        // it keeps the wire format to types this project has already proven, and PlayerRole's
        // explicit values make the conversion stable.
        private readonly NetworkVariable<int> _roleA = NewRoleSlot();
        private readonly NetworkVariable<int> _roleB = NewRoleSlot();
        private readonly NetworkVariable<int> _roleC = NewRoleSlot();
        private readonly NetworkVariable<int> _roleD = NewRoleSlot();

        /// <summary>Raised on every peer whenever membership or readiness changes.</summary>
        public event Action RosterChanged;

        /// <summary>
        /// Built here rather than in Awake. Unity gives no ordering guarantee between one object's
        /// Awake and another object's OnEnable, so the lobby UI could ask about the crew before this
        /// component had woken up and every accessor below would dereference null. A field
        /// initializer runs during construction, which nothing else on the scene can precede.
        ///
        /// Declared after the slots on purpose: initializers run top to bottom, so the four fields
        /// already exist by the time these arrays reference them.
        /// </summary>
        private readonly NetworkVariable<ulong>[] _members;
        private readonly NetworkVariable<bool>[] _ready;
        private readonly NetworkVariable<int>[] _roles;

        public CrewRoster()
        {
            _members = new[] { _memberA, _memberB, _memberC, _memberD };
            _ready = new[] { _readyA, _readyB, _readyC, _readyD };
            _roles = new[] { _roleA, _roleB, _roleC, _roleD };
        }

        private static NetworkVariable<ulong> NewMemberSlot()
        {
            return new NetworkVariable<ulong>(EmptySlot, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        }

        private static NetworkVariable<bool> NewReadySlot()
        {
            return new NetworkVariable<bool>(false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        }

        private static NetworkVariable<int> NewRoleSlot()
        {
            return new NetworkVariable<int>((int)PlayerRole.None, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        }

        public override void OnNetworkSpawn()
        {
            for (int i = 0; i < SlotCount; i++)
            {
                _members[i].OnValueChanged += HandleMemberChanged;
                _ready[i].OnValueChanged += HandleReadyChanged;
                _roles[i].OnValueChanged += HandleRoleChanged;
            }

            if (IsServer)
            {
                // Everyone already connected when the lobby opened, which on a fresh crew is just
                // the host, and on a mid-session return is the whole crew.
                foreach (ulong clientId in NetworkManager.Singleton.ConnectedClientsIds)
                {
                    AddMember(clientId);
                }

                NetworkManager.Singleton.OnClientConnectedCallback += AddMember;
                NetworkManager.Singleton.OnClientDisconnectCallback += RemoveMember;
            }

            RosterChanged?.Invoke();
        }

        public override void OnNetworkDespawn()
        {
            for (int i = 0; i < SlotCount; i++)
            {
                _members[i].OnValueChanged -= HandleMemberChanged;
                _ready[i].OnValueChanged -= HandleReadyChanged;
                _roles[i].OnValueChanged -= HandleRoleChanged;
            }

            if (IsServer && NetworkManager.Singleton != null)
            {
                NetworkManager.Singleton.OnClientConnectedCallback -= AddMember;
                NetworkManager.Singleton.OnClientDisconnectCallback -= RemoveMember;
            }
        }

        public bool IsSlotOccupied(int slot)
        {
            return IsValidSlot(slot) && _members[slot].Value != EmptySlot;
        }

        public ulong GetMemberAt(int slot)
        {
            return IsValidSlot(slot) ? _members[slot].Value : EmptySlot;
        }

        public bool IsReadyAt(int slot)
        {
            return IsValidSlot(slot) && _ready[slot].Value;
        }

        public PlayerRole GetRoleAt(int slot)
        {
            return IsValidSlot(slot) ? (PlayerRole)_roles[slot].Value : PlayerRole.None;
        }

        public bool IsLocalMemberAt(int slot)
        {
            return NetworkManager.Singleton != null
                   && IsSlotOccupied(slot)
                   && _members[slot].Value == NetworkManager.Singleton.LocalClientId;
        }

        public int MemberCount
        {
            get
            {
                int count = 0;
                for (int i = 0; i < SlotCount; i++)
                {
                    if (IsSlotOccupied(i))
                    {
                        count++;
                    }
                }

                return count;
            }
        }

        /// <summary>
        /// True only when there is somebody in the crew and every one of them is ready. Empty slots
        /// are not treated as agreement, so a crew of one still has to say yes.
        /// </summary>
        public bool AllReady
        {
            get
            {
                int occupied = 0;

                for (int i = 0; i < SlotCount; i++)
                {
                    if (!IsSlotOccupied(i))
                    {
                        continue;
                    }

                    occupied++;

                    if (!_ready[i].Value)
                    {
                        return false;
                    }
                }

                return occupied > 0;
            }
        }

        /// <summary>
        /// Whether anybody in the crew has taken the wheel's job. Read from the slots rather than
        /// from the persistent role registry on purpose: the question is about who is in this lobby
        /// right now, and the slots are what empty on disconnect and change as people choose.
        /// </summary>
        public bool HasNavigator
        {
            get
            {
                for (int i = 0; i < SlotCount; i++)
                {
                    if (IsSlotOccupied(i) && (PlayerRole)_roles[i].Value == PlayerRole.Navigator)
                    {
                        return true;
                    }
                }

                return false;
            }
        }

        /// <summary>
        /// The whole rule for whether the crew may set off, kept here rather than in the menu so the
        /// button is only ever reporting a decision the server-owned roster has already made.
        ///
        /// Without a Navigator the crew would arrive at a boat none of them is allowed to steer.
        /// </summary>
        public bool CanStartMission => AllReady && HasNavigator;

        public bool IsLocalMemberReady
        {
            get
            {
                if (NetworkManager.Singleton == null)
                {
                    return false;
                }

                // Not folded into a single expression with a fallback of EmptySlot: that would match
                // the first empty slot and report its flag as though it were this player's.
                int slot = FindSlotOf(NetworkManager.Singleton.LocalClientId);
                return slot >= 0 && _ready[slot].Value;
            }
        }

        /// <summary>
        /// Asks the server to change this player's own readiness. Clients never write the state
        /// themselves, and the request carries no player identity: the server uses the sender the
        /// transport reports, so one player cannot mark another ready.
        /// </summary>
        public void RequestSetReady(bool isReady)
        {
            if (!IsSpawned)
            {
                return;
            }

            SetReadyServerRpc(isReady);
        }

        [ServerRpc(RequireOwnership = false)]
        private void SetReadyServerRpc(bool isReady, ServerRpcParams parameters = default)
        {
            int slot = FindSlotOf(parameters.Receive.SenderClientId);
            if (slot < 0)
            {
                return;
            }

            _ready[slot].Value = isReady;
        }

        /// <summary>The job this player has chosen, or None if they have not chosen one.</summary>
        public PlayerRole LocalMemberRole
        {
            get
            {
                if (NetworkManager.Singleton == null)
                {
                    return PlayerRole.None;
                }

                int slot = FindSlotOf(NetworkManager.Singleton.LocalClientId);
                return slot >= 0 ? (PlayerRole)_roles[slot].Value : PlayerRole.None;
            }
        }

        /// <summary>
        /// Asks the server to change this player's own role, on exactly the terms readiness uses:
        /// the request carries no player identity, so the server decides whose role it is from the
        /// sender the transport reports and nobody can reassign anyone else's job.
        ///
        /// Changing role deliberately leaves readiness alone. Picking a job is not agreeing to sail.
        /// </summary>
        public void RequestSetRole(PlayerRole role)
        {
            if (!IsSpawned)
            {
                return;
            }

            SetRoleServerRpc((int)role);
        }

        [ServerRpc(RequireOwnership = false)]
        private void SetRoleServerRpc(int role, ServerRpcParams parameters = default)
        {
            int slot = FindSlotOf(parameters.Receive.SenderClientId);
            if (slot < 0)
            {
                return;
            }

            // The value arrived as a plain integer, so it is checked rather than trusted: anything
            // outside the enum would otherwise be stored and shown as a nonsense job.
            if (!Enum.IsDefined(typeof(PlayerRole), role))
            {
                GameLog.Warn(LogCategory.Network, $"Client {parameters.Receive.SenderClientId} asked for role {role}, which does not exist.");
                return;
            }

            _roles[slot].Value = role;

            // Mirrored into the registry, which is what the wheel will consult once the lobby has
            // been unloaded and this component no longer exists.
            MirrorRoleToRegistry(parameters.Receive.SenderClientId, (PlayerRole)role);
        }

        /// <summary>
        /// Copies an accepted role into the persistent store. Deliberately only ever writes: the
        /// registry forgets a role on disconnect and on nothing else, so this component being torn
        /// down with the Lobby scene cannot take a chosen Navigator's role with it.
        /// </summary>
        private static void MirrorRoleToRegistry(ulong clientId, PlayerRole role)
        {
            CrewRoleRegistry registry = ServiceRegistry.Get<CrewRoleRegistry>();
            if (registry == null)
            {
                // ServiceRegistry has already logged the miss.
                return;
            }

            registry.SetRole(clientId, role);
        }

        private void AddMember(ulong clientId)
        {
            if (!IsServer || FindSlotOf(clientId) >= 0)
            {
                // Already present. Connect and lobby entry can coincide, and adding twice would
                // put one player in two slots.
                return;
            }

            int free = FindSlotOf(EmptySlot);
            if (free < 0)
            {
                // Not a refusal: the connection stands, there is simply no slot to show it in.
                // Enforcing a crew size is a separate piece of work.
                GameLog.Warn(LogCategory.Network, $"Client {clientId} joined but the roster has no free slot.");
                return;
            }

            _members[free].Value = clientId;

            // A new arrival has not agreed to anything, so a crew that was ready no longer is.
            _ready[free].Value = false;

            // Nor have they picked a job yet, which also clears whatever the slot's last occupant chose.
            _roles[free].Value = (int)PlayerRole.None;

            // Kept in step with the slot, so a crew that comes back to the lobby chooses again
            // rather than silently carrying the last mission's jobs, and so what the roster shows is
            // always what the registry will enforce. This runs on joining, never on unloading.
            MirrorRoleToRegistry(clientId, PlayerRole.None);
        }

        private void RemoveMember(ulong clientId)
        {
            if (!IsServer)
            {
                return;
            }

            int slot = FindSlotOf(clientId);
            if (slot < 0)
            {
                return;
            }

            _members[slot].Value = EmptySlot;
            _ready[slot].Value = false;
            _roles[slot].Value = (int)PlayerRole.None;
        }

        private int FindSlotOf(ulong clientId)
        {
            for (int i = 0; i < SlotCount; i++)
            {
                if (_members[i].Value == clientId)
                {
                    return i;
                }
            }

            return -1;
        }

        private static bool IsValidSlot(int slot)
        {
            return slot >= 0 && slot < SlotCount;
        }

        private void HandleMemberChanged(ulong previous, ulong current)
        {
            RosterChanged?.Invoke();
        }

        private void HandleReadyChanged(bool previous, bool current)
        {
            RosterChanged?.Invoke();
        }

        private void HandleRoleChanged(int previous, int current)
        {
            RosterChanged?.Invoke();
        }
    }
}
