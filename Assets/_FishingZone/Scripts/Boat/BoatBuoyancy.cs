using FishingZone.Core;
using UnityEngine;

namespace FishingZone.Boat
{
    /// <summary>
    /// Keeps the hull floating by pushing up at several points, which also makes it roll and pitch
    /// naturally instead of hovering rigidly. Not a fluid simulation: the goal is a boat that feels
    /// slightly heavy and settles believably, per the MVP's "controlled chaos" direction.
    ///
    /// Water height is read only through <see cref="SampleWaterHeight"/>. Everything else in the
    /// project stays ignorant of how water works, so replacing the flat placeholder with real waves
    /// later means changing that one method and nothing else.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class BoatBuoyancy : MonoBehaviour
    {
        [SerializeField]
        private Transform[] _floatPoints;

        /// <summary>World Y of the flat placeholder water surface.</summary>
        [SerializeField]
        private float _waterLevel;

        /// <summary>How deep a point must be before it produces full lift. Softens the surface.</summary>
        [SerializeField]
        private float _fullSubmersionDepth = 1f;

        /// <summary>
        /// Applied as acceleration rather than force, so tuning does not have to be redone every time
        /// the hull's mass changes. Must exceed gravity for the boat to rise.
        /// </summary>
        [SerializeField]
        private float _buoyancyAcceleration = 25f;

        [SerializeField]
        private float _airDrag = 0.05f;

        [SerializeField]
        private float _waterDrag = 2f;

        [SerializeField]
        private float _airAngularDrag = 0.05f;

        [SerializeField]
        private float _waterAngularDrag = 3f;

        private Rigidbody _rigidbody;
        private bool _isConfigured;

        private void Awake()
        {
            _rigidbody = GetComponent<Rigidbody>();

            _isConfigured = _floatPoints != null && _floatPoints.Length > 0;
            if (!_isConfigured)
            {
                GameLog.Error(LogCategory.Boot, "BoatBuoyancy has no float points assigned. Add at least three so the hull can roll and pitch.");
            }
        }

        private void FixedUpdate()
        {
            if (!_isConfigured)
            {
                return;
            }

            // Guarded because a depth of zero would divide by zero and produce NaN forces.
            float submersionDepth = Mathf.Max(_fullSubmersionDepth, 0.01f);
            float totalSubmersion = 0f;

            for (int i = 0; i < _floatPoints.Length; i++)
            {
                Transform point = _floatPoints[i];
                if (point == null)
                {
                    continue;
                }

                float depth = SampleWaterHeight(point.position) - point.position.y;
                float submersion = Mathf.Clamp01(depth / submersionDepth);
                totalSubmersion += submersion;

                if (submersion <= 0f)
                {
                    continue;
                }

                float lift = _buoyancyAcceleration * submersion / _floatPoints.Length;
                _rigidbody.AddForceAtPosition(Vector3.up * lift, point.position, ForceMode.Acceleration);
            }

            // Blending damping by how submerged the hull is stops it oscillating forever, and keeps
            // it responsive in the air if it is ever dropped in or launched off a wave.
            float averageSubmersion = totalSubmersion / _floatPoints.Length;
            _rigidbody.linearDamping = Mathf.Lerp(_airDrag, _waterDrag, averageSubmersion);
            _rigidbody.angularDamping = Mathf.Lerp(_airAngularDrag, _waterAngularDrag, averageSubmersion);
        }

        /// <summary>
        /// The single seam between the boat and the water implementation.
        /// Flat placeholder today; override or replace this body to add waves without touching
        /// buoyancy, movement or anything else.
        /// </summary>
        protected virtual float SampleWaterHeight(Vector3 worldPosition)
        {
            return _waterLevel;
        }

        private void OnDrawGizmosSelected()
        {
            if (_floatPoints == null)
            {
                return;
            }

            Gizmos.color = Color.cyan;
            for (int i = 0; i < _floatPoints.Length; i++)
            {
                if (_floatPoints[i] != null)
                {
                    Gizmos.DrawWireSphere(_floatPoints[i].position, 0.2f);
                }
            }
        }
    }
}
