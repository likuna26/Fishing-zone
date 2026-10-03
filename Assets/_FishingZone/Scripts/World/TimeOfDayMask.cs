using System;

namespace FishingZone.World
{
    /// <summary>
    /// Which parts of the day something applies to — dawn, day, dusk, night, or any mix of them.
    ///
    /// One bit per <see cref="TimeOfDayBand"/>, in the band's own order, so a band converts to its bit
    /// without a table. Written out because masks are stored in assets, and letting the bits shift
    /// with a reordering would silently move a night creature into the day.
    /// </summary>
    [Flags]
    public enum TimeOfDayMask
    {
        None = 0,
        Dawn = 1 << 0,
        Day = 1 << 1,
        Dusk = 1 << 2,
        Night = 1 << 3,
        Any = Dawn | Day | Dusk | Night
    }

    public static class TimeOfDayMaskExtensions
    {
        public static bool Includes(this TimeOfDayMask mask, TimeOfDayBand band)
        {
            return (mask & (TimeOfDayMask)(1 << (int)band)) != 0;
        }
    }
}
