using Sirenix.OdinInspector;
using UnityEngine;

namespace Weizza
{
    /// <summary>
    /// Everything one Weizza school is, as data.
    ///
    /// No stance behaviour is written in code as an if on StanceId. A stance is
    /// this asset plus its attack list, which means a designer retunes the whole
    /// feel of a school without a recompile, and the balance tool can read all
    /// three side by side.
    /// </summary>
    [CreateAssetMenu(menuName = "Weizza/Stance", fileName = "STANCE_New")]
    public sealed class StanceDefinition : ScriptableObject
    {
        [BoxGroup("Identity")]
        [InfoBox("A fresh asset defaults to Normal: walk and jump, no dash, no " +
                 "parry, no lifesteal. Change the Id below before this is meant " +
                 "to be one of the three real schools.")]
        [HideLabel, EnumToggleButtons]
        public StanceId id = StanceId.Normal;

        [BoxGroup("Identity")]
        public string displayName = "Normal";

        [BoxGroup("Identity")]
        [Tooltip("Used by the debug window and any stance UI.")]
        public Color tint = new(0.75f, 0.75f, 0.75f);

        [BoxGroup("Identity")]
        [Tooltip("One line a designer can read to remember what this school is for.")]
        public string intent = "No school yet. Walk, swing, nothing fancier.";

        // ---- Vitality -----------------------------------------------------

        [FoldoutGroup("Vitality", expanded: true)]
        [MinValue(1)]
        public float maxHealth = 120f;

        [FoldoutGroup("Vitality")]
        [PropertyRange(0.25f, 2f)]
        [Tooltip("Multiplies incoming damage. This is the fragility dial.")]
        public float damageTaken = 0.8f;

        [FoldoutGroup("Vitality")]
        [PropertyRange(0f, 0.5f)]
        [Tooltip("Fraction of damage dealt returned as health on every connect. " +
                 "No stance uses this since the redraw of October 2026, In heals " +
                 "on a kill instead. Kept for tuning experiments.")]
        public float lifesteal;

        [FoldoutGroup("Vitality")]
        [PropertyRange(0f, 60f)]
        [Tooltip("In. Health returned when one of this stance's hits or shots " +
                 "finishes an enemy. In has no other sustain outside a shrine, so " +
                 "this is what makes picking targets off at range worth it.")]
        public float healOnKill;

        [FoldoutGroup("Vitality")]
        [ToggleLeft]
        [Tooltip("Than. Weight is a platforming tool: plates press, weak wood " +
                 "breaks, bounce mushrooms burst, wind cannot move it.")]
        public bool isHeavy;

        [FoldoutGroup("Vitality")]
        [ToggleLeft]
        [Tooltip("Se. Poison effects and miasma do nothing. Se draws life out of " +
                 "a wound through alchemy, so the same hands are not hurt by it.")]
        public bool poisonImmune;

        // ---- Ground movement ----------------------------------------------

        [FoldoutGroup("Movement", expanded: true)]
        public float runSpeed = 5.2f;

        [FoldoutGroup("Movement")]
        [Tooltip("Depth walking is always slower than running along the screen, " +
                 "or the arena reads as a top down game.")]
        public float depthSpeed = 3.0f;

        [FoldoutGroup("Movement")]
        [Tooltip("Metres per second per second. Low is heavy, high is twitchy.")]
        public float acceleration = 60f;

        [FoldoutGroup("Movement")]
        public float deceleration = 70f;

        [FoldoutGroup("Movement")]
        [PropertyRange(0f, 1f)]
        [Tooltip("How much of ground acceleration applies in the air.")]
        public float airControl = 0.65f;

        // ---- Jump ---------------------------------------------------------

        [FoldoutGroup("Jump", expanded: true)]
        [ToggleLeft]
        [Tooltip("Se cannot jump. Every room that wants height asks for another school.")]
        public bool canJump = true;

        [FoldoutGroup("Jump")]
        [ShowIf(nameof(canJump))]
        [Tooltip("Metres at the top of a full hold.")]
        public float jumpHeight = 2.4f;

        [FoldoutGroup("Jump")]
        [ShowIf(nameof(canJump))]
        [Tooltip("Seconds from the ground to the top of the arc.")]
        public float jumpRiseTime = 0.34f;

        [FoldoutGroup("Jump")]
        [ShowIf(nameof(canJump))]
        [PropertyRange(1f, 3f)]
        [Tooltip("Gravity multiplier on the way down. Above 1 is the single " +
                 "biggest thing that stops a jump feeling floaty.")]
        public float fallGravityMultiplier = 1.7f;

        [FoldoutGroup("Jump")]
        [ShowIf(nameof(canJump))]
        [PropertyRange(0f, 1f)]
        [Tooltip("Upward speed kept when the button is released early.")]
        public float jumpCutMultiplier = 0.45f;

        [FoldoutGroup("Jump")]
        [ShowIf(nameof(canJump))]
        [PropertyRange(0, 12)]
        [Tooltip("Frames after walking off a ledge where jump still works.")]
        public int coyoteFrames = 6;

        [FoldoutGroup("Jump")]
        [ShowIf(nameof(canJump))]
        [PropertyRange(0, 12)]
        [Tooltip("Frames before landing where a jump press is remembered.")]
        public int jumpBufferFrames = 7;

        [FoldoutGroup("Jump")]
        [ShowIf(nameof(canJump))]
        [ShowInInspector, DisplayAsString]
        [LabelText("Resulting gravity")]
        public string GravityReadout =>
            jumpRiseTime > 0f
                ? $"{RiseGravity:0.0} up / {RiseGravity * fallGravityMultiplier:0.0} down  " +
                  $"(launch {JumpVelocity:0.0} m/s)"
                : "-";

        [FoldoutGroup("Jump")]
        [ShowIf(nameof(canJump))]
        [PropertyRange(0.2f, 1f)]
        [Tooltip("Gravity multiplier near the top of a jump while the button is " +
                 "still held. Celeste halves it here: the extra hang time is what " +
                 "lets a player adjust a landing without the jump feeling floaty. " +
                 "1 turns it off.")]
        public float apexGravityMultiplier = 0.55f;

        [FoldoutGroup("Jump")]
        [ShowIf(nameof(canJump))]
        [PropertyRange(0f, 4f)]
        [Tooltip("Vertical speed, in metres per second either side of zero, " +
                 "that counts as the top of the arc.")]
        public float apexSpeedBand = 1.6f;

        /// <summary>Gravity that produces the authored height in the authored time.</summary>
        public float RiseGravity =>
            jumpRiseTime <= 0f ? 0f : 2f * jumpHeight / (jumpRiseTime * jumpRiseTime);

        /// <summary>Launch speed that reaches the authored height.</summary>
        public float JumpVelocity => jumpRiseTime <= 0f ? 0f : 2f * jumpHeight / jumpRiseTime;

        // ---- Wall -----------------------------------------------------------

        [FoldoutGroup("Wall", expanded: true)]
        [ToggleLeft]
        [Tooltip("In only. Slide down a wall while pressing into it, jump off it. " +
                 "A shaft built around this is a room that asks for In.")]
        public bool canWallJump;

        [FoldoutGroup("Wall")]
        [ShowIf(nameof(canWallJump))]
        [PropertyRange(0.5f, 6f)]
        [Tooltip("Fastest fall while sliding, metres per second.")]
        public float wallSlideSpeed = 2.2f;

        [FoldoutGroup("Wall")]
        [ShowIf(nameof(canWallJump))]
        [PropertyRange(0.02f, 0.4f)]
        [Tooltip("How far from the wall still counts as touching it. Celeste " +
                 "lets a wall jump happen 2 pixels away; this is the same favour " +
                 "in metres. Too small and wall jumps randomly fail.")]
        public float wallCheckDistance = 0.12f;

        [FoldoutGroup("Wall")]
        [ShowIf(nameof(canWallJump))]
        [PropertyRange(1f, 12f)]
        [Tooltip("Sideways speed away from the wall on a wall jump.")]
        public float wallJumpSpeedX = 5.5f;

        [FoldoutGroup("Wall")]
        [ShowIf(nameof(canWallJump))]
        [PropertyRange(0.5f, 4f)]
        [Tooltip("Height a wall jump reaches, in metres.")]
        public float wallJumpHeight = 2.1f;

        [FoldoutGroup("Wall")]
        [ShowIf(nameof(canWallJump))]
        [PropertyRange(0, 20)]
        [Tooltip("Frames after a wall jump where the stick cannot steer back " +
                 "toward the wall. Zero lets a player climb one wall forever by " +
                 "jumping straight back at it.")]
        public int wallJumpLockFrames = 9;

        [FoldoutGroup("Wall")]
        [ShowIf(nameof(canWallJump))]
        [PropertyRange(0, 12)]
        [Tooltip("Frames after leaving a wall where a wall jump still works. " +
                 "Coyote time, for walls.")]
        public int wallCoyoteFrames = 6;

        [FoldoutGroup("Wall")]
        [ToggleLeft]
        [Tooltip("In. Can catch and swing on vines.")]
        public bool canSwing;

        // ---- Ranged ---------------------------------------------------------

        [FoldoutGroup("Ranged", expanded: true)]
        [Tooltip("In. When set, the attack button fires this kit instead of " +
                 "swinging the light combo. Empty means a melee stance.")]
        public RangedKit rangedKit;

        public bool IsRanged => rangedKit != null;

        // ---- Dash ---------------------------------------------------------

        [FoldoutGroup("Dash", expanded: true)]
        [ToggleLeft]
        [Tooltip("Se dashes with invincibility. Than and In have no dash.")]
        public bool canDash;

        [FoldoutGroup("Dash")]
        [ShowIf(nameof(canDash))]
        public float dashDistance = 3.6f;

        [FoldoutGroup("Dash")]
        [ShowIf(nameof(canDash))]
        [PropertyRange(1, 30)]
        public int dashFrames = 10;

        [FoldoutGroup("Dash")]
        [ShowIf(nameof(canDash))]
        [PropertyRange(0, 30)]
        [Tooltip("First invincible frame of the dash, counted from its start.")]
        public int iFrameStart = 1;

        [FoldoutGroup("Dash")]
        [ShowIf(nameof(canDash))]
        [PropertyRange(0, 30)]
        [Tooltip("Last invincible frame. Ending before the dash does is what " +
                 "makes a dash readable instead of a free escape.")]
        public int iFrameEnd = 8;

        [FoldoutGroup("Dash")]
        [ShowIf(nameof(canDash))]
        [PropertyRange(0, 120)]
        public int dashCooldownFrames = 24;

        [FoldoutGroup("Dash")]
        [ShowIf(nameof(canDash))]
        [ToggleLeft]
        [Tooltip("Teleports through anything in the way instead of sweeping. A plain " +
                 "dash sweeps and stops at walls.")]
        public bool dashIsBlink;

        // ---- Defence ------------------------------------------------------

        [FoldoutGroup("Defence", expanded: true)]
        [ToggleLeft]
        [Tooltip("Parry was cut on 17 September 2026. Leave off.")]
        public bool canParry;

        [FoldoutGroup("Defence")]
        [ShowIf(nameof(canParry))]
        [PropertyRange(1, 20)]
        public int parryWindowFrames = 8;

        [FoldoutGroup("Defence")]
        [ShowIf(nameof(canParry))]
        [PropertyRange(0, 60)]
        public int parryRecoveryFrames = 18;

        [FoldoutGroup("Defence")]
        [ShowIf(nameof(canParry))]
        [PropertyRange(0, 40)]
        [Tooltip("Frames the attacker is frozen on a successful parry. This is " +
                 "the reward, so make it generous enough to punish with.")]
        public int parryStunFrames = 26;

        [FoldoutGroup("Defence")]
        [ToggleLeft]
        [Tooltip("Than only. A hit still deals damage, but never interrupts " +
                 "whatever the character was doing and never flinches. This is " +
                 "the trait a wind corridor or a narrow beam is designed around: " +
                 "everyone else gets blown off, Than does not move.")]
        public bool hyperArmor;

        [FoldoutGroup("Defence")]
        [ShowIf(nameof(hyperArmor))]
        [PropertyRange(0f, 1f)]
        [Tooltip("Fraction of incoming knockback removed. 1 means a hit lands " +
                 "and pushes nothing, which is the weight the design brief asks " +
                 "for: heavy enough to stand on a platform other stances would " +
                 "be knocked off of.")]
        public float knockbackResistance = 0.7f;

        // ---- Attacks ------------------------------------------------------

        [FoldoutGroup("Attacks", expanded: true)]
        [Tooltip("Light combo, in order. Each entry chains into the next.")]
        [ListDrawerSettings(ShowFoldout = true, DefaultExpandedState = true, ShowIndexLabels = true)]
        public AttackDefinition[] lightCombo;

        [FoldoutGroup("Attacks")]
        public AttackDefinition heavy;

        [FoldoutGroup("Attacks")]
        [Tooltip("PROPOSAL, not canon yet. Light pressed in the air with down held. " +
                 "Neva has one; empty means this stance has no down attack and " +
                 "nothing changes. Give the attack a hitbox offset below the feet.")]
        public AttackDefinition downAttack;

        [FoldoutGroup("Attacks")]
        [ShowIf(nameof(HasDownAttack))]
        [PropertyRange(0f, 20f)]
        [Tooltip("Upward speed when the down attack connects. A bounce off an " +
                 "enemy's head is also a platforming tool.")]
        public float pogoBounce = 10f;

        public bool HasDownAttack => downAttack != null;

        [FoldoutGroup("Attacks")]
        [ShowInInspector, DisplayAsString]
        [LabelText("Combo total")]
        public string ComboReadout
        {
            get
            {
                if (lightCombo == null || lightCombo.Length == 0) return "-";
                var frames = 0;
                var damage = 0f;
                foreach (var attack in lightCombo)
                {
                    if (attack == null) continue;
                    frames += attack.TotalFrames;
                    damage += attack.damage;
                }
                var seconds = Timing.Frames(frames);
                return seconds > 0f
                    ? $"{damage:0} damage over {frames} frames  ({damage / seconds:0.0} dps)"
                    : "-";
            }
        }
    }
}
