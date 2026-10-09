using System;
using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;

namespace Weizza
{
    /// <summary>One move a boss phase may pick, by id, with how often.</summary>
    [Serializable]
    public sealed class BossMoveChoice
    {
        [Tooltip("A move id from the boss's EnemyDefinition.")]
        public string moveId = "slam";

        [MinValue(0f)]
        public float weight = 1f;

        [Tooltip("Never picked twice in a row. Stops a big move becoming the only move.")]
        public bool noRepeat = true;

        [Tooltip("Only picked while the player is between these distances (metres, sideways). " +
                 "Zero for both means any distance. A clamp up close, a spit from afar, a hop when crowded.")]
        public Vector2 playerDistance;

        public bool InDistance(float distance) =>
            playerDistance == Vector2.zero || (distance >= playerDistance.x && distance <= playerDistance.y);
    }

    /// <summary>
    /// One stage of a boss fight: when it starts, which moves it uses, and how
    /// the body changes.
    ///
    /// Phases are data on purpose, so a fight's shape is tuned by moving
    /// thresholds and weights, never by editing the brain. The brain only
    /// reads these and calls PerformMove, the one seam the settled rule in
    /// the project notes allows a boss driver.
    /// </summary>
    [CreateAssetMenu(menuName = "Weizza/Boss Phase", fileName = "BOSS_Phase")]
    public sealed class BossPhase : ScriptableObject
    {
        [BoxGroup("When")]
        [PropertyRange(0f, 1f)]
        [Tooltip("Health fraction at or below which this phase begins. The first " +
                 "phase uses 1.")]
        public float startsAtHealth = 1f;

        [BoxGroup("When")]
        [Tooltip("Move played once when the phase begins, before anything else. " +
                 "A roar, a shell cracking off. Empty skips it.")]
        public string entryMoveId = "";

        [BoxGroup("Moves")]
        [ListDrawerSettings(ShowFoldout = true, DefaultExpandedState = true)]
        public List<BossMoveChoice> moves = new();

        [BoxGroup("Moves")]
        [MinMaxSlider(0f, 4f, showFields: true)]
        [Tooltip("Seconds of pause between moves, picked at random in this range. " +
                 "The breathing room is where the player gets to hit back.")]
        public Vector2 restBetweenMoves = new(0.8f, 1.6f);

        [BoxGroup("Body")]
        [MinValue(0f)]
        [Tooltip("Poise meter for this phase. Usually rises each phase.")]
        public float poise = 60f;

        [BoxGroup("Body")]
        [PropertyRange(0.5f, 2f)]
        [Tooltip("Scales the gap between moves. Below 1 is more aggressive.")]
        public float tempo = 1f;

        [BoxGroup("Body")]
        [Tooltip("Tint held for the whole phase. White means none.")]
        public Color phaseTint = Color.white;

        [BoxGroup("Drawings")]
        [InfoBox("For a body that changes between phases (armoured, then not). Empty keeps the drawing " +
                 "the boss already uses.")]
        public string idleClip = "";

        [BoxGroup("Drawings")]
        public string runClip = "";

        [BoxGroup("Drawings")]
        public string hurtClip = "";
    }
}
