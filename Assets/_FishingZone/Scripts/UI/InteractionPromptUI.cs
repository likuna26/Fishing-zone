using System.Collections;
using FishingZone.Core;
using FishingZone.Player;
using TMPro;
using Unity.Netcode;
using UnityEngine;

namespace FishingZone.UI
{
    /// <summary>
    /// Shows the interaction prompt for whatever the local player is currently looking at.
    /// The prompt references remain scene-owned, while the PlayerInteraction may arrive later as a
    /// network-spawned player object.
    /// </summary>
    public class InteractionPromptUI : MonoBehaviour
    {
        [SerializeField]
        private PlayerInteraction _playerInteraction;

        [SerializeField]
        private GameObject _promptRoot;

        [SerializeField]
        private TMP_Text _promptText;

        private bool _isUiConfigured;
        private bool _isBound;
        private Coroutine _bindRoutine;

        private void Awake()
        {
            _isUiConfigured = _promptRoot != null && _promptText != null;
            if (!_isUiConfigured)
            {
                GameLog.Error(LogCategory.UI,
                    "InteractionPromptUI is missing a Prompt Root or Prompt Text reference. Assign both in the Inspector.");
                return;
            }

            _promptRoot.SetActive(false);
        }

        private void OnEnable()
        {
            if (!_isUiConfigured)
            {
                return;
            }

            if (TryBindPlayerInteraction())
            {
                return;
            }

            _bindRoutine = StartCoroutine(WaitForLocalPlayer());
        }

        private void OnDisable()
        {
            if (_bindRoutine != null)
            {
                StopCoroutine(_bindRoutine);
                _bindRoutine = null;
            }

            UnbindPlayerInteraction();
        }

        private IEnumerator WaitForLocalPlayer()
        {
            while (isActiveAndEnabled && !TryBindPlayerInteraction())
            {
                yield return null;
            }

            _bindRoutine = null;
        }

        private bool TryBindPlayerInteraction()
        {
            if (_isBound)
            {
                return true;
            }

            if (_playerInteraction == null)
            {
                NetworkManager networkManager = NetworkManager.Singleton;
                NetworkObject localPlayer = networkManager != null
                    ? networkManager.LocalClient?.PlayerObject
                    : null;

                if (localPlayer == null)
                {
                    return false;
                }

                _playerInteraction = localPlayer.GetComponent<PlayerInteraction>();
                if (_playerInteraction == null)
                {
                    GameLog.Error(LogCategory.UI,
                        "The local network player has no PlayerInteraction component, so InteractionPromptUI cannot bind.");
                    return false;
                }
            }

            _playerInteraction.FocusChanged += OnFocusChanged;
            _isBound = true;
            OnFocusChanged(_playerInteraction.CurrentTarget);
            return true;
        }

        private void UnbindPlayerInteraction()
        {
            if (_isBound && _playerInteraction != null)
            {
                _playerInteraction.FocusChanged -= OnFocusChanged;
            }

            _isBound = false;
            _playerInteraction = null;

            if (_promptRoot != null)
            {
                _promptRoot.SetActive(false);
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
