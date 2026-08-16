using FishingZone.Core;
using FishingZone.Roles;
using Unity.Netcode;
using UnityEngine;

namespace FishingZone.Fishing
{
    /// <summary>
    /// A place aboard where a Fisher may fish. For now it only answers whether they may.
    ///
    /// Nothing is caught, cast, reeled or held here yet: this exists to establish that the job
    /// chosen back in the lobby still governs what a player may do once the lobby is long gone, and
    /// that the server is what decides it. The fishing itself is built on top of this.
    ///
    /// One station is one place. Two Fishers cannot stand on the same stretch of rail, so a crew
    /// that carries two of them gets two of these rather than one station keeping two seats. That
    /// costs nothing today, when the component holds no state at all, and it means a second place to
    /// fish is another object in the scene rather than a capacity number inside this class.
    ///
    /// Deliberately holds no occupancy. The wheel is exclusive because two people steering wrecked
    /// the hull; nothing of the sort is yet known to be true of fishing, and seating a player before
    /// the mechanics exist would be guessing at whether a Fisher even stands still to do it.
    /// </summary>
    public class FisherStation : NetworkBehaviour, IInteractable
    {
        [SerializeField]
        private string _fishText = "Fish here";

        [SerializeField]
        private string _wrongRoleText = "Only the Fisher may fish here";

        /// <summary>
        /// True for everybody, including players who will certainly be refused.
        ///
        /// Returning false here would do more than forbid the press: PlayerInteraction drops a
        /// target that cannot be interacted with, so the station would stop being looked at at all
        /// and a Navigator would see no prompt, no refusal and no reason — it would read as scenery.
        /// Worse, the request would never leave the machine, and a rule the server never gets asked
        /// about is a rule nobody can show is working.
        /// </summary>
        public bool CanInteract(GameObject interactor)
        {
            return true;
        }

        /// <summary>
        /// The one place this class is allowed to consult the role carried on the player object.
        ///
        /// That value is a replicated mirror which a determined client could edit in its own memory.
        /// The worst it can do here is make somebody's own screen offer them something the server
        /// will then refuse, which is a cosmetic lie and nothing more. It decides what a player
        /// reads. It never decides what a player may do; see the request below.
        /// </summary>
        public string GetInteractionText(GameObject interactor)
        {
            return PlayerRoleController.GetRoleOf(interactor) == PlayerRole.Fisher
                ? _fishText
                : _wrongRoleText;
        }

        /// <summary>
        /// Asks the server, whatever the prompt just said.
        ///
        /// A player the local mirror believes is no Fisher still gets to ask, and still gets a
        /// refusal from the machine entitled to give one. Refusing locally would be quicker and
        /// would hide the only thing worth proving.
        /// </summary>
        public void Interact(GameObject interactor)
        {
            if (!IsSpawned)
            {
                // Sending before the object is spawned throws. This can happen for an in-scene
                // station in the moments after the scene loads and before Netcode has spawned it.
                return;
            }

            RequestFishServerRpc();
        }

        /// <summary>
        /// The decision, and the only one that counts.
        ///
        /// Who is asking comes from the transport rather than from anything the caller sent, so one
        /// player cannot ask on another's behalf. What they are comes from the crew registry, which
        /// is server-only, written by the server when the lobby accepted their choice, and still
        /// standing long after the lobby was unloaded. PlayerRoleController is not consulted and
        /// must never be: it is a copy that lives on the asking machine.
        ///
        /// Ownership is not the check either, which is why it is not required. A station belongs to
        /// nobody; the question is what job the asker took, not who owns this object.
        /// </summary>
        [ServerRpc(RequireOwnership = false)]
        private void RequestFishServerRpc(ServerRpcParams parameters = default)
        {
            ulong senderId = parameters.Receive.SenderClientId;

            CrewRoleRegistry registry = ServiceRegistry.Get<CrewRoleRegistry>();
            if (registry == null)
            {
                // Refused, where the wheel would have allowed it. The wheel reasons that a crew
                // unable to steer is a worse failure than a role going unchecked, and it is right.
                // Here there is nothing to lose by refusing and everything to lose by permitting: a
                // station that quietly let the whole crew fish would look exactly like one that was
                // working, and the missing registry would go unnoticed until it mattered elsewhere.
                GameLog.Error(LogCategory.Fish,
                    $"Refused client {senderId} at '{name}': no crew registry, so nobody's job can be confirmed.");
                return;
            }

            PlayerRole role = registry.GetRole(senderId);
            if (role != PlayerRole.Fisher)
            {
                GameLog.Info(LogCategory.Fish,
                    $"Refused client {senderId} at '{name}': only a Fisher may fish there, and they are {role}.");
                return;
            }

            // The whole of the accepted path, for now. What a Fisher then does with the station is
            // the next piece of work; that this is where it will be authorised is the point of this one.
            GameLog.Info(LogCategory.Fish, $"Client {senderId} may fish at '{name}'.");
        }
    }
}
