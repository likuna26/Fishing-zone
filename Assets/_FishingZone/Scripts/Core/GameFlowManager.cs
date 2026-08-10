using System;
using System.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FishingZone.Core
{
    /// <summary>
    /// The only object in the project allowed to change scenes.
    /// Everything else asks for a state change and observes <see cref="StateChanged"/>,
    /// which keeps scene loading out of gameplay scripts and gives multiplayer a single
    /// place to switch to a host-driven network scene load later.
    /// </summary>
    public class GameFlowManager : MonoBehaviour
    {
        [SerializeField]
        private SceneCatalog _sceneCatalog;

        /// <summary>Raised after the target scene has finished loading and become active.</summary>
        public event Action<GameState> StateChanged;

        public GameState CurrentState { get; private set; } = GameState.Boot;

        public bool IsTransitioning { get; private set; }

        private static bool IsSessionActive =>
            NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening;

        private bool _isSceneEventHooked;

        private void Start()
        {
            if (NetworkManager.Singleton == null)
            {
                return;
            }

            NetworkManager.Singleton.OnServerStarted += HookSceneEvents;
            NetworkManager.Singleton.OnClientStarted += HookSceneEvents;
            NetworkManager.Singleton.OnServerStopped += HandleSessionStopped;
            NetworkManager.Singleton.OnClientStopped += HandleSessionStopped;
        }

        private void OnDestroy()
        {
            if (NetworkManager.Singleton == null)
            {
                return;
            }

            NetworkManager.Singleton.OnServerStarted -= HookSceneEvents;
            NetworkManager.Singleton.OnClientStarted -= HookSceneEvents;
            NetworkManager.Singleton.OnServerStopped -= HandleSessionStopped;
            NetworkManager.Singleton.OnClientStopped -= HandleSessionStopped;
        }

        public void GoTo(GameState target)
        {
            if (IsSessionActive && !NetworkManager.Singleton.IsServer)
            {
                GameLog.Warn(LogCategory.Flow, $"Ignored transition to {target}: only the host changes scene during a session.");
                return;
            }

            if (IsTransitioning)
            {
                GameLog.Warn(LogCategory.Flow, $"Ignored transition to {target}: a transition is already in progress.");
                return;
            }

            if (target == CurrentState)
            {
                GameLog.Warn(LogCategory.Flow, $"Ignored transition to {target}: already in that state.");
                return;
            }

            if (!IsTransitionAllowed(CurrentState, target))
            {
                GameLog.Error(LogCategory.Flow, $"Illegal transition {CurrentState} -> {target}.");
                return;
            }

            if (_sceneCatalog == null)
            {
                GameLog.Error(LogCategory.Flow, "No SceneCatalog assigned on GameFlowManager.");
                return;
            }

            string sceneName = _sceneCatalog.GetSceneName(target);
            if (sceneName == null)
            {
                GameLog.Error(LogCategory.Flow, $"SceneCatalog has no scene name for state {target}.");
                return;
            }

            StartCoroutine(TransitionRoutine(target, sceneName));
        }

        private static bool IsTransitionAllowed(GameState from, GameState to)
        {
            switch (from)
            {
                case GameState.Boot:
                    return to == GameState.MainMenu;
                case GameState.MainMenu:
                    return to == GameState.Lobby;
                case GameState.Lobby:
                    return to == GameState.Port || to == GameState.MainMenu;
                case GameState.Port:
                    return to == GameState.Expedition || to == GameState.MainMenu;
                case GameState.Expedition:
                    return to == GameState.Port || to == GameState.MainMenu;
                default:
                    return false;
            }
        }

        private IEnumerator TransitionRoutine(GameState target, string sceneName)
        {
            IsTransitioning = true;
            GameLog.Info(LogCategory.Flow, $"{CurrentState} -> {target} (loading scene '{sceneName}')");

            IEnumerator load = IsSessionActive
                ? LoadForSession(sceneName)
                : LoadLocally(sceneName);

            while (load.MoveNext())
            {
                yield return load.Current;
            }

            if (!IsTransitioning)
            {
                yield break;
            }

            CurrentState = target;
            IsTransitioning = false;
            GameLog.Info(LogCategory.Flow, $"Entered state {target}.");

            StateChanged?.Invoke(target);
        }

        private IEnumerator LoadLocally(string sceneName)
        {
            AsyncOperation load = SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Single);
            if (load == null)
            {
                GameLog.Error(LogCategory.Flow, $"Scene '{sceneName}' could not be loaded. Is it in the Build Profiles scene list?");
                IsTransitioning = false;
                yield break;
            }

            while (!load.isDone)
            {
                yield return null;
            }
        }

        private IEnumerator LoadForSession(string sceneName)
        {
            NetworkSceneManager sceneManager = NetworkManager.Singleton.SceneManager;
            if (sceneManager == null)
            {
                GameLog.Error(LogCategory.Flow, "Scene management is disabled on the NetworkManager, so the host cannot move the crew between scenes.");
                IsTransitioning = false;
                yield break;
            }

            SceneEventProgressStatus status = sceneManager.LoadScene(sceneName, LoadSceneMode.Single);
            if (status != SceneEventProgressStatus.Started)
            {
                GameLog.Error(LogCategory.Flow, $"Networked load of '{sceneName}' was refused: {status}.");
                IsTransitioning = false;
                yield break;
            }

            while (SceneManager.GetActiveScene().name != sceneName)
            {
                yield return null;
            }
        }

        private void HookSceneEvents()
        {
            if (_isSceneEventHooked || NetworkManager.Singleton == null || NetworkManager.Singleton.SceneManager == null)
            {
                return;
            }

            NetworkManager.Singleton.SceneManager.OnLoadComplete += HandleNetworkLoadComplete;
            _isSceneEventHooked = true;
        }

        private void HandleSessionStopped(bool isHost)
        {
            _isSceneEventHooked = false;
        }

        private void HandleNetworkLoadComplete(ulong clientId, string sceneName, LoadSceneMode loadSceneMode)
        {
            if (NetworkManager.Singleton == null
                || NetworkManager.Singleton.IsServer
                || clientId != NetworkManager.Singleton.LocalClientId)
            {
                return;
            }

            if (!TryGetStateForScene(sceneName, out GameState state) || state == CurrentState)
            {
                return;
            }

            CurrentState = state;
            IsTransitioning = false;
            GameLog.Info(LogCategory.Flow, $"Followed the host into {state}.");

            StateChanged?.Invoke(state);
        }

        private bool TryGetStateForScene(string sceneName, out GameState state)
        {
            if (_sceneCatalog != null)
            {
                foreach (GameState candidate in Enum.GetValues(typeof(GameState)))
                {
                    if (_sceneCatalog.GetSceneName(candidate) == sceneName)
                    {
                        state = candidate;
                        return true;
                    }
                }
            }

            state = GameState.Boot;
            return false;
        }
    }
}
