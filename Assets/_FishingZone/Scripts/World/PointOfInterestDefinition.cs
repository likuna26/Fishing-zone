using UnityEngine;

namespace FishingZone.World
{
    /// <summary>
    /// One kind of thing a crew can come across at sea, and how it comes and goes.
    ///
    /// Data only. Where a point may appear belongs to the scene's director, which knows the routes;
    /// what it does belongs to its prefab. This says what it is called, what to spawn, and its
    /// rhythm — every number here is a starting value meant to be balanced, so none of them lives
    /// in code.
    /// </summary>
    [CreateAssetMenu(fileName = "Poi_New", menuName = "Fishing Zone/Point Of Interest")]
    public class PointOfInterestDefinition : ScriptableObject
    {
        /// <summary>Stable identity for logs and anything later that must remember a kind of point.</summary>
        [SerializeField]
        private int _id;

        [SerializeField]
        private string _displayName = "Point of interest";

        [SerializeField]
        private PointOfInterest _prefab;

        /// <summary>How long after the crew reach the sea the first one may appear.</summary>
        [SerializeField]
        private float _minFirstAppearanceSeconds = 45f;

        [SerializeField]
        private float _maxFirstAppearanceSeconds = 120f;

        /// <summary>How long one stays before it starts to leave.</summary>
        [SerializeField]
        private float _minLifetimeSeconds = 240f;

        [SerializeField]
        private float _maxLifetimeSeconds = 360f;

        /// <summary>How long the sea stays empty after one has gone before the next may appear.</summary>
        [SerializeField]
        private float _minGapSeconds = 60f;

        [SerializeField]
        private float _maxGapSeconds = 180f;

        /// <summary>When it may be found. One that is out when its hours end leaves early.</summary>
        [SerializeField]
        private TimeOfDayMask _timesOfDay = TimeOfDayMask.Dawn | TimeOfDayMask.Day | TimeOfDayMask.Dusk;

        public int Id => _id;

        public string DisplayName => _displayName;

        public PointOfInterest Prefab => _prefab;

        public float MinFirstAppearanceSeconds => _minFirstAppearanceSeconds;

        public float MaxFirstAppearanceSeconds => _maxFirstAppearanceSeconds;

        public float MinLifetimeSeconds => _minLifetimeSeconds;

        public float MaxLifetimeSeconds => _maxLifetimeSeconds;

        public float MinGapSeconds => _minGapSeconds;

        public float MaxGapSeconds => _maxGapSeconds;

        public TimeOfDayMask TimesOfDay => _timesOfDay;
    }
}
