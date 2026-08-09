using FishingZone.Core;
using FishingZone.Player;
using TMPro;
using UnityEngine;

namespace FishingZone.UI
{
    /// <summary>
    /// Shows the interaction prompt for whatever the player is currently looking at.
    /// Driven entirely by <see cref="PlayerInteraction.FocusChanged"/>: the prompt is rebuilt only
    /// when the target actually changes, never polled per frame (Technical Specification section 41).
    /// The text itself always comes from the interactable, so this class never describes any object.
    /// </summary>
    public class InteractionPromptUI : MonoBehaviour
    {
        [SerializeField]
        private PlayerInteraction _playerInteraction;

        /// <summary>Object switched on and off to reveal the prompt. Usually the text's parent panel.</summary>
        [SerializeField]
        private GameObject _promptRoot;

        [SerializeField]
        private TMP_Text _promptText;

        private bool _isConfigured;

        private void Awake()
        {
            _isConfigured = _playerInteraction != null && _promptRoot != null && _promptText != null;
            if (!_isConfigured)
            {
                GameLog.Error(LogCategory.UI, "InteractionPromptUI is missing a Player Interaction, Prompt Root or Prompt Text reference. Assign all three in the Inspector.");
            }
        }

        private void OnEnable()
        {
            if (!_isConfigured)
            {
                return;
            }

            _playerInteraction.FocusChanged += OnFocusChanged;

            // The player may already be looking at something by the time this enables,
            // so adopt the current target rather than waiting for the next change.
            OnFocusChanged(_playerInteraction.CurrentTarget);
        }

        private void OnDisable()
        {
            if (_isConfigured)
            {
                _playerInteraction.FocusChanged -= OnFocusChanged;
            }
        }

        private void OnFocusChanged(IInteractable target)
        {
            if (target == null)
            {
                _promptRoot.SetActive(false);
                return;
            }

            _promptText.text = target.GetInteractionText(_playerInteraction.gameObject);
            _promptRoot.SetActive(true);
        }
    }
}
