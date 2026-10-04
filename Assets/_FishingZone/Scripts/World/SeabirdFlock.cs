using UnityEngine;

namespace FishingZone.World
{
    /// <summary>
    /// Seabirds working a patch of sea: the only sign that something is there.
    ///
    /// Birds mean fish are present. They do not mean the fish are biting, so nothing here reads the
    /// water: they circle and dive on the same rhythm whether it is feeding or quiet, and reading
    /// that stays the lookout's job.
    ///
    /// Purely visual and purely local. Each peer animates its own copy, seeded from where the point
    /// is, which every peer already agrees on — so the flocks look alike everywhere without a single
    /// message. They need not match bird for bird; they only need to be in the same place.
    ///
    /// When the point leaves, the birds disperse and climb away, then are hidden. Every frame's work
    /// is arithmetic on arrays made once.
    /// </summary>
    public class SeabirdFlock : MonoBehaviour
    {
        /// <summary>Each bird, with its two wings as its first two children.</summary>
        [SerializeField]
        private Transform[] _birds;

        [SerializeField]
        private float _minCircleRadius = 4f;

        [SerializeField]
        private float _maxCircleRadius = 14f;

        [SerializeField]
        private float _minHeight = 7f;

        [SerializeField]
        private float _maxHeight = 20f;

        /// <summary>Degrees per second around the circle.</summary>
        [SerializeField]
        private float _minCircleSpeed = 35f;

        [SerializeField]
        private float _maxCircleSpeed = 70f;

        /// <summary>Seconds between one bird's dives. Fixed rhythm, never tied to the water.</summary>
        [SerializeField]
        private float _minDiveInterval = 3f;

        [SerializeField]
        private float _maxDiveInterval = 8f;

        [SerializeField]
        private float _diveSeconds = 1.6f;

        /// <summary>How close to the sea a dive comes, in metres.</summary>
        [SerializeField]
        private float _diveLowestHeight = 0.6f;

        [SerializeField]
        private float _flapsPerSecond = 2.5f;

        [SerializeField]
        private float _flapDegrees = 30f;

        [SerializeField]
        private float _leaveSeconds = 8f;

        [SerializeField]
        private float _leaveSpeed = 14f;

        [SerializeField]
        private float _leaveClimb = 5f;

        private float[] _angle;
        private float[] _radius;
        private float[] _height;
        private float[] _speed;
        private float[] _diveCountdown;
        private float[] _diveElapsed;
        private float[] _flapPhase;
        private Vector3[] _leaveDirection;

        private System.Random _random;
        private PointOfInterest _point;
        private bool _isLeaving;
        private float _leaveElapsed;
        private bool _hasHidden;

        private void Awake()
        {
            int count = _birds != null ? _birds.Length : 0;
            _angle = new float[count];
            _radius = new float[count];
            _height = new float[count];
            _speed = new float[count];
            _diveCountdown = new float[count];
            _diveElapsed = new float[count];
            _flapPhase = new float[count];
            _leaveDirection = new Vector3[count];

            // Seeded from where the flock is, which every peer agrees on, so each draws the same flock.
            Vector3 at = transform.position;
            int seed = unchecked(Mathf.RoundToInt(at.x) * 73856093 ^ Mathf.RoundToInt(at.z) * 19349663);
            _random = new System.Random(seed);
            System.Random random = _random;

            for (int i = 0; i < count; i++)
            {
                _angle[i] = PoiPlacement.Range(random, 0f, 360f);
                _radius[i] = PoiPlacement.Range(random, _minCircleRadius, _maxCircleRadius);
                _height[i] = PoiPlacement.Range(random, _minHeight, _maxHeight);
                _speed[i] = PoiPlacement.Range(random, _minCircleSpeed, _maxCircleSpeed) * (random.Next(2) == 0 ? 1f : -1f);
                _diveCountdown[i] = PoiPlacement.Range(random, 0f, _maxDiveInterval);
                _diveElapsed[i] = -1f;
                _flapPhase[i] = PoiPlacement.Range(random, 0f, Mathf.PI * 2f);
            }

            _point = GetComponentInParent<PointOfInterest>();
        }

        private void OnEnable()
        {
            if (_point != null)
            {
                _point.Leaving += HandleLeaving;
            }
        }

        private void OnDisable()
        {
            if (_point != null)
            {
                _point.Leaving -= HandleLeaving;
            }
        }

        private void HandleLeaving()
        {
            if (_isLeaving)
            {
                return;
            }

            _isLeaving = true;
            _leaveElapsed = 0f;

            // Each bird heads off roughly the way it was already flying, and a little outward.
            for (int i = 0; i < _birds.Length; i++)
            {
                if (_birds[i] == null)
                {
                    continue;
                }

                Vector3 offset = _birds[i].localPosition;
                offset.y = 0f;
                Vector3 outward = offset.sqrMagnitude > 0.01f ? offset.normalized : Vector3.forward;
                Vector3 along = Quaternion.Euler(0f, _speed[i] > 0f ? -90f : 90f, 0f) * outward;
                _leaveDirection[i] = (along + outward * 0.6f).normalized;
            }
        }

        private void Update()
        {
            if (_hasHidden || _birds == null)
            {
                return;
            }

            float deltaTime = Time.deltaTime;

            if (_isLeaving)
            {
                Disperse(deltaTime);
                return;
            }

            for (int i = 0; i < _birds.Length; i++)
            {
                Transform bird = _birds[i];
                if (bird == null)
                {
                    continue;
                }

                _angle[i] += _speed[i] * deltaTime;

                float height = _height[i];
                if (_diveElapsed[i] >= 0f)
                {
                    _diveElapsed[i] += deltaTime;
                    float t = _diveElapsed[i] / _diveSeconds;
                    if (t >= 1f)
                    {
                        _diveElapsed[i] = -1f;
                    }
                    else
                    {
                        // Down fast and back up, lowest at the middle of the dive.
                        float depth = Mathf.Sin(t * Mathf.PI);
                        height = Mathf.Lerp(_height[i], _diveLowestHeight, depth);
                    }
                }
                else
                {
                    _diveCountdown[i] -= deltaTime;
                    if (_diveCountdown[i] <= 0f)
                    {
                        _diveElapsed[i] = 0f;
                        _diveCountdown[i] = PoiPlacement.Range(_random, _minDiveInterval, _maxDiveInterval);
                    }
                }

                float radians = _angle[i] * Mathf.Deg2Rad;
                bird.localPosition = new Vector3(Mathf.Cos(radians) * _radius[i], height, Mathf.Sin(radians) * _radius[i]);

                // Facing along the circle, the way it is flying.
                Vector3 tangent = new Vector3(-Mathf.Sin(radians), 0f, Mathf.Cos(radians)) * Mathf.Sign(_speed[i]);
                bird.localRotation = Quaternion.LookRotation(tangent, Vector3.up);

                Flap(bird, i);
            }
        }

        private void Disperse(float deltaTime)
        {
            _leaveElapsed += deltaTime;

            for (int i = 0; i < _birds.Length; i++)
            {
                Transform bird = _birds[i];
                if (bird == null)
                {
                    continue;
                }

                Vector3 step = _leaveDirection[i] * _leaveSpeed + Vector3.up * _leaveClimb;
                bird.localPosition += step * deltaTime;
                bird.localRotation = Quaternion.LookRotation(_leaveDirection[i], Vector3.up);
                Flap(bird, i);
            }

            if (_leaveElapsed >= _leaveSeconds)
            {
                for (int i = 0; i < _birds.Length; i++)
                {
                    if (_birds[i] != null)
                    {
                        _birds[i].gameObject.SetActive(false);
                    }
                }

                _hasHidden = true;
            }
        }

        private void Flap(Transform bird, int index)
        {
            if (bird.childCount < 2)
            {
                return;
            }

            float flap = Mathf.Sin(Time.time * _flapsPerSecond * Mathf.PI * 2f + _flapPhase[index]) * _flapDegrees;
            bird.GetChild(0).localRotation = Quaternion.Euler(0f, 0f, flap);
            bird.GetChild(1).localRotation = Quaternion.Euler(0f, 0f, -flap);
        }
    }
}
