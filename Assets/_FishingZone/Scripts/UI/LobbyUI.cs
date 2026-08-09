using FishingZone.Core;
using UnityEngine;

namespace FishingZone.UI
{
    /// <summary>
    /// Button actions for the Lobby screen.
    /// It asks <see cref="GameFlowManager"/> for a state change and never touches SceneManager,
    /// so the lobby holds no knowledge of which scene comes next.
    /// </summary>
    public class LobbyUI : MonoBehaviour
    {
        /// <summary>
        /// Wired to the START button's On Click () list, which is why this is public.
        /// The manager is resolved per click rather than cached: it lives on the persistent
        /// services object from the Bootstrap scene, and Unity cannot serialize a reference
        /// across scenes. A dictionary lookup on a button press costs nothing.
        /// </summary>
        public void StartGame()
        {
            GoTo(GameState.Port);
        }

        /// <summary>Wired to the BACK button's On Click () list.</summary>
        public void Back()
        {
            GoTo(GameState.MainMenu);
        }

        private static void GoTo(GameState target)
        {
            GameFlowManager flow = ServiceRegistry.Get<GameFlowManager>();
            if (flow == null)
            {
                // ServiceRegistry has already logged why, which is almost always
                // that play started from this scene instead of Bootstrap.
                return;
            }

            flow.GoTo(target);
        }
    }
}
