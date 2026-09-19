using Sirenix.OdinInspector;
using StoryKit;
using UnityEngine;

namespace Weizza
{
    /// <summary>What the player is busy doing. Exactly one at a time, always.</summary>
    public enum PlayerState
    {
        Locomotion = 0,
        Attack = 1,
        Dash = 2,
        StanceSwitch = 3,
        Dead = 4,

        /// <summary>
        /// Took a hit that broke through. Input is ignored so the knockback is
        /// allowed to actually move the character. Without this state the
        /// movement code rewrites the velocity on the same frame the hit set
        /// it, and being hit costs health with no other consequence at all.
        /// </summary>
        Hurt = 5,
    }

    /// <summary>
    /// The player. Reads input, decides what state to be in, and tells the motor
    /// what velocity to spend.
    ///
    /// Written as one explicit state machine rather than a graph of components
    /// because every rule in the design brief is a rule about what may interrupt
    /// what, and those rules are only checkable when they sit next to each other
    /// in one file. In cannot jump; Than cannot dash; the stance switch has no
    /// armour and no invincibility. All three are readable below without opening
    /// another file.
    ///
    /// Movement runs in Update rather than FixedUpdate. CharacterController is
    /// not a rigidbody, so nothing is gained by matching the physics tick, and
    /// stepping at the display rate is what keeps a side scroller from
    /// shimmering when the camera follows it.
    /// </summary>
    [AddComponentMenu("Weizza/Player Controller")]
    [RequireComponent(typeof(CharacterMotor3D))]
    public sealed class PlayerController : MonoBehaviour
    {
        [BoxGroup("Setup")]
        [Required]
        [Tooltip("Holds the three stances and the rules that sit above them.")]
        [SerializeField] private PlayerTuning tuning;

        [BoxGroup("Setup")]
        [SerializeField] private SpriteAnimator animator;

        [BoxGroup("Setup")]
        [SerializeField] private SpriteFeel spriteFeel;

        [BoxGroup("Setup")]
        [Tooltip("The active frames of an attack. Usually a child object out " +
                 "at arm's length from the character root.")]
        [SerializeField] private HitboxEmitter hitbox;

        [BoxGroup("Animation", centerLabel: true)]
        [InfoBox("Clip names as typed into the Sprite Animator. A name that " +
                 "does not exist is ignored rather than throwing, so a half " +
                 "animated character still walks.")]
        [HorizontalGroup("Animation/Row")]
        [LabelWidth(40)]
        [SerializeField] private string idleClip = "Idle";

        [HorizontalGroup("Animation/Row")]
        [LabelWidth(34)]
        [SerializeField] private string runClip = "Run";

        [HorizontalGroup("Animation/Row")]
        [LabelWidth(40)]
        [SerializeField] private string jumpClip = "Jump";

        [BoxGroup("Animation")]
        [Tooltip("Played for the whole dash. No art for it yet, so leaving this " +
                 "pointing at a clip that does not exist is fine: the animator " +
                 "warns once and the dash still works. Fill the clip in later " +
                 "and nothing else has to change.")]
        [SerializeField] private string dashClip = "Dash";

        [BoxGroup("Animation")]
        [Tooltip("Played while flinching from a hit. No art for it yet is fine, " +
                 "the animator falls back and warns once.")]
        [SerializeField] private string hurtClip = "Hurt";

        [BoxGroup("Animation")]
        [Tooltip("Played once on death and held on its last frame until the " +
                 "respawn director puts the player back on their feet.")]
        [SerializeField] private string deathClip = "Common_Death";

        [BoxGroup("Animation")]
        [PropertyRange(0.05f, 2f)]
        [Tooltip("Ground speed above which the run animation plays.")]
        [SerializeField] private float runThreshold = 0.35f;

        // ---- Live -----------------------------------------------------------

        [FoldoutGroup("Live", expanded: true)]
        [ShowInInspector, ReadOnly, EnumToggleButtons]
        public PlayerState State { get; private set; } = PlayerState.Locomotion;

        [FoldoutGroup("Live")]
        [ShowInInspector, ReadOnly, EnumToggleButtons]
        public StanceId Stance { get; private set; } = StanceId.Normal;

        [FoldoutGroup("Live")]
        [ShowInInspector, ReadOnly]
        [Tooltip("Seconds left of the vulnerable stance switch window.")]
        public float SwitchRemaining =>
            State == PlayerState.StanceSwitch && tuning != null
                ? Mathf.Max(0f, tuning.SwitchSeconds - _stateTimer)
                : 0f;

        [FoldoutGroup("Live")]
        [ShowInInspector, ReadOnly] public bool Invincible => Vitals != null && Vitals.Invincible;

        [FoldoutGroup("Live")]
        [ShowInInspector, ReadOnly] public int ComboIndex => _comboIndex;

        /// <summary>The attack running right now, or null.</summary>
        public AttackDefinition CurrentAttack => _attack;

        [FoldoutGroup("Live")]
        [ShowInInspector, DisplayAsString]
        private string InputReadout =>
            Input == null ? "no Input Reader in the scene" : Input.MoveRaw.ToString("0.00");

        public CharacterMotor3D Motor { get; private set; }
        public Vitals Vitals { get; private set; }
        public StanceDefinition CurrentStance { get; private set; }
        public PlayerTuning Tuning => tuning;
        public float DashCooldownRemaining => Mathf.Max(0f, _dashReadyAt - Time.time);

        private float _stateTimer;
        private float _coyoteUntil;
        private float _jumpBufferedUntil;
        private float _dashBufferedUntil;
        private float _dashReadyAt;
        private float _switchReadyAt;
        private StanceId _pendingStance;
        private HitReactor _reactor;

        private float _attackBufferedUntil;
        private bool _attackBufferHeavy;
        private AttackDefinition _attack;
        private int _comboIndex;
        private bool _hitboxOpen;

        private static InputReader Input => InputReader.Instance;

        private void Awake()
        {
            Motor = GetComponent<CharacterMotor3D>();
            Vitals = GetComponent<Vitals>();
            if (animator == null) animator = GetComponentInChildren<SpriteAnimator>();
            if (spriteFeel == null) spriteFeel = GetComponentInChildren<SpriteFeel>();
            _reactor = GetComponent<HitReactor>();

            if (_reactor == null)
            {
                Debug.LogWarning(
                    $"[Weizza] '{name}' has no HitReactor, so getting hit will cost health " +
                    "and nothing else: no flinch, no knockback, no flash.", this);
            }
            if (hitbox == null) hitbox = GetComponentInChildren<HitboxEmitter>();

            if (animator == null)
            {
                Debug.LogWarning(
                    $"[Weizza] '{name}' has no Sprite Animator anywhere under it, so " +
                    "Idle/Run/Jump will never play. It needs to sit on the same " +
                    "object as the character's Sprite Renderer.", this);
            }

            if (hitbox == null)
            {
                Debug.LogWarning(
                    $"[Weizza] '{name}' has no Hitbox Emitter anywhere under it, so " +
                    "attacks will swing without ever being able to connect. Add a " +
                    "child object with a Hitbox Emitter on it.", this);
            }
        }

        private void OnEnable()
        {
            Motor.Landed += OnLanded;
            if (Vitals != null) Vitals.Died += OnDied;
            if (hitbox != null) hitbox.Connected += OnHitConnected;
            if (_reactor != null) _reactor.Staggered += OnStaggered;
        }

        private void OnDisable()
        {
            Motor.Landed -= OnLanded;
            if (Vitals != null) Vitals.Died -= OnDied;
            if (hitbox != null) hitbox.Connected -= OnHitConnected;
            if (_reactor != null) _reactor.Staggered -= OnStaggered;
        }

        private void Start() => ApplyStance(
            tuning != null ? tuning.startingStance : StanceId.Normal, refillHealth: true);

        private void Update()
        {
            var dt = Time.deltaTime;
            _stateTimer += dt;

            ReadInput();

            switch (State)
            {
                case PlayerState.Locomotion: TickLocomotion(dt); break;
                case PlayerState.Attack: TickAttack(dt); break;
                case PlayerState.Dash: TickDash(dt); break;
                case PlayerState.StanceSwitch: TickSwitch(dt); break;
                case PlayerState.Hurt: TickHurt(dt); break;
                case PlayerState.Dead: TickDead(dt); break;
            }

            ApplyGravity(dt);
            Motor.Step(dt);

            if (_hitboxOpen) hitbox.Sweep();

            PushAnimation();
        }

        // ---- Input -----------------------------------------------------------

        private void ReadInput()
        {
            if (Input == null || tuning == null || State == PlayerState.Dead) return;

            // Buffers are filled even while busy. Half of what reads as
            // responsiveness is a press that was accepted a few frames early.
            if (Input.JumpPressed)
            {
                var frames = CurrentStance != null ? CurrentStance.jumpBufferFrames : 6;
                _jumpBufferedUntil = Time.time + Timing.Frames(frames);
            }

            if (Input.DashPressed)
            {
                _dashBufferedUntil = Time.time + Timing.Frames(tuning.dashBufferFrames);
            }

            if (Input.LightPressed) BufferAttack(heavy: false);
            if (Input.HeavyPressed) BufferAttack(heavy: true);

            if (StoryControl.MovementLocked) return;

            var request = Input.StanceRequest(Stance);
            if (request.HasValue) TryStartSwitch(request.Value);
        }

        private Vector2 Move()
        {
            if (Input == null || StoryControl.MovementLocked) return Vector2.zero;
            return Input.MoveRaw;
        }

        // ---- Locomotion --------------------------------------------------------

        private void TickLocomotion(float dt)
        {
            var move = Move();
            Drive(move, dt, 1f);
            Motor.FaceMovement(move.x);

            if (TryConsumeJump()) return;
            if (TryConsumeDash()) return;
            TryConsumeAttack();
        }

        private void BufferAttack(bool heavy)
        {
            if (tuning == null) return;
            _attackBufferedUntil = Time.time + Timing.Frames(tuning.attackBufferFrames);
            _attackBufferHeavy = heavy;
        }

        /// <summary>
        /// Accelerates along the screen. Only the horizontal half of the stick
        /// drives movement: up and down are free for whatever a level needs
        /// them for, such as a door or a ladder, rather than for walking.
        /// </summary>
        private void Drive(Vector2 input, float dt, float scale)
        {
            if (CurrentStance == null) return;

            var targetX = input.x * CurrentStance.runSpeed * scale;

            var rate = Mathf.Abs(input.x) > 0.1f
                ? CurrentStance.acceleration
                : CurrentStance.deceleration;

            if (!Motor.Grounded) rate *= CurrentStance.airControl;

            Motor.Velocity.x = Mathf.MoveTowards(Motor.Velocity.x, targetX, rate * dt);
        }

        private void ApplyGravity(float dt)
        {
            if (CurrentStance == null) return;
            if (State == PlayerState.Dash) return;
            if (Motor.Grounded && Motor.Velocity.y <= 0f) return;

            var gravity = CurrentStance.RiseGravity;

            // Gravity is derived from the jump numbers, so a stance authored
            // with no jump (In) can end up deriving zero, and a character with
            // zero gravity that gets knocked upwards never comes back down.
            // Falling is not optional, whatever the stance can or cannot do.
            if (gravity <= 0.01f) gravity = Mathf.Abs(Physics.gravity.y) * 3f;

            // Falling faster than rising is the single biggest thing that stops
            // a jump feeling like a balloon.
            //
            // Clamped to at least one, because this is a multiplier and a
            // stance authored with no jump leaves it at zero. Zero here undoes
            // the guard above and gives the character no gravity at all, which
            // showed up as the player being knocked upwards by an enemy and
            // then hanging in the air permanently with Velocity.y stuck at the
            // knockback value.
            var falling = Motor.Velocity.y < 0f || Input == null || !Input.JumpHeld;
            if (falling) gravity *= Mathf.Max(1f, CurrentStance.fallGravityMultiplier);

            Motor.Velocity.y -= gravity * dt;
        }

        private bool TryConsumeJump()
        {
            if (CurrentStance == null || !CurrentStance.canJump) return false;
            if (Time.time > _jumpBufferedUntil) return false;
            if (!Motor.Grounded && Time.time > _coyoteUntil) return false;

            _jumpBufferedUntil = 0f;
            _coyoteUntil = 0f;
            Motor.Velocity.y = CurrentStance.JumpVelocity;
            return true;
        }

        private void LateUpdate()
        {
            if (Motor.Grounded && CurrentStance != null)
            {
                _coyoteUntil = Time.time + Timing.Frames(CurrentStance.coyoteFrames);
            }

            // Variable jump height. Cutting the rise on the frame the button
            // comes up is what separates a hop from a full jump without asking
            // the player for a second input.
            if (Input == null || CurrentStance == null) return;
            if (!Input.JumpReleased || Motor.Velocity.y <= 0f) return;
            Motor.Velocity.y *= CurrentStance.jumpCutMultiplier;
        }

        // ---- Dash ---------------------------------------------------------------

        private bool TryConsumeDash()
        {
            if (CurrentStance == null || !CurrentStance.canDash) return false;
            if (Time.time > _dashBufferedUntil) return false;
            if (Time.time < _dashReadyAt) return false;

            _dashBufferedUntil = 0f;
            EnterState(PlayerState.Dash);
            _dashReadyAt = Time.time + Timing.Frames(
                CurrentStance.dashFrames + CurrentStance.dashCooldownFrames);

            var direction = Motor.Facing.Sign();

            if (CurrentStance.dashIsBlink && TryBlinkLane()) return true;

            if (CurrentStance.dashIsBlink)
            {
                // In moves by a written ward, not by legs: it does not travel
                // through the space between, it stops being in one place and
                // starts being in the other.
                Motor.Teleport(transform.position +
                               new Vector3(CurrentStance.dashDistance * direction, 0f, 0f));
            }
            else
            {
                var duration = Timing.Frames(CurrentStance.dashFrames);
                Motor.Velocity = new Vector3(CurrentStance.dashDistance / duration * direction, 0f, 0f);
            }

            return true;
        }

        /// <summary>
        /// Blinks onto the lane behind or in front, if the stick was pushed
        /// that way and the scene has a lane there to land on.
        ///
        /// Deliberately the same press as the ordinary blink. In already moves
        /// by stepping out of one place and into another, so crossing a lane is
        /// the same idea aimed differently rather than a second ability with
        /// its own button, and a level that offers no second lane simply gives
        /// back the sideways blink with nothing to explain.
        /// </summary>
        private bool TryBlinkLane()
        {
            if (ViewRig.LaneCount <= 1) return false;

            var push = Input != null ? Input.MoveRaw.y : 0f;
            if (Mathf.Abs(push) < 0.5f) return false;

            // Up goes away from the camera, matching the way the lanes are laid
            // out and the way a player reads a background as being further up
            // the screen.
            var next = Motor.Lane + (push > 0f ? 1 : -1);
            if (next < 0 || next >= ViewRig.LaneCount) return false;
            if (!Motor.ShiftToLane(next)) return false;

            StoryEvents.Raise(GameEvents.PlayerBlinkedLane, next.ToString());
            return true;
        }

        private void TickDash(float dt)
        {
            if (CurrentStance == null)
            {
                EnterState(PlayerState.Locomotion);
                return;
            }

            var frame = Mathf.FloorToInt(_stateTimer / Timing.Frame);
            if (Vitals != null)
            {
                Vitals.Invincible = frame >= CurrentStance.iFrameStart &&
                                    frame <= CurrentStance.iFrameEnd;
            }

            if (_stateTimer < Timing.Frames(CurrentStance.dashFrames)) return;

            if (Vitals != null) Vitals.Invincible = false;
            Motor.Velocity.x = 0f;
            EnterState(PlayerState.Locomotion);
        }

        // ---- Attack ---------------------------------------------------------------

        private void TryConsumeAttack()
        {
            if (Time.time > _attackBufferedUntil || CurrentStance == null || hitbox == null) return;

            _attackBufferedUntil = 0f;
            StartAttack(_attackBufferHeavy ? null : NextComboStep(0), _attackBufferHeavy);
        }

        private AttackDefinition NextComboStep(int index)
        {
            var combo = CurrentStance.lightCombo;
            if (combo == null || combo.Length == 0) return null;
            return combo[Mathf.Clamp(index, 0, combo.Length - 1)];
        }

        private void StartAttack(AttackDefinition attack, bool heavy)
        {
            if (heavy) attack = CurrentStance.heavy;
            if (attack == null) return;

            _attack = attack;
            _hitboxOpen = false;
            EnterState(PlayerState.Attack);

            if (Vitals != null) Vitals.SuperArmor = attack.superArmor;

            if (attack.lungeDistance > 0f)
            {
                var duration = Mathf.Max(Timing.Frame, attack.StartupSeconds + attack.ActiveSeconds);
                Motor.Velocity.x = attack.lungeDistance / duration * Motor.Facing.Sign();
            }
            else
            {
                Motor.Velocity.x = 0f;
            }

            if (animator != null) animator.PlayStretched(attack.animationState, attack.TotalSeconds);
        }

        private void TickAttack(float dt)
        {
            if (_attack == null)
            {
                EndAttack();
                return;
            }

            // Slow to a stop through the swing rather than snapping, so a
            // lunge reads as weight instead of a teleport.
            Motor.Velocity.x = Mathf.MoveTowards(Motor.Velocity.x, 0f, CurrentStance.deceleration * 0.6f * dt);

            var startup = _attack.StartupSeconds;
            var activeEnd = startup + _attack.ActiveSeconds;

            if (!_hitboxOpen && _stateTimer >= startup && _stateTimer < activeEnd)
            {
                _hitboxOpen = true;
                hitbox.Begin(_attack, Motor.Facing, gameObject, damageScale: 1f,
                    lifesteal: CurrentStance.lifesteal);
            }
            else if (_hitboxOpen && _stateTimer >= activeEnd)
            {
                CloseHitbox();
                if (Vitals != null) Vitals.SuperArmor = false;
            }

            // No early cancel: the swing has to run its full length, recovery
            // included, before anything happens next. Mashing the button
            // never buys back a single frame of recovery, it only decides
            // whether the very next thing that happens is another swing or a
            // return to standing still.
            if (_stateTimer < _attack.TotalSeconds) return;

            if (Time.time <= _attackBufferedUntil)
            {
                _attackBufferedUntil = 0f;
                var heavy = _attackBufferHeavy;
                var next = heavy ? 0 : _comboIndex + 1;
                if (!heavy && (CurrentStance.lightCombo == null || next >= CurrentStance.lightCombo.Length))
                {
                    next = 0;
                }

                _comboIndex = heavy ? 0 : next;
                StartAttack(heavy ? null : NextComboStep(_comboIndex), heavy);
                return;
            }

            EndAttack();
        }

        private void CloseHitbox()
        {
            if (!_hitboxOpen) return;
            _hitboxOpen = false;
            hitbox.End();
        }

        private void EndAttack()
        {
            CloseHitbox();
            if (Vitals != null) Vitals.SuperArmor = false;
            _attack = null;
            _comboIndex = 0;
            EnterState(PlayerState.Locomotion);
        }

        private void OnHitConnected(IDamageable victim, HitResult result, DamageInfo info)
        {
            if (result is HitResult.Missed or HitResult.Ignored) return;

            HitStop.Freeze(info.hitStopFrames);
            CameraFeel.Instance?.Shake(info.screenShake, new Vector2(info.knockback.x, 0f));

            // Scaled off hit stop rather than off damage, because hit stop is
            // already the number tuned for how heavy a hit is meant to read.
            HitSpark.Play(info.point, info.knockback, info.hitStopFrames * 0.12f,
                result == HitResult.Hit ? Color.white : new Color(1f, 0.85f, 0.4f));

            if (result == HitResult.Parried) return;

            // Se has no self heal outside a shrine. Lifesteal is the only tap,
            // which is what makes the glass cannon a real choice instead of a
            // worse stance with a bigger number.
            if (info.lifesteal > 0f && Vitals != null) Vitals.Heal(info.amount * info.lifesteal);
        }

        // ---- Getting hit ------------------------------------------------------------

        /// <summary>
        /// Raised only for hits that broke through. A hit the player armoured
        /// or blinked past never reaches here, so Than keeps swinging through
        /// chip damage exactly the way an enemy with poise does.
        /// </summary>
        private void OnStaggered(DamageInfo info)
        {
            if (State == PlayerState.Dead) return;

            // Whatever was happening is over. Leaving the hitbox open through a
            // flinch would let a player trade hits by getting hit, which reads
            // as the game not noticing.
            CloseHitbox();
            if (Vitals != null)
            {
                Vitals.SuperArmor = false;
                Vitals.Invincible = false;
            }

            EnterState(PlayerState.Hurt);
        }

        private void TickHurt(float dt)
        {
            // Deliberately does not touch Velocity.x. The knockback the reactor
            // wrote is the whole point of this state, and the movement code
            // would erase it on the first frame.
            if (_reactor == null || !_reactor.InHitstun)
            {
                EnterState(PlayerState.Locomotion);
                return;
            }

            // Bleed off sideways speed slowly so the shove has weight and then
            // settles, rather than sliding until the stun happens to end.
            Motor.Velocity.x = Mathf.MoveTowards(Motor.Velocity.x, 0f, 8f * dt);
        }

        // ---- Stance switching -----------------------------------------------------

        /// <summary>
        /// The one thing nobody else in the world can do, and it costs about
        /// 0.4 seconds of standing there with nothing protecting you.
        /// </summary>
        public bool TryStartSwitch(StanceId target)
        {
            if (target == Stance || tuning == null) return false;
            if (State is PlayerState.Dead or PlayerState.StanceSwitch
                or PlayerState.Attack or PlayerState.Hurt) return false;
            if (Time.time < _switchReadyAt) return false;
            if (tuning.Get(target) == null) return false;

            _pendingStance = target;
            EnterState(PlayerState.StanceSwitch);

            if (Vitals != null)
            {
                // No super armour, no invincibility, and a penalty on top. Being
                // the exception to the world rule is supposed to be felt.
                Vitals.SuperArmor = false;
                Vitals.Invincible = false;
                Vitals.DamageMultiplier = (CurrentStance?.damageTaken ?? 1f) *
                                          tuning.switchDamageMultiplier;
            }

            return true;
        }

        private void TickSwitch(float dt)
        {
            Drive(Move(), dt, tuning.switchMoveScale);

            if (_stateTimer < tuning.SwitchSeconds) return;

            ApplyStance(_pendingStance, refillHealth: false);
            _switchReadyAt = Time.time + tuning.SwitchCooldownSeconds;
            EnterState(PlayerState.Locomotion);
        }

        private void ApplyStance(StanceId id, bool refillHealth)
        {
            var stance = tuning != null ? tuning.Get(id) : null;
            if (stance == null)
            {
                Debug.LogWarning(
                    $"[Weizza] No stance asset for {id}. Check the Player Tuning asset.", this);
                return;
            }

            Stance = id;
            CurrentStance = stance;

            if (Vitals != null)
            {
                Vitals.SetMaxHealth(stance.maxHealth, refillHealth);
                Vitals.DamageMultiplier = stance.damageTaken;
                Vitals.Invincible = false;
                Vitals.SuperArmor = false;
                Vitals.Parrying = false;
            }

            StoryState.Global.Set(StateKeys.Stance, (int)id);
            StoryEvents.Raise(GameEvents.StanceSwitched, id.ToString());
        }

        // ---- Death ----------------------------------------------------------------

        private void OnDied()
        {
            CloseHitbox();
            EnterState(PlayerState.Dead);
            Motor.Velocity = Vector3.zero;
            StoryEvents.Raise(GameEvents.PlayerDied);
        }

        private void TickDead(float dt)
        {
            Motor.Velocity.x = Mathf.MoveTowards(Motor.Velocity.x, 0f, 30f * dt);
        }

        /// <summary>
        /// Puts the player back on their feet. Being dead and getting up again
        /// is the premise of the game, not a fail state, so this is ordinary
        /// gameplay rather than a debug affordance.
        /// </summary>
        public void Revive(float healthFraction = 1f)
        {
            if (Vitals != null) Vitals.SetHealth(Vitals.Max * Mathf.Clamp01(healthFraction));
            EnterState(PlayerState.Locomotion);
            StoryEvents.Raise(GameEvents.PlayerRevived);
        }

        private void OnLanded(float impactSpeed) => spriteFeel?.Land(impactSpeed);

        private void EnterState(PlayerState next)
        {
            State = next;
            _stateTimer = 0f;
        }

        // ---- Animation --------------------------------------------------------------

        private void PushAnimation()
        {
            if (animator == null) return;

            // Death beats everything, including an attack that was mid swing
            // when the killing hit landed. Without this the body keeps running
            // its idle on the floor while the respawn sequence plays over it.
            if (State == PlayerState.Dead)
            {
                animator.Play(deathClip, restart: false);
                return;
            }

            // An attack owns its own clip for its whole duration, stretched to
            // the frame data by StartAttack. Falling through to the ordinary
            // idle/run/jump logic below would stomp it the instant it started.
            if (State == PlayerState.Attack) return;

            if (State == PlayerState.Hurt)
            {
                animator.Play(hurtClip);
                return;
            }

            // The dash owns its clip the same way, stretched so a nine frame
            // blink and a twelve frame slide both read as one complete motion
            // rather than as a clip cut off partway.
            if (State == PlayerState.Dash)
            {
                if (CurrentStance != null)
                {
                    animator.PlayStretched(dashClip, Timing.Frames(CurrentStance.dashFrames),
                        restart: false);
                }
                return;
            }

            if (!Motor.Grounded)
            {
                animator.Play(jumpClip);
                return;
            }

            animator.Play(Motor.GroundSpeed > runThreshold ? runClip : idleClip);
        }
    }
}
