using System;
using UnityEngine;

namespace FishingZone.Core
{
    /// <summary>
    /// Maps a <see cref="GameState"/> to the scene that represents it.
    /// Scene names live in data so renaming or re-targeting a scene never requires a code change,
    /// and so no system other than GameFlowManager ever needs to know a scene name at all.
    /// </summary>
    [CreateAssetMenu(fileName = "SceneCatalog", menuName = "Fishing Zone/Core/Scene Catalog")]
    public class SceneCatalog : ScriptableObject
    {
        [Serializable]
        private struct Entry
        {
            public GameState State;
            public string SceneName;
        }

        [SerializeField]
        private Entry[] _entries = Array.Empty<Entry>();

        /// <summary>
        /// Where a session goes when the lobby starts it: Port, to set sail from a jetty, or
        /// Expedition, to begin aboard the boat in a sea whose harbour is its shore.
        ///
        /// Kept beside the scene names because the two are one configuration. Pointing Expedition
        /// back at a sea without a harbour means setting this back to Port as well; nothing works it
        /// out, so both are changed here, together. Port by default, the route that always worked.
        /// </summary>
        [SerializeField]
        private GameState _sessionStartState = GameState.Port;

        /// <summary>
        /// The first gameplay state of a session. Only Port and Expedition can be one; anything else
        /// is said loudly and treated as Port rather than sending a crew nowhere.
        /// </summary>
        public GameState SessionStartState
        {
            get
            {
                if (IsValidSessionStart(_sessionStartState))
                {
                    return _sessionStartState;
                }

                GameLog.Error(LogCategory.Flow,
                    $"SceneCatalog '{name}' starts sessions in {_sessionStartState}, which is not somewhere a crew can begin. " +
                    "Starting in Port instead; set it to Port or Expedition.");
                return GameState.Port;
            }
        }

        /// <summary>Whether a session can begin in this state.</summary>
        public static bool IsValidSessionStart(GameState state)
        {
            return state == GameState.Port || state == GameState.Expedition;
        }

        /// <summary>
        /// Returns the scene name for a state, or null if the catalog has no entry for it.
        /// Callers are expected to treat null as a configuration error, not a normal case.
        /// </summary>
        public string GetSceneName(GameState state)
        {
            for (int i = 0; i < _entries.Length; i++)
            {
                if (_entries[i].State == state)
                {
                    return string.IsNullOrWhiteSpace(_entries[i].SceneName) ? null : _entries[i].SceneName;
                }
            }

            return null;
        }
    }
}
