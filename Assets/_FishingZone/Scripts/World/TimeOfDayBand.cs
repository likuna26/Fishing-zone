namespace FishingZone.World
{
    /// <summary>
    /// Which part of the day the world is in.
    ///
    /// Coarse on purpose: these are what the crew can see and what later content will key off —
    /// night fish, things that only surface at dusk. Anything that needs finer timing asks the clock
    /// for the hour instead.
    ///
    /// Values are written out because a band may be sent or saved as an integer, and letting them
    /// shift with a future reordering would silently turn one part of the day into another.
    /// </summary>
    public enum TimeOfDayBand
    {
        Dawn = 0,
        Day = 1,
        Dusk = 2,
        Night = 3
    }
}
