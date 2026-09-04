using System;
using Sirenix.OdinInspector;
using UnityEngine;

namespace Weizza
{
    /// <summary>
    /// Health, and the three answers a character can give to being hit:
    /// take it, shrug it off, or refuse it.
    ///
    /// The defensive flags are set by whoever owns the character (the player
    /// controller, an enemy brain) and read here, so there is exactly one place
    /// that decides what a hit does. Splitting that decision between attacker
    /// and victim is how a parry ends up eating damage anyway on some frames.
    /// </summary>
    [AddComponentMenu("Weizza/Vitals")]
    public sealed class Vitals : MonoBehaviour, IDamageable
    {
        [BoxGroup("Health", centerLabel: true)]
        [HorizontalGroup("Health/Row")]
        [LabelWidth(60)]
        [MinValue(1)]
        [SerializeField] private float maxHealth = 100f;

        [HorizontalGroup("Health/Row")]
        [LabelWidth(80)]
        [ShowInInspector, ReadOnly]
        public float Current { get; private set; }

        [BoxGroup("Health")]
        [ProgressBar(0, nameof(Max), ColorGetter = nameof(BarColor))]
        [ShowInInspector, HideLabel]
        private float HealthBar => Current;

        [BoxGroup("Defence", centerLabel: true)]
        [ShowInInspector, ReadOnly]
        [Tooltip("Set by In's blink and by any scripted invulnerability.")]
        public bool Invincible { get; set; }

        [BoxGroup("Defence")]
        [ShowInInspector, ReadOnly]
        [Tooltip("Set by Than's heavy attacks. Damage lands, the flinch does not.")]
        public bool SuperArmor { get; set; }

        [BoxGroup("Defence")]
        [ShowInInspector, ReadOnly]
        [Tooltip("Set by Than's parry for the length of its window.")]
        public bool Parrying { get; set; }

        [BoxGroup("Defence")]
        [ShowInInspector, ReadOnly]
        [Tooltip("Stance fragility times any temporary penalty, such as the " +
                 "stance switch window.")]
        public float DamageMultiplier { get; set; } = 1f;

        [BoxGroup("Debug")]
        [ShowInInspector]
        [Tooltip("Debug window only. Nothing in gameplay sets this.")]
        public bool GodMode { get; set; }

        public float Max => maxHealth;
        public float Normalized => maxHealth <= 0f ? 0f : Mathf.Clamp01(Current / maxHealth);
        public bool IsAlive => Current > 0f;
        public Transform Transform => transform;

        public float DepthForHits => _motor != null ? _motor.Depth : 0f;

        /// <summary>Damage that actually landed, and how it was received.</summary>
        public event Action<DamageInfo, HitResult> Damaged;

        /// <summary>Raised once when health reaches zero.</summary>
        public event Action Died;

        /// <summary>Raised when a parry catches a hit, with the attacker.</summary>
        public event Action<GameObject> Parried;

        public event Action<float> Healed;

        private CharacterMotor3D _motor;

        private void Awake()
        {
            _motor = GetComponent<CharacterMotor3D>();
            Current = maxHealth;
        }

        /// <summary>Changes the ceiling, keeping the same fraction filled.</summary>
        public void SetMaxHealth(float value, bool refill)
        {
            var fraction = Normalized;
            maxHealth = Mathf.Max(1f, value);
            Current = refill ? maxHealth : maxHealth * fraction;
        }

        public HitResult Receive(in DamageInfo info)
        {
            if (!IsAlive) return HitResult.Missed;

            if (info.defendable)
            {
                if (Parrying)
                {
                    Parried?.Invoke(info.source);
                    Damaged?.Invoke(info, HitResult.Parried);
                    return HitResult.Parried;
                }

                if (Invincible)
                {
                    Damaged?.Invoke(info, HitResult.Ignored);
                    return HitResult.Ignored;
                }
            }

            var result = SuperArmor && info.defendable ? HitResult.Armored : HitResult.Hit;

            if (!GodMode)
            {
                Current = Mathf.Max(0f, Current - info.amount * Mathf.Max(0f, DamageMultiplier));
            }

            Damaged?.Invoke(info, result);

            if (Current <= 0f) Died?.Invoke();
            return result;
        }

        public void Heal(float amount)
        {
            if (amount <= 0f || !IsAlive) return;
            var before = Current;
            Current = Mathf.Min(maxHealth, Current + amount);
            if (Current > before) Healed?.Invoke(Current - before);
        }

        /// <summary>Debug and scripted revival. Does not raise Damaged.</summary>
        public void SetHealth(float value)
        {
            var wasAlive = IsAlive;
            Current = Mathf.Clamp(value, 0f, maxHealth);
            if (wasAlive && Current <= 0f) Died?.Invoke();
        }

        public void Kill()
        {
            if (!IsAlive) return;
            Current = 0f;
            Died?.Invoke();
        }

        private Color BarColor()
        {
            if (!IsAlive) return new Color(0.35f, 0.35f, 0.38f);
            if (Normalized > 0.5f) return new Color(0.35f, 0.75f, 0.4f);
            return Normalized > 0.25f
                ? new Color(0.9f, 0.72f, 0.25f)
                : new Color(0.85f, 0.3f, 0.3f);
        }
    }
}
