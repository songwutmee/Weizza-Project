using Sirenix.OdinInspector;
using StoryKit;
using UnityEngine;

namespace Weizza
{
    /// <summary>
    /// Fires the current stance's ranged kit. Does nothing at all while the
    /// stance has no kit, so it can sit on the player permanently and only
    /// wakes up in In.
    ///
    /// Three modes, picked on the kit asset (and overridable here to compare
    /// them in play):
    ///
    ///   Cursor Aim   reticle on the cursor, left click fires at it, hold right
    ///                click to slow time and line the shot up
    ///   Power Throw  hold to wind up, release to throw, a tap lands close and a
    ///                full hold lands far, gravity always pulls it down
    ///   Charge Line  hold to charge, time slows and the path draws itself out,
    ///                release fires harder the longer it was held
    ///
    /// Every hold timer runs on unscaled time. The modes slow the world down,
    /// and a charge that also slowed down would take four times longer to fill
    /// exactly when the player has chosen to take their time.
    /// </summary>
    [AddComponentMenu("Weizza/Player Shooter")]
    [RequireComponent(typeof(PlayerController))]
    public sealed class PlayerShooter : MonoBehaviour
    {
        private enum Phase { Ready, Winding, Charging, QuickHold }

        [BoxGroup("Layers")]
        [Tooltip("What a shot can hurt. The Enemy layer.")]
        [SerializeField] private LayerMask targetMask;

        [BoxGroup("Layers")]
        [Tooltip("What a shot breaks on. Level geometry, usually Default.")]
        [SerializeField] private LayerMask worldMask;

        [BoxGroup("Compare modes", centerLabel: true)]
        [InfoBox("Ticked, the mode below wins over the one on the kit asset. For " +
                 "trying all three in the same room without editing data.")]
        [ToggleLeft]
        [SerializeField] private bool overrideMode;

        [BoxGroup("Compare modes")]
        [ShowIf(nameof(overrideMode))]
        [EnumToggleButtons, HideLabel]
        [SerializeField] private ShotMode modeOverride = ShotMode.CursorAim;

        [BoxGroup("Compare modes")]
        [ToggleLeft]
        [Tooltip("Number keys 7, 8 and 9 switch mode during play. Debug only.")]
        [SerializeField] private bool debugModeKeys = true;

        [FoldoutGroup("Live", expanded: true)]
        [ShowInInspector, ReadOnly] public ShotMode Mode => CurrentMode();

        [FoldoutGroup("Live")]
        [ShowInInspector, ReadOnly, ProgressBar(0, 1)] public float Charge { get; private set; }

        [FoldoutGroup("Live")]
        [ShowInInspector, ReadOnly] public bool Slowing => _slowing;

        [FoldoutGroup("Live")]
        [ShowInInspector, ReadOnly] public string Aiming => _usingPad ? "right stick" : _usingMouse ? "mouse" : "resting";

        /// <summary>World point the next shot is aimed at.</summary>
        public Vector3 AimPoint { get; private set; }

        /// <summary>Direction the next shot leaves in.</summary>
        public Vector2 AimDirection { get; private set; } = Vector2.right;

        /// <summary>True while the kit is winding up, charging, or aim slowed. The camera leans on this.</summary>
        public bool IsAiming => _phase != Phase.Ready || _slowing;

        private PlayerController _player;
        private AimReticle _reticle;
        private TrajectoryLine _line;
        private Transform _visualRoot;

        private Phase _phase;
        private float _holdStarted;
        private bool _announcedFull;
        private float _readyAt;
        private Vector2 _lastPointer;
        private bool _usingMouse;
        private bool _usingPad;
        private Targetable _assistTarget;

        private bool _slowing;
        private float _slowWeight;
        private float _slowRestore = 1f;
        private float _slowTarget = 1f;
        private float _aimSlowStarted;
        private float _aimSlowReadyAt;
        private bool _aimSlowSpent;

        private static InputReader Input => InputReader.Instance;

        private void Reset()
        {
            targetMask = LayerMask.GetMask("Enemy");
            worldMask = LayerMask.GetMask("Default");
        }

        private void Awake()
        {
            _player = GetComponent<PlayerController>();
            if (targetMask.value == 0) targetMask = LayerMask.GetMask("Enemy");
            if (worldMask.value == 0) worldMask = LayerMask.GetMask("Default");

            // Visuals live outside the player so its facing flip and the sprite
            // tilt never touch them.
            _visualRoot = new GameObject("[Aim Visuals]").transform;
            _reticle = new AimReticle(_visualRoot);
            _line = new TrajectoryLine(_visualRoot);

            // A ranged stance can fire several wards a second and a full charge
            // pierces; twelve spare covers a busy fight with no object made mid swing.
            Projectile.Prewarm(12);
        }

        private void OnDisable()
        {
            CancelHold();
            ForceEndSlow();
            _reticle?.SetVisible(false);
            _line?.SetVisible(false, 0.1f);
        }

        private void OnDestroy()
        {
            ForceEndSlow();
            _reticle?.Destroy();
            _line?.Destroy();
            if (_visualRoot != null) Destroy(_visualRoot.gameObject);
        }

        private void Update()
        {
            var kit = ActiveKit();
            var dt = Time.unscaledDeltaTime;

            if (kit == null || !_player.CanAct)
            {
                CancelHold();
                _slowTarget = 1f;
                TickSlow(false, dt);
                _reticle.SetVisible(false);
                _reticle.Tick(AimPoint, 0f, Color.white, Color.white, null);
                _line.SetVisible(false, 0.1f);
                _player.ExternalMoveScale = 1f;
                return;
            }

            if (debugModeKeys) ReadDebugKeys();

            ResolveAim(kit);

            switch (CurrentMode())
            {
                case ShotMode.CursorAim: TickCursor(kit, dt); break;
                case ShotMode.PowerThrow: TickPower(kit); break;
                case ShotMode.ChargeLine: TickCharge(kit, dt); break;
            }

            _player.ExternalMoveScale = _phase is Phase.Winding or Phase.Charging ? kit.moveScaleWhileHolding : 1f;

            var muzzle = Muzzle(kit);
            _reticle.Tick(AimPoint, Charge, kit.pathColour, kit.fullChargeColour,
                kit.aimAssist > 0f && _assistTarget != null ? _assistTarget.transform : null);
            var previewGravity = kit.projectile == null || CurrentMode() == ShotMode.CursorAim ? 0f : kit.projectile.gravity;
            _line.Tick(muzzle, (Vector3)(AimDirection * PreviewSpeed(kit)), previewGravity,
                kit.pathPreviewSeconds, kit.projectile != null ? kit.projectile.radius * 0.5f : 0.05f, worldMask,
                kit.pathWidth, Charge >= 0.999f ? kit.fullChargeColour : kit.pathColour, kit.pathScrollSpeed);
        }

        private void LateUpdate()
        {
            // Turned toward the aim after the controller has faced the stick,
            // so winding up a throw behind you actually turns around.
            if (ActiveKit() == null || !_player.CanAct) return;
            if (_phase != Phase.Ready || _slowing) FaceAim();
        }

        // ---- Modes --------------------------------------------------------------

        private void TickCursor(RangedKit kit, float dt)
        {
            _reticle.SetVisible(true);
            Charge = 0f;

            // Hold aim to slow everything down, for a limited real time, then
            // a cooldown before it can be used again. Releasing and pressing
            // again does not reset the clock, or the limit means nothing.
            var wantSlow = Input != null && Input.AimHeld && !_aimSlowSpent && Time.unscaledTime >= _aimSlowReadyAt;
            if (wantSlow && !_slowing) _aimSlowStarted = Time.unscaledTime;

            if (_slowing && Time.unscaledTime - _aimSlowStarted > kit.aimSlowMaxSeconds)
            {
                _aimSlowSpent = true;
                wantSlow = false;
            }

            if (Input != null && Input.AimReleased)
            {
                if (_slowing || _aimSlowSpent) _aimSlowReadyAt = Time.unscaledTime + kit.aimSlowCooldown;
                _aimSlowSpent = false;
            }

            _slowTarget = kit.aimSlowScale;
            TickSlow(wantSlow, dt);
            _line.SetVisible(kit.showPathWhileAiming && _slowing, 0.15f);

            if (Input != null && Input.LightPressed && CooledDown())
            {
                Fire(kit, kit.projectile.speed, 1f, 0, "CursorAim");
                _reticle.Punch(-0.15f);
            }
        }

        private void TickPower(RangedKit kit)
        {
            TickSlow(false, Time.unscaledDeltaTime);

            // No reticle and no path in this mode, ever. The only input is how
            // long the button is held: a tap drops the ward close, a long hold
            // sends it far, gravity does the rest.
            _reticle.SetVisible(false);
            _line.SetVisible(false, 0.1f);

            if (_phase == Phase.Ready)
            {
                Charge = 0f;
                if (Input != null && Input.LightPressed && CooledDown())
                {
                    _phase = Phase.Winding;
                    _holdStarted = Time.unscaledTime;
                }
                return;
            }

            Charge = Mathf.Clamp01((Time.unscaledTime - _holdStarted) / Mathf.Max(0.01f, kit.fullPowerSeconds));

            if (Input == null || Input.LightHeld) return;

            var power = kit.powerCurve != null ? kit.powerCurve.Evaluate(Charge) : Charge;
            Fire(kit, Mathf.Lerp(kit.minLaunchSpeed, kit.maxLaunchSpeed, power), 1f, 0, "PowerThrow");
            _phase = Phase.Ready;
            Charge = 0f;
        }

        private void TickCharge(RangedKit kit, float dt)
        {
            // The cursor is always there in charge mode, it says where. The line
            // only appears while charging and says how: the arc this power gives.
            // Feel comes from the reticle answering every step (a nudge on press,
            // tightening as power builds, a ping at full, a kick on release),
            // all on unscaled time so it stays sharp inside the slow down.
            _reticle.SetVisible(true);

            if (_phase == Phase.Ready)
            {
                Charge = 0f;
                _announcedFull = false;
                _line.SetVisible(false, 0.1f);
                TickSlow(false, dt);

                if (Input != null && Input.LightPressed && CooledDown())
                {
                    _phase = Phase.QuickHold;
                    _holdStarted = Time.unscaledTime;
                    _reticle.Punch(0.12f);
                }
                return;
            }

            var held = Time.unscaledTime - _holdStarted;
            var released = Input == null || !Input.LightHeld;

            // Below the start delay this is still a tap: a quick weak shot
            // with no slow down, so charge mode never takes away the ability
            // to just shoot.
            if (_phase == Phase.QuickHold)
            {
                if (released)
                {
                    Fire(kit, kit.chargeSpeed.x, 1f, 0, "ChargeTap");
                    _reticle.Punch(-0.1f);
                    _phase = Phase.Ready;
                    return;
                }

                if (held < kit.chargeStartDelay) return;
                _phase = Phase.Charging;
            }

            Charge = Mathf.Clamp01((held - kit.chargeStartDelay) / Mathf.Max(0.01f, kit.fullChargeSeconds));
            _slowTarget = kit.chargeSlowScale;
            TickSlow(true, dt);
            _line.SetVisible(true, kit.lineDrawSeconds);

            // The moment it is full is announced once, loudly enough to feel
            // without looking: the reticle pings, the camera ticks, audio gets
            // an event. Releasing early is a choice; missing the moment is not.
            if (Charge >= 0.999f && !_announcedFull)
            {
                _announcedFull = true;
                _reticle.Punch(0.35f);
                CameraFeel.Instance?.Shake(0.06f);
                StoryEvents.Raise(GameEvents.ShotChargeFull);
            }

            var overHeld = held - kit.chargeStartDelay - kit.fullChargeSeconds > kit.chargeMaxHoldSeconds;
            if (!released && !overHeld) return;

            var full = Charge >= 0.999f;
            Fire(kit, Mathf.Lerp(kit.chargeSpeed.x, kit.chargeSpeed.y, Charge),
                Mathf.Lerp(1f, kit.fullChargeDamage, Charge), full ? kit.fullChargeExtraPierce : 0,
                full ? "ChargeFull" : "Charge");

            // Release kick scales with power, and a full shot gets a few frames of
            // hit stop on the way out, so letting go of a big charge has weight.
            _reticle.Punch(-0.25f);
            CameraFeel.Instance?.Shake(Mathf.Lerp(0.03f, 0.16f, Charge), new Vector2(-AimDirection.x, -AimDirection.y));
            if (full) HitStop.Freeze(3);

            _phase = Phase.Ready;
            Charge = 0f;
            TickSlow(false, dt);
        }

        // ---- Firing ----------------------------------------------------------------

        private void Fire(RangedKit kit, float speed, float damageScale, int extraPierce, string label)
        {
            if (kit.projectile == null) return;

            _readyAt = Time.time + kit.CooldownSeconds;
            FaceAim();

            var spec = new ShotSpec
            {
                definition = kit.projectile,
                origin = Muzzle(kit),
                velocity = (Vector3)(AimDirection * speed),
                owner = gameObject,
                targetMask = targetMask,
                worldMask = worldMask,
                damageScale = damageScale,
                extraPierce = extraPierce,
                // Cursor Aim flies dead straight to the reticle. The other two
                // modes are throws, and the arc is the point of them.
                straight = CurrentMode() == ShotMode.CursorAim,
                onConnected = _player.ReportRangedHit,
            };

            Projectile.Fire(spec);
            StoryEvents.Raise(GameEvents.ShotFired, label);
        }

        private bool CooledDown() => Time.time >= _readyAt;

        private float PreviewSpeed(RangedKit kit)
        {
            switch (CurrentMode())
            {
                case ShotMode.PowerThrow:
                    var power = kit.powerCurve != null ? kit.powerCurve.Evaluate(Charge) : Charge;
                    return Mathf.Lerp(kit.minLaunchSpeed, kit.maxLaunchSpeed, power);
                case ShotMode.ChargeLine:
                    return Mathf.Lerp(kit.chargeSpeed.x, kit.chargeSpeed.y, Charge);
                default:
                    return kit.projectile != null ? kit.projectile.speed : 10f;
            }
        }

        // ---- Aim ---------------------------------------------------------------------

        private void ResolveAim(RangedKit kit)
        {
            var muzzle = Muzzle(kit);
            var facing = _player.Motor.Facing.Sign();
            var stick = Input != null ? Input.AimStick : Vector2.zero;
            var pointer = Input != null ? Input.PointerPosition : Vector2.zero;

            if (stick.magnitude > kit.padDeadzone)
            {
                _usingPad = true;
                _usingMouse = false;
            }
            else if ((pointer - _lastPointer).sqrMagnitude > 1f || (Input != null && (Input.AimHeld || Input.LightPressed) && !_usingPad))
            {
                _usingMouse = pointer != Vector2.zero;
                _usingPad = false;
            }
            _lastPointer = pointer;

            Vector2 direction;
            Vector3 point;

            if (CurrentMode() == ShotMode.PowerThrow)
            {
                // Throw mode ignores the mouse and the aim stick on purpose: the
                // ward always leaves forward at the kit's angle. Up and down on
                // the move stick tilt it, which is the only aiming there is.
                var move = Input != null ? Input.MoveRaw.y : 0f;
                var angle = move > 0.5f ? kit.throwAngleUp : move < -0.5f ? kit.throwAngleDown : kit.throwAngle;
                AimDirection = Ballistics.FromAngle(facing, angle);
                AimPoint = muzzle + (Vector3)(AimDirection * kit.padReticleDistance);
                _assistTarget = null;
                return;
            }

            if (_usingPad && stick.magnitude > kit.padDeadzone)
            {
                direction = stick.normalized;
                point = muzzle + (Vector3)(direction * kit.padReticleDistance);
            }
            else if (_usingMouse && Ballistics.ScreenToLane(Camera.main, pointer, muzzle.z, out var world))
            {
                point = world;
                Vector2 to = world - muzzle;
                direction = to.sqrMagnitude > 0.0001f ? to.normalized : new Vector2(facing, 0f);
            }
            else
            {
                direction = Ballistics.FromAngle(facing, kit.restAngle);
                point = muzzle + (Vector3)(direction * kit.padReticleDistance);
            }

            // Aim magnetism, blended rather than snapped, so the player's own
            // aim still decides which enemy and roughly where.
            _assistTarget = kit.aimAssist > 0f
                ? Targetable.BestAlong(muzzle, direction, kit.aimAssistAngle, 14f, targetMask)
                : null;

            if (_assistTarget != null)
            {
                Vector2 toTarget = _assistTarget.AimPoint - muzzle;
                direction = Vector3.Slerp(direction, toTarget.normalized, kit.aimAssist);
                direction.Normalize();
            }

            AimDirection = direction;
            AimPoint = new Vector3(point.x, point.y, muzzle.z);
        }

        private Vector3 Muzzle(RangedKit kit)
        {
            var facing = _player.Motor.Facing.Sign();
            var p = transform.position;
            return new Vector3(p.x + kit.muzzleOffset.x * facing, p.y + kit.muzzleOffset.y, _player.Motor.Depth);
        }

        private void FaceAim()
        {
            if (Mathf.Abs(AimDirection.x) < 0.05f) return;
            _player.Motor.SetFacing(FacingExtensions.FromSign(AimDirection.x));
        }

        // ---- Slow time -------------------------------------------------------------

        /// <summary>
        /// Eases the base time scale toward the slow target and back.
        ///
        /// Goes through StoryTimeScale.SetBase, never Time.timeScale, so a
        /// hit stop landing mid aim still freezes properly and lifting it
        /// returns to the slow speed rather than to full speed.
        /// </summary>
        private void TickSlow(bool wanted, float dt)
        {
            const float rampSeconds = 0.08f;
            _slowWeight = Mathf.MoveTowards(_slowWeight, wanted ? 1f : 0f, dt / rampSeconds);

            if (_slowWeight > 0f && !_slowing)
            {
                _slowRestore = StoryTimeScale.Base;
                _slowing = true;
                StoryEvents.Raise(GameEvents.AimSlowStarted);
            }

            if (!_slowing) return;

            StoryTimeScale.SetBase(Mathf.Lerp(_slowRestore, _slowRestore * _slowTarget, _slowWeight));

            if (_slowWeight <= 0f) ForceEndSlow();
        }

        private void ForceEndSlow()
        {
            if (!_slowing) return;
            _slowing = false;
            _slowWeight = 0f;
            StoryTimeScale.SetBase(_slowRestore);
            StoryEvents.Raise(GameEvents.AimSlowEnded);
        }

        private void CancelHold()
        {
            _phase = Phase.Ready;
            Charge = 0f;
        }

        // ---- Mode selection --------------------------------------------------------

        private RangedKit ActiveKit()
        {
            var stance = _player != null ? _player.CurrentStance : null;
            return stance != null && stance.rangedKit != null && stance.rangedKit.projectile != null
                ? stance.rangedKit
                : null;
        }

        private ShotMode CurrentMode()
        {
            if (overrideMode) return modeOverride;
            var kit = ActiveKit();
            return kit != null ? kit.mode : ShotMode.CursorAim;
        }

        private void ReadDebugKeys()
        {
            var keyboard = UnityEngine.InputSystem.Keyboard.current;
            if (keyboard == null) return;

            ShotMode? picked = null;
            if (keyboard.digit7Key.wasPressedThisFrame) picked = ShotMode.CursorAim;
            if (keyboard.digit8Key.wasPressedThisFrame) picked = ShotMode.PowerThrow;
            if (keyboard.digit9Key.wasPressedThisFrame) picked = ShotMode.ChargeLine;
            if (!picked.HasValue) return;

            CancelHold();
            ForceEndSlow();
            overrideMode = true;
            modeOverride = picked.Value;
            Debug.Log($"[Weizza] Shot mode: {picked.Value}");
        }

        [FoldoutGroup("Live")]
        [Button("Cursor Aim"), HorizontalGroup("Live/Modes")]
        private void UseCursor() { overrideMode = true; modeOverride = ShotMode.CursorAim; }

        [Button("Power Throw"), HorizontalGroup("Live/Modes")]
        private void UsePower() { overrideMode = true; modeOverride = ShotMode.PowerThrow; }

        [Button("Charge Line"), HorizontalGroup("Live/Modes")]
        private void UseCharge() { overrideMode = true; modeOverride = ShotMode.ChargeLine; }
    }
}
