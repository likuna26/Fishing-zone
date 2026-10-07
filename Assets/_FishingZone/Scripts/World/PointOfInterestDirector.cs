using System.Collections.Generic;
using FishingZone.Core;
using FishingZone.Fishing;
using Unity.Netcode;
using UnityEngine;

namespace FishingZone.World
{
    /// <summary>
    /// Decides, on the server, when and where points of interest appear during a voyage.
    ///
    /// Placed in the voyage scene, so it begins when the crew reach the sea and ends when they sail
    /// home, and everything it spawned goes with the scene. Not networked: it only decides, and what
    /// it decides reaches clients as the spawned points themselves.
    ///
    /// A voyage draws one seed and everything else from it — when the first point comes, how long
    /// each stays, how long the sea is empty after, and every candidate place — through its own
    /// random source and a fixed number of draws per attempt. The same seed therefore gives the same
    /// voyage, which is what the seed override is for: replaying a layout to look at it again.
    ///
    /// Points appear along the routes between the stops given here, never in the waters listed to
    /// avoid, never on top of a permanent ground, a landmark or the boat, and only at the hours their
    /// definition allows. One that is out when its hours end is sent away.
    /// </summary>
    public class PointOfInterestDirector : MonoBehaviour
    {
        [SerializeField]
        private PointOfInterestDefinition _definition;

        [SerializeField]
        private int _maxActive = 1;

        [Header("Routes")]
        /// <summary>The places crews sail between. Points appear on the legs joining every pair.</summary>
        [SerializeField]
        private Transform[] _routeStops;

        [SerializeField]
        [Range(0f, 1f)]
        private float _minAlongLeg = 0.3f;

        [SerializeField]
        [Range(0f, 1f)]
        private float _maxAlongLeg = 0.7f;

        /// <summary>How far either side of a leg's line a point may sit, in metres.</summary>
        [SerializeField]
        private float _maxOffsetFromRoute = 60f;

        [Header("Keep clear of")]
        [SerializeField]
        private FishingGround[] _permanentGrounds;

        /// <summary>From a permanent ground's centre, so a point never overlaps or crowds one.</summary>
        [SerializeField]
        private float _groundClearance = 120f;

        /// <summary>Buoys, rocks and anything else a point should not sit on.</summary>
        [SerializeField]
        private Transform[] _landmarks;

        [SerializeField]
        private float _landmarkClearance = 40f;

        /// <summary>Waters a point never appears in, by region — the harbour's own water.</summary>
        [SerializeField]
        private RegionDefinition[] _excludedRegions;

        /// <summary>So nothing appears beside the crew: it should be found, not happen to them.</summary>
        [SerializeField]
        private float _boatClearance = 150f;

        /// <summary>Half the width of the sea a point may appear in, inside the world's edge.</summary>
        [SerializeField]
        private float _worldHalfExtent = 950f;

        [Header("Drawing")]
        /// <summary>
        /// Candidates drawn per attempt, all of them every time, so the next attempt does not depend
        /// on how many of these were any good.
        /// </summary>
        [SerializeField]
        private int _candidatesPerAttempt = 12;

        /// <summary>How long to wait before trying again when every candidate was ruled out.</summary>
        [SerializeField]
        private float _retrySeconds = 10f;

        /// <summary>Development only: a non-zero value replays that voyage's layout.</summary>
        [SerializeField]
        private int _seedOverride;

        private readonly List<PointOfInterest> _active = new List<PointOfInterest>();
        private readonly List<Vector2> _stops = new List<Vector2>();
        private readonly List<PoiKeepClear> _keepClear = new List<PoiKeepClear>();

        private System.Random _random;
        private bool _hasStarted;
        private float _countdown;
        private float _nextGap;
        private int _appeared;

        /// <summary>This voyage's seed, once the server has drawn it. Zero before.</summary>
        public int Seed { get; private set; }

        private void Update()
        {
            NetworkManager network = NetworkManager.Singleton;
            if (network == null || !network.IsListening || !network.IsServer)
            {
                return;
            }

            if (!_hasStarted)
            {
                // A sea with a harbour in it starts the voyage's clock when the boat leaves the
                // harbour, so the first birds are timed from the departure rather than from the crew
                // arriving. Anywhere else, arriving is the departure, as it always was.
                HarbourVoyage voyage = HarbourVoyage.Current;
                if (voyage != null && !voyage.IsUnderWay)
                {
                    return;
                }

                Begin();
                if (!_hasStarted)
                {
                    return;
                }
            }

            PruneGone();

            bool isAllowedHour = IsAllowedHour();
            if (!isAllowedHour)
            {
                SendAwayOutOfHours();
            }

            if (_active.Count >= _maxActive)
            {
                return;
            }

            _countdown -= Time.deltaTime;
            if (_countdown > 0f || !isAllowedHour)
            {
                return;
            }

            TryAppear();
        }

        private void Begin()
        {
            if (_definition == null || _definition.Prefab == null)
            {
                GameLog.Error(LogCategory.Flow,
                    $"Point of interest director '{name}' has no definition or prefab, so nothing will appear this voyage.");
                enabled = false;
                return;
            }

            if (_routeStops == null || _routeStops.Length < 2)
            {
                GameLog.Error(LogCategory.Flow,
                    $"Point of interest director '{name}' needs at least two route stops, so nothing will appear this voyage.");
                enabled = false;
                return;
            }

            Seed = _seedOverride != 0 ? _seedOverride : NewSeed();
            _random = new System.Random(Seed);
            _countdown = PoiPlacement.Range(_random,
                _definition.MinFirstAppearanceSeconds, _definition.MaxFirstAppearanceSeconds);
            _hasStarted = true;

            GameLog.Info(LogCategory.Flow,
                $"Points of interest this voyage: '{_definition.DisplayName}', seed {Seed}" +
                $"{(_seedOverride != 0 ? " (override)" : "")}, first in {_countdown:F0}s.");
        }

        private static int NewSeed()
        {
            int seed = new System.Random().Next(1, int.MaxValue);
            return seed;
        }

        /// <summary>
        /// One attempt: how long the point would stay and the gap after it, then every candidate,
        /// always in that order and always the same number of draws.
        /// </summary>
        private void TryAppear()
        {
            float lifetime = PoiPlacement.Range(_random, _definition.MinLifetimeSeconds, _definition.MaxLifetimeSeconds);
            float gap = PoiPlacement.Range(_random, _definition.MinGapSeconds, _definition.MaxGapSeconds);

            CollectStops();
            CollectKeepClear();

            bool found = false;
            Vector2 chosen = Vector2.zero;

            for (int i = 0; i < _candidatesPerAttempt; i++)
            {
                Vector2 candidate = PoiPlacement.DrawOnLegs(_random, _stops, _minAlongLeg, _maxAlongLeg, _maxOffsetFromRoute);
                if (!found && PoiPlacement.IsClear(candidate, _keepClear, _worldHalfExtent) && !IsInExcludedRegion(candidate))
                {
                    found = true;
                    chosen = candidate;
                }
            }

            if (!found)
            {
                _countdown = _retrySeconds;
                GameLog.Info(LogCategory.Flow,
                    $"No clear place for '{_definition.DisplayName}' this time; trying again in {_retrySeconds:F0}s.");
                return;
            }

            Appear(chosen, lifetime);

            _nextGap = gap;
            _countdown = _active.Count >= _maxActive ? float.PositiveInfinity : gap;
        }

        private void Appear(Vector2 at, float lifetime)
        {
            _appeared++;

            PointOfInterest point = Instantiate(_definition.Prefab, new Vector3(at.x, 0f, at.y), Quaternion.identity);
            point.name = $"{_definition.Prefab.name}_{_appeared}";
            point.SetLifetimeOnServer(lifetime);
            point.NetworkObject.Spawn(destroyWithScene: true);

            _active.Add(point);

            GameLog.Info(LogCategory.Flow,
                $"'{point.name}' appeared at ({at.x:F0}, {at.y:F0}) for {lifetime:F0}s (seed {Seed}).");
        }

        /// <summary>
        /// Forgets points that have gone, and starts the empty spell after the last one only once it
        /// has actually gone — not when it started leaving, and not while a line kept it here.
        /// </summary>
        private void PruneGone()
        {
            int before = _active.Count;

            for (int i = _active.Count - 1; i >= 0; i--)
            {
                PointOfInterest point = _active[i];
                if (point == null || !point.IsSpawned)
                {
                    _active.RemoveAt(i);
                }
            }

            if (_active.Count < before && _active.Count < _maxActive && float.IsPositiveInfinity(_countdown))
            {
                _countdown = _nextGap;
                GameLog.Info(LogCategory.Flow, $"The sea stays empty for {_nextGap:F0}s.");
            }
        }

        private void SendAwayOutOfHours()
        {
            for (int i = 0; i < _active.Count; i++)
            {
                _active[i].BeginLeavingOnServer("its hours are over");
            }
        }

        private bool IsAllowedHour()
        {
            WorldClock clock = WorldClock.Current;

            // No clock means no hours to keep, which is how every other time rule in the project fails.
            return clock == null || _definition.TimesOfDay.Includes(clock.Band);
        }

        private void CollectStops()
        {
            _stops.Clear();
            for (int i = 0; i < _routeStops.Length; i++)
            {
                if (_routeStops[i] != null)
                {
                    _stops.Add(Flat(_routeStops[i].position));
                }
            }
        }

        private void CollectKeepClear()
        {
            _keepClear.Clear();

            if (_permanentGrounds != null)
            {
                for (int i = 0; i < _permanentGrounds.Length; i++)
                {
                    if (_permanentGrounds[i] != null)
                    {
                        _keepClear.Add(new PoiKeepClear(Flat(_permanentGrounds[i].transform.position), _groundClearance));
                    }
                }
            }

            if (_landmarks != null)
            {
                for (int i = 0; i < _landmarks.Length; i++)
                {
                    if (_landmarks[i] != null)
                    {
                        _keepClear.Add(new PoiKeepClear(Flat(_landmarks[i].position), _landmarkClearance));
                    }
                }
            }

            BoatRegionTracker boat = FindAnyObjectByType<BoatRegionTracker>();
            if (boat != null)
            {
                _keepClear.Add(new PoiKeepClear(Flat(boat.transform.position), _boatClearance));
            }
        }

        private bool IsInExcludedRegion(Vector2 point)
        {
            if (_excludedRegions == null || _excludedRegions.Length == 0)
            {
                return false;
            }

            RegionDefinition region = RegionVolume.FindRegion(new Vector3(point.x, 0f, point.y));
            if (region == null)
            {
                return false;
            }

            for (int i = 0; i < _excludedRegions.Length; i++)
            {
                if (_excludedRegions[i] != null && _excludedRegions[i].Id == region.Id)
                {
                    return true;
                }
            }

            return false;
        }

        private static Vector2 Flat(Vector3 position)
        {
            return new Vector2(position.x, position.z);
        }
    }
}
