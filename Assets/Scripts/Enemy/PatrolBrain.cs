using Sirenix.OdinInspector;
using UnityEngine;

namespace Weizza
{
    /// <summary>
    /// The Hollow Knight crawler: walks back and forth on its platform, turns at
    /// ledges and walls, and only fights once the player is close and on its lane.
    /// Then it hands control to EnemyController's own brain, and takes it back
    /// when the player is gone.
    ///
    /// This is what makes a room feel lived in before the fight starts. An enemy
    /// standing still until the player arrives reads as a target dummy; one that
    /// was already going about its business reads as a creature.
    ///
    /// Drives the body through DirectedWalk only. It never writes velocity or
    /// opens a hitbox, the same rule every boss brain follows.
    /// </summary>
    [AddComponentMenu("Weizza/Enemy/Patrol Brain")]
    [RequireComponent(typeof(EnemyController))]
    public sealed class PatrolBrain : MonoBehaviour
    {
        public enum Mode
        {
            /// <summary>Walk until a ledge or a wall, turn, repeat.</summary>
            EdgeToEdge,
            /// <summary>Stay within a distance of where it was placed.</summary>
            Leash,
            /// <summary>Stand, look around now and then. A sentry.</summary>
            Idle,
        }

        [BoxGroup("Patrol")]
        [SerializeField] private Mode mode = Mode.EdgeToEdge;

        [BoxGroup("Patrol")]
        [ShowIf(nameof(mode), Mode.Leash)]
        [PropertyRange(0.5f, 20f)]
        [SerializeField] private float leashDistance = 4f;

        [BoxGroup("Patrol")]
        [PropertyRange(0f, 3f)]
        [Tooltip("Seconds it stops at each turn. A pause is what lets the player read the pattern and time a pass.")]
        [SerializeField] private float pauseAtTurn = 0.6f;

        [BoxGroup("Patrol")]
        [PropertyRange(0.2f, 1f)]
        [Tooltip("Fraction of walk speed while patrolling. Slower than its chase, so noticing the player reads as a change.")]
        [SerializeField] private float patrolPace = 0.7f;

        [BoxGroup("Senses")]
        [PropertyRange(0.1f, 1.5f)]
        [Tooltip("How far ahead of its feet it looks for a ledge.")]
        [SerializeField] private float ledgeLookAhead = 0.5f;

        [BoxGroup("Senses")]
        [PropertyRange(0.1f, 2f)]
        [SerializeField] private float wallLookAhead = 0.6f;

        [BoxGroup("Senses")]
        [Tooltip("What counts as floor and wall. Default is the level geometry.")]
        [SerializeField] private LayerMask groundMask = 1;

        [BoxGroup("Senses")]
        [ToggleLeft]
        [Tooltip("Hollow Knight crawlers do not notice you at all, they just walk. Off makes this enemy patrol forever and only hurt on contact.")]
        [SerializeField] private bool noticesPlayer = true;

        [ShowInInspector, ReadOnly] private bool _fighting;
        [ShowInInspector, ReadOnly] private float _direction = 1f;

        private EnemyController _enemy;
        private CapsuleCollider _capsule;
        private CharacterController _controller;
        private Vector3 _home;
        private float _pauseUntil;
        private float _nextLookAround;
        private float _nextSense;

        private void Awake()
        {
            _enemy = GetComponent<EnemyController>();
            _controller = GetComponent<CharacterController>();
            _capsule = GetComponent<CapsuleCollider>();
            _home = transform.position;
            _direction = Random.value < 0.5f ? -1f : 1f;
        }

        private void OnEnable() => StartPatrol();

        private void OnDisable()
        {
            if (_enemy == null) return;
            _enemy.DirectedWalk = 0f;
            _enemy.FaceWalkDirection = false;
        }

        private void StartPatrol()
        {
            _fighting = false;
            _enemy.AutoBrain = false;
            _enemy.FaceWalkDirection = true;
        }

        private void Update()
        {
            if (_enemy == null || !_enemy.IsAlive) return;

            if (noticesPlayer)
            {
                var data = _enemy.Data;
                var sees = data != null && _enemy.TargetOnSameLane && _enemy.DistanceToTarget <= data.aggroRange;
                var lost = data == null || !_enemy.TargetOnSameLane || _enemy.DistanceToTarget > data.loseAggroRange;

                if (!_fighting && sees)
                {
                    _fighting = true;
                    _enemy.DirectedWalk = 0f;
                    _enemy.FaceWalkDirection = false;
                    _enemy.AutoBrain = true;
                    return;
                }

                // Only let go between swings, never in the middle of one.
                if (_fighting && lost && !_enemy.Busy) StartPatrol();
            }

            if (_fighting) return;
            Patrol();
        }

        private void Patrol()
        {
            if (Time.time < _pauseUntil)
            {
                _enemy.DirectedWalk = 0f;
                return;
            }

            if (mode == Mode.Idle)
            {
                _enemy.DirectedWalk = 0f;
                if (Time.time >= _nextLookAround)
                {
                    _nextLookAround = Time.time + Random.Range(1.5f, 3.5f);
                    _direction = -_direction;
                    // A single step is enough to turn the body round.
                    _enemy.DirectedWalk = _direction * 0.05f;
                }
                return;
            }

            // Senses at 20 Hz, not every frame. At walk speed that is 4 cm between
            // checks, well inside the look ahead, and a room of twenty crawlers
            // costs a third of the raycasts.
            if (Time.time < _nextSense)
            {
                _enemy.DirectedWalk = _direction * patrolPace;
                return;
            }
            _nextSense = Time.time + 0.05f;

            var mustTurn = mode switch
            {
                Mode.EdgeToEdge => LedgeAhead() || WallAhead(),
                Mode.Leash => (transform.position.x - _home.x) * _direction > leashDistance || LedgeAhead() || WallAhead(),
                _ => false,
            };

            if (mustTurn)
            {
                _direction = -_direction;
                _pauseUntil = Time.time + pauseAtTurn;
                _enemy.DirectedWalk = 0f;
                return;
            }

            _enemy.DirectedWalk = _direction * patrolPace;
        }

        private float Radius => _controller != null ? _controller.radius : _capsule != null ? _capsule.radius : 0.3f;

        private bool LedgeAhead()
        {
            if (_enemy.Data != null && _enemy.Data.flying) return false;
            var origin = transform.position + new Vector3(_direction * (Radius + ledgeLookAhead), 0.3f, 0f);
            return !Physics.Raycast(origin, Vector3.down, 1.0f, groundMask, QueryTriggerInteraction.Ignore);
        }

        private bool WallAhead()
        {
            var origin = transform.position + Vector3.up * 0.5f;
            return Physics.Raycast(origin, new Vector3(_direction, 0f, 0f), Radius + wallLookAhead, groundMask,
                QueryTriggerInteraction.Ignore);
        }

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            var home = Application.isPlaying ? _home : transform.position;
            Gizmos.color = new Color(0.5f, 0.9f, 0.6f, 0.8f);
            if (mode == Mode.Leash)
                Gizmos.DrawLine(home + Vector3.left * leashDistance, home + Vector3.right * leashDistance);
            var dir = Application.isPlaying ? _direction : 1f;
            var r = _controller != null ? _controller.radius : 0.3f;
            var ledge = transform.position + new Vector3(dir * (r + ledgeLookAhead), 0.3f, 0f);
            Gizmos.DrawLine(ledge, ledge + Vector3.down);
        }
#endif
    }
}
