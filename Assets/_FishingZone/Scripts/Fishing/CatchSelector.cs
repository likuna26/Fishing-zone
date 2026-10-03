using System.Collections.Generic;

namespace FishingZone.Fishing
{
    /// <summary>
    /// Picks what bites, out of spawn tables, for given circumstances and a given roll.
    ///
    /// A pure function and nothing more: it reads its arguments, touches no scene, no clock and no
    /// randomness of its own, and returns the same entry for the same inputs every time. The caller
    /// owns the roll, which is what lets the server be the only machine that ever rolls one, and lets
    /// a test hand it whatever roll it likes.
    ///
    /// An entry is a candidate only if its fish is usable, can be named through the catalog, has a
    /// positive weight, and its conditions allow it here and now. Nothing outside those can be
    /// returned — in particular, never a fish the catalog cannot resolve, since every peer has to be
    /// able to say what was caught.
    /// </summary>
    public static class CatchSelector
    {
        /// <summary>
        /// The chosen entry, weighted, or null when nothing is eligible. <paramref name="roll01"/> is a
        /// number from 0 to 1; it is clamped, so 1 is safe.
        ///
        /// Walks the tables twice — once to total the weights, once to find the roll — in a fixed
        /// order, so the answer depends only on the arguments. Allocates nothing.
        /// </summary>
        public static FishSpawnEntry Choose(IReadOnlyList<FishSpawnTable> tables, in CatchContext context,
            FishCatalog catalog, float roll01)
        {
            if (tables == null || catalog == null)
            {
                return null;
            }

            float total = 0f;
            FishSpawnEntry last = null;

            for (int t = 0; t < tables.Count; t++)
            {
                IReadOnlyList<FishSpawnEntry> entries = EntriesOf(tables[t]);
                for (int e = 0; e < entries.Count; e++)
                {
                    if (IsCandidate(entries[e], context, catalog, false))
                    {
                        total += entries[e].Weight;
                        last = entries[e];
                    }
                }
            }

            if (last == null || !(total > 0f))
            {
                return null;
            }

            float target = (roll01 < 0f ? 0f : roll01 > 1f ? 1f : roll01) * total;
            float cumulative = 0f;

            for (int t = 0; t < tables.Count; t++)
            {
                IReadOnlyList<FishSpawnEntry> entries = EntriesOf(tables[t]);
                for (int e = 0; e < entries.Count; e++)
                {
                    if (!IsCandidate(entries[e], context, catalog, false))
                    {
                        continue;
                    }

                    cumulative += entries[e].Weight;
                    if (target < cumulative)
                    {
                        return entries[e];
                    }
                }
            }

            // A roll of exactly 1 lands on the far edge of the last entry.
            return last;
        }

        /// <summary>
        /// Every eligible entry, in order, into <paramref name="results"/> (which is cleared first).
        /// With <paramref name="visibleToLookoutOnly"/>, entries the lookout may not name are left out
        /// — they are still caught; they are simply not announced.
        /// </summary>
        public static void CollectEligible(IReadOnlyList<FishSpawnTable> tables, in CatchContext context,
            FishCatalog catalog, bool visibleToLookoutOnly, List<FishSpawnEntry> results)
        {
            results.Clear();

            if (tables == null || catalog == null)
            {
                return;
            }

            for (int t = 0; t < tables.Count; t++)
            {
                IReadOnlyList<FishSpawnEntry> entries = EntriesOf(tables[t]);
                for (int e = 0; e < entries.Count; e++)
                {
                    if (IsCandidate(entries[e], context, catalog, visibleToLookoutOnly))
                    {
                        results.Add(entries[e]);
                    }
                }
            }
        }

        /// <summary>
        /// Whether these tables list anything at all, eligible or not. Tells "nothing bites here now"
        /// — a ground with rules that rule everything out at this hour — apart from "nobody set this
        /// ground up", which falls back to the station's own list.
        /// </summary>
        public static bool HasEntries(IReadOnlyList<FishSpawnTable> tables)
        {
            if (tables == null)
            {
                return false;
            }

            for (int t = 0; t < tables.Count; t++)
            {
                IReadOnlyList<FishSpawnEntry> entries = EntriesOf(tables[t]);
                for (int e = 0; e < entries.Count; e++)
                {
                    if (entries[e] != null && entries[e].Fish != null)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static bool IsCandidate(FishSpawnEntry entry, in CatchContext context, FishCatalog catalog,
            bool visibleToLookoutOnly)
        {
            return entry != null
                   && entry.Fish != null
                   && entry.Fish.IsValid
                   && entry.Weight > 0f
                   && !float.IsInfinity(entry.Weight)
                   && (!visibleToLookoutOnly || entry.VisibleToLookout)
                   && entry.IsEligible(context)
                   && catalog.Resolves(entry.Fish);
        }

        private static readonly FishSpawnEntry[] NoEntries = new FishSpawnEntry[0];

        private static IReadOnlyList<FishSpawnEntry> EntriesOf(FishSpawnTable table)
        {
            return table != null && table.Entries != null ? table.Entries : NoEntries;
        }
    }
}
