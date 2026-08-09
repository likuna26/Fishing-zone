using FishingZone.Core;
using UnityEngine;
using UnityEngine.InputSystem;

namespace FishingZone.Player
{
    /// <summary>
    /// First-person look. Yaw turns the body so that movement follows the view, while pitch stays
    /// on the camera anchor alone, which keeps the CharacterController upright.
    /// This is the only component that knows the camera is first-person: changing to a third-person
    /// rig later means replacing this file, not the movement or interaction code.
    /// </summary>
    public class PlayerCamera : MonoBehaviour
    {
        [SerializeField]
        private InputActionReference _lookAction;

        [SerializeField]
        private Transform _cameraAnchor;

        /// <summary>Degrees per pixel of mouse movement. Mouse delta is already frame-independent.</summary>
        [SerializeField]
        private float _mouseSensitivity = 0.12f;

        /// <summary>Degrees per second at full stick deflection. Scaled by delta time, unlike the mouse.</summary>
        [SerializeField]
        private float _gamepadSensitivity = 180f;

        [SerializeField]
        private float _minPitch = -85f;

        [SerializeField]
        private float _maxPitch = 85f;

        [SerializeField]
        private bool _invertY;

        private float _pitch;
        private bool _isConfigured;

        private void Awake()
        {
            _isConfigured = _lookAction != null && _cameraAnchor != null;
            if (!_isConfigured)
            {
                GameLog.Error(LogCategory.Input, "PlayerCamera is missing a Look action reference or a Camera Anchor. Assign both in the Inspector.");
                return;
            }

            _pitch = _cameraAnchor.localEulerAngles.x;
        }

        private void OnEnable()
        {
            if (_isConfigured)
            {
                SetCursorLocked(true);
            }
        }

        private void OnDisable()
        {
            // Menus and the Editor both need the pointer back when the player goes away.
            SetCursorLocked(false);
        }

        private void Update()
        {
            if (!_isConfigured)
            {
                return;
            }

            Vector2 look = _lookAction.action.ReadValue<Vector2>();
            if (look == Vector2.zero)
            {
                return;
            }

            // A mouse reports a delta that is already per-frame; a stick reports a held position,
            // so only the stick is scaled by delta time. Mixing the two up makes one of them unusable.
            float sensitivity = IsGamepadLook()
                ? _gamepadSensitivity * Time.deltaTime
                : _mouseSensitivity;

            transform.Rotate(Vector3.up, look.x * sensitivity, Space.World);

            float pitchDelta = look.y * sensitivity * (_invertY ? 1f : -1f);
            _pitch = Mathf.Clamp(_pitch + pitchDelta, _minPitch, _maxPitch);
            _cameraAnchor.localRotation = Quaternion.Euler(_pitch, 0f, 0f);
        }

        private bool IsGamepadLook()
        {
            InputControl control = _lookAction.action.activeControl;
            return control != null && control.device is Gamepad;
        }

        private static void SetCursorLocked(bool locked)
        {
            Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !locked;
        }
    }
}
