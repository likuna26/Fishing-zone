using System.Collections.Generic;
using FishingZone.Core;
using FishingZone.World;
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

        /// <summary>
        /// What lives here, and when and how often: the spawn tables this water is fished from.
        ///
        /// Several are allowed so a ground can share a table with its neighbours and keep one of its
        /// own, which is how two grounds in the same region come to differ. The rules — weights,
        /// hours, regions — live in the tables, never here, so this stays a description of a place.
        ///
        /// Left empty on purpose is a supported arrangement rather than a mistake: a ground with no
        /// tables is fished from the station's own list, which is how every ground behaved before
        /// there was such a thing as habitat. A ground WITH tables that rule everything out at this
        /// hour is different — that is water where nothing is feeding, and nothing bites.
        /// </summary>
        [SerializeField]
        private FishSpawnTable[] _spawnTables;

        /// <summary>
        /// Whether this water may lie outside every region on purpose.
        ///
        /// Off for anything placed by hand, where open sea almost always means a region volume drawn
        /// too small. On for water that comes and goes — a point of interest found on the way
        /// somewhere — whose whole reason to exist is the sea between the regions.
        /// </summary>
        [SerializeField]
        private bool _mayLieInOpenSea;

        public string DisplayName => _displayName;

        /// <summary>
        /// Whether a new line may still go into this water. Always true for permanent grounds.
        ///
        /// Turned off by water that is going away, so nobody starts something it would have to cut
        /// short. A line already out is not affected: it finishes where it began. Set on every peer
        /// by whatever owns the water, so a Fisher's prompt and the server's refusal agree.
        /// </summary>
        public bool AcceptsNewLines => _acceptsNewLines;

        private bool _acceptsNewLines = true;

        /// <summary>
        /// Stops new lines going into this water, for good. There is no way back, because nothing
        /// that closes a ground means to open it again.
        /// </summary>
        public void CloseToNewLines()
        {
            _acceptsNewLines = false;
        }

        public float Radius => _radius;

        /// <summary>
        /// The tables this ground is fished from, for reading only. Choosing from them belongs to
        /// <see cref="CatchSelector"/>, never to the place.
        /// </summary>
        public IReadOnlyList<FishSpawnTable> SpawnTables => _spawnTables;

        /// <summary>
        /// The region this ground belongs to: whichever region its centre lies in, or null if it lies
        /// in open sea or the scene has no regions.
        ///
        /// Worked out from where the ground is rather than dragged in, so a ground and its region
        /// cannot be separated by an Inspector reference nobody re-checked — the same reasoning that
        /// ties a ground's water to it. Grounds do not move, so it is settled once and kept.
        ///
        /// A ground that straddles two regions belongs to the one holding its centre. Nothing reads
        /// this for fishing yet; what is caught still comes from the ground's own list.
        /// </summary>
        public RegionDefinition Region
        {
            get
            {
                if (!_hasResolvedRegion)
                {
                    _region = RegionVolume.FindRegion(transform.position);
                    _hasResolvedRegion = true;
                }

                return _region;
            }
        }

        private RegionDefinition _region;

        private bool _hasResolvedRegion;

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
            _hasResolvedRegion = false;
        }

        /// <summary>
        /// Settles the region once every volume in the scene has registered, which is after every
        /// OnEnable and therefore here. A ground left outside every region of a scene that has them
        /// is almost certainly a volume drawn too small, so it is said once, loudly.
        /// </summary>
        private void Start()
        {
            _hasResolvedRegion = false;

            if (RegionVolume.AnyExist && Region == null && !_mayLieInOpenSea)
            {
                GameLog.Error(LogCategory.Fish,
                    $"Fishing ground '{name}' lies outside every region in this scene. " +
                    "Extend a Region Volume over it, or move it.");
            }
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
