using System.Collections.Generic;
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

        /// <summary>
        /// Takes the list of what lives below, already joined into a phrase.
        ///
        /// Habitat was configured two commits ago and no player has been able to see it since: a
        /// crew could only learn that the north held salmon by fishing there and remembering, which
        /// is knowledge the game had and the players had to keep for it. This is the Observer's
        /// second job, and the reason the ground they are over is worth naming.
        /// </summary>
        [SerializeField]
        private string _habitatText = "{0} run here";

        /// <summary>
        /// Said over a ground that advertises nothing.
        ///
        /// That is a real answer rather than a failure: a ground with no list of its own is fished
        /// from the station's, and this post has no way to know which of two stations would be
        /// consulted or what either holds. Saying so beats guessing, and it beats reporting a list
        /// that might not be the one the fish come from.
        /// </summary>
        [SerializeField]
        private string _unknownHabitatText = "Hard to say what runs here";

        /// <summary>
        /// What the Lookout can tell about how hard this water has been worked.
        ///
        /// Said as a condition and never as a count: no number, no threshold, no share of anything.
        /// The Observer is meant to judge whether it is worth crossing, and a figure on a sign would
        /// do that judging for them.
        ///
        /// Silent on untouched water by default, so the first warning is something appearing rather
        /// than something changing — and so the usual report stays as short as it was.
        /// </summary>
        [SerializeField]
        private string _stockFreshText = string.Empty;

        [SerializeField]
        private string _stockWorkingText = "The fish here are growing wary";

        [SerializeField]
        private string _stockTiredText = "These waters have been worked hard";

        /// <summary>
        /// What goes between the two halves of the report.
        ///
        /// Kept as its own field rather than baked into the sentences above, so that adding habitat
        /// changes nothing already written: the wording of the water is untouched and needs no
        /// placeholder added to it, which means a scene that has already been through an Inspector
        /// reads correctly the moment this lands.
        /// </summary>
        [SerializeField]
        private string _reportSeparator = ". ";

        /// <summary>
        /// Offered only when pressing would actually work: quiet water, a ground below, and a call
        /// this ground will still answer. A prompt that offered something about to be refused would
        /// make the Observer's judgement worthless, since the judgement is the whole of the role.
        /// </summary>
        [SerializeField]
        private string _callReadyText = "Interact to call them up";

        /// <summary>
        /// Said while the water will not answer again yet. No number and no bar: how long is the
        /// server's business, and a countdown on a sign would turn a decision into an egg timer.
        /// </summary>
        [SerializeField]
        private string _callCoolingText = "They will not come up again yet";

        /// <summary>
        /// The three things the light can be doing, appended to whatever else this post has to say.
        ///
        /// A fact about the trip rather than about the water below, which is why it is said over
        /// open sea as well: a crew hunting for a buoy with the light going is exactly the crew with
        /// a decision to make.
        ///
        /// Leave any of these empty to silence that state. Emptying the first is the obvious edit —
        /// a lookout need not keep announcing that everything is fine — but it is left saying
        /// something by default so a crew can see the day is running at all.
        /// </summary>
        [SerializeField]
        private string _lightGoodText = "The light is good";

        [SerializeField]
        private string _lightFadingText = "The light is going";

        [SerializeField]
        private string _lightGoneText = "The light has gone";

        /// <summary>What this post last said, so the boat moving is noticed once rather than tested against.</summary>
        private FishingGround _lastGround;

        private WaterActivity _lastWater;

        private bool _lastFeeding;

        /// <summary>
        /// Tracked alongside the rest, because the offer to call appears and disappears while a
        /// player stands perfectly still looking at this post — when the cooldown runs out, and when
        /// somebody spends it.
        /// </summary>
        private bool _lastCallReady;

        /// <summary>
        /// Tracked with the rest, because the light goes while a player stands perfectly still
        /// looking at this post — which is the moment the warning is worth anything at all.
        /// </summary>
        private ExpeditionPhase _lastLight;

        /// <summary>
        /// Tracked with the rest, because a ground tires while somebody is standing still watching
        /// it — which is the moment the warning is worth anything at all.
        /// </summary>
        private WaterStock _lastStock;

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
            bool callReady = water != null && water.IsCallReady;
            WaterStock stock = water != null ? water.Stock : WaterStock.Fresh;
            ExpeditionPhase light = CurrentLight();

            if (ReferenceEquals(ground, _lastGround)
                && ReferenceEquals(water, _lastWater)
                && feeding == _lastFeeding
                && callReady == _lastCallReady
                && stock == _lastStock
                && light == _lastLight)
            {
                return;
            }

            _lastGround = ground;
            _lastWater = water;
            _lastFeeding = feeding;
            _lastCallReady = callReady;
            _lastStock = stock;
            _lastLight = light;

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
            if (!PlayerRoleController.IsAuthorizedFor(interactor, PlayerRole.Observer))
            {
                return _wrongRoleText;
            }

            FishingGround ground = FishingGround.Find(transform.position);
            if (ground == null)
            {
                return _openWaterText + DescribeLight();
            }

            WaterActivity water = WaterActivity.Under(transform.position);
            if (water == null)
            {
                return _unknownText + DescribeLight();
            }

            string text = water.IsFeeding ? _feedingText : _quietText;

            if (string.IsNullOrEmpty(text))
            {
                return _unknownText + DescribeLight();
            }

            return text.Replace("{0}", ground.DisplayName)
                   + _reportSeparator + DescribeHabitat(ground)
                   + DescribeStock(water)
                   + DescribeCall(water)
                   + DescribeLight();
        }

        /// <summary>
        /// How worked this water is, said as a condition.
        ///
        /// Sits with the habitat rather than with the call, because both are facts about the ground
        /// below: what lives here, and how much of it is left. Silent on untouched water, so the
        /// crew hear about it exactly when there is something to hear.
        /// </summary>
        private string DescribeStock(WaterActivity water)
        {
            string text;
            switch (water.Stock)
            {
                case WaterStock.Tired:
                    text = _stockTiredText;
                    break;
                case WaterStock.Working:
                    text = _stockWorkingText;
                    break;
                default:
                    text = _stockFreshText;
                    break;
            }

            return string.IsNullOrEmpty(text) ? string.Empty : _reportSeparator + text;
        }

        /// <summary>
        /// What the light is doing, said last because it is the only clause here that is not about
        /// the water below.
        ///
        /// Appended to every report, including the one over open sea. A crew crossing between
        /// grounds with the light going is precisely the crew who need telling, and a post that went
        /// quiet about the day the moment the boat left a ground would go quiet exactly when it
        /// mattered most.
        ///
        /// Silent when there is no day to report — a scene nobody has given a window to still reads
        /// correctly, and says nothing rather than guessing.
        /// </summary>
        private string DescribeLight()
        {
            ExpeditionWindow window = ExpeditionWindow.Current;
            if (window == null)
            {
                return string.Empty;
            }

            string text;
            switch (window.Phase)
            {
                case ExpeditionPhase.Closed:
                    text = _lightGoneText;
                    break;
                case ExpeditionPhase.Fading:
                    text = _lightFadingText;
                    break;
                default:
                    text = _lightGoodText;
                    break;
            }

            return string.IsNullOrEmpty(text) ? string.Empty : _reportSeparator + text;
        }

        /// <summary>
        /// The day's state for the purpose of noticing it change, with no day reading as Open.
        ///
        /// A steady value rather than a third case, because this is only ever compared with the last
        /// one: a scene with no window never changes, so it never asks for a re-read, which is
        /// exactly right for a post that has nothing to say about the day.
        /// </summary>
        private static ExpeditionPhase CurrentLight()
        {
            ExpeditionWindow window = ExpeditionWindow.Current;

            return window != null ? window.Phase : ExpeditionPhase.Open;
        }

        /// <summary>
        /// Whether this water can be called, and nothing about how long until it can.
        ///
        /// Silent on feeding water, because there is nothing to offer and nothing to wait for: the
        /// fish are already up and the report above has just said so. The offer therefore appears
        /// exactly when pressing would work, which is what makes it an offer rather than a hint.
        /// </summary>
        private string DescribeCall(WaterActivity water)
        {
            if (water.IsFeeding)
            {
                return string.Empty;
            }

            string text = water.IsCallReady ? _callReadyText : _callCoolingText;

            return string.IsNullOrEmpty(text) ? string.Empty : _reportSeparator + text;
        }

        /// <summary>
        /// What lives here, said as a phrase.
        ///
        /// Read straight off the ground rather than kept anywhere: habitat is configured in one
        /// place and this is a window onto it, exactly as the water is. A second copy would be a
        /// second thing to keep in step and a second thing to be wrong.
        ///
        /// Never the station's list, even though that is what a ground advertising nothing is
        /// actually fished from. This post cannot know which of two stations a Fisher is standing
        /// at, and reporting a list the fish might not come from would be worse than admitting it
        /// does not know.
        /// </summary>
        private string DescribeHabitat(FishingGround ground)
        {
            string species = JoinSpecies(ground.FishPool);

            if (species == null || string.IsNullOrEmpty(_habitatText))
            {
                return _unknownHabitatText;
            }

            return _habitatText.Replace("{0}", species);
        }

        /// <summary>
        /// The usable fish of a list, in the order somebody typed them, as English rather than as a
        /// dump: one is itself, two are joined by "and", and more take commas until the last.
        ///
        /// Configured order rather than sorted, so what a player hears matches what an Inspector
        /// shows and a wrong entry is findable.
        ///
        /// Entries nobody filled in are skipped on the same test the server chooses by, so a list
        /// with a hole in it reads as the fish that are in it rather than as a gap, an empty name or
        /// a stray comma. Null when nothing in the list can be caught — which is a different answer
        /// from a list of nothing, and the caller says so differently.
        /// </summary>
        private static string JoinSpecies(IReadOnlyList<FishDefinition> pool)
        {
            if (pool == null)
            {
                return null;
            }

            int usable = 0;
            for (int i = 0; i < pool.Count; i++)
            {
                if (pool[i] != null && pool[i].IsValid)
                {
                    usable++;
                }
            }

            if (usable == 0)
            {
                return null;
            }

            string joined = string.Empty;
            int written = 0;

            for (int i = 0; i < pool.Count; i++)
            {
                if (pool[i] == null || !pool[i].IsValid)
                {
                    continue;
                }

                // Separator chosen from what has already been written rather than from where this
                // entry sits in the list, so the holes skipped above cannot put a comma before the
                // first name or an "and" in the middle.
                joined = written == 0 ? pool[i].DisplayName
                    : written == usable - 1 ? joined + " and " + pool[i].DisplayName
                    : joined + ", " + pool[i].DisplayName;

                written++;
            }

            return joined;
        }

        /// <summary>
        /// The Observer's one act, and the reason this method stopped being empty.
        ///
        /// Asks whatever the prompt said. A player whose own copy of their role says they are no
        /// Observer still gets to ask, and gets their answer from the machine entitled to give one;
        /// refusing here would be quicker and would hide the only thing worth proving.
        /// </summary>
        public void Interact(GameObject interactor)
        {
            if (!IsSpawned)
            {
                // Sending before the object is spawned throws. This can happen for an in-scene post
                // in the moments after the scene loads and before Netcode has spawned it.
                return;
            }

            RequestCallServerRpc();
        }

        /// <summary>
        /// The decision, and the only one that counts.
        ///
        /// Carries nothing. There is no ground to name and no state to report, so there is no field
        /// for a client to lie in: which water this is comes from where this post stands, on the
        /// server's own copy of the scene, and whether it will answer comes from the water itself.
        /// A client cannot call a stretch of water the boat is not over, because it has no way to
        /// say which stretch it means.
        ///
        /// Who is asking comes from the transport rather than from anything the caller sent, and
        /// what they are comes from the crew registry, which is server-only and still standing long
        /// after the lobby was unloaded. PlayerRoleController is not consulted and must never be:
        /// it is a copy that lives on the asking machine and it decides what a player reads, never
        /// what they may do.
        ///
        /// Ownership is not the check, which is why it is not required: a crow's nest belongs to
        /// nobody. The question is what job the asker took.
        /// </summary>
        [ServerRpc(RequireOwnership = false)]
        private void RequestCallServerRpc(ServerRpcParams parameters = default)
        {
            ulong senderId = parameters.Receive.SenderClientId;

            CrewRoleRegistry registry = ServiceRegistry.Get<CrewRoleRegistry>();
            if (registry == null)
            {
                // Refused rather than allowed, as the fishing stations refuse. There is nothing lost
                // by refusing a call and something real lost by permitting one on nobody's
                // authority, and a post that quietly let the whole crew call would look exactly like
                // one that was working.
                GameLog.Error(LogCategory.Fish,
                    $"Refused client {senderId} at '{name}': no crew registry, so nobody's job can be confirmed.");
                return;
            }

            PlayerRole role = registry.GetRole(senderId);
            if (!registry.IsAuthorizedFor(senderId, PlayerRole.Observer))
            {
                GameLog.Info(LogCategory.Fish,
                    $"Refused client {senderId} at '{name}': only the Lookout calls the fish up, and they are {role}.");
                return;
            }

            WaterActivity water = WaterActivity.Under(transform.position);
            if (water == null)
            {
                GameLog.Info(LogCategory.Fish,
                    $"Ignored client {senderId} at '{name}': there is no water below to call.");
                return;
            }

            if (!water.TryCallOnServer())
            {
                // Refused without spending anything. The water said no because it is already
                // feeding or because it has not come back to itself yet, and either way the crew
                // still has their call.
                GameLog.Info(LogCategory.Fish,
                    $"Ignored client {senderId} at '{name}': '{water.name}' would not answer.");
                return;
            }

            GameLog.Info(LogCategory.Fish, $"Client {senderId} called the fish up at '{water.name}'.");
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
