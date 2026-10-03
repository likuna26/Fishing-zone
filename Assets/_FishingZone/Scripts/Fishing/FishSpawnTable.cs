using System;
using System.Collections.Generic;
using FishingZone.World;
using UnityEngine;

namespace FishingZone.Fishing
{
    /// <summary>
    /// One fish that can be caught, and when and where and how often.
    ///
    /// The rules live here and not on the fish. A FishDefinition says what a salmon is; whether one
    /// bites at night in the north is a fact about the sea, and the same species can be common in one
    /// table and rare in another.
    ///
    /// Conditions are plain fields checked by <see cref="IsEligible"/>, and that method is the whole
    /// of the extension point: a new kind of condition is a field here and a line there, and nothing
    /// that chooses or catches fish has to change. If they ever grow numerous enough to want a list of
    /// pluggable conditions, that change stays inside this class too.
    /// </summary>
    [Serializable]
    public class FishSpawnEntry
    {
        [SerializeField]
        private FishDefinition _fish;

        /// <summary>
        /// How often, relative to the other eligible entries. Not a probability: an entry of 1 beside
        /// one of 3 comes up a quarter of the time. Zero or less switches the entry off. A very rare
        /// catch is simply a very small weight.
        /// </summary>
        [SerializeField]
        [Min(0f)]
        private float _weight = 1f;

        [SerializeField]
        private TimeOfDayMask _timesOfDay = TimeOfDayMask.Any;

        /// <summary>
        /// Where, by region. Empty means anywhere this table is used. Compared by region id, never by
        /// name or asset, so renaming a region changes nothing here.
        /// </summary>
        [SerializeField]
        private RegionDefinition[] _regions;

        /// <summary>
        /// Whether the lookout may name this fish when describing the water. Off for anything the crew
        /// should have to find out about by catching it. It hides the name and nothing else: the fish
        /// is caught exactly as often either way.
        /// </summary>
        [SerializeField]
        private bool _visibleToLookout = true;

        public FishDefinition Fish => _fish;

        public float Weight => _weight;

        public TimeOfDayMask TimesOfDay => _timesOfDay;

        public IReadOnlyList<RegionDefinition> Regions => _regions;

        public bool VisibleToLookout => _visibleToLookout;

        /// <summary>
        /// Whether these conditions allow this fish here and now. Says nothing about whether the fish
        /// itself is usable or catalogued; the selector asks that separately.
        /// </summary>
        public bool IsEligible(in CatchContext context)
        {
            if (context.HasTimeOfDay && !_timesOfDay.Includes(context.Band))
            {
                return false;
            }

            if (_regions == null || _regions.Length == 0)
            {
                return true;
            }

            for (int i = 0; i < _regions.Length; i++)
            {
                RegionDefinition region = _regions[i];
                if (region != null && region.Id != RegionDefinition.NoRegion && region.Id == context.RegionId)
                {
                    return true;
                }
            }

            return false;
        }
    }

    /// <summary>
    /// A list of what may be caught, with the rules for each. Attached to fishing grounds; one table
    /// can be shared by many grounds, and a ground can use several, so a stretch of coast can have
    /// something in common and something of its own.
    /// </summary>
    [CreateAssetMenu(fileName = "FishTable", menuName = "Fishing Zone/Fish/Fish Spawn Table")]
    public class FishSpawnTable : ScriptableObject
    {
        [SerializeField]
        private List<FishSpawnEntry> _entries = new List<FishSpawnEntry>();

        public IReadOnlyList<FishSpawnEntry> Entries => _entries;
    }
}
