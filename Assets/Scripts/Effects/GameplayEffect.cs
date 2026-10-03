using Sirenix.OdinInspector;
using UnityEngine;

namespace Weizza
{
    /// <summary>What happens when the same effect lands on someone who already has it.</summary>
    public enum EffectStacking
    {
        /// <summary>The timer starts over. Poison from miasma: standing in it keeps it fresh.</summary>
        Refresh = 0,

        /// <summary>Another copy runs alongside, up to the stack limit. Every tick hits harder.</summary>
        Stack = 1,

        /// <summary>The new one is thrown away while the old one runs.</summary>
        Ignore = 2,
    }

    /// <summary>
    /// Something that lasts: poison, a shrine's regeneration, a haste, a slow.
    ///
    /// The design bible has a poison corridor and shrine buffs, and nothing in
    /// the code could say "damage over time for five seconds" before this.
    /// That is the whole reason effects were adopted, per the GAS decision in
    /// the project notes: tags first, then effects, ability objects probably never.
    /// </summary>
    [CreateAssetMenu(menuName = "Weizza/Gameplay Effect", fileName = "FX_New")]
    public sealed class GameplayEffect : ScriptableObject
    {
        [BoxGroup("Identity")]
        public string displayName = "Poison";

        [BoxGroup("Identity")]
        [Tooltip("Stable id the save and the console use. Never renamed.")]
        public string id = "poison";

        // ---- Timing -----------------------------------------------------------

        [BoxGroup("Timing", centerLabel: true)]
        [PropertyRange(0f, 60f)]
        [Tooltip("Seconds it lasts. Zero lasts until something removes it, such " +
                 "as leaving the zone that applied it.")]
        public float duration = 4f;

        [BoxGroup("Timing")]
        [PropertyRange(0.05f, 5f)]
        [Tooltip("Seconds between ticks of damage or healing.")]
        public float period = 0.5f;

        [BoxGroup("Timing")]
        [EnumToggleButtons]
        public EffectStacking stacking = EffectStacking.Refresh;

        [BoxGroup("Timing")]
        [ShowIf(nameof(Stacks))]
        [PropertyRange(1, 10)]
        public int maxStacks = 3;

        public bool Stacks => stacking == EffectStacking.Stack;

        // ---- Per tick ---------------------------------------------------------

        [BoxGroup("Per tick", centerLabel: true)]
        [MinValue(0f)]
        [Tooltip("Health lost every tick. Not defendable: i-frames and armour " +
                 "do not stop poison, or a blink would cure it.")]
        public float damagePerTick;

        [BoxGroup("Per tick")]
        [MinValue(0f)]
        public float healPerTick;

        [BoxGroup("Per tick")]
        [ToggleLeft]
        [Tooltip("Ticks can bring health to zero. Off stops at 1, which keeps " +
                 "poison a pressure rather than a surprise death.")]
        public bool canKill;

        // ---- While active ------------------------------------------------------

        [BoxGroup("While active", centerLabel: true)]
        [PropertyRange(0f, 2f)]
        [Tooltip("Run speed multiplier. Below 1 slows, above 1 hastes.")]
        public float moveSpeed = 1f;

        [BoxGroup("While active")]
        [PropertyRange(0f, 3f)]
        [Tooltip("Incoming damage multiplier, on top of the stance's own.")]
        public float damageTaken = 1f;

        [BoxGroup("While active")]
        [Tooltip("Tags held for as long as this runs.")]
        public GameTag[] grantsTags = { };

        [BoxGroup("While active")]
        [Tooltip("This effect is refused outright by anyone carrying this tag. " +
                 "Poison is blocked by PoisonImmune, which Se carries.")]
        public bool blockedByTag;

        [BoxGroup("While active")]
        [ShowIf(nameof(blockedByTag))]
        public GameTag blockingTag = GameTag.PoisonImmune;

        // ---- Look -------------------------------------------------------------

        [BoxGroup("Look", centerLabel: true)]
        [InfoBox("Violet is poison and mint is healing in the colour table. Keep " +
                 "new effects clear of white, amber, red and warm yellow, which " +
                 "already mean something.")]
        public Color tickTint = new(0.604f, 0.42f, 0.847f);

        [BoxGroup("Look")]
        [ToggleLeft]
        [Tooltip("Blink the tint on each tick.")]
        public bool flashOnTick = true;

        [BoxGroup("Readout", centerLabel: true)]
        [ShowInInspector, DisplayAsString, HideLabel]
        private string Totals
        {
            get
            {
                if (duration <= 0f) return "Lasts until removed.";
                var ticks = Mathf.FloorToInt(duration / Mathf.Max(0.05f, period));
                var parts = $"{ticks} ticks over {duration:0.#} s";
                if (damagePerTick > 0f) parts += $", {damagePerTick * ticks:0} damage";
                if (healPerTick > 0f) parts += $", {healPerTick * ticks:0} healing";
                return parts + ".";
            }
        }
    }
}
