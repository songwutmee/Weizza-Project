using Sirenix.OdinInspector;
using UnityEngine;

namespace Weizza
{
    /// <summary>
    /// The rules that sit above any one stance: how switching costs, how
    /// forgiving input is, how the depth band behaves by default.
    ///
    /// Kept separate from StanceDefinition on purpose. A number here changes the
    /// shape of the whole combat system, a number in a stance changes one school.
    /// </summary>
    [CreateAssetMenu(menuName = "Weizza/Player Tuning", fileName = "PlayerTuning")]
    public sealed class PlayerTuning : ScriptableObject
    {
        [BoxGroup("Stances", centerLabel: true)]
        [InfoBox("Order does not matter. Lookup is by the id inside each asset.")]
        [ListDrawerSettings(ShowFoldout = false, DefaultExpandedState = true)]
        public StanceDefinition[] stances;

        [BoxGroup("Stances")]
        public StanceId startingStance = StanceId.Normal;

        // ---- The switch ---------------------------------------------------

        [BoxGroup("Stance switch", centerLabel: true)]
        [InfoBox("The design rule: switching is roughly 0.4 seconds with no " +
                 "super armor and no invincibility. It is the price of being " +
                 "the only person alive who can do it.")]
        [PropertyRange(1, 60)]
        [Tooltip("Frames the switch takes. 24 frames is 0.4 seconds.")]
        public int switchFrames = 24;

        [BoxGroup("Stance switch")]
        [PropertyRange(1f, 3f)]
        [Tooltip("Damage multiplier while switching. Above 1 makes a mistimed " +
                 "switch genuinely punishing instead of merely slow.")]
        public float switchDamageMultiplier = 1.35f;

        [BoxGroup("Stance switch")]
        [PropertyRange(0f, 1f)]
        [Tooltip("Movement speed kept while switching. Zero roots the player, " +
                 "which reads as heavy but plays as a trap.")]
        public float switchMoveScale = 0.45f;

        [BoxGroup("Stance switch")]
        [PropertyRange(0, 60)]
        [Tooltip("Frames before another switch is allowed. Stops mashing " +
                 "through all three schools as a panic button.")]
        public int switchCooldownFrames = 12;

        [BoxGroup("Stance switch")]
        [ShowInInspector, DisplayAsString]
        [LabelText("Window")]
        public string SwitchReadout =>
            $"{switchFrames} frames  ({Timing.Frames(switchFrames) * 1000f:0} ms vulnerable)";

        public float SwitchSeconds => Timing.Frames(switchFrames);
        public float SwitchCooldownSeconds => Timing.Frames(switchCooldownFrames);

        // ---- Input forgiveness --------------------------------------------

        [BoxGroup("Input", centerLabel: true)]
        [PropertyRange(0, 20)]
        [Tooltip("Frames an attack press is remembered while busy.")]
        public int attackBufferFrames = 8;

        [BoxGroup("Input")]
        [PropertyRange(0, 20)]
        [Tooltip("Frames a dash press is remembered while busy.")]
        public int dashBufferFrames = 6;

        // ---- Lookup -------------------------------------------------------

        public StanceDefinition Get(StanceId id)
        {
            if (stances == null) return null;
            foreach (var stance in stances)
            {
                if (stance != null && stance.id == id) return stance;
            }
            return null;
        }
    }
}
