using System.Collections.Generic;
using Sirenix.OdinInspector;
using StoryKit;
using UnityEngine;

namespace Weizza
{
    /// <summary>
    /// An enemy body that knows how to run one move at a time, plus a small
    /// brain that decides which move to run.
    ///
    /// The two are deliberately separable. Everything under "acting" is how a
    /// move is physically performed: line up, telegraph, swing, carry the
    /// body, recover. Everything under "deciding" is a grunt's own judgement,
    /// and it can be switched off so something smarter drives the same body
    /// by calling PerformMove directly. A mini boss is not a different
    /// component, it is this one with a longer move list and its decisions
    /// coming from outside.
    ///
    /// Combat, movement and physics stay here rather than in StoryKit, which
    /// handles dialogue, items and cutscenes only.
    /// </summary>
    [AddComponentMenu("Weizza/Enemy Controller")]
    [RequireComponent(typeof(CharacterMotor3D), typeof(Vitals), typeof(HitReactor))]
    public sealed class EnemyController : MonoBehaviour
    {
        public enum State { Idle, Chase, Telegraph, Commit, Recover, Stagger, Dead }

        /// <summary>
        /// Everyone alive right now, so each one can steer away from the rest.
        ///
        /// Two CharacterControllers pass straight through each other in Unity:
        /// the component only resolves against static geometry, never against
        /// another controller. Without this list a pack stacks into one spot
        /// and reads as a single enemy with several sprites on top of it, and
        /// no amount of clever position picking fixes it, because nothing is
        /// stopping them occupying the same position.
        /// </summary>
        private static readonly List<EnemyController> Live = new(16);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => Live.Clear();

        [BoxGroup("Setup")]
        [Required]
        [SerializeField] private EnemyDefinition data;

        [BoxGroup("Setup")]
        [Tooltip("Leave empty to find one in children.")]
        [SerializeField] private SpriteAnimator animator;

        [BoxGroup("Setup")]
        [Tooltip("Leave empty to find one in children.")]
        [SerializeField] private HitboxEmitter hitbox;

        [BoxGroup("Setup")]
        [Tooltip("Leave empty to find one in children.")]
        [SerializeField] private SpriteFeel spriteFeel;

        [BoxGroup("Setup")]
        [Tooltip("Leave empty to use the first PlayerController found at Awake.")]
        [SerializeField] private Transform target;

        [BoxGroup("Crowding", centerLabel: true)]
        [InfoBox("Two CharacterControllers walk straight through each other in " +
                 "Unity, so a pack has to be told to keep its distance or it " +
                 "stacks into one spot.")]
        [PropertyRange(0f, 3f)]
        [Tooltip("How close before they start pushing apart. Roughly two body " +
                 "widths reads best.")]
        [SerializeField] private float separationRadius = 0.9f;

        [BoxGroup("Crowding")]
        [PropertyRange(0f, 8f)]
        [Tooltip("How hard. Too high and the pack behaves like magnets, too low " +
                 "and they still overlap while shuffling for position.")]
        [SerializeField] private float separationStrength = 2.5f;

        [BoxGroup("Crowd")]
        [Tooltip("How far above or below the player an enemy can still reach. " +
                 "Past this it closes at a walk instead of committing, because " +
                 "the player is on another platform.")]
        [PropertyRange(0.2f, 4f)]
        [SerializeField] private float reachHeight = 1.2f;

        [BoxGroup("Brain", centerLabel: true)]
        [InfoBox("Off hands the decisions to something else, which then calls " +
                 "PerformMove by id. The body still acts on its own.")]
        [ToggleLeft]
        [SerializeField] private bool autoBrain = true;

        [BoxGroup("Animation", centerLabel: true)]
        [SerializeField] private string idleClip = "Idle";

        [BoxGroup("Animation")]
        [SerializeField] private string walkClip = "Walk";

        [BoxGroup("Animation")]
        [SerializeField] private string runClip = "Run";

        [BoxGroup("Animation")]
        [Tooltip("Played while flinching from a hit that broke through. Leave " +
                 "empty for a sheet that has no flinch drawing.")]
        [SerializeField] private string hurtClip = "Hit";

        [BoxGroup("Animation")]
        [Tooltip("Played once on death, then the object is despawned. Some " +
                 "sheets ship one drawing that is both the flinch and the " +
                 "death, in which case put the same name in both boxes.")]
        [SerializeField] private string deathClip = "Death";

        [BoxGroup("Animation")]
        [PropertyRange(0f, 4f)]
        [SerializeField] private float despawnDelay = 0.6f;

        // ---- Live ------------------------------------------------------------

        [FoldoutGroup("Live", expanded: true)]
        [ShowInInspector, ReadOnly] public State Current { get; private set; } = State.Idle;

        [FoldoutGroup("Live")]
        [ShowInInspector, ReadOnly] public string CurrentMove => _move?.id ?? "-";

        [FoldoutGroup("Live")]
        [ShowInInspector, ReadOnly]
        public float DistanceToTarget
        {
            get
            {
                if (target == null) return -1f;
                var to = target.position - transform.position;
                return new Vector2(to.x, to.y).magnitude;
            }
        }

        public bool IsAlive => _vitals != null && _vitals.IsAlive;

        /// <summary>
        /// Whether the target is close enough in Z to be fought at all.
        ///
        /// A player who has shifted onto another lane is not hidden and not
        /// safe forever, but they are somewhere this enemy cannot reach, so it
        /// stops closing rather than jogging on the spot against an invisible
        /// wall. Shifting away is meant to buy the player a few seconds, and
        /// this is the whole of what makes that true.
        /// </summary>
        private bool TargetOnSameLane
        {
            get
            {
                if (target == null) return false;
                if (_motor == null) return true;
                return Mathf.Abs(target.position.z - _motor.Depth) < 0.5f;
            }
        }

        private CharacterMotor3D _motor;
        private Vitals _vitals;
        private HitReactor _reactor;
        private Poise _poise;

        private readonly Dictionary<string, float> _readyAt = new();
        private readonly List<EnemyMove> _candidates = new(8);

        private EnemyMove _move;
        private float _stateTimer;
        private float _decideAt;
        private bool _hitboxOpen;

        private void Awake()
        {
            _motor = GetComponent<CharacterMotor3D>();
            _vitals = GetComponent<Vitals>();
            _reactor = GetComponent<HitReactor>();
            _poise = GetComponent<Poise>();

            if (animator == null) animator = GetComponentInChildren<SpriteAnimator>();
            if (hitbox == null) hitbox = GetComponentInChildren<HitboxEmitter>();
            if (spriteFeel == null) spriteFeel = GetComponentInChildren<SpriteFeel>();

            if (target == null)
            {
                var player = FindFirstObjectByType<PlayerController>();
                if (player != null) target = player.transform;
            }

            if (data == null)
            {
                Debug.LogWarning($"[Weizza] '{name}' has no EnemyDefinition. It will stand still.", this);
                return;
            }

            _vitals.SetMaxHealth(data.maxHealth, refill: true);
            if (_poise != null) _poise.SetMax(data.poise, refill: true);
        }

        private void OnEnable()
        {
            if (!Live.Contains(this)) Live.Add(this);
            _vitals.Died += OnDied;
            _reactor.Staggered += OnStaggered;
            if (hitbox != null) hitbox.Connected += OnHitConnected;
        }

        private void OnDisable()
        {
            Live.Remove(this);
            _vitals.Died -= OnDied;
            _reactor.Staggered -= OnStaggered;
            if (hitbox != null) hitbox.Connected -= OnHitConnected;

            ReleaseClaim();
            EncounterDirector.Instance?.LeaveSlot(this);
        }

        private void Update()
        {
            if (data == null || Current == State.Dead) return;
            var dt = Time.deltaTime;
            _stateTimer += dt;

            switch (Current)
            {
                case State.Idle: TickIdle(); break;
                case State.Chase: TickChase(dt); break;
                case State.Telegraph: TickTelegraph(); break;
                case State.Commit: TickCommit(); break;
                case State.Recover: TickRecover(); break;
                case State.Stagger: TickStagger(); break;
            }

            ApplySeparation();
            ApplyGravity(dt);
            _motor.Step(dt);
            if (_hitboxOpen && hitbox != null) hitbox.Sweep();
            PushAnimation();
        }

        // ---- Deciding --------------------------------------------------------

        private void TickIdle()
        {
            Brake();
            if (!autoBrain || target == null) return;
            if (Time.time < _decideAt) return;
            _decideAt = Time.time + data.decisionInterval;

            if (TargetOnSameLane && DistanceToTarget <= data.aggroRange) Enter(State.Chase);
        }

        private void TickChase(float dt)
        {
            if (!autoBrain || target == null)
            {
                Brake();
                return;
            }

            var to = target.position - transform.position;
            var distance = new Vector2(to.x, to.y).magnitude;

            if (!TargetOnSameLane)
            {
                ReleaseClaim();
                Enter(State.Idle);
                return;
            }

            if (distance > data.loseAggroRange)
            {
                ReleaseClaim();
                Enter(State.Idle);
                return;
            }

            // Always watching the player, even while walking the other way.
            // An enemy that turns its back to reposition reads as having lost
            // interest, which is the opposite of the intended threat.
            _motor.FaceMovement(to.x);

            var director = EncounterDirector.Instance;

            // With no director in the scene every enemy is free to act, so a
            // single enemy in a test room behaves exactly as it did before any
            // of this existed.
            var committed = director == null || director.Holds(this);

            if (Time.time >= _decideAt)
            {
                _decideAt = Time.time + data.decisionInterval;

                // Asking, not taking. The director compares everyone who wants
                // a turn this frame and picks, which is what lets it keep
                // sending whoever is behind the player.
                if (!committed && distance <= data.aggroRange)
                {
                    director.RequestTurn(this, distance);
                }

                if (committed && Mathf.Abs(to.y) <= reachHeight)
                {
                    var chosen = ChooseMove(distance);
                    if (chosen != null)
                    {
                        Begin(chosen);
                        return;
                    }
                }
            }

            if (committed) Close(to, distance);
            else HoldStandingSpot();
        }

        /// <summary>
        /// Closing on the player, having been given permission to attack.
        /// </summary>
        private void Close(Vector3 to, float distance)
        {
            // Too close for anything in the move list to be legal. Back off so
            // there is room to wind up again. Without this a charger walks into
            // the player's face and stands there, which reads as the brain
            // having given up rather than as the player being safe up close.
            if (distance < ClosestMoveRange() - 0.15f)
            {
                _motor.Velocity.x = -Mathf.Sign(to.x) * data.walkSpeed;
                return;
            }

            // Slows to a walk when the player is above or below, because
            // charging at full speed at someone on another platform only ever
            // ends in running past underneath them.
            var offHeight = Mathf.Abs(to.y) > reachHeight;
            _motor.Velocity.x = Mathf.Sign(to.x) * (offHeight ? data.walkSpeed : data.chaseSpeed);
        }

        /// <summary>
        /// Waiting for a turn. Walks to the spot the director set aside and
        /// stands there, facing the player.
        ///
        /// This is the half of the system the player actually reads. Enemies
        /// that freeze in place while they wait look switched off; enemies
        /// that walk to a position and hold it look like they are working
        /// together, even though nothing here is coordinating anything beyond
        /// picking a place to stand.
        /// </summary>
        private void HoldStandingSpot()
        {
            var director = EncounterDirector.Instance;
            if (director == null)
            {
                Brake();
                return;
            }

            var to = director.StandingSpot(this) - transform.position;
            if (Mathf.Abs(to.x) < 0.3f)
            {
                Brake();
                return;
            }

            _motor.Velocity.x = Mathf.Clamp(to.x, -1f, 1f) * data.walkSpeed;
        }

        private void ReleaseClaim()
        {
            EncounterDirector.Instance?.Release(this);
        }

        /// <summary>
        /// The nearest distance any move can still be thrown from. Inside this,
        /// the enemy has nothing legal to do and should make space.
        /// </summary>
        private float ClosestMoveRange()
        {
            if (data.moves == null) return 0f;

            var closest = float.MaxValue;
            foreach (var move in data.moves)
            {
                if (move == null || move.attack == null) continue;
                if (move.MinRange < closest) closest = move.MinRange;
            }

            return closest == float.MaxValue ? 0f : closest;
        }

        /// <summary>
        /// Picks among the moves that are legal at this range and off
        /// cooldown, by weight.
        ///
        /// Cooldowns are keyed by the move's own id rather than by its
        /// position in the list, because an inspector reorder must never
        /// silently hand one move another's cooldown.
        /// </summary>
        private EnemyMove ChooseMove(float distance)
        {
            if (data.moves == null) return null;

            _candidates.Clear();
            var total = 0f;

            foreach (var move in data.moves)
            {
                if (move == null || move.attack == null) continue;
                if (!move.InRange(distance)) continue;
                if (_readyAt.TryGetValue(move.id, out var ready) && Time.time < ready) continue;

                _candidates.Add(move);
                total += Mathf.Max(0f, move.weight);
            }

            if (_candidates.Count == 0) return null;
            if (total <= 0f) return _candidates[Random.Range(0, _candidates.Count)];

            var roll = Random.value * total;
            foreach (var move in _candidates)
            {
                roll -= Mathf.Max(0f, move.weight);
                if (roll <= 0f) return move;
            }

            return _candidates[^1];
        }

        // ---- Acting ----------------------------------------------------------

        /// <summary>
        /// Runs a move by id, whatever the brain thinks. This is the seam a
        /// smarter director drives the body through.
        /// </summary>
        public bool PerformMove(string moveId)
        {
            if (data == null || Current == State.Dead) return false;

            var move = data.FindMove(moveId);
            if (move == null || move.attack == null)
            {
                Debug.LogWarning($"[Weizza] '{name}' has no move called '{moveId}'.", this);
                return false;
            }

            Begin(move);
            return true;
        }

        /// <summary>Drops whatever is running and stands still.</summary>
        public void Hold()
        {
            CloseHitbox();
            Enter(State.Idle);
        }

        private void Begin(EnemyMove move)
        {
            _move = move;

            if (target != null)
            {
                _motor.FaceMovement(target.position.x - transform.position.x);
            }

            Brake();
            Enter(State.Telegraph);

            if (spriteFeel != null && move.telegraphFrames > 0)
            {
                spriteFeel.HoldTint(move.telegraphTint);
            }

            var clip = string.IsNullOrEmpty(move.telegraphClip) ? idleClip : move.telegraphClip;
            if (animator != null) animator.Play(clip);
        }

        private void TickTelegraph()
        {
            Brake();
            if (_stateTimer < Timing.Frames(_move.telegraphFrames)) return;

            spriteFeel?.ReleaseTint();
            Enter(State.Commit);

            var clip = string.IsNullOrEmpty(_move.travelClip)
                ? _move.attack.animationState
                : _move.travelClip;

            if (animator != null) animator.PlayStretched(clip, _move.attack.TotalSeconds);
        }

        private void TickCommit()
        {
            var attack = _move.attack;
            var activeEnd = attack.StartupSeconds + attack.ActiveSeconds;

            // A charge carries the body only while the hitbox is live, so the
            // distance travelled and the dangerous window are the same thing.
            // Anything else lets a boar run past you harmlessly and still look
            // like it should have connected.
            var travelling = _move.travelSpeed > 0f &&
                             _stateTimer >= attack.StartupSeconds &&
                             _stateTimer < activeEnd;

            _motor.Velocity.x = travelling
                ? _motor.Facing.Sign() * _move.travelSpeed
                : Mathf.MoveTowards(_motor.Velocity.x, 0f, 40f * Time.deltaTime);

            if (!_hitboxOpen && _stateTimer >= attack.StartupSeconds && _stateTimer < activeEnd)
            {
                _hitboxOpen = true;
                hitbox.Begin(attack, _motor.Facing, gameObject, damageScale: 1f, lifesteal: 0f);
            }
            else if (_hitboxOpen && _stateTimer >= activeEnd)
            {
                CloseHitbox();
            }

            if (_stateTimer < attack.TotalSeconds) return;

            _readyAt[_move.id] = Time.time + _move.cooldown;

            // Handing permission back the instant the swing ends, not when the
            // cooldown runs out. Holding it through the cooldown would let one
            // enemy own the encounter's only attack slot while doing nothing.
            ReleaseClaim();
            Enter(State.Recover);
        }

        private void TickRecover()
        {
            Brake();

            // Recovery is the attack's own recovery frames, already spent
            // inside Commit. This state only exists so the enemy visibly
            // settles before deciding again rather than snapping straight
            // back into a chase on the same frame the swing ended.
            if (_stateTimer < data.decisionInterval) return;
            Enter(autoBrain ? State.Chase : State.Idle);
        }

        private void TickStagger()
        {
            if (_reactor.InHitstun) return;
            Enter(autoBrain ? State.Chase : State.Idle);
        }

        // ---- Plumbing --------------------------------------------------------

        private void Enter(State next)
        {
            Current = next;
            _stateTimer = 0f;
        }

        private void Brake()
        {
            _motor.Velocity.x = 0f;
        }

        private void CloseHitbox()
        {
            if (!_hitboxOpen) return;
            _hitboxOpen = false;
            if (hitbox != null) hitbox.End();
        }

        /// <summary>
        /// Pushes away from anyone standing too close, on top of whatever the
        /// brain already decided to do.
        ///
        /// Added to the velocity rather than replacing it, so an enemy walking
        /// into a crowded spot still gets there, it just arrives beside the
        /// others instead of inside them. A charge in progress is left alone:
        /// being shoved off line mid commit would make the one attack the
        /// player is trying to read wander unpredictably.
        /// </summary>
        private void ApplySeparation()
        {
            if (Current is State.Commit or State.Dead) return;

            var push = 0f;
            var position = transform.position;

            foreach (var other in Live)
            {
                if (other == null || other == this || !other.IsAlive) continue;

                var otherPosition = other.transform.position;

                // Someone on another platform is not crowding anyone, so height
                // gates the check rather than feeding into it. Without this an
                // enemy on a ledge shoves the one underneath it sideways.
                if (Mathf.Abs(position.y - otherPosition.y) > separationRadius) continue;

                var away = position.x - otherPosition.x;
                var distance = Mathf.Abs(away);
                if (distance >= separationRadius) continue;

                // Two bodies on exactly the same spot have no side to separate
                // towards, so give them one rather than dividing by zero and
                // leaving them welded together.
                if (distance < 0.01f)
                {
                    push += Random.value < 0.5f ? -1f : 1f;
                    continue;
                }

                push += Mathf.Sign(away) * (1f - distance / separationRadius);
            }

            if (Mathf.Abs(push) < 0.01f) return;

            _motor.Velocity.x += push * separationStrength;
        }

        private void ApplyGravity(float dt)
        {
            if (_motor.Grounded && _motor.Velocity.y <= 0f) return;
            _motor.Velocity.y += Physics.gravity.y * dt;
        }

        private void PushAnimation()
        {
            if (animator == null) return;
            if (Current is State.Telegraph or State.Commit or State.Dead) return;

            // The flinch used to fall through to the speed based clips below,
            // so an enemy that was knocked backwards played its walk cycle
            // while sliding, and the hit drawing the artist made was never
            // shown once in the whole game.
            if (Current == State.Stagger)
            {
                if (!string.IsNullOrEmpty(hurtClip)) animator.Play(hurtClip);
                return;
            }

            var speed = _motor.GroundSpeed;
            if (speed < 0.1f) animator.Play(idleClip);
            else if (speed < data.chaseSpeed * 0.75f) animator.Play(walkClip);
            else animator.Play(runClip);
        }

        private void OnStaggered(DamageInfo info)
        {
            // Poise already decided this hit was worth interrupting for, so
            // the swing is dropped mid animation. A hit that did not break
            // poise never reaches here, which is what lets a heavy enemy
            // finish its attack through chip damage.
            CloseHitbox();
            spriteFeel?.ReleaseTint();

            // Interrupted, so someone else gets the turn. Keeping it would let
            // a player who staggers the front enemy stop the whole encounter
            // by hitting one thing forever.
            ReleaseClaim();
            Enter(State.Stagger);
        }

        private void OnHitConnected(IDamageable victim, HitResult result, DamageInfo info)
        {
            HitStop.Freeze(info.hitStopFrames);
            CameraFeel.Instance?.Shake(info.screenShake, new Vector2(info.knockback.x, 0f));
            HitSpark.Play(info.point, info.knockback, info.hitStopFrames * 0.12f,
                result == HitResult.Hit ? Color.white : new Color(1f, 0.85f, 0.4f));
        }

        private void OnDied()
        {
            CloseHitbox();
            spriteFeel?.ReleaseTint();
            ReleaseClaim();
            EncounterDirector.Instance?.LeaveSlot(this);
            StoryEvents.Raise(GameEvents.EnemyDefeated, data != null ? data.displayName : name);
            Enter(State.Dead);
            _motor.Velocity = Vector3.zero;

            if (animator != null) animator.Play(deathClip, restart: true);
            if (despawnDelay > 0f) Destroy(gameObject, despawnDelay);
        }
    }
}
