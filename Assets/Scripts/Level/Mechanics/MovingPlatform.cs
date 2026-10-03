using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;

namespace Weizza
{
    /// <summary>
    /// A platform that travels between waypoints and carries whoever stands on it.
    ///
    /// Riders are moved by the platform's own frame delta through
    /// CharacterMotor3D.AddDisplacement, never by parenting. Parenting a
    /// CharacterController fights its own position bookkeeping and jitters.
    ///
    /// Momentum is kept for a few frames after jumping off, the Celeste rule:
    /// jumping from a platform moving right should carry the player right,
    /// or the jump feels like the platform yanked the floor away.
    /// </summary>
    [AddComponentMenu("Weizza/Level/Moving Platform")]
    public sealed class MovingPlatform : MonoBehaviour, IActivatable
    {
        public enum Loop { PingPong, Cycle, Once }

        [BoxGroup("Path")]
        [Tooltip("Local offsets from the start position. The start itself is the first point.")]
        [SerializeField] private List<Vector3> points = new() { new Vector3(4f, 0f, 0f) };

        [BoxGroup("Path")]
        [EnumToggleButtons]
        [SerializeField] private Loop loop = Loop.PingPong;

        [BoxGroup("Path")]
        [PropertyRange(0.2f, 12f)]
        [SerializeField] private float speed = 2.5f;

        [BoxGroup("Path")]
        [PropertyRange(0f, 3f)]
        [Tooltip("Seconds it waits at each point.")]
        [SerializeField] private float pause = 0.6f;

        [BoxGroup("Path")]
        [ToggleLeft]
        [Tooltip("Off waits for a lever or plate to switch it on.")]
        [SerializeField] private bool startMoving = true;

        [BoxGroup("Path")]
        [PropertyRange(0f, 0.5f)]
        [Tooltip("Seconds a jumping rider keeps the platform's speed.")]
        [SerializeField] private float momentumKeep = 0.15f;

        [FoldoutGroup("Live")]
        [ShowInInspector, ReadOnly] public bool IsActivated { get; private set; }

        private Vector3 _origin;
        private int _target = 1;
        private int _direction = 1;
        private float _waitUntil;
        private Vector3 _lastVelocity;
        private CharacterMotor3D _lastRider;
        private float _riderLeftAt = -1f;

        private void Awake()
        {
            _origin = transform.position;
            IsActivated = startMoving;
        }

        public void SetActivated(bool activated, GameObject source) => IsActivated = activated;

        private Vector3 Point(int index) => index == 0 ? _origin : _origin + points[index - 1];
        private int Count => points.Count + 1;

        private void Update()
        {
            var dt = Time.deltaTime;
            if (dt <= 0f) return;

            var before = transform.position;
            if (IsActivated && Time.time >= _waitUntil && points.Count > 0) Advance(dt);
            var delta = transform.position - before;
            _lastVelocity = delta / dt;

            CarryRiders(delta);
        }

        private void Advance(float dt)
        {
            var goal = Point(_target);
            transform.position = Vector3.MoveTowards(transform.position, goal, speed * dt);
            if ((transform.position - goal).sqrMagnitude > 0.0001f) return;

            _waitUntil = Time.time + pause;

            switch (loop)
            {
                case Loop.Cycle:
                    _target = (_target + 1) % Count;
                    break;
                case Loop.Once:
                    if (_target < Count - 1) _target++;
                    else IsActivated = false;
                    break;
                default:
                    if (_target + _direction >= Count || _target + _direction < 0) _direction = -_direction;
                    _target += _direction;
                    break;
            }
        }

        private void CarryRiders(Vector3 delta)
        {
            var player = PlayerLocator.Player;
            if (player == null) return;
            var motor = player.Motor;

            var riding = LevelPiece.Owns(this, motor.GroundCollider);
            if (riding)
            {
                if (delta.sqrMagnitude > 0f) motor.AddDisplacement(delta);
                _lastRider = motor;
                _riderLeftAt = -1f;
                return;
            }

            if (_lastRider != motor) return;

            // Just stepped or jumped off: hand over the platform's speed for a
            // moment so the jump carries it, then let go entirely.
            if (_riderLeftAt < 0f) _riderLeftAt = Time.time;
            if (Time.time - _riderLeftAt <= momentumKeep && !motor.Grounded)
            {
                motor.AddDisplacement(new Vector3(_lastVelocity.x, 0f, 0f) * Time.deltaTime);
            }
            else
            {
                _lastRider = null;
            }
        }

#if UNITY_EDITOR
        private void OnDrawGizmos()
        {
            var origin = Application.isPlaying ? _origin : transform.position;
            Gizmos.color = new Color(0.45f, 0.8f, 1f, 0.9f);
            var previous = origin;
            foreach (var offset in points)
            {
                var next = origin + offset;
                Gizmos.DrawLine(previous, next);
                Gizmos.DrawWireCube(next, new Vector3(0.4f, 0.1f, 0.4f));
                previous = next;
            }
            if (loop == Loop.Cycle && points.Count > 0) Gizmos.DrawLine(previous, origin);
        }
#endif
    }
}
