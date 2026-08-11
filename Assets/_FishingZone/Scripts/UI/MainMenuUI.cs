using FishingZone.Core;
using FishingZone.Networking;
using UnityEngine;

namespace FishingZone.UI
{
    /// <summary>
    /// Button actions for the Main Menu screen.
    /// It asks GameFlowManager for scene transitions and SessionManager for session lifecycle,
    /// without touching SceneManager or NetworkManager directly.
    /// </summary>
    public class MainMenuUI : MonoBehaviour
    {
        public void Play()
        {
            CreateCrew();
        }

        public void CreateCrew()
        {
            SessionManager session = ServiceRegistry.Get<SessionManager>();
            if (session != null && !session.IsSessionActive)
            {
                session.StartHost();
            }

            GameFlowManager flow = ServiceRegistry.Get<GameFlowManager>();
            if (flow == null)
            {
                return;
            }

            flow.GoTo(GameState.Lobby);
        }

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
