using Sirenix.OdinInspector;
using UnityEngine;

namespace Weizza
{
    /// <summary>
    /// One attack, described in frames rather than seconds.
    ///
    /// Frames are the unit because that is the unit a fighting game is balanced
    /// in and the unit hand drawn animation is authored in. Everything the
    /// player reads as weight comes from these three numbers, so they are the
    /// first thing to tune and the last thing to touch once a stance feels right.
    /// </summary>
    [CreateAssetMenu(menuName = "Weizza/Attack", fileName = "ATK_New")]
    public sealed class AttackDefinition : ScriptableObject
    {
        [BoxGroup("Identity")]
        public string displayName = "Light 1";

        [BoxGroup("Identity")]
        [Tooltip("Animation state name on the sprite Animator.")]
        public string animationState = "Attack_Light_1";

        // ---- Timing -------------------------------------------------------

        [BoxGroup("Timing", centerLabel: true)]
        [InfoBox("Startup is how heavy it reads. Active is how forgiving it is. " +
                 "Recovery is the price of missing.")]
        [PropertyRange(1, 60)]
        public int startupFrames = 6;

        [BoxGroup("Timing")]
        [PropertyRange(1, 30)]
        public int activeFrames = 3;

        [BoxGroup("Timing")]
        [PropertyRange(1, 90)]
        public int recoveryFrames = 12;

        [BoxGroup("Timing")]
        [PropertyRange(0, 120)]
        [Tooltip("First frame the next combo step may be buffered into. " +
                 "Set inside recovery to make the combo flow, after it to make it commit.")]
        public int cancelFrame = 14;

        [BoxGroup("Timing")]
        [ShowInInspector, DisplayAsString]
        [LabelText("Total")]
        public string TotalReadout =>
            $"{TotalFrames} frames  ({TotalSeconds * 1000f:0} ms)";

        public int TotalFrames => startupFrames + activeFrames + recoveryFrames;
        public float TotalSeconds => Timing.Frames(TotalFrames);
        public float StartupSeconds => Timing.Frames(startupFrames);
        public float ActiveSeconds => Timing.Frames(activeFrames);
        public float RecoverySeconds => Timing.Frames(recoveryFrames);
        public float CancelSeconds => Timing.Frames(cancelFrame);

        // ---- Damage -------------------------------------------------------

        [BoxGroup("Damage", centerLabel: true)]
        [MinValue(0)]
        public float damage = 10f;

        [BoxGroup("Damage")]
        [MinValue(0)]
        [Tooltip("Chip damage against a guard or super armor.")]
        public float poiseDamage = 10f;

        [BoxGroup("Damage")]
        [ShowInInspector, DisplayAsString]
        [LabelText("Damage per second")]
        public string DpsReadout =>
            TotalSeconds > 0f ? $"{damage / TotalSeconds:0.0} dps" : "-";

        // ---- Hitbox -------------------------------------------------------

        [BoxGroup("Hitbox", centerLabel: true)]
        [Tooltip("Offset from the character root, in the facing direction.")]
        public Vector2 hitboxOffset = new(0.8f, 0.6f);

        [BoxGroup("Hitbox")]
        public Vector2 hitboxSize = new(1.2f, 1.0f);

        [BoxGroup("Hitbox")]
        [PropertyRange(0.05f, 2f)]
        [Tooltip("Arena rooms only. How far apart in the depth band two " +
                 "characters can be and still connect. Too small and the fight " +
                 "feels like it is missing on purpose.")]
        public float depthTolerance = 0.45f;

        // ---- Reaction -----------------------------------------------------

        [BoxGroup("Reaction", centerLabel: true)]
        public float knockbackX = 4f;

        [BoxGroup("Reaction")]
        public float knockbackY;

        [BoxGroup("Reaction")]
        [PropertyRange(0, 30)]
        [Tooltip("Frames both sides freeze on connect. This is most of what " +
                 "makes a hit feel heavy. Light jabs 2 to 4, heavies 8 to 14.")]
        public int hitStopFrames = 5;

        [BoxGroup("Reaction")]
        [PropertyRange(0f, 1f)]
        public float screenShake = 0.15f;

        [BoxGroup("Reaction")]
        [PropertyRange(0, 60)]
        [Tooltip("How long the victim cannot act. Shorter than knockback " +
                 "travel makes combos escapable.")]
        public int hitStunFrames = 14;

        // ---- Flags --------------------------------------------------------

        [BoxGroup("Flags", centerLabel: true)]
        [ToggleLeft]
        [Tooltip("Than only. The attacker eats a hit without flinching while " +
                 "this runs.")]
        public bool superArmor;

        [BoxGroup("Flags")]
        [ToggleLeft]
        public bool launcher;

        [BoxGroup("Flags")]
        [ToggleLeft]
        [Tooltip("Ignores the depth band, hits the whole column. Use rarely.")]
        public bool fullDepth;

        [BoxGroup("Flags")]
        [PropertyRange(0f, 2f)]
        [Tooltip("Forward slide while the attack runs, in metres. Commitment " +
                 "you can feel is better than commitment you have to read.")]
        public float lungeDistance = 0.2f;
    }
}
