using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Sirenix.OdinInspector;
using UnityEngine;

namespace Weizza
{
    /// <summary>
    /// What every character does the instant it is on the receiving end of a
    /// hit: flash, get shoved, and lose control for a few frames.
    ///
    /// Vitals decides what a hit costs. This decides what it looks and feels
    /// like. Player and enemy both point at the same component so a change
    /// here never has to be typed twice, and never drifts between the two
    /// the way it would if each controller rolled its own reaction.
    /// </summary>
    [AddComponentMenu("Weizza/Hit Reactor")]
    [RequireComponent(typeof(Vitals))]
    public sealed class HitReactor : MonoBehaviour
    {
        [BoxGroup("Setup")]
        [Tooltip("Leave empty to use the Vitals on this object's own motor.")]
        [SerializeField] private CharacterMotor3D motor;

        [BoxGroup("Setup")]
        [Tooltip("Leave empty to find one in children.")]
        [SerializeField] private SpriteFeel spriteFeel;

        [BoxGroup("Knockback", centerLabel: true)]
        [PropertyRange(0f, 2f)]
        [Tooltip("0 flashes but never moves the character. For something that " +
                 "should read as a wall rather than a body.")]
        [SerializeField] private float knockbackScale = 1f;

        [BoxGroup("Juggle", centerLabel: true)]
        [InfoBox("A launcher hit throws the target up. While it is in the air, " +
                 "every hit that breaks through pops it a little higher and gravity " +
                 "eases off, so a combo can keep it aloft. Absolum keeps this as a " +
                 "reserved tool; AttackDefinition.launcher is what triggers it.")]
        [PropertyRange(0f, 20f)]
        [SerializeField] private float launchSpeed = 9f;

        [BoxGroup("Juggle")]
        [PropertyRange(0f, 10f)]
        [Tooltip("Upward speed given by a hit that lands while already airborne.")]
        [SerializeField] private float airHitPop = 3.5f;

        [BoxGroup("Juggle")]
        [PropertyRange(0.1f, 1f)]
        [Tooltip("Gravity multiplier while juggled.")]
        [SerializeField] private float juggleGravityScale = 0.55f;

        [BoxGroup("Juggle")]
        [PropertyRange(0, 120)]
        [Tooltip("Frames the reduced gravity lasts after the last juggling hit.")]
        [SerializeField] private int juggleFrames = 40;

        /// <summary>Gravity multiplier the owner should apply. Below 1 while juggled.</summary>
        public float GravityScale => Time.time < _juggleUntil ? juggleGravityScale : 1f;

        public bool Juggled => Time.time < _juggleUntil;

        private float _juggleUntil;

        [FoldoutGroup("Live")]
        [ShowInInspector, ReadOnly]
        [Tooltip("True while a hit is still holding this character's actions " +
                 "hostage. Owning controllers should stop reading input while " +
                 "this is true.")]
        public bool InHitstun { get; private set; }

        /// <summary>
        /// Fraction of knockback removed, on top of the authored scale. Set by
        /// the player controller from the stance (Than's weight), zero for
        /// everyone else.
        /// </summary>
        [FoldoutGroup("Live")]
        [ShowInInspector, ReadOnly, PropertyRange(0f, 1f)]
        public float KnockbackResistance { get; set; }

        /// <summary>Raised after the flash/knockback/stun is already applied.</summary>
        public event Action<DamageInfo, HitResult> Reacted;

        /// <summary>
        /// Raised only for hits that actually broke through. A brain listens
        /// to this to drop whatever it was doing; a hit that was shrugged off
        /// deliberately does not raise it, so a boss keeps swinging through
        /// chip damage.
        /// </summary>
        public event Action<DamageInfo> Staggered;

        private Vitals _vitals;
        private Poise _poise;
        private CancellationTokenSource _hitstunLife;

        private void Awake()
        {
            _vitals = GetComponent<Vitals>();
            _poise = GetComponent<Poise>();
            if (motor == null) motor = GetComponent<CharacterMotor3D>();
            if (spriteFeel == null) spriteFeel = GetComponentInChildren<SpriteFeel>();
        }

        private void OnEnable() => _vitals.Damaged += OnDamaged;

        private void OnDisable()
        {
            _vitals.Damaged -= OnDamaged;
            CancelStun();
            InHitstun = false;
        }

        private void OnDamaged(DamageInfo info, HitResult result)
        {
            // Armored (Than's hyper armor), Ignored (i-frames) and Parried
            // never flinch and never get pushed, whatever the meter says.
            // Only a plain Hit gets as far as asking poise.
            var broke = result == HitResult.Hit &&
                        (_poise == null || _poise.AbsorbHit(info.poiseDamage));

            // A hit that landed but did not move the character reads to the
            // player exactly like armour, so it borrows armour's colour. One
            // tint to learn: that one did damage but did not open anything.
            var shown = result == HitResult.Hit && !broke ? HitResult.Armored : result;
            spriteFeel?.FlashFor(shown);

            if (broke)
            {
                var scale = knockbackScale * (1f - Mathf.Clamp01(KnockbackResistance));
                if (motor != null && scale > 0f)
                {
                    motor.Velocity.x = info.knockback.x * scale;
                    motor.Velocity.y = Mathf.Max(motor.Velocity.y, info.knockback.y * scale);
                }

                if (motor != null && scale > 0f)
                {
                    var airborne = !motor.Grounded;
                    if (info.launcher) motor.Velocity.y = Mathf.Max(motor.Velocity.y, launchSpeed * scale);
                    else if (airborne && Juggled) motor.Velocity.y = Mathf.Max(motor.Velocity.y, airHitPop * scale);
                    if (info.launcher || (airborne && Juggled)) _juggleUntil = Time.time + Timing.Frames(juggleFrames);
                }

                var bonus = _poise != null && _poise.Broken ? _poise.BreakBonusStunFrames : 0;
                Stun(info.hitStunFrames + bonus);
                Staggered?.Invoke(info);
            }

            Reacted?.Invoke(info, result);
        }

        private void Stun(int frames)
        {
            CancelStun();
            if (frames <= 0)
            {
                InHitstun = false;
                return;
            }

            _hitstunLife = new CancellationTokenSource();
            StunAsync(Timing.Frames(frames), _hitstunLife.Token).Forget();
        }

        private void CancelStun()
        {
            _hitstunLife?.Cancel();
            _hitstunLife?.Dispose();
            _hitstunLife = null;
        }

        private async UniTaskVoid StunAsync(float seconds, CancellationToken token)
        {
            InHitstun = true;
            try
            {
                // Scaled time on purpose: while HitStop has frozen the world,
                // this should not be quietly ticking down unseen.
                await UniTask.Delay(TimeSpan.FromSeconds(seconds),
                    DelayType.DeltaTime, PlayerLoopTiming.Update, token);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            InHitstun = false;
        }
    }
}
