using FishingZone.Core;
using UnityEngine;

namespace FishingZone.Boat
{
    /// <summary>
    /// Where a crewmate stands when they arrive aboard.
    ///
    /// Bolted to the boat rather than to the scene. A spawn point placed in the world describes where
    /// the boat was when the scene loaded, and the boat does not stay there: a player joining a crew
    /// that has already sailed would arrive over open water. Points that are children of the hull
    /// move with it, so wherever the boat has got to, the deck is where they are.
    ///
    /// Not a NetworkBehaviour, and it needs nothing replicated. Every peer runs the same boat prefab,
    /// so the list and its order are identical everywhere, and a slot can be sent as a plain index:
    /// the server says which one, and each machine finds that point on its own copy of the hull.
    ///
    /// Deliberately not made of <see cref="FishingZone.Networking.PlayerSpawnPoint"/>s. Those
    /// register with the scene's list and describe places in the world, which is still what the
    /// port wants; mixing the two would let a deck position be handed out as a fixed one.
    ///
    /// One per scene, reachable without a reference: what asks about it is spawned by code and has
    /// no Inspector to be wired in.
    /// </summary>
    public class BoatBoardingPoints : MonoBehaviour
    {
        /// <summary>No slot: the player was not placed aboard. Also what a fresh player holds.</summary>
        public const int NoSlot = -1;

        private static BoatBoardingPoints _current;

        public static BoatBoardingPoints Current => _current;

        /// <summary>
        /// In order of preference, so the first crewmate aboard gets the best place to stand and a
        /// short crew never ends up in the corners. Each must leave room for a whole player clear of
        /// every solid collider on deck.
        /// </summary>
        [SerializeField]
        private Transform[] _points;

        public int Count => _points != null ? _points.Length : 0;

        // Static state outlives a play session when domain reload is disabled, exactly as the other
        // scene registries in this project do.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetOnPlay()
        {
            _current = null;
        }

        private void OnEnable()
        {
            if (_current != null && _current != this)
            {
                GameLog.Error(LogCategory.Network,
                    $"'{name}' is a second boat with boarding points in this scene; '{_current.name}' already has them. " +
                    "Crew will board the first one.");
                return;
            }

            _current = this;
        }

        private void OnDisable()
        {
            if (_current == this)
            {
                _current = null;
            }
        }

        /// <summary>
        /// Where a slot is right now, on this machine's copy of the boat. The heading is kept flat,
        /// so a hull caught mid-roll does not spawn somebody leaning.
        /// </summary>
        public bool TryGetPose(int slot, out Vector3 position, out Quaternion rotation)
        {
            position = default;
            rotation = Quaternion.identity;

            if (slot < 0 || slot >= Count || _points[slot] == null)
            {
                return false;
            }

            Transform point = _points[slot];
            position = point.position;

            Vector3 heading = Vector3.ProjectOnPlane(point.forward, Vector3.up);
            if (heading.sqrMagnitude > 0.0001f)
            {
                rotation = Quaternion.LookRotation(heading, Vector3.up);
            }

            return true;
        }

        private void OnDrawGizmos()
        {
            if (_points == null)
            {
                return;
            }

            Gizmos.color = Color.green;
            for (int i = 0; i < _points.Length; i++)
            {
                if (_points[i] == null)
                {
                    continue;
                }

                Gizmos.DrawWireSphere(_points[i].position, 0.5f);
                Gizmos.DrawRay(_points[i].position, _points[i].forward);
            }
        }
    }
}
