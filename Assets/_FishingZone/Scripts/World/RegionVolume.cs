using System.Collections.Generic;
using FishingZone.Core;
using UnityEngine;

namespace FishingZone.World
{
    /// <summary>
    /// Where a region is: a rectangle of sea, turned with this object's heading.
    ///
    /// Not a NetworkBehaviour, and it needs no NetworkObject, for the reason a fishing ground needs
    /// none: it is a shape placed in a scene every peer loads, and the boat's position already
    /// travels. Every machine works out which region the boat is in for itself, gets the same
    /// answer, and nothing is sent.
    ///
    /// Measured flat. Height is thrown away, as it is for the grounds: a region is an area of sea,
    /// and how high a hull rides or a station sits has nothing to say about which one it is.
    ///
    /// Regions may overlap. The one with the higher priority wins, so a small place can sit inside a
    /// large one; equal priorities are settled by the lower region id. That makes the answer depend
    /// only on what the regions are, never on the order a scene happened to load them in, so two
    /// peers cannot disagree about a point both of them can see. One region may be built from
    /// several volumes that name the same definition.
    ///
    /// Outside every volume is open sea, which is an answer and not a failure: the query returns
    /// null, and a scene with no regions at all behaves exactly as it did before regions existed.
    /// </summary>
    public class RegionVolume : MonoBehaviour
    {
        private static readonly List<RegionVolume> Registered = new List<RegionVolume>();

        [SerializeField]
        private RegionDefinition _region;

        /// <summary>Width (X) and length (Z) of the rectangle in metres, before this object's heading.</summary>
        [SerializeField]
        private Vector2 _size = new Vector2(50f, 50f);

        /// <summary>Higher wins where volumes overlap.</summary>
        [SerializeField]
        private int _priority;

        public RegionDefinition Region => _region;

        public int Priority => _priority;

        public Vector2 Size => _size;

        /// <summary>Whether this scene marks out any regions at all.</summary>
        public static bool AnyExist => Registered.Count > 0;

        // The list is static, so it outlives a play session when domain reload is disabled.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetOnPlay()
        {
            Registered.Clear();
        }

        private void OnEnable()
        {
            if (_region == null || !_region.IsValid)
            {
                GameLog.Error(LogCategory.Flow,
                    $"'{name}' has no usable Region Definition (it needs a non-zero Id and a Display Name), " +
                    "so no point is ever inside it.");
            }
            else
            {
                WarnOnDuplicateId();
            }

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
        /// Two different definitions sharing an id would be two regions the rest of the game cannot
        /// tell apart, so it is said loudly. The same definition on several volumes is one region
        /// built from several shapes, and is fine.
        /// </summary>
        private void WarnOnDuplicateId()
        {
            for (int i = 0; i < Registered.Count; i++)
            {
                RegionDefinition other = Registered[i] != null ? Registered[i]._region : null;
                if (other != null && other != _region && other.Id == _region.Id)
                {
                    GameLog.Error(LogCategory.Flow,
                        $"Region id {_region.Id} is used by both '{_region.name}' and '{other.name}'. " +
                        "Region ids must be unique; give one of them a new id.");
                    return;
                }
            }
        }

        /// <summary>The region this point is in, or null for open sea.</summary>
        public static RegionDefinition FindRegion(Vector3 worldPosition)
        {
            RegionVolume volume = Find(worldPosition);
            return volume != null ? volume._region : null;
        }

        /// <summary>
        /// The winning volume containing this point, or null. Highest priority first, then lowest
        /// region id, so the result is the same whatever order the volumes registered in.
        ///
        /// A walk of a short list; regions are few and large. If that ever stops being true, this
        /// is the one place an index goes.
        /// </summary>
        public static RegionVolume Find(Vector3 worldPosition)
        {
            RegionVolume best = null;

            for (int i = 0; i < Registered.Count; i++)
            {
                RegionVolume volume = Registered[i];
                if (volume == null || volume._region == null || !volume._region.IsValid
                    || !volume.Contains(worldPosition))
                {
                    continue;
                }

                if (best == null
                    || volume._priority > best._priority
                    || (volume._priority == best._priority && volume._region.Id < best._region.Id))
                {
                    best = volume;
                }
            }

            return best;
        }

        /// <summary>Whether this point is inside the rectangle, measured flat.</summary>
        public bool Contains(Vector3 worldPosition)
        {
            Vector3 delta = worldPosition - transform.position;
            delta.y = 0f;

            Vector3 local = Quaternion.Inverse(Quaternion.Euler(0f, transform.eulerAngles.y, 0f)) * delta;

            return Mathf.Abs(local.x) <= _size.x * 0.5f && Mathf.Abs(local.z) <= _size.y * 0.5f;
        }

        /// <summary>Drawn so a region can be sized against the map. Unity never calls this in a build.</summary>
        private void OnDrawGizmos()
        {
            Gizmos.color = new Color(1f, 0.8f, 0.2f, 0.8f);
            Gizmos.matrix = Matrix4x4.TRS(transform.position, Quaternion.Euler(0f, transform.eulerAngles.y, 0f), Vector3.one);
            Gizmos.DrawWireCube(Vector3.zero, new Vector3(_size.x, 0.5f, _size.y));
            Gizmos.matrix = Matrix4x4.identity;
        }
    }
}
