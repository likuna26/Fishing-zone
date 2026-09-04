using FishingZone.Core;
using FishingZone.Player;
using FishingZone.Roles;
using Unity.Netcode;
using UnityEngine;

namespace FishingZone.Fishing
{
    /// <summary>
    /// Where the Observer reads the water.
    ///
    /// The first thing in this project that gives that job anything to do. It is also the only place
    /// aboard where <see cref="WaterActivity"/> can be seen at all: the Fishers it matters to are
    /// told nothing, anywhere, by anything. What the lookout learns reaches them only if the
    /// Observer says it out loud, which is not a limitation being worked around but the whole of the
    /// design — there is no prompt, no marker and no message that could carry it for them.
    ///
    /// Holds nothing and does nothing. There is no state here, no occupancy, no seat and no request:
    /// the water belongs to WaterActivity, and this is a window onto it. Reading is the interaction,
    /// so the key press has nothing left to do and no message to send.
    ///
    /// Scene-placed and voyage-scoped, like the water it reports.
    /// </summary>
    public class LookoutStation : NetworkBehaviour, IInteractable
    {
        /// <summary>
        /// The water this post looks out over. Assigned in the Inspector, because both objects live
        /// in the same scene and neither outlives it.
        /// </summary>
        [SerializeField]
        private WaterActivity _water;

        [SerializeField]
        private string _feedingText = "The water is alive — they're feeding";

        [SerializeField]
        private string _quietText = "The water is quiet — little is moving";

        /// <summary>
        /// Said to everybody else. The Navigator and the Fishers are not being kept from something
        /// they could otherwise use; the point of the post is that one person has to look.
        /// </summary>
        [SerializeField]
        private string _wrongRoleText = "Only the Lookout can read the water";

        /// <summary>
        /// Said when there is no water to read: a post that was never given one, or one whose value
        /// has not arrived yet. Reads as a view rather than as an error, because a player standing on
        /// deck cannot fix either and the console has already said which it was.
        /// </summary>
        [SerializeField]
        private string _unknownText = "You cannot make out the water from here";

        /// <summary>
        /// Says once, and loudly, that this post will never show anything. A lookout with no water
        /// assigned reads exactly like a lookout on quiet water, and the two want very different
        /// fixing.
        /// </summary>
        public override void OnNetworkSpawn()
        {
            if (_water == null)
            {
                GameLog.Error(LogCategory.Fish,
                    $"'{name}' has no Water Activity assigned, so the Lookout has nothing to read. " +
                    "Assign the scene's Water Activity on it in the Inspector.");
                return;
            }

            _water.FeedingChanged += HandleFeedingChanged;

            // Adopted rather than waited for, so a post spawning after the player who is looking at
            // it still reads correctly. Null-safe before any player exists.
            RefreshLocalPrompt();
        }

        public override void OnNetworkDespawn()
        {
            if (_water != null)
            {
                _water.FeedingChanged -= HandleFeedingChanged;
            }
        }

        /// <summary>
        /// True for everybody, including the three players who will only ever be told no.
        ///
        /// Returning false would do more than forbid the press: PlayerInteraction drops a target it
        /// cannot interact with, so a Fisher would see no prompt, no refusal and no reason, and the
        /// post would read as scenery. A crew who cannot see that the lookout exists cannot work out
        /// that somebody should be standing at it.
        /// </summary>
        public bool CanInteract(GameObject interactor)
        {
            return true;
        }

        /// <summary>
        /// The whole of it. The Observer reads the water; everyone else reads that it is not theirs
        /// to read.
        ///
        /// The role comes from the copy carried on the player object, and here that is all it could
        /// come from: this decides what words appear and nothing else. There is no request, no
        /// server-side action and therefore nothing to authorize — which is exactly the use
        /// PlayerRoleController is for, and the reason no registry is consulted.
        ///
        /// Be clear-eyed about what that gate is worth: a determined client could edit its own copy
        /// of its role and read the water without being the Observer. It would learn one boolean
        /// about bite timing that a crewmate would have told them anyway. This is a role playing its
        /// part, not a secret being kept.
        /// </summary>
        public string GetInteractionText(GameObject interactor)
        {
            if (PlayerRoleController.GetRoleOf(interactor) != PlayerRole.Observer)
            {
                return _wrongRoleText;
            }

            if (_water == null)
            {
                return _unknownText;
            }

            return _water.IsFeeding ? _feedingText : _quietText;
        }

        /// <summary>
        /// Deliberately nothing. The Observer has already read it, and there is nobody to send it to.
        /// </summary>
        public void Interact(GameObject interactor)
        {
        }

        /// <summary>
        /// The water turns while somebody is standing perfectly still looking at this post, which is
        /// the one situation prompt text is otherwise wrong in: it is read once, when the player
        /// first looks. So the moment the value arrives, whoever is watching is asked to read again.
        /// </summary>
        private void HandleFeedingChanged(bool feeding)
        {
            RefreshLocalPrompt();
        }

        /// <summary>
        /// Asks the local player to read its prompt again.
        ///
        /// Refreshing whatever the player happens to be looking at, rather than insisting it is this
        /// post, keeps this from having to know: re-reading another object's prompt produces the
        /// same words it already had. It re-raises an event with the value already held, so it can
        /// disturb nothing, and it is safe before any player exists.
        /// </summary>
        private static void RefreshLocalPrompt()
        {
            NetworkObject playerObject = NetworkManager.Singleton?.LocalClient?.PlayerObject;
            if (playerObject == null)
            {
                return;
            }

            PlayerInteraction interaction = playerObject.GetComponent<PlayerInteraction>();
            if (interaction != null)
            {
                interaction.RefreshFocus();
            }
        }
    }
}
