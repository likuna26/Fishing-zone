using FishingZone.Core;
using UnityEngine;
using UnityEngine.InputSystem;

namespace FishingZone.Player
{
    /// <summary>
    /// Ground movement and jumping for a first-person player, driven by a CharacterController.
    /// Movement is relative to where the body is facing, so the look component owns yaw and this
    /// component simply follows it.
    /// Every value is exposed for tuning rather than baked in, following the "no hardcoded physics
    /// values" rule the Technical Specification sets out for the boat and which applies equally here.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public class PlayerMovement : MonoBehaviour
    {
        [SerializeField]
        private InputActionReference _moveAction;

        [SerializeField]
        private InputActionReference _jumpAction;

        [SerializeField]
        private float _moveSpeed = 4.5f;

        [SerializeField]
        private float _jumpHeight = 1.1f;

        /// <summary>Negative. Higher magnitude than real gravity keeps jumps snappy rather than floaty.</summary>
        [SerializeField]
        private float _gravity = -20f;

        /// <summary>
        /// A small downward velocity held while grounded. CharacterController.isGrounded only stays
        /// true if the controller keeps being pushed into the floor, so without this it flickers on slopes.
        /// </summary>
        [SerializeField]
        private float _groundedStickVelocity = -2f;

        private CharacterController _controller;
        private float _verticalVelocity;

        /// <summary>
        /// Latched when a jump begins and held for the whole arc, including while descending back
        /// within probe range of the deck. Deliberately not derived from isGrounded, which drops for
        /// a frame at a time on a bobbing hull and would detach a player who never left the deck.
        /// </summary>
        private bool _isJumpDetached;

        /// <summary>
        /// Read by the platform rider, which cannot work this out for itself: its downward probe
        /// cannot tell a standing player from one in the first frames of a jump, because both are
        /// still within range of the deck.
        /// </summary>
        public bool IsPlatformDetached => _isJumpDetached;

        private bool _isConfigured;

        private void Awake()
        {
            _controller = GetComponent<CharacterController>();

            _isConfigured = _moveAction != null && _jumpAction != null;
            if (!_isConfigured)
            {
                GameLog.Error(LogCategory.Input, "PlayerMovement is missing a Move or Jump action reference. Assign both in the Inspector.");
            }
        }

        private void Update()
        {
            if (!_isConfigured)
            {
                return;
            }

            UpdateVerticalVelocity();

            Vector2 input = _moveAction.action.ReadValue<Vector2>();
            Vector3 horizontal = (transform.right * input.x) + (transform.forward * input.y);
            if (horizontal.sqrMagnitude > 1f)
            {
                horizontal.Normalize();
            }

            Vector3 motion = (horizontal * _moveSpeed) + (Vector3.up * _verticalVelocity);

            // Only the player's own motion. Inherited deck motion is applied by the platform rider
            // in LateUpdate, once a locally simulated hull has finished its physics step and a
            // replicated one has finished interpolating.
            _controller.Move(motion * Time.deltaTime);
        }

        private void UpdateVerticalVelocity()
        {
            // Reflects the previous Move, so on the frame a jump is pressed this is still true.
            bool isGrounded = _controller.isGrounded;

            if (isGrounded && _verticalVelocity < 0f)
            {
                _verticalVelocity = _groundedStickVelocity;

                // Grounded while descending is the only thing that counts as a landing, so an arc
                // cannot be ended early by brushing the deck on the way past.
                _isJumpDetached = false;
            }

            if (isGrounded && _jumpAction.action.WasPressedThisFrame())
            {
                // Velocity needed to reach _jumpHeight. Abs keeps this valid if gravity is mis-signed.
                _verticalVelocity = Mathf.Sqrt(_jumpHeight * 2f * Mathf.Abs(_gravity));

                // Set after the landing check above, so the jump frame ends detached rather than
                // being cleared by the grounded state it still reports.
                _isJumpDetached = true;
            }

            _verticalVelocity += _gravity * Time.deltaTime;
        }
    }
}
