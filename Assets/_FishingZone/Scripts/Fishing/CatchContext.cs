using FishingZone.World;

namespace FishingZone.Fishing
{
    /// <summary>
    /// The circumstances a fish is being chosen in: where the line went in, and when.
    ///
    /// Built by whoever is choosing, out of facts it already holds, and handed to the spawn rules so
    /// none of them has to go looking for the world itself. That keeps every rule testable with a
    /// context made up on the spot, and it is where a new circumstance — weather, an event, what the
    /// crew has discovered — arrives when one exists, without the rules learning where it comes from.
    ///
    /// For a real catch it is built on the server only. The lookout builds one too, on its own peer,
    /// but only to describe the water; nothing it builds decides a catch.
    /// </summary>
    public readonly struct CatchContext
    {
        public CatchContext(int regionId, bool hasTimeOfDay, TimeOfDayBand band)
        {
            RegionId = regionId;
            HasTimeOfDay = hasTimeOfDay;
            Band = band;
        }

        /// <summary>The region by id, or RegionDefinition.NoRegion for open sea and scenes without regions.</summary>
        public int RegionId { get; }

        /// <summary>
        /// False when there is no clock. Time restrictions are then ignored rather than treated as
        /// never satisfied: a scene with no clock fishes as one where it is always every time of day.
        /// </summary>
        public bool HasTimeOfDay { get; }

        public TimeOfDayBand Band { get; }
    }
}
