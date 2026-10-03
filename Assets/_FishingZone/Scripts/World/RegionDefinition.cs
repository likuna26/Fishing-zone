using UnityEngine;

namespace FishingZone.World
{
    /// <summary>
    /// One region of the sea: a number that says which one it is, and what the crew calls it.
    ///
    /// The id is the region. Anything that needs to know whether two places are the same region,
    /// remember where something happened, or one day save it, compares ids and nothing else. The
    /// display name is words for a person to read, and it can be changed at any time — renaming a
    /// region must never move a fish, break a save or change which region a crew is in.
    ///
    /// Deliberately holds nothing else yet. What lives here, what is dangerous here and what it
    /// takes to reach it are all real questions, each with its own step; a field nothing reads is a
    /// decision made early and badly, as FishDefinition puts it.
    ///
    /// Ids must be non-zero and unique across every region. Zero is reserved for "no region", which
    /// is what a freshly created asset holds, so a definition nobody has filled in reads as
    /// unconfigured rather than as a place.
    /// </summary>
    [CreateAssetMenu(fileName = "Region", menuName = "Fishing Zone/World/Region Definition")]
    public class RegionDefinition : ScriptableObject
    {
        /// <summary>No region: open sea, or an asset nobody has filled in.</summary>
        public const int NoRegion = 0;

        [SerializeField]
        private int _id;

        [SerializeField]
        private string _displayName = "Region";

        /// <summary>The region's identity. Stable for good; never derived from the name.</summary>
        public int Id => _id;

        /// <summary>What the crew calls it. For reading only — never for telling regions apart.</summary>
        public string DisplayName => _displayName;

        /// <summary>
        /// Whether this is filled in enough to be a place. Checked rather than assumed, because an
        /// asset created and forgotten is the likeliest way this goes wrong.
        /// </summary>
        public bool IsValid => _id != NoRegion && !string.IsNullOrWhiteSpace(_displayName);
    }
}
