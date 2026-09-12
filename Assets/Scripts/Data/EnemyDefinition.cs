using System;
using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;

namespace Weizza
{
    /// <summary>
    /// One thing an enemy knows how to do.
    ///
    /// The frame data, damage, knockback and hit stop all live on a normal
    /// AttackDefinition, the same asset type the player's stances use, so a
    /// swing is tuned in one place no matter whose arm it is on. What lives
    /// here is only the decision layer: when this move is legal, how often it
    /// gets picked, and how the body carries itself while it runs.
    /// </summary>
    [Serializable]
    public sealed class EnemyMove
    {
        [HorizontalGroup("Head", 0.35f)]
        [HideLabel]
        [Tooltip("Stable name a director asks for by hand. Never renamed once " +
                 "a behaviour tree or a state machine references it.")]
        public string id = "charge";

        [HorizontalGroup("Head", 0.4f)]
        [LabelWidth(46)]
        [Tooltip("Frames, damage, knockback, hit stop. A normal attack asset.")]
        [Required]
        public AttackDefinition attack;

        [HorizontalGroup("Head", 0.25f)]
        [LabelWidth(52)]
        [Tooltip("Relative chance of being picked among the moves that are " +
                 "legal at this range. Not a percentage.")]
        [MinValue(0f)]
        public float weight = 1f;

        [FoldoutGroup("$id")]
        [MinMaxSlider(0f, 14f, showFields: true)]
        [Tooltip("Distance to the target this move is allowed at. Moves whose " +
                 "bands overlap give the enemy a real choice; moves whose " +
                 "bands do not touch leave a gap it will walk around in " +
                 "forever without ever swinging.")]
        public Vector2 range = new(0f, 1.4f);

        [FoldoutGroup("$id")]
        [MinValue(0f)]
        [Tooltip("Seconds before this specific move can be used again. This is " +
                 "what stops the one big move being the only move.")]
        public float cooldown = 2f;

        [FoldoutGroup("$id")]
        [PropertyRange(0, 90)]
        [Tooltip("Frames spent holding still and tinted before the swing " +
                 "starts. This is the whole difference between an attack that " +
                 "feels unfair and one the player learns to read. The design " +
                 "bible says juice is done in code against hand drawn frames, " +
                 "so a telegraph costs a colour, never a new drawing.")]
        public int telegraphFrames = 20;

        [FoldoutGroup("$id")]
        [InfoBox("One colour, one meaning, across the whole game. White is a hit " +
                 "that landed, amber is a hit that did not stagger, red is the " +
                 "player losing health. A wind up gets warm yellow and pulses, " +
                 "so it cannot be read as any of those.")]
        public Color telegraphTint = new(1f, 0.93f, 0.5f);

        [FoldoutGroup("$id")]
        [Tooltip("Clip played while telegraphing. Empty uses the idle clip.")]
        public string telegraphClip = "";

        [FoldoutGroup("$id")]
        [MinValue(0f)]
        [Tooltip("Metres per second the body carries itself forward while the " +
                 "attack runs. Zero stands and swings. Above zero is a charge, " +
                 "which is how an enemy with no attack drawing at all still " +
                 "gets a readable attack: it runs at you with the hitbox open.")]
        public float travelSpeed;

        [FoldoutGroup("$id")]
        [Tooltip("Clip played during the swing. Empty uses the attack's own " +
                 "Animation State. Set this to the run clip for a charge.")]
        public string travelClip = "";

        public float MinRange => range.x;
        public float MaxRange => range.y;

        public bool InRange(float distance) => distance >= range.x && distance <= range.y;
    }

    /// <summary>
    /// One enemy, as data: a body, a set of senses, and a move list.
    ///
    /// There is deliberately no phase system here. StoryKit already owns
    /// that: its StateMachineRunner exists for boss phases and its
    /// BehaviorTreeRunner for enemy decisions, and the project rule is that
    /// StoryKit directs while this code acts. A mini boss is therefore not a
    /// different asset type with extra machinery, it is this same asset with
    /// a longer move list, driven from outside instead of by the small brain
    /// built into the controller.
    /// </summary>
    [CreateAssetMenu(menuName = "Weizza/Enemy", fileName = "ENEMY_New")]
    public sealed class EnemyDefinition : ScriptableObject
    {
        [BoxGroup("Identity")]
        public string displayName = "Boar";

        [BoxGroup("Identity")]
        [Tooltip("One line a designer can read to remember what this enemy is " +
                 "for. Wholesome, not horror: the design bible's standing rule " +
                 "applies to enemies too.")]
        public string intent = "Charges in a straight line. Punishes standing still, harmless if you move.";

        // ---- Body -----------------------------------------------------------

        [FoldoutGroup("Body", expanded: true)]
        [MinValue(1f)]
        public float maxHealth = 40f;

        [FoldoutGroup("Body")]
        [InfoBox("Poise is what separates a grunt from a mini boss, not health. " +
                 "Set it below one light attack's poise damage and this staggers " +
                 "on everything. Set it around a full light combo and mashing " +
                 "never holds it still, which is what makes a player start " +
                 "choosing their hits.")]
        [MinValue(0f)]
        [Tooltip("Zero means every hit staggers it.")]
        public float poise = 12f;

        [FoldoutGroup("Body")]
        [MinValue(0f)]
        public float poiseRecoveryPerSecond = 10f;

        [FoldoutGroup("Body")]
        [PropertyRange(0f, 2f)]
        [Tooltip("Scales knockback taken. Heavy things should barely move.")]
        public float knockbackScale = 1f;

        // ---- Movement -------------------------------------------------------

        [FoldoutGroup("Movement", expanded: true)]
        [Tooltip("Speed when wandering or holding position.")]
        public float walkSpeed = 1.2f;

        [FoldoutGroup("Movement")]
        [Tooltip("Speed when closing on the player.")]
        public float chaseSpeed = 2.6f;

        [FoldoutGroup("Movement")]
        [Tooltip("Speed it lines up along the depth axis. Brawler rooms are " +
                 "walkable in Z, so an enemy that only tracks X stands on the " +
                 "wrong line and swings at nothing.")]
        public float depthSpeed = 1.6f;

        // ---- Senses ---------------------------------------------------------

        [FoldoutGroup("Senses", expanded: true)]
        [MinValue(0f)]
        [Tooltip("Distance the player has to be inside before this wakes up.")]
        public float aggroRange = 7f;

        [FoldoutGroup("Senses")]
        [MinValue(0f)]
        [Tooltip("Distance it gives up at. Larger than aggro on purpose, or it " +
                 "flickers awake and asleep on the boundary.")]
        public float loseAggroRange = 11f;

        [FoldoutGroup("Senses")]
        [MinValue(0.05f)]
        [Tooltip("How close along the depth axis it needs to be before it will " +
                 "commit to a move. Too tight and it circles forever.")]
        public float depthTolerance = 0.45f;

        [FoldoutGroup("Senses")]
        [PropertyRange(0f, 2f)]
        [Tooltip("Seconds between deciding what to do. Zero reacts on every " +
                 "frame, which reads as a machine rather than an animal.")]
        public float decisionInterval = 0.25f;

        // ---- Moves ----------------------------------------------------------

        [FoldoutGroup("Moves", expanded: true)]
        [InfoBox("$MoveReadout")]
        [ListDrawerSettings(ShowFoldout = true, DefaultExpandedState = true, ShowIndexLabels = false)]
        public EnemyMove[] moves = Array.Empty<EnemyMove>();

        /// <summary>Finds a move by id. Null when nothing answers to that name.</summary>
        public EnemyMove FindMove(string moveId)
        {
            if (moves == null || string.IsNullOrEmpty(moveId)) return null;
            foreach (var move in moves)
            {
                if (move != null && move.id == moveId) return move;
            }
            return null;
        }

        /// <summary>
        /// Reports duplicate and empty ids, and gaps in range coverage.
        ///
        /// Both failures are silent in game: a duplicate id means a director
        /// asking for a move gets whichever one happens to be first, and a
        /// range gap means an enemy that walks up to the player and then
        /// stands there doing nothing forever, which reads as a broken brain
        /// rather than as bad data.
        /// </summary>
        private string MoveReadout
        {
            get
            {
                if (moves == null || moves.Length == 0)
                    return "No moves. This enemy will chase and never attack.";

                var seen = new HashSet<string>();
                var problems = new List<string>();
                var nearest = float.MaxValue;
                var furthest = 0f;

                foreach (var move in moves)
                {
                    if (move == null) continue;

                    if (string.IsNullOrWhiteSpace(move.id)) problems.Add("a move has no id");
                    else if (!seen.Add(move.id)) problems.Add($"'{move.id}' is used twice");

                    if (move.attack == null) problems.Add($"'{move.id}' has no attack asset");

                    nearest = Mathf.Min(nearest, move.MinRange);
                    furthest = Mathf.Max(furthest, move.MaxRange);
                }

                if (problems.Count > 0) return "Problem: " + string.Join(", ", problems) + ".";

                return $"{seen.Count} moves, covering {nearest:0.0} m to {furthest:0.0} m. " +
                       $"Anything closer than {nearest:0.0} m has no legal move.";
            }
        }
    }
}
