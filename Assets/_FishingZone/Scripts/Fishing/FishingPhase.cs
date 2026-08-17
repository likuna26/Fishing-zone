namespace FishingZone.Fishing
{
    /// <summary>
    /// What is happening at a fishing station right now.
    ///
    /// An enum rather than a flag, even though only two of these exist, because casting, waiting for
    /// a bite and reeling are all phases of the same activity and will be added here. A bool that
    /// meant "fishing" would have no honest answer once there are five of them, and changing the
    /// replicated type later would mean rewriting every place that reads it.
    ///
    /// Values are written out because they travel over the network as integers, exactly as
    /// PlayerRole's do, and letting them shift with a future reordering would silently turn one
    /// phase into another.
    ///
    /// This describes a station, never a player. Two stations may be fishing at once, and a station
    /// is only ever fishing while somebody holds it.
    /// </summary>
    public enum FishingPhase
    {
        Idle = 0,
        Fishing = 1
    }
}
