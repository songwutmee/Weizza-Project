using System;
using Sirenix.OdinInspector;
using UnityEngine;

namespace Weizza
{
    /// <summary>
    /// How much punishment a character absorbs before it flinches.
    ///
    /// This is the single mechanic that separates a grunt from a mini boss,
    /// and it matters far more than the health bar does. A grunt with low
    /// poise staggers on almost every hit, so a combo lands as one unbroken
    /// beating and feels great to throw. A boss with high poise eats the
    /// jabs, keeps swinging, and only buckles when a heavy or the end of a
    /// full combo finally breaks the meter, which is what makes the player
    /// stop mashing and start choosing.
    ///
    /// Give a boss more health instead and all that happens is the same
    /// stunlock takes longer. Damage and poise are separate on purpose:
    /// every hit always costs health, only some of them cost composure.
    ///
    /// Leave this component off a character and every hit staggers, which
    /// is the right default for anything small.
    /// </summary>
    [AddComponentMenu("Weizza/Poise")]
    public sealed class Poise : MonoBehaviour
    {
        [BoxGroup("Meter", centerLabel: true)]
        [InfoBox("Compare this against the poise damage on the attacks that " +
                 "will be used against it. A grunt wants a max low enough " +
                 "that one light attack breaks it. A mini boss wants a max " +
                 "around a full light combo, so mashing never holds it still.")]
        [MinValue(0f)]
        [SerializeField] private float maxPoise = 20f;

        [BoxGroup("Meter")]
        [Tooltip("Poise regained per second once nothing has connected for the " +
                 "grace period below. Too fast and combos never break it, too " +
                 "slow and one opening lasts the whole fight.")]
        [MinValue(0f)]
        [SerializeField] private float recoveryPerSecond = 12f;

        [BoxGroup("Meter")]
        [Tooltip("Seconds after the last hit before recovery starts. This is " +
                 "what keeps a combo's gaps from refilling the meter mid combo.")]
        [PropertyRange(0f, 3f)]
        [SerializeField] private float recoveryDelay = 1.2f;

        [BoxGroup("Break", centerLabel: true)]
        [Tooltip("Extra frames of helplessness on top of the attack's own hit " +
                 "stun, given only on the hit that actually breaks poise. This " +
                 "is the reward for working the meter down.")]
        [PropertyRange(0, 90)]
        [SerializeField] private int breakBonusStunFrames = 24;

        [BoxGroup("Break")]
        [Tooltip("Fraction of the meter refilled after a break. Zero means the " +
                 "next hit staggers again immediately, which turns a broken " +
                 "boss back into a grunt.")]
        [PropertyRange(0f, 1f)]
        [SerializeField] private float refillAfterBreak = 1f;

        [FoldoutGroup("Live", expanded: true)]
        [ShowInInspector, ReadOnly]
        [ProgressBar(0, nameof(Max))]
        public float Current { get; private set; }

        [FoldoutGroup("Live")]
        [ShowInInspector, ReadOnly] public bool Broken { get; private set; }

        public float Max => maxPoise;

        public float Normalized => maxPoise <= 0f ? 0f : Mathf.Clamp01(Current / maxPoise);

        /// <summary>Extra stun frames the breaking hit is worth.</summary>
        public int BreakBonusStunFrames => breakBonusStunFrames;

        /// <summary>Raised on the hit that empties the meter.</summary>
        public event Action Broke;

        private float _recoverAt;

        private void Awake() => Current = maxPoise;

        private void Update()
        {
            if (recoveryPerSecond <= 0f || Current >= maxPoise) return;
            if (Time.time < _recoverAt) return;

            Current = Mathf.Min(maxPoise, Current + recoveryPerSecond * Time.deltaTime);
            if (Current > 0f) Broken = false;
        }

        /// <summary>
        /// Spends the meter on one hit and answers the only question the hit
        /// reaction actually needs: does this one stagger, or does the
        /// character shrug it off and carry on.
        /// </summary>
        public bool AbsorbHit(float poiseDamage)
        {
            _recoverAt = Time.time + recoveryDelay;

            // A character authored with no meter at all is one that always
            // flinches. Saying that here rather than making every caller
            // check keeps the rule in one place.
            if (maxPoise <= 0f) return true;

            Current = Mathf.Max(0f, Current - Mathf.Max(0f, poiseDamage));
            if (Current > 0f) return false;

            Broken = true;
            Broke?.Invoke();
            Current = maxPoise * refillAfterBreak;
            return true;
        }

        /// <summary>Refills the meter. For phase changes and scripted resets.</summary>
        public void Refill(float scale = 1f)
        {
            Current = maxPoise * Mathf.Clamp01(scale);
            Broken = false;
        }

        /// <summary>Retunes the ceiling, used when a boss enters a new phase.</summary>
        public void SetMax(float value, bool refill)
        {
            maxPoise = Mathf.Max(0f, value);
            if (refill) Refill();
            else Current = Mathf.Min(Current, maxPoise);
        }
    }
}
