using FishingZone.Core.Input;
using FishingZone.Networking;
using UnityEngine;

namespace FishingZone.Core
{
    /// <summary>
    /// Startup entry point. It makes the persistent services survive scene loads, publishes them,
    /// and hands control to <see cref="GameFlowManager"/>. It deliberately holds no gameplay state:
    /// systems are added to the services object, not to this class.
    /// </summary>
    public class Bootstrap : MonoBehaviour
    {
        [SerializeField]
        private GameFlowManager _gameFlow;

        [SerializeField]
        private GameInput _gameInput;

        /// <summary>
        /// Optional on purpose. Without it the game still boots and plays locally, it simply never
        /// opens a session, which keeps a missing reference from bricking startup entirely.
        /// </summary>
        [SerializeField]
        private SessionManager _sessionManager;

        /// <summary>Guards against a second Bootstrap scene load creating a duplicate set of services.</summary>
        private static bool _hasInitialized;

        /// <summary>
        /// True only on the instance that actually registered the services, so a duplicate
        /// Bootstrap destroying itself cannot unregister the live ones.
        /// </summary>
        private bool _isServiceOwner;

        // Static state survives play sessions when domain reload is disabled, so it is reset per run.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetOnPlay()
        {
            _hasInitialized = false;
        }

        private void Awake()
        {
            if (_hasInitialized)
            {
                GameLog.Warn(LogCategory.Boot, "Services already initialized; destroying the duplicate Bootstrap object.");
                Destroy(gameObject);
                return;
            }

            if (_gameFlow == null || _gameInput == null)
            {
                GameLog.Error(LogCategory.Boot, "Bootstrap is missing a service reference. Assign GameFlowManager and GameInput in the Inspector.");
                return;
            }

            _hasInitialized = true;
            _isServiceOwner = true;
            DontDestroyOnLoad(gameObject);

            ServiceRegistry.Register(_gameFlow);
            ServiceRegistry.Register(_gameInput);

            if (_sessionManager != null)
            {
                ServiceRegistry.Register(_sessionManager);
            }

            GameLog.Info(LogCategory.Boot, "Persistent services initialized.");
        }

        private void Start()
        {
            if (!_isServiceOwner)
            {
                return;
            }

            // The menu is driven by UI input only; other maps stay off until a scene asks for them.
            _gameInput.EnableMap(InputMap.UI);

            // No session is opened here. Hosting at boot meant every copy of the game claimed the
            // transport's port the moment it started, so a second instance on the same machine
            // could not bind and whichever launched last was left unable to host at all.
            //
            // The crew is opened by Create Crew instead, which is the first thing a player does and
            // is still not a NetworkManager button. Solo play remains a host of one; it simply
            // becomes one a moment later, and there is still no separate offline path.
            _gameFlow.GoTo(GameState.MainMenu);
        }

        private void OnDestroy()
        {
            if (!_isServiceOwner)
            {
                return;
            }

            ServiceRegistry.Unregister<GameFlowManager>();
            ServiceRegistry.Unregister<GameInput>();
            ServiceRegistry.Unregister<SessionManager>();
            _hasInitialized = false;
        }
    }
}
