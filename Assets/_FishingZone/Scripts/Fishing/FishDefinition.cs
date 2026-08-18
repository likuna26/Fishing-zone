using UnityEngine;

namespace FishingZone.Fishing
{
    /// <summary>
    /// One kind of fish: what it is called, and a number that says which one it is.
    ///
    /// Deliberately holds nothing else. Size, weight, worth, rarity and where it lives are all real
    /// questions with no systems behind them yet, and a field nothing reads is a decision made early
    /// and badly. This answers only "what did they catch".
    ///
    /// The id exists because the asset itself cannot travel. A ScriptableObject reference means
    /// nothing on another machine, so the server sends the number and every peer looks it up in the
    /// same list it was already given. That keeps the wire to the plain types this project has
    /// proven, and it means adding a fish is an asset and a line in an Inspector rather than code.
    ///
    /// Ids must be non-zero and unique among the fish a station can catch. Zero is reserved for
    /// "nothing", which is what a freshly created asset holds, so a definition nobody has filled in
    /// reads as unconfigured rather than as a fish.
    /// </summary>
    [CreateAssetMenu(fileName = "Fish", menuName = "Fishing Zone/Fish/Fish Definition")]
    public class FishDefinition : ScriptableObject
    {
        /// <summary>No fish. Also the id a new asset starts with, so the two coincide on purpose.</summary>
        public const int NoFish = 0;

        [SerializeField]
        private int _id;

        [SerializeField]
        private string _displayName = "Fish";

        public int Id => _id;

        public string DisplayName => _displayName;

        /// <summary>
        /// Whether this is filled in enough to be caught. Checked rather than assumed, because an
        /// asset created and forgotten is the likeliest way this goes wrong, and a catch that
        /// announced a nameless fish with the id zero would be worse than one that said nothing.
        /// </summary>
        public bool IsValid => _id != NoFish && !string.IsNullOrWhiteSpace(_displayName);
    }
}
