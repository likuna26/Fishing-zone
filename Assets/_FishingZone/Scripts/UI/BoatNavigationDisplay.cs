using System.Text;
using FishingZone.Core;
using FishingZone.World;
using TMPro;
using UnityEngine;

namespace FishingZone.UI
{
    /// <summary>
    /// The dashboard at the wheel: which way the boat points, how fast it is going, and which
    /// waters it is in.
    ///
    /// Display only. Every peer works its own reading out from the boat it can already see — the
    /// replicated transform and the local region tracker — so this sends nothing, owns nothing and
    /// decides nothing. Heading and region come straight from the transform the server replicates,
    /// which is what keeps them the same on every machine.
    ///
    /// Speed is measured from how far the boat moved rather than read from its Rigidbody, because
    /// on clients that body is kinematic and reports no velocity at all. The host measures the same
    /// way so both screens are reading one formula, smoothed so the number does not flicker.
    /// </summary>
    public class BoatNavigationDisplay : MonoBehaviour
    {
        private const float MetresPerSecondToKnots = 1.943844f;

        private static readonly string[] CompassPoints =
        {
            "N", "NNE", "NE", "ENE", "E", "ESE", "SE", "SSE",
            "S", "SSW", "SW", "WSW", "W", "WNW", "NW", "NNW"
        };

        [SerializeField]
        private TMP_Text _text;

        /// <summary>Left empty on the boat prefab: the region tracker above this is the boat.</summary>
        [SerializeField]
        private BoatRegionTracker _regionTracker;

        [SerializeField]
        private string _openSeaText = "Open Sea";

        /// <summary>How often the text is rebuilt. Speed is still sampled every frame.</summary>
        [SerializeField]
        private float _refreshInterval = 0.2f;

        /// <summary>Time constant of the speed smoothing; longer is steadier and slower to react.</summary>
        [SerializeField]
        private float _speedSmoothingTime = 0.5f;

        private readonly StringBuilder _builder = new StringBuilder(64);

        private Transform _boat;
        private bool _isConfigured;

        private Vector3 _lastPosition;
        private bool _hasLastPosition;
        private float _smoothedSpeed;
        private float _nextRefreshTime;

        private int _shownHeading = -1;
        private int _shownKnots = -1;
        private int _shownRegionId = int.MinValue;

        private void Awake()
        {
            if (_text == null)
            {
                _text = GetComponent<TMP_Text>();
            }

            if (_regionTracker == null)
            {
                _regionTracker = GetComponentInParent<BoatRegionTracker>();
            }

            _isConfigured = _text != null && _regionTracker != null;
            if (!_isConfigured)
            {
                GameLog.Error(LogCategory.UI,
                    $"BoatNavigationDisplay on '{name}' needs a TMP text and a BoatRegionTracker above it.");
                return;
            }

            _boat = _regionTracker.transform;
        }

        private void OnEnable()
        {
            // Forgotten on purpose: a boat placed by a scene load would otherwise read as one
            // great leap and show a speed it never had.
            _hasLastPosition = false;
            _smoothedSpeed = 0f;
            _shownHeading = -1;
            _shownKnots = -1;
            _shownRegionId = int.MinValue;
        }

        /// <summary>
        /// Late, so the client has already moved the boat to this frame's interpolated pose.
        /// </summary>
        private void LateUpdate()
        {
            if (!_isConfigured)
            {
                return;
            }

            SampleSpeed();

            if (Time.unscaledTime < _nextRefreshTime)
            {
                return;
            }

            _nextRefreshTime = Time.unscaledTime + _refreshInterval;
            Refresh();
        }

        private void SampleSpeed()
        {
            Vector3 position = _boat.position;
            float deltaTime = Time.deltaTime;

            if (_hasLastPosition && deltaTime > 0f)
            {
                Vector3 moved = position - _lastPosition;
                moved.y = 0f;

                float speed = moved.magnitude / deltaTime;
                float blend = 1f - Mathf.Exp(-deltaTime / Mathf.Max(_speedSmoothingTime, 0.01f));
                _smoothedSpeed = Mathf.Lerp(_smoothedSpeed, speed, blend);
            }

            _lastPosition = position;
            _hasLastPosition = true;
        }

        /// <summary>
        /// Rebuilds the text only when something it shows has changed, into a reused builder, so a
        /// steady course costs nothing and a changing one allocates nothing.
        /// </summary>
        private void Refresh()
        {
            Vector3 forward = _boat.forward;
            float heading = Mathf.Atan2(forward.x, forward.z) * Mathf.Rad2Deg;
            if (heading < 0f)
            {
                heading += 360f;
            }

            int shownHeading = Mathf.RoundToInt(heading) % 360;
            int knots = Mathf.RoundToInt(_smoothedSpeed * MetresPerSecondToKnots);
            int regionId = _regionTracker.CurrentRegionId;

            if (shownHeading == _shownHeading && knots == _shownKnots && regionId == _shownRegionId)
            {
                return;
            }

            _shownHeading = shownHeading;
            _shownKnots = knots;
            _shownRegionId = regionId;

            RegionDefinition region = _regionTracker.CurrentRegion;
            string regionName = region != null ? region.DisplayName : _openSeaText;
            int point = Mathf.RoundToInt(heading / 22.5f) % CompassPoints.Length;

            _builder.Clear();
            _builder.Append(CompassPoints[point]).Append(' ');
            AppendDigits(_builder, shownHeading, 3);
            _builder.Append("°   ");
            AppendDigits(_builder, knots, 1);
            _builder.Append(" kn\n").Append(regionName);

            _text.SetText(_builder);
        }

        /// <summary>StringBuilder.Append(int) allocates on this runtime; digits written by hand do not.</summary>
        private static void AppendDigits(StringBuilder builder, int value, int minDigits)
        {
            if (value < 0)
            {
                value = 0;
            }

            int digits = 1;
            for (int scale = 10; scale <= value; scale *= 10)
            {
                digits++;
            }

            for (int i = digits; i < minDigits; i++)
            {
                builder.Append('0');
            }

            int divisor = 1;
            for (int i = 1; i < digits; i++)
            {
                divisor *= 10;
            }

            for (; divisor > 0; divisor /= 10)
            {
                builder.Append((char)('0' + value / divisor % 10));
            }
        }
    }
}
