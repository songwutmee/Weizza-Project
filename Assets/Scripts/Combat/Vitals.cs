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
        /// <summary>Dev console "onehit": anything the player hits dies in one hit, bosses included.</summary>
        public static bool PlayerOneHitKill;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => PlayerOneHitKill = false;

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
        [PropertyRange(0, 12)]
        [Tooltip("Hollow Knight health. Above 0, health is this many wings and every hit " +
                 "costs whole wings no matter how hard it was: one wing, or more only when a " +
                 "damage multiplier rounds past 1. Heals and poison collect in a bank and move " +
                 "whole wings at a time. 0 is ordinary health, which every enemy uses.")]
        [SerializeField] private int wings;

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

        /// <summary>
        /// Hits never take health below this. A boss whose death is a story beat sets
        /// it to its finisher point, so even a one hit kill lands on the finisher and
        /// plays the scene instead of skipping it. Kill() still ignores it.
        /// </summary>
        public float HealthFloor { get; set; }

        /// <summary>Number of wings, 0 when this body uses ordinary health.</summary>
        public int Wings
        {
            get => wings;
            set
            {
                wings = Mathf.Max(0, value);
                _healBank = 0f;
                _drainBank = 0f;
                if (wings > 0) Current = SnapToWing(Current);
            }
        }

        public bool UsesWings => wings > 0;
        public float WingSize => wings > 0 ? maxHealth / wings : maxHealth;
        public int WingsLeft => wings > 0 ? Mathf.RoundToInt(Current / WingSize) : 0;

        /// <summary>How far the next wing has regrown from banked healing, 0 to 1. The HUD draws it.</summary>
        public float PendingWing => wings > 0 ? Mathf.Clamp01(_healBank / WingSize) : 0f;

        private float _healBank;
        private float _drainBank;

        private float SnapToWing(float value) =>
            value <= 0f ? 0f : Mathf.Clamp(Mathf.Ceil(value / WingSize - 0.001f) * WingSize, WingSize, maxHealth);

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
        private TagContainer _tags;
        private EffectReceiver _effects;

        /// <summary>
        /// Invincible from the flag or from any counted hold on the tag. New
        /// code should hold the tag; the flag stays for the code that already
        /// sets it.
        /// </summary>
        public bool IsInvincible => Invincible || (_tags != null && _tags.Has(GameTag.Invincible));

        /// <summary>Armoured from a swing (super armour) or as a standing trait (hyper armour).</summary>
        public bool IsArmored => SuperArmor ||
                                 (_tags != null && (_tags.Has(GameTag.SuperArmor) || _tags.Has(GameTag.HyperArmor)));

        private void Awake()
        {
            _motor = GetComponent<CharacterMotor3D>();
            _tags = GetComponent<TagContainer>();
            Current = maxHealth;
        }

        /// <summary>Lets a TagContainer added after Awake be found.</summary>
        public void RefreshTags() => _tags = GetComponent<TagContainer>();

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
            if (_tags == null) _tags = GetComponent<TagContainer>();

            if (info.defendable)
            {
                if (Parrying)
                {
                    Parried?.Invoke(info.source);
                    Damaged?.Invoke(info, HitResult.Parried);
                    return HitResult.Parried;
                }

                if (IsInvincible)
                {
                    Damaged?.Invoke(info, HitResult.Ignored);
                    return HitResult.Ignored;
                }
            }

            var result = IsArmored && info.defendable ? HitResult.Armored : HitResult.Hit;

            if (!GodMode)
            {
                if (_effects == null) _effects = GetComponent<EffectReceiver>();
                var fromEffects = _effects != null ? _effects.DamageTakenMultiplier : 1f;
                var scale = Mathf.Max(0f, DamageMultiplier) * fromEffects;
                if (wings > 0)
                {
                    // Every hit is one wing. A multiplier only matters once it
                    // rounds to 2 or more, so In's fragility costs two wings only
                    // from something that genuinely doubles damage.
                    var lost = Mathf.Max(1, Mathf.RoundToInt(scale));
                    Current = Mathf.Max(0f, Current - lost * WingSize);
                }
                else if (PlayerOneHitKill && info.source != null && info.source.GetComponentInParent<PlayerController>() != null)
                {
                    Current = Mathf.Min(Current, HealthFloor);
                }
                else
                {
                    Current = Mathf.Max(Mathf.Min(Current, HealthFloor), Current - info.amount * scale);
                }
            }

            Damaged?.Invoke(info, result);

            if (Current <= 0f) Died?.Invoke();
            return result;
        }

        public void Heal(float amount)
        {
            if (amount <= 0f || !IsAlive) return;
            var before = Current;
            if (wings > 0)
            {
                // Heals fill a bank; each full wing's worth grows one wing back.
                // A full butterfly keeps no bank, or healing at full would let the
                // next hit be refunded on the spot.
                _healBank += amount;
                while (_healBank >= WingSize && Current < maxHealth - 0.001f)
                {
                    _healBank -= WingSize;
                    Current = Mathf.Min(maxHealth, Current + WingSize);
                }
                if (Current >= maxHealth - 0.001f) _healBank = 0f;
            }
            else
            {
                Current = Mathf.Min(maxHealth, Current + amount);
            }
            if (Current > before) Healed?.Invoke(Current - before);
        }

        /// <summary>
        /// Damage over time (poison) that skips hit reactions. With wings it banks
        /// and takes whole wings, so a poison tick never leaves half a wing.
        /// canKill false never takes the last wing, or the last point of health.
        /// </summary>
        public void Drain(float amount, bool canKill)
        {
            if (amount <= 0f || !IsAlive || GodMode) return;
            if (wings > 0)
            {
                _drainBank += amount;
                while (_drainBank >= WingSize)
                {
                    if (!canKill && Current <= WingSize + 0.001f) { _drainBank = 0f; return; }
                    _drainBank -= WingSize;
                    SetHealth(Current - WingSize);
                    if (!IsAlive) return;
                }
                return;
            }
            if (!canKill) amount = Mathf.Min(amount, Mathf.Max(0f, Current - 1f));
            if (amount > 0f) SetHealth(Current - amount);
        }

        /// <summary>Debug and scripted revival. Does not raise Damaged.</summary>
        public void SetHealth(float value)
        {
            var wasAlive = IsAlive;
            Current = Mathf.Clamp(value, 0f, maxHealth);
            if (wings > 0) Current = SnapToWing(Current);
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
