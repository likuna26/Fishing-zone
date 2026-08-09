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

            // Milestone 2 note: standing on the moving boat will need the platform's delta added
            // here, because a CharacterController does not inherit motion from what it stands on.
            Vector2 input = _moveAction.action.ReadValue<Vector2>();
            Vector3 horizontal = (transform.right * input.x) + (transform.forward * input.y);
            if (horizontal.sqrMagnitude > 1f)
            {
                horizontal.Normalize();
            }

            Vector3 motion = (horizontal * _moveSpeed) + (Vector3.up * _verticalVelocity);
            _controller.Move(motion * Time.deltaTime);
        }

        private void UpdateVerticalVelocity()
        {
            bool isGrounded = _controller.isGrounded;

            if (isGrounded && _verticalVelocity < 0f)
            {
                _verticalVelocity = _groundedStickVelocity;
            }

            if (isGrounded && _jumpAction.action.WasPressedThisFrame())
            {
                // Velocity needed to reach _jumpHeight. Abs keeps this valid if gravity is mis-signed.
                _verticalVelocity = Mathf.Sqrt(_jumpHeight * 2f * Mathf.Abs(_gravity));
            }

            _verticalVelocity += _gravity * Time.deltaTime;
        }
    }
}
