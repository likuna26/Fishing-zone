using System.Collections.Generic;
using UnityEngine;

namespace FishingZone.Fishing
{
    /// <summary>
    /// A patch of water worth putting a line into.
    ///
    /// The first thing in this project that makes where the boat is mean anything. Until now a crew
    /// could park at the spawn point and fish exactly as well as one that had sailed for a minute,
    /// which left the wheel — the most elaborate station aboard — decorative the moment the port was
    /// out of sight. A ground gives the Navigator an arrival to steer for and the rest of the crew a
    /// reason to wait for them.
    ///
    /// Deliberately not a NetworkBehaviour, and it needs no NetworkObject. There is nothing here to
    /// replicate: a ground is a position and a radius, placed in the scene, and every machine loads
    /// the same scene. The boat's transform already travels, so both ends of the question are on
    /// every peer already and the answer comes out the same on all of them. A NetworkVariable here
    /// would send a number that never changes.
    ///
    /// Grounds register themselves as they load, so nothing has to search the scene, and the list is
    /// static for the same reason PlayerSpawnPoint's is: it must be reachable before anything that
    /// asks about it exists.
    /// </summary>
    public class FishingGround : MonoBehaviour
    {
        private static readonly List<FishingGround> Registered = new List<FishingGround>();

        /// <summary>
        /// Whether this scene marks out any water at all.
        ///
        /// Asked so that a scene nobody has wired yet fishes the way it did before grounds existed.
        /// The alternative — no grounds meaning no fishing anywhere — would turn one forgotten
        /// object into a level that silently cannot be played.
        /// </summary>
        public static bool AnyExist => Registered.Count > 0;

        /// <summary>
        /// What this water is called. Not shown anywhere yet; kept because a scene with three of
        /// these is otherwise three identical objects in a hierarchy, and naming the water is how a
        /// crew will eventually be told which one they are over.
        /// </summary>
        [SerializeField]
        private string _displayName = "the fishing grounds";

        /// <summary>
        /// How far the good water reaches, in metres. Tens rather than units: a boat holding station
        /// to the centimetre is not a game, and the gizmo is there so this can be judged against the
        /// map rather than guessed.
        /// </summary>
        [SerializeField]
        private float _radius = 30f;

        public string DisplayName => _displayName;

        public float Radius => _radius;

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
        /// The ground this point is over, or null for open water.
        ///
        /// The first that contains it rather than the nearest. Overlapping grounds are a scene
        /// mistake rather than a feature, and picking between them would invent a rule to cover one.
        ///
        /// A walk of a list that holds a handful of entries, asked once when somebody casts. There is
        /// nothing here worth caching, and a cached answer would have to be given up every time the
        /// boat moved.
        /// </summary>
        public static FishingGround Find(Vector3 worldPosition)
        {
            for (int i = 0; i < Registered.Count; i++)
            {
                FishingGround ground = Registered[i];
                if (ground != null && ground.Contains(worldPosition))
                {
                    return ground;
                }
            }

            return null;
        }

        /// <summary>
        /// Whether this point is over the ground, measured flat.
        ///
        /// Height is thrown away on purpose. A hull rides up and down on the water, a station may sit
        /// on a deck or up a mast, and none of that should decide whether there are fish below. What
        /// is being asked is where the boat is, not how high.
        ///
        /// Compared as squares, so nothing takes a square root to answer a yes or no.
        /// </summary>
        public bool Contains(Vector3 worldPosition)
        {
            Vector3 delta = worldPosition - transform.position;
            delta.y = 0f;

            return delta.sqrMagnitude <= _radius * _radius;
        }

        /// <summary>
        /// Drawn so the water can be sized against the map rather than typed in blind.
        ///
        /// A sphere because that is what Gizmos draws; read it as the column it really is, since the
        /// test above ignores height entirely. Unity never calls this in a build.
        /// </summary>
        private void OnDrawGizmos()
        {
            Gizmos.color = new Color(0.2f, 0.6f, 1f, 0.9f);
            Gizmos.DrawWireSphere(transform.position, _radius);
        }
    }
}
