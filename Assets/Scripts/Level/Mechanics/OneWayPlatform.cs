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
            var feet = player.transform.position.y;

            var standingOnMe = LevelPiece.Owns(this, motor.GroundCollider);
            if (standingOnMe && input != null && input.DownHeld && input.JumpPressed)
            {
                _dropUntil = Time.time + dropSeconds;
            }

            bool ignore;
            if (standingOnMe)
            {
                // Already on it: stay on, even near a rounded edge, until a deliberate drop.
                ignore = Time.time < _dropUntil;
            }
            else
            {
                // Only solid to a body coming down onto it from fully above. Judged by the
                // highest point under the whole capsule, not just its centre: walking in at
                // ground level, the low lip at the edge used to switch the rock on while the
                // body was already half inside it, and the controller climbed it or snagged.
                // Beside the rock, its nearest edge counts: a body at ground level next to it is
                // below that lip and so walks in, never into a wall.
                var radius = motor.Controller.radius;
                var b = _collider.bounds;
                var x = player.transform.position.x;
                // Sampled a little inside the rim: the drawn outline curls down to the ground at
                // its ends, and a lip that low would turn on under a body walking past it.
                var inset = Mathf.Min(0.6f, b.extents.x * 0.5f);
                float Under(float at) => SurfaceAt(Mathf.Clamp(at, b.min.x + inset, b.max.x - inset));
                var highest = Mathf.Max(Under(x), Mathf.Max(Under(x - radius), Under(x + radius)));
                var falling = motor.Velocity.y <= 0.01f;
                ignore = Time.time < _dropUntil || !falling || feet < highest - tolerance;
            }

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
