using Sirenix.OdinInspector;
using UnityEngine;

namespace Weizza
{
    /// <summary>The three ways a ranged stance can be fired. Switchable live to compare.</summary>
    public enum ShotMode
    {
        /// <summary>
        /// A reticle sits on the cursor (or out along the right stick). Click
        /// fires at it. Holding aim slows everything down to line the shot up.
        /// </summary>
        CursorAim = 0,

        /// <summary>
        /// Press and hold, release to throw. A tap barely leaves the hand, a
        /// long hold throws far. Gravity always pulls it down, so distance is
        /// the skill.
        /// </summary>
        PowerThrow = 1,

        /// <summary>
        /// Hold to charge. Time slows while charging and the flight path draws
        /// itself out ahead. Release fires harder the longer it was held.
        /// </summary>
        ChargeLine = 2,
    }

    /// <summary>
    /// How one ranged stance fires: which mode, which projectile, and every
    /// number the mode needs.
    ///
    /// Kept apart from the stance asset so the same kit can be swapped between
    /// stances while the design is still being decided, and so the three modes
    /// can be compared by changing one dropdown rather than three stances.
    /// </summary>
    [CreateAssetMenu(menuName = "Weizza/Ranged Kit", fileName = "KIT_New")]
    public sealed class RangedKit : ScriptableObject
    {
        [BoxGroup("Mode")]
        [EnumToggleButtons, HideLabel]
        public ShotMode mode = ShotMode.CursorAim;

        [BoxGroup("Mode")]
        [ShowInInspector, DisplayAsString(false), HideLabel]
        private string ModeExplained => mode switch
        {
            ShotMode.CursorAim => "Reticle on the cursor. Left click fires at it. Hold right " +
                                  "click to slow time and line it up.",
            ShotMode.PowerThrow => "Hold to wind up, release to throw. A tap lands close, a full " +
                                   "hold lands far. Gravity always pulls it down.",
            _ => "Hold to charge. Time slows and the flight path draws itself out. " +
                 "Release fires harder the longer it was held.",
        };

        [BoxGroup("Shot", centerLabel: true)]
        [Required]
        public ProjectileDefinition projectile;

        [BoxGroup("Shot")]
        [PropertyRange(0, 60)]
        [Tooltip("Frames before the next shot can leave.")]
        public int cooldownFrames = 14;

        [BoxGroup("Shot")]
        [Tooltip("Where the shot leaves from, relative to the character's feet. " +
                 "X is mirrored by facing.")]
        public Vector2 muzzleOffset = new(0.35f, 1.1f);

        [BoxGroup("Shot")]
        [PropertyRange(0f, 1f)]
        [Tooltip("Run speed kept while winding up or charging. Zero roots the " +
                 "player, which feels committed but reads as a trap in a crowd.")]
        public float moveScaleWhileHolding = 0.35f;

        // ---- Aim --------------------------------------------------------------

        [BoxGroup("Aim", centerLabel: true)]
        [PropertyRange(1f, 12f)]
        [Tooltip("With a pad there is no cursor, so the reticle sits this far out " +
                 "along the right stick.")]
        public float padReticleDistance = 5f;

        [BoxGroup("Aim")]
        [PropertyRange(0f, 0.9f)]
        public float padDeadzone = 0.25f;

        [BoxGroup("Aim")]
        [PropertyRange(-30f, 60f)]
        [Tooltip("Angle above flat used when nothing is aiming at all (no mouse " +
                 "movement, stick at rest). A slight upward angle makes a blind " +
                 "throw land on the floor ahead rather than straight into it.")]
        public float restAngle = 12f;

        [BoxGroup("Aim")]
        [PropertyRange(0f, 1f)]
        [Tooltip("Aim magnetism: how far the aim is pulled toward a target near " +
                 "the line. Zero is raw aim, one snaps onto the target.")]
        public float aimAssist = 0.25f;

        [BoxGroup("Aim")]
        [PropertyRange(5f, 45f)]
        [Tooltip("How wide around the aim line assist will look for a target, in degrees.")]
        public float aimAssistAngle = 14f;

        // ---- Cursor aim -------------------------------------------------------

        [FoldoutGroup("Cursor Aim", expanded: true)]
        [ShowIf(nameof(IsCursor))]
        [PropertyRange(0.05f, 1f)]
        [Tooltip("Time scale while aim is held. 0.25 is a quarter speed.")]
        public float aimSlowScale = 0.25f;

        [FoldoutGroup("Cursor Aim")]
        [ShowIf(nameof(IsCursor))]
        [PropertyRange(0.5f, 8f)]
        [Tooltip("Real seconds the slow can be held before it lets go by itself. " +
                 "Without a limit, aiming is a pause button.")]
        public float aimSlowMaxSeconds = 2.5f;

        [FoldoutGroup("Cursor Aim")]
        [ShowIf(nameof(IsCursor))]
        [PropertyRange(0f, 5f)]
        [Tooltip("Real seconds before the slow can be used again after it ends.")]
        public float aimSlowCooldown = 1f;

        [FoldoutGroup("Cursor Aim")]
        [ShowIf(nameof(IsCursor))]
        [ToggleLeft]
        [Tooltip("Draw the flight path while aim is held.")]
        public bool showPathWhileAiming = true;

        // ---- Power throw ------------------------------------------------------

        [FoldoutGroup("Power Throw", expanded: true)]
        [ShowIf(nameof(IsPower))]
        [PropertyRange(1f, 30f)]
        [Tooltip("Launch speed of the shortest tap.")]
        public float minLaunchSpeed = 5f;

        [FoldoutGroup("Power Throw")]
        [ShowIf(nameof(IsPower))]
        [PropertyRange(2f, 50f)]
        [Tooltip("Launch speed of a full hold.")]
        public float maxLaunchSpeed = 17f;

        [FoldoutGroup("Power Throw")]
        [ShowIf(nameof(IsPower))]
        [PropertyRange(0.1f, 3f)]
        [Tooltip("Seconds of holding to reach full power.")]
        public float fullPowerSeconds = 0.9f;

        [FoldoutGroup("Power Throw")]
        [ShowIf(nameof(IsPower))]
        [Tooltip("Shape of the wind up. Easing in makes short taps finer to " +
                 "control, which is where the skill is.")]
        public AnimationCurve powerCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

        [FoldoutGroup("Power Throw")]
        [ShowIf(nameof(IsPower))]
        [PropertyRange(0f, 80f)]
        [Tooltip("Degrees above flat the ward leaves at. Power Throw has no reticle " +
                 "and ignores the mouse: the player only decides how long to hold, " +
                 "so distance is a pure feel skill.")]
        public float throwAngle = 35f;

        [FoldoutGroup("Power Throw")]
        [ShowIf(nameof(IsPower))]
        [PropertyRange(0f, 85f)]
        [Tooltip("Angle while holding up, for lobbing onto a ledge.")]
        public float throwAngleUp = 60f;

        [FoldoutGroup("Power Throw")]
        [ShowIf(nameof(IsPower))]
        [PropertyRange(-30f, 60f)]
        [Tooltip("Angle while holding down, a flatter skimming throw.")]
        public float throwAngleDown = 10f;

        // ---- Charge line ------------------------------------------------------

        [FoldoutGroup("Charge Line", expanded: true)]
        [ShowIf(nameof(IsCharge))]
        [PropertyRange(0f, 0.5f)]
        [Tooltip("Real seconds a press is held before it counts as charging. A " +
                 "release before this fires a quick, weak shot with no slow down.")]
        public float chargeStartDelay = 0.14f;

        [FoldoutGroup("Charge Line")]
        [ShowIf(nameof(IsCharge))]
        [PropertyRange(0.1f, 4f)]
        [Tooltip("Real seconds of charging to reach full.")]
        public float fullChargeSeconds = 1.1f;

        [FoldoutGroup("Charge Line")]
        [ShowIf(nameof(IsCharge))]
        [PropertyRange(0.05f, 1f)]
        public float chargeSlowScale = 0.35f;

        [FoldoutGroup("Charge Line")]
        [ShowIf(nameof(IsCharge))]
        [PropertyRange(0.5f, 8f)]
        [Tooltip("Real seconds a full charge can be held before it fires by itself.")]
        public float chargeMaxHoldSeconds = 3f;

        [FoldoutGroup("Charge Line")]
        [ShowIf(nameof(IsCharge))]
        [MinMaxSlider(1f, 60f, showFields: true)]
        [Tooltip("Launch speed from an empty charge to a full one.")]
        public Vector2 chargeSpeed = new(10f, 26f);

        [FoldoutGroup("Charge Line")]
        [ShowIf(nameof(IsCharge))]
        [PropertyRange(1f, 4f)]
        [Tooltip("Damage multiplier of a full charge.")]
        public float fullChargeDamage = 2.2f;

        [FoldoutGroup("Charge Line")]
        [ShowIf(nameof(IsCharge))]
        [PropertyRange(0, 5)]
        [Tooltip("Extra enemies a full charge pierces.")]
        public int fullChargeExtraPierce = 2;

        [FoldoutGroup("Charge Line")]
        [ShowIf(nameof(IsCharge))]
        [PropertyRange(0.05f, 1f)]
        [Tooltip("Real seconds the path takes to draw itself out.")]
        public float lineDrawSeconds = 0.25f;

        // ---- Line look --------------------------------------------------------

        [FoldoutGroup("Path Look")]
        public Color pathColour = new(0.85f, 0.95f, 1f, 0.85f);

        [FoldoutGroup("Path Look")]
        public Color fullChargeColour = new(1f, 0.95f, 0.7f, 1f);

        [FoldoutGroup("Path Look")]
        [PropertyRange(0.01f, 0.2f)]
        public float pathWidth = 0.06f;

        [FoldoutGroup("Path Look")]
        [PropertyRange(0f, 8f)]
        [Tooltip("How fast the dashes run along the path, in metres per second.")]
        public float pathScrollSpeed = 2.5f;

        [FoldoutGroup("Path Look")]
        [PropertyRange(0.5f, 3f)]
        [Tooltip("Seconds of flight the path previews.")]
        public float pathPreviewSeconds = 1.2f;

        public bool IsCursor => mode == ShotMode.CursorAim;
        public bool IsPower => mode == ShotMode.PowerThrow;
        public bool IsCharge => mode == ShotMode.ChargeLine;

        public float CooldownSeconds => Timing.Frames(cooldownFrames);
    }
}
