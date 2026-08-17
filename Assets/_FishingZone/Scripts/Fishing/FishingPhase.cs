namespace FishingZone.Fishing
{
    /// <summary>
    /// What is happening at a fishing station right now.
    ///
    /// An enum rather than a flag, even though only two of these exist, because a bite and the
    /// playing of a fish are further phases of the same activity and will be added here. A bool that
    /// meant "fishing" would have no honest answer once there are several of them, and changing the
    /// replicated type later would mean rewriting every place that reads it.
    ///
    /// Named for what is true rather than for the activity as a whole. A station that has cast is
    /// waiting, and what it is waiting for is a bite; calling that state "fishing" would leave the
    /// bite itself with no word of its own, since biting is fishing too.
    ///
    /// Values are written out because they travel over the network as integers, exactly as
    /// PlayerRole's do, and letting them shift with a future reordering would silently turn one
    /// phase into another.
    ///
    /// This describes a station, never a player. Two stations may have a line out at once, and a
    /// station is only ever past Idle while somebody holds it.
    /// </summary>
    public enum FishingPhase
    {
        /// <summary>Nothing is in the water here: either nobody holds the station, or its holder has not cast.</summary>
        Idle = 0,

        /// <summary>A Fisher holds this station and has cast. The line is out and nothing has happened yet.</summary>
        Waiting = 1,

        /// <summary>
        /// A fish is at the line here, now. Says nothing about whether it can be hooked, held or
        /// lost: those are separate things that need words of their own, and inventing them before
        /// the mechanics exist would be guessing at what they mean.
        /// </summary>
        Bite = 2
    }
}
