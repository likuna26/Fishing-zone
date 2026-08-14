using FishingZone.Core;
using FishingZone.Networking;
using UnityEngine;

namespace FishingZone.UI
{
    /// <summary>
    /// Button actions for the Main Menu screen.
    /// It asks <see cref="GameFlowManager"/> for a state change and never touches SceneManager,
    /// so the menu holds no knowledge of which scene comes next, and it asks
    /// <see cref="SessionManager"/> about the session rather than touching NetworkManager.
    ///
    /// The two crew actions are asymmetric on purpose. Every instance already opened a host of one
    /// during boot, so creating a crew has nothing to start and only needs to move to the lobby,
    /// while joining one has to give up that host first and then wait to be led by whoever is
    /// hosting.
    /// </summary>
    public class MainMenuUI : MonoBehaviour
    {
        /// <summary>
        /// Kept so existing PLAY buttons keep working. Creating a crew is what starting a game has
        /// always meant here: solo play is a crew of one.
        /// </summary>
        public void Play()
        {
            CreateCrew();
        }

        /// <summary>
        /// Wired to the CREATE CREW button's On Click () list, which is why this is public.
        /// Managers are resolved per click rather than cached: they live on the persistent services
        /// object from the Bootstrap scene, and Unity cannot serialize a reference across scenes.
        /// </summary>
        public void CreateCrew()
        {
            SessionManager session = ServiceRegistry.Get<SessionManager>();

            // This is where the crew actually opens. Nothing hosts at boot, because a copy that
            // claims the transport port on startup stops any second copy on the same machine from
            // hosting at all. Guarded rather than unconditional so pressing it twice is harmless.
            if (session != null && !session.IsSessionActive)
            {
                session.StartHost();
            }

            GameFlowManager flow = ServiceRegistry.Get<GameFlowManager>();
            if (flow == null)
            {
                // ServiceRegistry has already logged why, which is almost always
                // that play started from this scene instead of Bootstrap.
                return;
            }

            flow.GoTo(GameState.Lobby);
        }

        /// <summary>
        /// Wired to the JOIN CREW button's On Click () list.
        /// No scene change is requested here: shared transitions belong to the host, and this client
        /// follows it into whichever scene the crew is already in.
        /// </summary>
        public void JoinCrew()
        {
            SessionManager session = ServiceRegistry.Get<SessionManager>();
            if (session == null)
            {
                return;
            }

            session.JoinAsClient();
        }
    }
}
