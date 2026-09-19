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
        /// Said after the session sentence, so the board answers the question a crew actually comes
        /// home with: not how the evening is going, but how that went.
        ///
        /// Its own sentence rather than a placeholder added to the one above, so a board already
        /// worded in an Inspector needs no editing for this to appear. The number is substituted
        /// rather than formatted, which is the idiom the stations settled on: a placeholder edited
        /// into something malformed loses the number instead of throwing inside a prompt.
        /// </summary>
        [SerializeField]
        private string _voyageCountText = "{0} of them this trip";

        /// <summary>
        /// Said when the trip was weighed as well as counted. Takes the count and then the weight.
        ///
        /// The line above remains for a trip that somehow came back unweighed — a fish whose range
        /// was never configured weighs nothing, exactly as the catch prompts already allow — so the
        /// board says what it knows and never invents a number it does not have.
        /// </summary>
        [SerializeField]
        private string _voyageWeighedText = "{0} of them this trip, {1} kg";

        /// <summary>
        /// Said when the crew came home empty. Its own sentence rather than the one above with a
        /// zero in it, for the reason the empty board has one: "0 of them this trip" is how a
        /// machine says it.
        /// </summary>
        [SerializeField]
        private string _voyageEmptyText = "None this trip";

        /// <summary>What goes between the two sentences.</summary>
        [SerializeField]
        private string _reportSeparator = ". ";

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
        /// What the trip just ended came to. Written beside the one above, at the same moment, from
        /// the same log — two numbers about the same crew, and no reason for either to arrive
        /// without the other.
        /// </summary>
        private readonly NetworkVariable<int> _voyageCatchCount = new NetworkVariable<int>(
            0,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        /// <summary>
        /// What that trip weighed, in tenths of a kilogram.
        ///
        /// Tenths on the wire and tenths on the board, divided once where it is read: an int cannot
        /// arrive rounded differently on two machines, which a float shared to one decimal place
        /// could. The same arrangement a single catch has always travelled by.
        /// </summary>
        private readonly NetworkVariable<int> _voyageWeightTenths = new NetworkVariable<int>(
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

            // Read at the same moment and from the same log. The trip count still stands here
            // because nothing clears it on the way home — it is cleared when the crew next reaches
            // the grounds, which is exactly after everybody has finished reading this.
            _voyageCatchCount.Value = log.GetVoyageCatchCount();
            _voyageWeightTenths.Value = log.GetVoyageCatchWeightTenths();

            GameLog.Info(LogCategory.Fish,
                $"'{name}' is showing {_voyageCatchCount.Value} from the trip weighing " +
                $"{FormatWeight(_voyageWeightTenths.Value)} kg, and a session total of {_crewCatchCount.Value}.");
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
        ///
        /// A crew who have landed nothing at all get the one sentence that says so, because a trip
        /// clause on an empty board would only say nothing twice.
        /// </summary>
        public string GetInteractionText(GameObject interactor)
        {
            int count = _crewCatchCount.Value;

            if (count <= 0)
            {
                return _emptyText;
            }

            return string.Format(_countText, count) + _reportSeparator + DescribeVoyage();
        }

        /// <summary>
        /// What the trip came to, said as a sentence of its own. Nought is a real answer here and
        /// not a missing one: a crew can come home empty from water that was quiet all evening, and
        /// the board should say so rather than leave them to work it out from a total that did not
        /// move.
        /// </summary>
        private string DescribeVoyage()
        {
            int voyage = _voyageCatchCount.Value;

            if (voyage <= 0)
            {
                return _voyageEmptyText;
            }

            int tenths = _voyageWeightTenths.Value;

            // Weighed if there is a weight to report, and named-but-unweighed otherwise — the same
            // degradation a single catch prompt already makes, and for the same reason: saying how
            // many beats saying nothing, and both beat inventing a number.
            if (tenths > 0 && !string.IsNullOrEmpty(_voyageWeighedText))
            {
                return _voyageWeighedText
                    .Replace("{0}", voyage.ToString())
                    .Replace("{1}", FormatWeight(tenths));
            }

            return string.IsNullOrEmpty(_voyageCountText)
                ? _voyageEmptyText
                : _voyageCountText.Replace("{0}", voyage.ToString());
        }

        /// <summary>
        /// Tenths of a kilogram as a number with one decimal place, built from the whole number
        /// rather than from a float, so every machine writes the same digits and none of them writes
        /// a comma where another writes a point.
        /// </summary>
        private static string FormatWeight(int tenths)
        {
            return $"{tenths / 10}.{tenths % 10}";
        }

        /// <summary>
        /// Deliberately nothing. The player has already read it.
        /// </summary>
        public void Interact(GameObject interactor)
        {
        }
    }
}
