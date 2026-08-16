using System;
using FishingZone.Core;
using UnityEngine;
using UnityEngine.InputSystem;

namespace FishingZone.Player
{
    /// <summary>
    /// Finds what the player is looking at and interacts with it on request.
    /// It only ever talks to <see cref="IInteractable"/>, so it needs no knowledge of NPCs,
    /// fishing stations, the boat wheel, shops or doors (Technical Specification section 16).
    /// </summary>
    public class PlayerInteraction : MonoBehaviour
    {
        [SerializeField]
        private InputActionReference _interactAction;

        /// <summary>
        /// Transform whose forward defines the view ray. In first person this is the camera anchor,
        /// so the ray follows pitch as well as yaw.
        /// </summary>
        [SerializeField]
        private Transform _viewSource;

        [SerializeField]
        private float _range = 3f;

        [SerializeField]
        private LayerMask _interactableLayers;

        /// <summary>Raised when the looked-at interactable changes, including to null. Null means nothing in range.</summary>
        public event Action<IInteractable> FocusChanged;

        public IInteractable CurrentTarget { get; private set; }

        private IInteractable _capturedTarget;
        private bool _isConfigured;

        private void Awake()
        {
            _isConfigured = _interactAction != null && _viewSource != null;
            if (!_isConfigured)
            {
                GameLog.Error(LogCategory.UI, "PlayerInteraction is missing an Interact action reference or a View Source. Assign both in the Inspector.");
            }
        }

        private void OnDisable()
        {
            _capturedTarget = null;
            SetTarget(null);
        }

        public void CaptureFocus(IInteractable target)
        {
            if (target == null)
            {
                return;
            }

            _capturedTarget = target;
            CurrentTarget = target;
            FocusChanged?.Invoke(target);
        }


        /// <summary>
        /// Announces the current target again without changing it, so its prompt is asked for a
        /// second time.
        ///
        /// Needed because prompt text is read once, when the focus is acquired: the ray runs every
        /// frame but a target that has not changed raises nothing, and the words on screen are
        /// whatever they were when the player first looked. That is fine for a prompt that depends
        /// only on who is reading it, and wrong for one that depends on something replicated, which
        /// can change while the player stands perfectly still — a station being claimed by somebody
        /// else, say. Such an interactable calls this when its state arrives.
        ///
        /// Safe at any time. It re-raises an event with the value already held, so a null target
        /// simply hides a prompt that is already hidden.
        /// </summary>
        public void RefreshFocus()
        {
            FocusChanged?.Invoke(CurrentTarget);
        }

        /// <summary>Releases a capture. Ignored if something else holds it.</summary>
        public void ReleaseFocus(IInteractable target)
        {
            if (!ReferenceEquals(_capturedTarget, target))
            {
                return;
            }

            _capturedTarget = null;
            CurrentTarget = null;
            FocusChanged?.Invoke(null);
        }

        private void Update()
        {
            if (!_isConfigured)
            {
                return;
            }

            SetTarget(_capturedTarget ?? FindTarget());

            if (CurrentTarget != null && _interactAction.action.WasPressedThisFrame())
            {
                CurrentTarget.Interact(gameObject);
            }
        }

        private IInteractable FindTarget()
        {
            if (!Physics.Raycast(_viewSource.position, _viewSource.forward, out RaycastHit hit, _range, _interactableLayers))
            {
                return null;
            }

            IInteractable interactable = hit.collider.GetComponentInParent<IInteractable>();
            if (interactable == null || !interactable.CanInteract(gameObject))
            {
                return null;
            }

            return interactable;
        }

        private void SetTarget(IInteractable target)
        {
            if (ReferenceEquals(CurrentTarget, target))
            {
                return;
            }

            CurrentTarget = target;
            FocusChanged?.Invoke(target);
        }
    }
}
