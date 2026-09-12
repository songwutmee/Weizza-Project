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

        [FoldoutGroup("Live")]
        [ShowInInspector, ReadOnly]
        [Tooltip("True while a hit is still holding this character's actions " +
                 "hostage. Owning controllers should stop reading input while " +
                 "this is true.")]
        public bool InHitstun { get; private set; }

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
                if (motor != null && knockbackScale > 0f)
                {
                    motor.Velocity.x = info.knockback.x * knockbackScale;
                    motor.Velocity.y = Mathf.Max(motor.Velocity.y, info.knockback.y * knockbackScale);
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
