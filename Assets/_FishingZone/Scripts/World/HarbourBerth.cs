using System.Collections.Generic;
using UnityEngine;

namespace FishingZone.World
{
    /// <summary>
    /// Where a boat stands in relation to a berth: elsewhere, there but still moving, or lying
    /// alongside.
    /// </summary>
    public enum BerthStatus
    {
        Away = 0,
        TooFast = 1,
        Berthed = 2
    }

    /// <summary>
    /// The stretch of water alongside the quay where a voyage ends.
    ///
    /// A voyage used to end wherever the Navigator happened to be standing, so the sail home was a
    /// button rather than a passage. This is the far end of that passage: the crew bring the boat in
    /// through the breakwater and lay her alongside, and only then can they step ashore.
    ///
    /// A strip rather than a circle, because a quay is a straight edge. Along it the boat may lie
    /// anywhere within the berth's length and still look alongside; out from it, a few metres is
    /// the difference between moored and merely nearby. Placed on the quay face itself, with its
    /// forward pointing out to sea, and measured to the boat's centre.
    ///
    /// Not a NetworkBehaviour, and it needs no NetworkObject, for the reason a fishing ground needs
    /// none: it is a place in a scene every peer loads, and the boat's position already travels.
    /// Every peer can work out whether the boat is berthed for its own prompt; only the server's
    /// answer is allowed to end a voyage.
    ///
    /// Measured flat, like the grounds and regions. Nothing here holds the boat, moves it or
    /// guides it in — the Navigator does the berthing.
    /// </summary>
    public class HarbourBerth : MonoBehaviour
    {
        private static readonly List<HarbourBerth> Registered = new List<HarbourBerth>();

        /// <summary>How far the berth runs along the quay, in metres, centred on this object.</summary>
        [SerializeField]
        private float _lengthAlongQuay = 24f;

        /// <summary>
        /// How far out from the quay face the boat's centre may lie and still count as alongside,
        /// in metres. Measured to the centre, so half a hull's width or length of it is boat.
        /// </summary>
        [SerializeField]
        private float _depthFromQuay = 8f;

        /// <summary>
        /// The fastest the boat may still be moving and count as berthed, in metres per second.
        /// A crew coming alongside at speed has not arrived, they are passing.
        /// </summary>
        [SerializeField]
        private float _maxSpeed = 1.5f;

        public float LengthAlongQuay => _lengthAlongQuay;

        public float DepthFromQuay => _depthFromQuay;

        public float MaxSpeed => _maxSpeed;

        /// <summary>
        /// Whether this scene has a berth at all. A scene without one ends a voyage anywhere, as
        /// every scene did before berths existed.
        /// </summary>
        public static bool AnyExist => Registered.Count > 0;

        // The list is static, so it outlives a play session when domain reload is disabled.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetOnPlay()
        {
            Registered.Clear();
        }

        private void OnEnable()
        {
            if (!Registered.Contains(this))
            {
                Registered.Add(this);
            }
        }

        private void OnDisable()
        {
            Registered.Remove(this);
        }

        /// <summary>
        /// How a boat at this position, moving this fast, stands against every berth in the scene.
        /// Berthed at any of them wins; inside one but moving beats being nowhere near any.
        /// </summary>
        public static BerthStatus StatusOf(Vector3 boatPosition, float boatSpeed)
        {
            BerthStatus best = BerthStatus.Away;

            for (int i = 0; i < Registered.Count; i++)
            {
                HarbourBerth berth = Registered[i];
                if (berth == null)
                {
                    continue;
                }

                Transform at = berth.transform;
                BerthStatus status = Evaluate(at.position, at.eulerAngles.y, berth._lengthAlongQuay,
                    berth._depthFromQuay, berth._maxSpeed, boatPosition, boatSpeed);
                if (status > best)
                {
                    best = status;
                }
            }

            return best;
        }

        /// <summary>
        /// The rule itself, apart from any scene. The quay face runs through the berth's position
        /// across its heading; the boat's centre must lie within half the length to either side
        /// and between the face and the depth out from it, measured flat, and the boat must be
        /// moving no faster than the limit. Every edge counts as inside.
        /// </summary>
        public static BerthStatus Evaluate(Vector3 berthPosition, float berthHeading, float lengthAlongQuay,
            float depthFromQuay, float maxSpeed, Vector3 boatPosition, float boatSpeed)
        {
            Vector3 delta = boatPosition - berthPosition;
            delta.y = 0f;

            Vector3 local = Quaternion.Inverse(Quaternion.Euler(0f, berthHeading, 0f)) * delta;

            if (Mathf.Abs(local.x) > lengthAlongQuay * 0.5f || local.z < 0f || local.z > depthFromQuay)
            {
                return BerthStatus.Away;
            }

            return boatSpeed <= maxSpeed ? BerthStatus.Berthed : BerthStatus.TooFast;
        }

        /// <summary>Drawn so the berth can be judged against the quay. Unity never calls this in a build.</summary>
        private void OnDrawGizmos()
        {
            Gizmos.color = new Color(0.3f, 1f, 0.4f, 0.9f);
            Gizmos.matrix = Matrix4x4.TRS(transform.position, Quaternion.Euler(0f, transform.eulerAngles.y, 0f), Vector3.one);
            Gizmos.DrawWireCube(new Vector3(0f, 0f, _depthFromQuay * 0.5f), new Vector3(_lengthAlongQuay, 0.5f, _depthFromQuay));
            Gizmos.matrix = Matrix4x4.identity;
        }
    }
}
