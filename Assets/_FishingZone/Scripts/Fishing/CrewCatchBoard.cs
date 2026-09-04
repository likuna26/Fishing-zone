using FishingZone.Core;
using Unity.Netcode;
using UnityEngine;

namespace FishingZone.Fishing
{
    /// <summary>
    /// A board ashore saying how many fish the crew has landed this session.
    ///
    /// The catch log is server-side and speaks to nobody, which is right for a thing that decides,
    /// and useless for a thing that tells. This is the telling: one number, copied out of the log
    /// once, replicated to everyone, and read by standing in front of it. Until now the only way a
    /// player learned anything about a catch was to land it themselves, and only in the second it
    /// happened.
    ///
    /// Crew-wide on purpose. A shared total is a fact about the crew, so everybody may read it and
    /// no one needs to be told a different number from anybody else. A per-player readout is a
    /// different feature wanting privacy, targeting and a message per client, and none of that is
    /// bought here by accident.
    ///
    /// Holds nothing that outlives the port. The count belongs to the log, which survives the
    /// voyage; this is a view of it, spawned with the scene and destroyed with it, which is why it
    /// can afford to read once. Nobody fishes in port, so the number cannot change while it is up.
    /// </summary>
    public class CrewCatchBoard : NetworkBehaviour, IInteractable
    {
        /// <summary>
        /// Said when the crew has landed nothing. Written as its own sentence rather than the one
        /// below with a zero in it, because "landed 0 fish" is how a machine says it.
        /// </summary>
        [SerializeField]
        private string _emptyText = "The crew has not landed anything yet";

        /// <summary>Takes the count. Needs its {0} or the number goes unsaid.</summary>
        [SerializeField]
        private string _countText = "The crew has landed {0} fish this session";

        /// <summary>
        /// Written once by the server, read by everyone, and that is the whole of the traffic. It
        /// travels with the spawn, so a client has the number before it has a player able to walk up
        /// and read it.
        /// </summary>
        private readonly NetworkVariable<int> _crewCatchCount = new NetworkVariable<int>(
            0,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        /// <summary>
        /// The server asks the log what the crew has landed and publishes the answer.
        ///
        /// Asked here rather than kept up to date, because the board is built fresh every time the
        /// crew comes home and the log is the thing that remembers. Each arrival in port therefore
        /// reads the authoritative total afresh, and no event, tick or timer is needed to keep a
        /// number current that nothing in port can change.
        ///
        /// A missing log leaves the count at zero. ServiceRegistry has already said loudly what was
        /// not registered, and a board reading nought is a better failure than one that stops the
        /// scene loading.
        /// </summary>
        public override void OnNetworkSpawn()
        {
            if (!IsServer)
            {
                return;
            }

            CrewCatchLog log = ServiceRegistry.Get<CrewCatchLog>();
            if (log == null)
            {
                return;
            }

            _crewCatchCount.Value = log.GetCrewCatchCount();

            GameLog.Info(LogCategory.Fish, $"'{name}' is showing the crew's session total of {_crewCatchCount.Value}.");
        }

        /// <summary>
        /// True for everyone, and not because everyone can do something with it: PlayerInteraction
        /// drops a target it cannot interact with, and a board that refuses to be looked at is a
        /// board that says nothing. Nobody is turned away from a notice.
        /// </summary>
        public bool CanInteract(GameObject interactor)
        {
            return true;
        }

        /// <summary>
        /// The whole of it. Reading is the interaction, and the prompt is where reading happens, so
        /// there is nothing left for the key press to do.
        /// </summary>
        public string GetInteractionText(GameObject interactor)
        {
            int count = _crewCatchCount.Value;

            return count <= 0 ? _emptyText : string.Format(_countText, count);
        }

        /// <summary>
        /// Deliberately nothing. The player has already read it.
        /// </summary>
        public void Interact(GameObject interactor)
        {
        }
    }
}
