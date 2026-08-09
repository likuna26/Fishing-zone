using FishingZone.Core;
using FishingZone.Core.Input;
using UnityEngine;

namespace FishingZone.Player
{
    /// <summary>
    /// Keeps the Player action map enabled for as long as this player is active in the scene.
    /// It exists so that no other player component has to care about input context: movement,
    /// look and interaction just read their actions and assume they are live.
    /// This is also where the Owner check belongs once Netcode arrives, since only the local
    /// player may enable input (Technical Specification section 10).
    /// </summary>
    public class PlayerInputContext : MonoBehaviour
    {
        /// <summary>
        /// Cached at enable time so teardown can still disable the map: during scene unload or
        /// application quit the registry may already have been cleared.
        /// </summary>
        private GameInput _gameInput;

        private void OnEnable()
        {
            _gameInput = ServiceRegistry.Get<GameInput>();
            if (_gameInput == null)
            {
                // ServiceRegistry has already logged why, which is almost always
                // that play started from this scene instead of Bootstrap.
                return;
            }

            _gameInput.EnableMap(InputMap.Player);
        }

        private void OnDisable()
        {
            if (_gameInput == null)
            {
                return;
            }

            _gameInput.DisableMap(InputMap.Player);
            _gameInput = null;
        }
    }
}
