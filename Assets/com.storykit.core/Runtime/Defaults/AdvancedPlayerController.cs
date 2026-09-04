using UnityEngine;
using StoryKit.Bridge;

namespace StoryKit.Defaults
{
    [AddComponentMenu("StoryKit/Advanced Player Controller")]
    [RequireComponent(typeof(Rigidbody))]
    public sealed class AdvancedPlayerController : MonoBehaviour
    {
        public enum MovementType
        {
            FirstPerson,
            ThirdPerson,
            TankControls,
            TopDown
        }

        [Header("Locomotion")]
        [SerializeField] private MovementType _movementType = MovementType.FirstPerson;
        [SerializeField] private float _walkSpeed = 4f;
        [SerializeField] private float _sprintSpeed = 7f;
        [SerializeField] private float _jumpForce = 5f;
        
        [Header("Looking")]
        [SerializeField] private float _lookSensitivity = 3f;
        [SerializeField] private float _turnSpeed = 10f;
        [SerializeField] private Transform _cameraTransform;

        [Header("Head Bobbing")]
        [SerializeField] private bool _enableHeadBob = true;
        [SerializeField] private float _bobFrequency = 14f;
        [SerializeField] private float _bobAmount = 0.05f;

        private Rigidbody _body;
        private float _cameraPitch = 0f;
        private float _defaultCameraY = 0f;
        private float _timer = 0f;
        private bool _isGrounded;

        public MovementType Type { get => _movementType; set => _movementType = value; }
        public Transform CameraTransform { get => _cameraTransform; set => _cameraTransform = value; }
        
        public bool IsGrounded => _isGrounded;
        public bool IsSprinting => StoryInput.Sprint && StoryInput.Move.y > 0f && !StoryControl.MovementLocked;
        public Vector3 Velocity => _body.linearVelocity;

        private void Awake()
        {
            _body = GetComponent<Rigidbody>();
            if (_cameraTransform != null)
                _defaultCameraY = _cameraTransform.localPosition.y;
        }

        private void Update()
        {
            if (StoryControl.MovementLocked) return;

            HandleMouseLook();
            HandleJump();
            HandleHeadBob();
        }

        private void FixedUpdate()
        {
            CheckGrounded();
            
            if (StoryControl.MovementLocked)
            {
                _body.linearVelocity = new Vector3(0f, _body.linearVelocity.y, 0f);
                return;
            }

            HandleMovement();
        }

        private void CheckGrounded()
        {
            float radius = 0.3f;
            float distance = 0.15f;
            Vector3 origin = transform.position + Vector3.up * radius;
            _isGrounded = Physics.SphereCast(origin, radius, Vector3.down, out _, distance, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        }

        private void HandleMouseLook()
        {
            if (_movementType == MovementType.TopDown) return;
            if (StoryUi.GameplayCursor != StoryUi.CursorMode.Locked || StoryUi.CursorReleased) return;

            var look = StoryInput.Look;
            
            if (_movementType == MovementType.FirstPerson || _movementType == MovementType.ThirdPerson)
            {
                transform.Rotate(0f, look.x * _lookSensitivity, 0f);
            }
            else if (_movementType == MovementType.TankControls)
            {
                // Tank controls rotate with A/D, not mouse.
                transform.Rotate(0f, StoryInput.Move.x * _turnSpeed * Time.deltaTime, 0f);
            }

            if (_movementType == MovementType.FirstPerson && _cameraTransform != null)
            {
                _cameraPitch -= look.y * _lookSensitivity;
                _cameraPitch = Mathf.Clamp(_cameraPitch, -89f, 89f);
                _cameraTransform.localEulerAngles = new Vector3(_cameraPitch, 0f, 0f);
            }
        }

        private void HandleJump()
        {
            if (StoryInput.Jump && _isGrounded)
            {
                _body.linearVelocity = new Vector3(_body.linearVelocity.x, _jumpForce, _body.linearVelocity.z);
            }
        }

        private void HandleMovement()
        {
            var input = StoryInput.Move;
            float speed = IsSprinting ? _sprintSpeed : _walkSpeed;

            Vector3 move = Vector3.zero;

            switch (_movementType)
            {
                case MovementType.FirstPerson:
                case MovementType.ThirdPerson:
                    move = new Vector3(input.x, 0f, input.y);
                    if (move.sqrMagnitude > 1f) move.Normalize();
                    move = transform.TransformDirection(move);
                    break;
                case MovementType.TankControls:
                    // Tank controls only move forward/backward based on Y input
                    move = transform.forward * input.y;
                    break;
                case MovementType.TopDown:
                    move = new Vector3(input.x, 0f, input.y);
                    if (move.sqrMagnitude > 1f) move.Normalize();
                    
                    if (move.sqrMagnitude > 0.01f)
                    {
                        Quaternion targetRot = Quaternion.LookRotation(move);
                        transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, _turnSpeed * Time.fixedDeltaTime);
                    }
                    break;
            }

            _body.linearVelocity = new Vector3(move.x * speed, _body.linearVelocity.y, move.z * speed);
        }

        private void HandleHeadBob()
        {
            if (!_enableHeadBob || _cameraTransform == null || _movementType != MovementType.FirstPerson) return;

            float speed = new Vector3(_body.linearVelocity.x, 0f, _body.linearVelocity.z).magnitude;
            
            if (speed > 0.1f && _isGrounded)
            {
                _timer += Time.deltaTime * _bobFrequency * (IsSprinting ? 1.3f : 1f);
                float bobY = _defaultCameraY + Mathf.Sin(_timer) * _bobAmount;
                _cameraTransform.localPosition = new Vector3(_cameraTransform.localPosition.x, bobY, _cameraTransform.localPosition.z);
            }
            else
            {
                _timer = 0f;
                _cameraTransform.localPosition = new Vector3(
                    _cameraTransform.localPosition.x, 
                    Mathf.Lerp(_cameraTransform.localPosition.y, _defaultCameraY, Time.deltaTime * 5f), 
                    _cameraTransform.localPosition.z);
            }
        }
    }
}
