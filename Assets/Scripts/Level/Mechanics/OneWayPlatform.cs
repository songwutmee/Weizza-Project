using Sirenix.OdinInspector;
using UnityEngine;

namespace Weizza
{
    /// <summary>
    /// A ledge the player can jump up through from below and drop down
    /// through by holding down and pressing jump. Also what makes a rock or a
    /// stump "stand on top": walked straight through at ground level, landed
    /// on from above.
    ///
    /// CharacterController has no one-way collision of its own, so the
    /// platform switches collision against the player's capsule on and off
    /// with Physics.IgnoreCollision depending on where the feet are.
    /// </summary>
    [AddComponentMenu("Weizza/Level/One Way Platform")]
    [RequireComponent(typeof(Collider))]
    public sealed class OneWayPlatform : MonoBehaviour
    {
        [BoxGroup("Platform")]
        [PropertyRange(0f, 0.5f)]
        [Tooltip("How far below the top surface the feet can be and still be " +
                 "treated as standing on it.")]
        [SerializeField] private float tolerance = 0.12f;

        [BoxGroup("Platform")]
        [PropertyRange(0.05f, 1f)]
        [Tooltip("Seconds collision stays off after dropping through.")]
        [SerializeField] private float dropSeconds = 0.3f;

        private Collider _collider;
        private bool _ignoring;
        private float _dropUntil;

        private void Awake() => _collider = GetComponent<Collider>();

        private void Update()
        {
            var player = PlayerLocator.Player;
            if (player == null || player.Motor.Controller == null) return;

            var motor = player.Motor;
            var input = InputReader.Instance;
            var top = SurfaceAt(player.transform.position.x);
            var feet = player.transform.position.y;

            var standingOnMe = LevelPiece.Owns(this, motor.GroundCollider);
            if (standingOnMe && input != null && input.DownHeld && input.JumpPressed)
            {
                _dropUntil = Time.time + dropSeconds;
            }

            var below = feet < top - tolerance;
            var rising = motor.Velocity.y > 0.01f;
            var ignore = Time.time < _dropUntil || below || (rising && feet < top + 0.05f);

            if (ignore == _ignoring) return;
            _ignoring = ignore;
            Physics.IgnoreCollision(motor.Controller, _collider, ignore);
        }

        // The top right above the feet, not the top of the bounds: a rounded rock is
        // lower at its edges, and judging by its peak would drop a player standing near
        // an edge through it.
        private float SurfaceAt(float x)
        {
            var b = _collider.bounds;
            var ray = new Ray(new Vector3(x, b.max.y + 1f, b.center.z), Vector3.down);
            return _collider.Raycast(ray, out var hit, b.size.y + 2f) ? hit.point.y : b.max.y;
        }

        private void OnDisable()
        {
            var player = PlayerLocator.Player;
            if (_ignoring && player != null && player.Motor.Controller != null && _collider != null)
                Physics.IgnoreCollision(player.Motor.Controller, _collider, false);
            _ignoring = false;
        }
    }
}
