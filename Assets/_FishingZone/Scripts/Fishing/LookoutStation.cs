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
    /// The first thing in this project that gave that job anything to do, and now the first thing
    /// that gives the Navigator a reason to touch the wheel twice. It reports the water the boat is
    /// actually over: a ground by name and what is happening on it. Sail somewhere else and it says
    /// something else, because each ground keeps its own.
    ///
    /// It reports where the crew IS and never where they are not. Being able to read every ground
    /// from one spot would make the Observer an oracle and the Navigator their driver; this way the
    /// only way to learn what the next stretch of water is doing is to go and look at it, and
    /// deciding whether that is worth the crossing is the whole of the crew's judgement.
    ///
    /// It is also still the only place aboard where <see cref="WaterActivity"/> can be seen at all.
    /// The Fishers it matters to are told nothing, anywhere, by anything, so what the lookout learns
    /// reaches them only if the Observer says it out loud — which is not a limitation being worked
    /// around but the whole of the design.
    ///
    /// Holds nothing and does nothing. There is no state here, no occupancy, no seat and no request:
    /// the water belongs to the grounds, and this is a window onto whichever one is below. Reading
    /// is the interaction, so the key press has nothing left to do and no message to send.
    /// </summary>
    public class LookoutStation : NetworkBehaviour, IInteractable
    {
        /// <summary>
        /// Takes the ground's name. The name is substituted rather than formatted, so a placeholder
        /// edited into something malformed loses the name instead of throwing in the middle of a
        /// prompt — the same reason the fishing stations substitute.
        /// </summary>
        [SerializeField]
        private string _feedingText = "Over {0} — they're feeding";

        [SerializeField]
        private string _quietText = "Over {0} — the water is quiet";

        /// <summary>
        /// Said between the grounds, which is most of the map. Not a failure and not an error: open
        /// sea is the ordinary state of the sea, and it is also the answer that tells the crew they
        /// have not arrived yet.
        /// </summary>
        [SerializeField]
        private string _openWaterText = "Open water — there is nothing below us here";

        /// <summary>
        /// Said over a ground whose water was never set up. Reads as a view rather than as an error,
        /// because a player standing on deck cannot fix it and the console has already named it.
        /// </summary>
        [SerializeField]
        private string _unknownText = "You cannot make out the water from here";

        /// <summary>
        /// Said to everybody else. The Navigator and the Fishers are not being kept from something
        /// they could otherwise use; the point of the post is that one person has to look.
        /// </summary>
        [SerializeField]
        private string _wrongRoleText = "Only the Lookout can read the water";

        /// <summary>What this post last said, so the boat moving is noticed once rather than tested against.</summary>
        private FishingGround _lastGround;

        private WaterActivity _lastWater;

        private bool _lastFeeding;

        /// <summary>
        /// Adopted rather than waited for, so a post spawning after the player who is looking at it
        /// still reads correctly. Null-safe before any player exists.
        /// </summary>
        public override void OnNetworkSpawn()
        {
            RefreshLocalPrompt();
        }

        /// <summary>
        /// Watches the boat cross from one stretch of water to another.
        ///
        /// Needed because prompt text is read once, when a player first looks at something. What
        /// this post says changes for two reasons that nothing announces: the water turning, and the
        /// boat moving. Subscribing to a turn would cover only the first, and would have to be given
        /// up and taken out again on a different object every time the boat crossed a boundary,
        /// which is a subscription to get wrong for no gain. Comparing the answer with the last one
        /// covers both cases with no lifetime to manage — the same edge the fishing stations use to
        /// notice the same crossing.
        ///
        /// The ground and the water are held as well as the flag, so a ground gaining its water
        /// after this first looked, or two grounds happening to be quiet at once, still read
        /// correctly rather than being mistaken for no change at all.
        /// </summary>
        private void Update()
        {
            if (!IsSpawned)
            {
                return;
            }

            FishingGround ground = FishingGround.Find(transform.position);
            WaterActivity water = WaterActivity.Under(transform.position);
            bool feeding = water != null && water.IsFeeding;

            if (ReferenceEquals(ground, _lastGround)
                && ReferenceEquals(water, _lastWater)
                && feeding == _lastFeeding)
            {
                return;
            }

            _lastGround = ground;
            _lastWater = water;
            _lastFeeding = feeding;

            RefreshLocalPrompt();
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
        /// The whole of it. The Observer reads the water below; everyone else reads that it is not
        /// theirs to read.
        ///
        /// Asked of this post's own position, which is the boat's, because the lookout is bolted to
        /// a deck that moves.
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

            FishingGround ground = FishingGround.Find(transform.position);
            if (ground == null)
            {
                return _openWaterText;
            }

            WaterActivity water = WaterActivity.Under(transform.position);
            if (water == null)
            {
                return _unknownText;
            }

            string text = water.IsFeeding ? _feedingText : _quietText;

            return string.IsNullOrEmpty(text)
                ? _unknownText
                : text.Replace("{0}", ground.DisplayName);
        }

        /// <summary>
        /// Deliberately nothing. The Observer has already read it, and there is nobody to send it to.
        /// </summary>
        public void Interact(GameObject interactor)
        {
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
