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

        /// <summary>
        /// Multiplies run speed. Set by whatever is slowing the player for its
        /// own reasons (winding up a throw, an effect), reset to one by the
        /// same owner. Never written by this class.
        /// </summary>
        public float ExternalMoveScale { get; set; } = 1f;

        /// <summary>Free to start something new: standing or moving, not locked by a cutscene, alive.</summary>
        public bool CanAct => State == PlayerState.Locomotion && !StoryControl.MovementLocked &&
                              (Vitals == null || Vitals.IsAlive);

        [FoldoutGroup("Live")]
        [ShowInInspector, ReadOnly]
        [Tooltip("-1 a wall on the left, 1 on the right, 0 none.")]
        public int WallSide => _wallSide;

        [FoldoutGroup("Live")]
        [ShowInInspector, ReadOnly] public bool WallSliding { get; private set; }

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
        private bool _downAttacking;

        private int _wallSide;
        private int _lastWallSide;
        private float _wallCoyoteUntil;
        private float _wallJumpLockUntil;

        private TagContainer _tags;
        private System.IDisposable _dashIFrames;
        private readonly System.Collections.Generic.List<System.IDisposable> _stanceTags = new(4);
        private EffectReceiver _effects;

        private static readonly RaycastHit[] WallHits = new RaycastHit[1];

        private static InputReader Input => InputReader.Instance;

        private const int PlayerWings = 4;

        private void Awake()
        {
            Motor = GetComponent<CharacterMotor3D>();
            Vitals = GetComponent<Vitals>();

            // The player's health is four butterfly wings, Hollow Knight style.
            // Set here so every scene's player gets it without editing each one;
            // a value typed on the Vitals component by hand still wins.
            if (Vitals != null && Vitals.Wings == 0) Vitals.Wings = PlayerWings;
            if (animator == null) animator = GetComponentInChildren<SpriteAnimator>();
            if (spriteFeel == null) spriteFeel = GetComponentInChildren<SpriteFeel>();
            _reactor = GetComponent<HitReactor>();
            _tags = this.Tags();
            // Added here rather than placed by hand, so an existing scene picks
            // up effects and ranged stances with no rewiring. Both sit idle
            // until something uses them.
            _effects = GetComponent<EffectReceiver>();
            if (_effects == null && Vitals != null) _effects = gameObject.AddComponent<EffectReceiver>();
            if (GetComponent<PlayerShooter>() == null) gameObject.AddComponent<PlayerShooter>();
            if (GetComponent<SafeGroundTracker>() == null) gameObject.AddComponent<SafeGroundTracker>();
            if (GetComponent<StanceSwitchFeedback>() == null) gameObject.AddComponent<StanceSwitchFeedback>();
            if (GetComponent<FootDust>() == null) gameObject.AddComponent<FootDust>();
            if (GetComponent<DashGhosts>() == null) gameObject.AddComponent<DashGhosts>();
            if (GetComponent<RunLean>() == null) gameObject.AddComponent<RunLean>();
            if (GetComponentInChildren<RimLight>() == null && spriteFeel != null)
            {
                var drawing = GetComponentInChildren<SpriteRenderer>();
                if (drawing != null) drawing.gameObject.AddComponent<RimLight>();
            }

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
            if (Vitals != null) Vitals.Damaged += OnDamagedForMercy;
        }

        private void OnDisable()
        {
            Motor.Landed -= OnLanded;
            if (Vitals != null) Vitals.Died -= OnDied;
            if (hitbox != null) hitbox.Connected -= OnHitConnected;
            if (_reactor != null) _reactor.Staggered -= OnStaggered;
            if (Vitals != null) Vitals.Damaged -= OnDamagedForMercy;
            EndMercy();
        }

        // ---- Mercy frames ---------------------------------------------------------

        [BoxGroup("Getting Hit", centerLabel: true)]
        [PropertyRange(0f, 2.5f)]
        [Tooltip("Seconds of invulnerability after losing a wing. Hollow Knight uses about 1.3. " +
                 "Without it, standing in a crowd or on a crawler drains every wing in a second.")]
        [SerializeField] private float mercySeconds = 1.1f;

        private System.IDisposable _mercy;
        private float _mercyUntil;
        private SpriteRenderer _bodyRenderer;

        private void OnDamagedForMercy(DamageInfo info, HitResult result)
        {
            if (result is not (HitResult.Hit or HitResult.Armored) || Vitals == null || !Vitals.IsAlive) return;
            if (!Vitals.UsesWings || mercySeconds <= 0f) return;
            _mercyUntil = Time.time + mercySeconds;
            if (_mercy == null && _tags != null) _mercy = _tags.Add(GameTag.Invincible);
        }

        /// <summary>Ticked from Update. Flickers the body while mercy runs, the universal "you can't be hit" cue.</summary>
        private void TickMercy()
        {
            if (_mercy == null) return;
            if (_bodyRenderer == null && animator != null) _bodyRenderer = animator.GetComponent<SpriteRenderer>();
            if (Time.time >= _mercyUntil) { EndMercy(); return; }
            if (_bodyRenderer != null) _bodyRenderer.enabled = Mathf.FloorToInt(Time.time * 16f) % 2 == 0;
        }

        private void EndMercy()
        {
            _mercy?.Dispose();
            _mercy = null;
            if (_bodyRenderer != null) _bodyRenderer.enabled = true;
        }

        private void Start() => ApplyStance(
            tuning != null ? tuning.startingStance : StanceId.Normal, refillHealth: true);

        private void Update()
        {
            var dt = Time.deltaTime;
            _stateTimer += dt;
            TickMercy();

            if (_override != null)
            {
                if (State != PlayerState.Locomotion || !_override.TickOverride(this, dt))
                {
                    EndOverride();
                }
                else
                {
                    Motor.Step(dt);
                    PushAnimation();
                    return;
                }
            }

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
            ApplyWallSlide();
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

            // A ranged stance has no melee. PlayerShooter reads the same
            // buttons, and buffering a swing here as well would play a light
            // combo on top of every shot.
            var ranged = CurrentStance != null && CurrentStance.IsRanged;
            if (!ranged && Input.LightPressed) BufferAttack(heavy: false);
            if (!ranged && Input.HeavyPressed) BufferAttack(heavy: true);

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
            DetectWall();

            // During the lock after a wall jump the stick cannot steer, or a
            // player would climb a single wall forever by jumping straight
            // back into it.
            if (Time.time >= _wallJumpLockUntil)
            {
                Drive(move, dt, ExternalMoveScale * SpeedMultiplier());
                Motor.FaceMovement(move.x);
            }

            if (TryConsumeWallJump()) return;
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

            // Apex hang, the Celeste trick: near the top of a jump that is
            // still being held, gravity eases off so there is a moment to line
            // up the landing. Checked before the fall multiplier on purpose,
            // so the first frames of falling still get the hang.
            var held = Input != null && Input.JumpHeld;
            var atApex = CurrentStance.canJump && held && !Motor.Grounded &&
                         Mathf.Abs(Motor.Velocity.y) < CurrentStance.apexSpeedBand;

            if (atApex) gravity *= Mathf.Clamp(CurrentStance.apexGravityMultiplier, 0.1f, 1f);
            else if (falling) gravity *= Mathf.Max(1f, CurrentStance.fallGravityMultiplier);

            Motor.Velocity.y -= gravity * dt;
        }

        private IPlayerOverride _override;

        /// <summary>True while a vine or another gadget is driving the player.</summary>
        public bool Overridden => _override != null;

        /// <summary>
        /// Hands movement to a gadget. Refused unless the player is in plain
        /// locomotion, so a gadget can never cut into an attack or a flinch.
        /// </summary>
        public bool BeginOverride(IPlayerOverride gadget)
        {
            if (gadget == null || _override != null || State != PlayerState.Locomotion) return false;
            _override = gadget;
            return true;
        }

        public void EndOverride()
        {
            if (_override == null) return;
            var ending = _override;
            _override = null;
            ending.EndOverride(this);
        }

        /// <summary>Lets a gadget consume a buffered jump press, the way the controller's own jump does.</summary>
        public bool ConsumeJumpPress()
        {
            if (Time.time > _jumpBufferedUntil) return false;
            _jumpBufferedUntil = 0f;
            return true;
        }

        private float SpeedMultiplier()
        {
            if (_tags != null && _tags.Has(GameTag.Rooted)) return 0f;
            return _effects != null ? _effects.MoveSpeedMultiplier : 1f;
        }

        // ---- Wall (In) -----------------------------------------------------------

        /// <summary>
        /// Looks a short way out from both sides of the capsule for a wall.
        ///
        /// Two rays, at knee and head height, rather than one at the middle,
        /// so a ledge lip that only catches the head still counts and a low
        /// step that only catches the feet does not get mistaken for a wall
        /// unless the head agrees.
        /// </summary>
        private void DetectWall()
        {
            _wallSide = 0;
            if (CurrentStance == null || !CurrentStance.canWallJump || Motor.Grounded) return;

            var controller = Motor.Controller;
            if (controller == null) return;

            var reach = controller.radius + CurrentStance.wallCheckDistance;
            var basePoint = transform.position + controller.center;
            var low = basePoint + Vector3.down * controller.height * 0.25f;
            var high = basePoint + Vector3.up * controller.height * 0.3f;

            if (WallAt(low, high, 1, reach)) _wallSide = 1;
            else if (WallAt(low, high, -1, reach)) _wallSide = -1;

            if (_wallSide == 0) return;
            _lastWallSide = _wallSide;
            _wallCoyoteUntil = Time.time + Timing.Frames(CurrentStance.wallCoyoteFrames);
        }

        private static bool WallAt(Vector3 low, Vector3 high, int side, float reach)
        {
            var direction = Vector3.right * side;
            var mask = LayerMask.GetMask("Default");
            return HitsWall(low, direction, reach, mask) && HitsWall(high, direction, reach, mask);
        }

        private static bool HitsWall(Vector3 from, Vector3 direction, float reach, int mask)
        {
            if (Physics.RaycastNonAlloc(from, direction, WallHits, reach, mask,
                    QueryTriggerInteraction.Ignore) == 0) return false;
            return Mathf.Abs(WallHits[0].normal.x) > 0.7f;
        }

        /// <summary>
        /// Caps the fall while pressing into a wall. Runs after gravity so it
        /// clamps the final speed rather than being undone by it.
        /// </summary>
        private void ApplyWallSlide()
        {
            WallSliding = false;
            if (State != PlayerState.Locomotion || _wallSide == 0 || CurrentStance == null) return;
            if (Motor.Velocity.y >= 0f) return;

            var push = Input != null && !StoryControl.MovementLocked ? Input.MoveRaw.x : 0f;
            if (Mathf.Abs(push) < 0.3f || Mathf.Sign(push) != _wallSide) return;

            WallSliding = true;
            Motor.Velocity.y = Mathf.Max(Motor.Velocity.y, -CurrentStance.wallSlideSpeed);
            Motor.SetFacing(FacingExtensions.FromSign(-_wallSide));
        }

        private bool TryConsumeWallJump()
        {
            if (CurrentStance == null || !CurrentStance.canWallJump) return false;
            if (Motor.Grounded) return false;
            if (Time.time > _jumpBufferedUntil) return false;

            var touching = _wallSide != 0;
            if (!touching && Time.time > _wallCoyoteUntil) return false;

            var side = touching ? _wallSide : _lastWallSide;
            if (side == 0) return false;

            _jumpBufferedUntil = 0f;
            _wallCoyoteUntil = 0f;

            var gravity = CurrentStance.RiseGravity > 0.01f ? CurrentStance.RiseGravity : Mathf.Abs(Physics.gravity.y) * 3f;
            Motor.Velocity.x = -side * CurrentStance.wallJumpSpeedX;
            Motor.Velocity.y = Mathf.Sqrt(2f * gravity * CurrentStance.wallJumpHeight);
            Motor.SetFacing(FacingExtensions.FromSign(-side));

            _wallJumpLockUntil = Time.time + Timing.Frames(CurrentStance.wallJumpLockFrames);
            StoryEvents.Raise(GameEvents.WallJumped, side.ToString());
            return true;
        }

        private bool TryConsumeJump()
        {
            if (CurrentStance == null || !CurrentStance.canJump) return false;
            if (Time.time > _jumpBufferedUntil) return false;
            if (!Motor.Grounded && Time.time > _coyoteUntil) return false;

            // Down and jump on a one way ledge is a drop, not a jump. The
            // platform handles the drop; jumping as well would launch the
            // player upward through it instead.
            if (Input != null && Input.DownHeld && Motor.GroundCollider != null &&
                Motor.GroundCollider.GetComponentInParent<OneWayPlatform>() != null)
            {
                _jumpBufferedUntil = 0f;
                return false;
            }

            _jumpBufferedUntil = 0f;
            _coyoteUntil = 0f;
            Motor.Velocity.y = CurrentStance.JumpVelocity;
            spriteFeel?.Stretch();
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
            StoryEvents.Raise(GameEvents.Dashed, Stance.ToString());

            // Celeste's dash beat: the world holds for a few frames and the frame kicks
            // the way of the dash, so the dash reads as a burst instead of a fast walk.
            // The dash itself still starts this frame; only the world waits.
            HitStop.Freeze(3);
            CameraFeel.Instance?.Shake(0.07f, new Vector2(direction, 0f));

            // Crossing a lane belongs to the dash button, whichever school owns
            // the dash. It used to be the blink's alone, and moved with the
            // dash to Se in the October 2026 redraw.
            if (TryBlinkLane()) return true;

            if (CurrentStance.dashIsBlink)
            {
                // A blink does not travel through the space between, it stops
                // being in one place and starts being in the other. The landing
                // is checked so a blink aimed into a wall stops short of it
                // instead of burying the capsule inside the geometry.
                var target = transform.position + new Vector3(CurrentStance.dashDistance * direction, 0f, 0f);
                Motor.Teleport(SafeBlinkTarget(transform.position, target));
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
            var inWindow = CurrentStance.iFrameEnd > 0 &&
                           frame >= CurrentStance.iFrameStart && frame <= CurrentStance.iFrameEnd;
            SetDashIFrames(inWindow);

            if (_stateTimer < Timing.Frames(CurrentStance.dashFrames)) return;

            SetDashIFrames(false);
            Motor.Velocity.x = 0f;
            EnterState(PlayerState.Locomotion);
        }

        /// <summary>
        /// Dash invulnerability as its own counted hold on the Invincible tag.
        /// A respawn grace running at the same time holds its own, so neither
        /// can end the other early, which is the stacking bug the tag system
        /// was adopted to fix.
        /// </summary>
        private void SetDashIFrames(bool on)
        {
            if (on && _dashIFrames == null && _tags != null) _dashIFrames = _tags.Add(GameTag.Invincible);
            else if (!on && _dashIFrames != null)
            {
                _dashIFrames.Dispose();
                _dashIFrames = null;
            }
        }

        /// <summary>
        /// Walks a blink destination back toward the start until the capsule
        /// fits there.
        /// </summary>
        private Vector3 SafeBlinkTarget(Vector3 from, Vector3 to)
        {
            var controller = Motor.Controller;
            if (controller == null) return to;

            var mask = LayerMask.GetMask("Default");
            var radius = controller.radius * 0.95f;
            var half = Mathf.Max(0f, controller.height * 0.5f - controller.radius);
            const int steps = 24;

            for (var i = 0; i <= steps; i++)
            {
                var candidate = Vector3.Lerp(to, from, i / (float)steps);
                // Lifted a hair, or a capsule resting on the floor counts as
                // overlapping the floor and every blink is refused.
                var centre = candidate + controller.center + Vector3.up * 0.06f;
                var blocked = Physics.CheckCapsule(centre + Vector3.up * half, centre + Vector3.down * half,
                    radius, mask, QueryTriggerInteraction.Ignore);
                if (!blocked) return candidate;
            }

            return from;
        }

        // ---- Attack ---------------------------------------------------------------

        private void TryConsumeAttack()
        {
            if (Time.time > _attackBufferedUntil || CurrentStance == null || hitbox == null) return;

            _attackBufferedUntil = 0f;
            // Light in the air with down held is the down attack, when the
            // stance has one. Checked first so it wins over a light combo.
            if (!_attackBufferHeavy && !Motor.Grounded && CurrentStance.HasDownAttack &&
                Input != null && Input.DownHeld)
            {
                _downAttacking = true;
                StartAttack(CurrentStance.downAttack, heavy: false);
                return;
            }

            _downAttacking = false;
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
                // A white slash on every swing, so a swing reads even before real attack art exists.
                var facing = Motor.Facing.Sign();
                GlowFx.Slash(transform.position + new Vector3(facing * 0.35f, 0.7f, -0.3f), facing,
                    _attack == CurrentStance.heavy ? 1.35f : 1f, Color.white);
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
            _downAttacking = false;
            if (Vitals != null) Vitals.SuperArmor = false;
            _attack = null;
            _comboIndex = 0;
            EnterState(PlayerState.Locomotion);
        }

        private void OnHitConnected(IDamageable victim, HitResult result, DamageInfo info)
        {
            CombatSignals.Report(victim, result, info, byPlayer: true, ranged: false);
            if (result is HitResult.Missed or HitResult.Ignored) return;

            ImpactFeel(victim, info, 1f);

            // Scaled off hit stop rather than off damage, because hit stop is
            // already the number tuned for how heavy a hit is meant to read.
            HitSpark.Play(info.point, info.knockback, info.hitStopFrames * 0.12f,
                result == HitResult.Hit ? Color.white : new Color(1f, 0.85f, 0.4f));

            // Heavy hits shake the plants around the impact (light jabs do not, or a
            // combo would keep the forest rattling).
            if (info.hitStopFrames >= 8) WindField.Ripple(info.point, Mathf.Clamp(info.hitStopFrames * 0.02f, 0.15f, 0.35f));

            if (result == HitResult.Parried) return;

            if (info.lifesteal > 0f && Vitals != null) Vitals.Heal(info.amount * info.lifesteal);
            if (_downAttacking && result != HitResult.Ignored)
            {
                Motor.Velocity.y = CurrentStance.pogoBounce;
                _coyoteUntil = 0f;
            }

            HealIfKilled(victim, result);
        }

        /// <summary>
        /// A shot from PlayerShooter connected. Gets the same freeze, shake and
        /// spark as a swing, scaled by the projectile's own smaller numbers,
        /// and the same heal on a kill.
        /// </summary>
        public void ReportRangedHit(IDamageable victim, HitResult result, DamageInfo info)
        {
            CombatSignals.Report(victim, result, info, byPlayer: true, ranged: true);
            if (result is HitResult.Missed or HitResult.Ignored) return;

            ImpactFeel(victim, info, 0.6f);
            HitSpark.Play(info.point, info.knockback, Mathf.Max(0.25f, info.hitStopFrames * 0.12f),
                result == HitResult.Hit ? Color.white : new Color(1f, 0.85f, 0.4f));

            HealIfKilled(victim, result);
        }

        /// <summary>
        /// In heals when its hit finishes something. Hitboxes and shots both
        /// skip anything already dead, so a victim that is dead after this hit
        /// was killed by it.
        /// </summary>
        // Every connecting hit gets a little shake, not only the heavies: light swings had a
        // shake of zero and read as hitting air. The floor comes from the hit stop, the number
        // already tuned for how heavy a hit is, so a jab nudges and a heavy thumps. The kick
        // points along the hit, plus a little downward, so it feels like weight landing.
        // A killing blow holds the freeze a few frames longer and hits harder: the moment
        // an enemy goes down should be the best feeling hit in the fight.
        private void ImpactFeel(IDamageable victim, DamageInfo info, float scale)
        {
            var killed = victim != null && !victim.IsAlive;
            HitStop.Freeze(info.hitStopFrames + (killed ? 4 : 0));
            var shake = Mathf.Max(info.screenShake, info.hitStopFrames * 0.016f) * 0.6f * scale;
            if (killed) shake = Mathf.Max(shake * 1.2f, 0.18f * scale);
            var along = Mathf.Abs(info.knockback.x) > 0.01f ? Mathf.Sign(info.knockback.x) : Motor.Facing.Sign();
            CameraFeel.Instance?.Shake(shake, new Vector2(along, -0.35f));
        }

        private void HealIfKilled(IDamageable victim, HitResult result)
        {
            if (CurrentStance == null || CurrentStance.healOnKill <= 0f || Vitals == null) return;
            if (result is HitResult.Missed or HitResult.Ignored or HitResult.Parried) return;
            if (victim == null || victim.IsAlive) return;

            Vitals.Heal(CurrentStance.healOnKill);
            StoryEvents.Raise(GameEvents.HealedOnKill, CurrentStance.healOnKill);
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
            SetDashIFrames(false);
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

            if (!StanceProgress.IsLearned(target))
            {
                StoryEvents.Raise(GameEvents.StanceLocked, target.ToString());
                return false;
            }

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

        /// <summary>
        /// Puts the player in a stance with no switch window. Loading a save
        /// and the debug console only; gameplay always goes through
        /// TryStartSwitch so the 0.4 second price is paid.
        /// </summary>
        public void ForceStance(StanceId id)
        {
            if (State == PlayerState.Dead) return;
            ApplyStance(id, refillHealth: false);
            if (State == PlayerState.StanceSwitch) EnterState(PlayerState.Locomotion);
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

            ApplyStanceTags(stance);
            if (_reactor != null) _reactor.KnockbackResistance = stance.hyperArmor ? stance.knockbackResistance : 0f;

            StoryState.Global.Set(StateKeys.Stance, (int)id);
            StoryEvents.Raise(GameEvents.StanceSwitched, id.ToString());
        }

        /// <summary>
        /// A school's standing traits as tags, so level pieces ask "is this
        /// body heavy" instead of "is this Than". A plate that reads the tag
        /// keeps working if weight ever moves to another school or an item.
        /// </summary>
        private void ApplyStanceTags(StanceDefinition stance)
        {
            foreach (var handle in _stanceTags) handle.Dispose();
            _stanceTags.Clear();
            if (_tags == null) return;

            if (stance.hyperArmor) _stanceTags.Add(_tags.Add(GameTag.HyperArmor));
            if (stance.isHeavy) _stanceTags.Add(_tags.Add(GameTag.Heavy));
            if (stance.poisonImmune) _stanceTags.Add(_tags.Add(GameTag.PoisonImmune));
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

        private void OnLanded(float impactSpeed)
        {
            spriteFeel?.Land(impactSpeed);
            // Only a real drop nudges the camera down; an ordinary jump landing must not,
            // or every hop in a fight shakes the frame.
            if (impactSpeed > 11f) CameraFeel.Instance?.Shake(Mathf.InverseLerp(11f, 22f, impactSpeed) * 0.18f, Vector2.down);
            // Than's weight shakes the plants around where it lands.
            if (CurrentStance != null && CurrentStance.isHeavy && impactSpeed > 9f)
                WindField.Ripple(transform.position, Mathf.InverseLerp(9f, 20f, impactSpeed) * 0.3f + 0.1f);
        }

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

            // Iron never flinches, so Than keeps its normal pose through a hit and plays no hurt drawing.
            if (State == PlayerState.Hurt && !(CurrentStance != null && CurrentStance.hyperArmor))
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
