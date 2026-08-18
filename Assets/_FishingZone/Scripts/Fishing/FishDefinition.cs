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

        /// <summary>
        /// The lightest and heaviest this kind of fish comes. Kilograms, because that is what a
        /// deck scale reads and what the prompt will say.
        ///
        /// A range and nothing more. How likely a big one is, what it is worth and whether it beats
        /// anybody's record are all questions for systems that do not exist.
        /// </summary>
        [SerializeField]
        private float _minWeightKg = 1f;

        [SerializeField]
        private float _maxWeightKg = 5f;

        public int Id => _id;

        public string DisplayName => _displayName;

        public float MinWeightKg => _minWeightKg;

        public float MaxWeightKg => _maxWeightKg;

        /// <summary>
        /// Whether this fish can be weighed at all. Kept apart from <see cref="IsValid"/> on
        /// purpose: a fish that is properly named but carelessly weighed should still be catchable
        /// and still be named, with the scale left out rather than guessed at.
        ///
        /// A weightless or negative fish is a mistake, and so is a maximum below the minimum.
        /// </summary>
        public bool HasValidWeightRange => _minWeightKg > 0f && _maxWeightKg >= _minWeightKg;

        /// <summary>
        /// Whether this is filled in enough to be caught. Checked rather than assumed, because an
        /// asset created and forgotten is the likeliest way this goes wrong, and a catch that
        /// announced a nameless fish with the id zero would be worse than one that said nothing.
        /// </summary>
        public bool IsValid => _id != NoFish && !string.IsNullOrWhiteSpace(_displayName);
    }
}
