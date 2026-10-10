using Sirenix.OdinInspector;
using UnityEngine;

namespace Weizza
{
    /// <summary>
    /// The standard starting grunt (the Ant Rider): walks in, swings, then backs
    /// off to regroup before walking in again.
    ///
    /// EnemyController's own brain already walks in and swings, with the
    /// telegraph and the attack token. This adds the part that makes it read as a
    /// creature rather than a turret: after every swing, and after being hit, it
    /// steps back still facing the player, waits a beat, and only then comes
    /// again. The gap is the player's turn. Two hits close together make it give
    /// more ground.
    ///
    /// It switches the body's auto brain off for the retreat and back on after,
    /// and moves it only through DirectedWalk, the rule every brain here follows.
    /// The body's walk speed is the retreat speed.
    /// </summary>
    [AddComponentMenu("Weizza/Enemy/Skirmish Brain")]
    [RequireComponent(typeof(EnemyController))]
    public sealed class SkirmishBrain : MonoBehaviour
    {
        [BoxGroup("Regroup")]
        [Tooltip("Seconds it backs off after its own swing, random between the two.")]
        [SerializeField] private Vector2 backOffAfterSwing = new(0.45f, 0.75f);

        [BoxGroup("Regroup")]
        [Tooltip("Seconds it backs off after being hit, random between the two.")]
        [SerializeField] private Vector2 backOffAfterHit = new(0.5f, 0.8f);

        [BoxGroup("Regroup")]
        [PropertyRange(1f, 3f)]
        [Tooltip("Back off is this many times longer when hit twice in quick succession.")]
        [SerializeField] private float shakenMultiplier = 1.7f;

        [BoxGroup("Regroup")]
        [Tooltip("Seconds it stands its ground after backing off, before walking in again, random between the two.")]
        [SerializeField] private Vector2 holdGround = new(0.1f, 0.45f);

        [BoxGroup("Smarts")]
        [PropertyRange(0f, 1f)]
        [Tooltip("Chance it stays in the fight after a swing instead of backing off. The move cooldown is then the only gap.")]
        [SerializeField] private float followUpChance = 0.25f;

        [BoxGroup("Smarts")]
        [PropertyRange(0f, 1f)]
        [Tooltip("Chance of a long back off after a swing, which leaves it at lunge range.")]
        [SerializeField] private float longBackOffChance = 0.3f;

        [BoxGroup("Smarts")]
        [Tooltip("Seconds of the long back off, random between the two.")]
        [SerializeField] private Vector2 longBackOff = new(0.7f, 1.1f);

        [BoxGroup("Smarts")]
        [PropertyRange(0f, 1f)]
        [Tooltip("Chance it steps back when the player swings while it is close.")]
        [SerializeField] private float dodgeChance = 0.3f;

        [BoxGroup("Smarts")]
        [PropertyRange(0.1f, 0.6f)]
        [SerializeField] private float dodgeSeconds = 0.3f;

        [BoxGroup("Smarts")]
        [PropertyRange(0.5f, 5f)]
        [SerializeField] private float dodgeCooldown = 2.2f;

        [BoxGroup("Smarts")]
        [PropertyRange(1f, 5f)]
        [Tooltip("If the player swings and misses while it is this close, it stops regrouping and answers at once.")]
        [SerializeField] private float punishRange = 3.2f;

        [BoxGroup("Body Feel")]
        [Tooltip("The drawing. Walking has one frame, so the step bounce is code.")]
        [SerializeField] private Transform drawing;

        [BoxGroup("Body Feel")]
        [PropertyRange(0f, 0.2f)]
        [SerializeField] private float stepBob = 0.05f;

        [BoxGroup("Senses")]
        [SerializeField] private LayerMask groundMask = 1;

        private enum Phase { Fighting, BackingOff, Holding }

        [BoxGroup("Live"), ShowInInspector, ReadOnly] private Phase _phase = Phase.Fighting;

        private EnemyController _enemy;
        private Vitals _vitals;
        private CharacterMotor3D _motor;
        private CharacterController _controller;
        private EnemyController.State _lastState;
        private Vector3 _drawingRest;
        private float _phaseUntil;
        private float _lastHitAt = -99f;
        private float _bobPhase;
        private bool _playerWasSwinging;
        private float _nextDodgeAt;
        private bool _pendingBackOff;
        private float _pendingSeconds;

        private void Awake()
        {
            _enemy = GetComponent<EnemyController>();
            _vitals = GetComponent<Vitals>();
            _motor = GetComponent<CharacterMotor3D>();
            _controller = GetComponent<CharacterController>();
            if (drawing == null)
            {
                var feel = GetComponentInChildren<SpriteFeel>();
                if (feel != null) drawing = feel.transform;
            }
            if (drawing != null) _drawingRest = drawing.localPosition;
        }

        private void OnEnable()
        {
            // A pooled body comes back with whatever it had when it died.
            _phase = Phase.Fighting;
            _pendingBackOff = false;
            _enemy.AutoBrain = true;
            _enemy.DirectedWalk = 0f;
            _enemy.FaceWalkDirection = false;
            if (_vitals != null) _vitals.Damaged += OnDamaged;
            if (drawing != null)
            {
                drawing.localPosition = _drawingRest;
                drawing.localRotation = Quaternion.identity;
            }
        }

        private void OnDisable()
        {
            if (_vitals != null) _vitals.Damaged -= OnDamaged;
            if (_enemy == null) return;
            _enemy.AutoBrain = true;
            _enemy.DirectedWalk = 0f;
        }

        private void Update()
        {
            if (_enemy == null) return;
            var state = _enemy.Current;

            if (!_enemy.IsAlive)
            {
                FeelDead();
                return;
            }

            // A swing just ended: the body moved from its strike into recovery.
            if (_lastState == EnemyController.State.Commit && state != EnemyController.State.Commit)
                AfterSwing();
            _lastState = state;

            ReadThePlayer(state);

            // The body owns a flinch and a swing; the retreat starts once both are done.
            if (_pendingBackOff && state is not (EnemyController.State.Stagger or EnemyController.State.Telegraph
                    or EnemyController.State.Commit))
            {
                _pendingBackOff = false;
                _phase = Phase.BackingOff;
                _phaseUntil = Time.time + _pendingSeconds;
                _enemy.AutoBrain = false;
            }

            switch (_phase)
            {
                case Phase.BackingOff: TickBackOff(); break;
                case Phase.Holding: TickHold(); break;
            }

            FeelBody();
        }

        private void TickBackOff()
        {
            var away = AwayFromPlayer();
            // Backs away while watching the player, so it reads as wary, not as running off.
            _enemy.FaceWalkDirection = false;
            _enemy.DirectedWalk = LedgeOrWall(away) ? 0f : away;
            if (Time.time < _phaseUntil) return;
            _enemy.DirectedWalk = 0f;
            _phase = Phase.Holding;
            _phaseUntil = Time.time + Random.Range(holdGround.x, holdGround.y);
        }

        // Not the same rhythm every time: sometimes it stays and swings again, sometimes it
        // gives a little ground, sometimes a lot (which leaves it at lunge range).
        private void AfterSwing()
        {
            var roll = Random.value;
            if (roll < followUpChance) return;
            if (roll > 1f - longBackOffChance) QueueBackOff(Random.Range(longBackOff.x, longBackOff.y));
            else QueueBackOff(Random.Range(backOffAfterSwing.x, backOffAfterSwing.y));
        }

        // Watches the player's swings. A swing that misses leaves the player open, so a
        // regrouping ant answers at once; a swing aimed at an ant that stands close makes it
        // step out of reach first, and the missed swing then gets answered.
        private void ReadThePlayer(EnemyController.State state)
        {
            var player = PlayerLocator.Player;
            var swinging = player != null && player.State == PlayerState.Attack;
            if (swinging && player != null)
            {
                var gap = Mathf.Abs(player.transform.position.x - transform.position.x);
                if (_phase != Phase.Fighting && gap <= punishRange && Time.time - _lastHitAt > 0.6f)
                {
                    _pendingBackOff = false;
                    _phase = Phase.Fighting;
                    _enemy.DirectedWalk = 0f;
                    _enemy.AutoBrain = true;
                }
                else if (!_playerWasSwinging && _phase == Phase.Fighting && gap < 2.2f && Time.time >= _nextDodgeAt
                         && (state == EnemyController.State.Idle || state == EnemyController.State.Chase)
                         && Random.value < dodgeChance)
                {
                    _nextDodgeAt = Time.time + dodgeCooldown;
                    _phase = Phase.BackingOff;
                    _phaseUntil = Time.time + dodgeSeconds;
                    _enemy.AutoBrain = false;
                }
            }
            _playerWasSwinging = swinging;
        }

        private void TickHold()
        {
            // A tiny step toward the player turns the body to face them without moving it.
            _enemy.DirectedWalk = -AwayFromPlayer() * 0.01f;
            if (Time.time < _phaseUntil) return;
            _enemy.DirectedWalk = 0f;
            _phase = Phase.Fighting;
            _enemy.AutoBrain = true;
        }

        private void OnDamaged(DamageInfo info, HitResult result)
        {
            if (result is not (HitResult.Hit or HitResult.Armored) || !_vitals.IsAlive) return;
            var shaken = Time.time - _lastHitAt < 1.2f;
            _lastHitAt = Time.time;
            QueueBackOff(Random.Range(backOffAfterHit.x, backOffAfterHit.y) * (shaken ? shakenMultiplier : 1f));
        }

        private void QueueBackOff(float seconds)
        {
            // A longer retreat wins over a shorter one already waiting.
            _pendingSeconds = _pendingBackOff ? Mathf.Max(_pendingSeconds, seconds) : seconds;
            _pendingBackOff = true;
        }

        private float AwayFromPlayer()
        {
            var player = PlayerLocator.Player;
            if (player == null) return 0f;
            var side = Mathf.Sign(transform.position.x - player.transform.position.x);
            return side == 0f ? 1f : side;
        }

        private bool LedgeOrWall(float dir)
        {
            if (dir == 0f) return true;
            var radius = _controller != null ? _controller.radius : 0.4f;
            var foot = transform.position + new Vector3(dir * (radius + 0.4f), 0.3f, 0f);
            if (!Physics.Raycast(foot, Vector3.down, 1.2f, groundMask, QueryTriggerInteraction.Ignore)) return true;
            return Physics.Raycast(transform.position + Vector3.up * 0.5f, new Vector3(dir, 0f, 0f), radius + 0.4f,
                groundMask, QueryTriggerInteraction.Ignore);
        }

        // ---- Body feel ------------------------------------------------------

        private void FeelBody()
        {
            if (drawing == null || _motor == null) return;
            var busy = _enemy.Current is EnemyController.State.Commit or EnemyController.State.Stagger;
            var speed = busy ? 0f : _motor.GroundSpeed;
            _bobPhase += Time.deltaTime * (5f + speed * 4f);
            var bob = speed > 0.1f ? Mathf.Abs(Mathf.Sin(_bobPhase)) * stepBob * Mathf.Min(1f, speed) : 0f;
            drawing.localPosition = _drawingRest + Vector3.up * bob;
        }

        // Wholesome, not grim: it flips onto its back, legs up, before it goes.
        private void FeelDead()
        {
            if (drawing == null) return;
            var dt = Time.deltaTime;
            drawing.localRotation = Quaternion.Slerp(drawing.localRotation, Quaternion.Euler(0f, 0f, 180f), dt * 10f);
            drawing.localPosition = Vector3.Lerp(drawing.localPosition, _drawingRest + Vector3.up * 0.4f, dt * 10f);
        }
    }
}
