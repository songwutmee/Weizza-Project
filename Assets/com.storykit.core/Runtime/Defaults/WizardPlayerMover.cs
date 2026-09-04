using UnityEngine;

namespace StoryKit.Defaults
{
    /// <summary>
    /// A flexible player mover that handles First Person (with mouse look) and Global movement.
    /// Uses Rigidbody physics so it can fall and collide properly.
    /// </summary>
    [AddComponentMenu("StoryKit/Simple Player Mover")]
    [RequireComponent(typeof(Rigidbody))]
    public sealed class WizardPlayerMover : MonoBehaviour
    {
        public enum MovementType
        {
            /// <summary>W moves forward where the player is looking (First Person, Third Person Action).</summary>
            RelativeToPlayer,
            /// <summary>W moves North/Up on screen (Top Down, Isometric).</summary>
            Global
        }

        [SerializeField] private float _speed = 5f;
        [SerializeField] private float _lookSensitivity = 3f;
        [SerializeField] private MovementType _movementType = MovementType.RelativeToPlayer;
        
        [Tooltip("Assign the camera if First Person so we can pitch it up/down.")]
        [SerializeField] private Transform _cameraTransform;

        private Rigidbody _body;
        private float _cameraPitch = 0f;

        public MovementType Type { get => _movementType; set => _movementType = value; }
        public Transform CameraTransform { get => _cameraTransform; set => _cameraTransform = value; }

        private void Awake()
        {
            _body = GetComponent<Rigidbody>();
        }

        private void Update()
        {
            // Input locked during dialogue or cutscenes.
            if (StoryKit.StoryControl.MovementLocked) return;

            // Only allow mouse look if the cursor is locked (First Person mode).
            // A visible cursor means we're in menus or a point-and-click game.
            if (StoryUi.GameplayCursor == StoryUi.CursorMode.Locked && !StoryUi.CursorReleased)
            {
                var look = StoryKit.StoryInput.Look;
                
                // Rotate player body left/right (Yaw)
                transform.Rotate(0f, look.x * _lookSensitivity, 0f);

                // Rotate camera up/down (Pitch)
                if (_cameraTransform != null)
                {
                    _cameraPitch -= look.y * _lookSensitivity;
                    _cameraPitch = Mathf.Clamp(_cameraPitch, -89f, 89f);
                    _cameraTransform.localEulerAngles = new Vector3(_cameraPitch, 0f, 0f);
                }
            }
        }

        private void FixedUpdate()
        {
            if (StoryKit.StoryControl.MovementLocked)
            {
                _body.linearVelocity = new Vector3(0f, _body.linearVelocity.y, 0f);
                return;
            }

            var input = StoryKit.StoryInput.Move;
            var move = new Vector3(input.x, 0f, input.y);
            if (move.sqrMagnitude > 1f) move.Normalize();

            Vector3 worldMove = _movementType == MovementType.RelativeToPlayer
                ? transform.TransformDirection(move)
                : move;
            
            // Retain Y velocity for gravity/falling
            _body.linearVelocity = new Vector3(worldMove.x * _speed, _body.linearVelocity.y, worldMove.z * _speed);
        }
    }
}
