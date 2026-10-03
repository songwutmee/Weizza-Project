using UnityEngine;

namespace Weizza
{
    /// <summary>
    /// What combat identity the player has right now.
    ///
    /// Normal is not a fourth Weizza school. It is what the protagonist is
    /// before Pyada has been put to any use: walk, swing, nothing fancier.
    /// Than, Se and In are the three real schools, and only the protagonist and
    /// Sojee can hold more than one, which is why the player can switch between
    /// them mid fight and nobody else in the world ever could.
    /// </summary>
    public enum StanceId
    {
        /// <summary>No school yet. Walk and jump, no dash, no parry.</summary>
        Normal = 0,

        /// <summary>Than, iron. Never flinches, hardest hits, slowest, no dash. Weight is a platforming tool.</summary>
        Than = 1,

        /// <summary>Se, herbal. Fastest feet and hands, dash with i-frames, shrugs off poison, cannot jump.</summary>
        Se = 2,

        /// <summary>In, inscription. Fights at range, heals on a kill, wall jumps, no dash, takes more damage.</summary>
        In = 3,
    }

    /// <summary>Numbers that must be identical everywhere or the feel drifts.</summary>
    public static class Timing
    {
        /// <summary>Design frame rate. All frame counts in data mean this rate.</summary>
        public const float ReferenceFps = 60f;

        /// <summary>Seconds in one design frame.</summary>
        public const float Frame = 1f / ReferenceFps;

        public static float Frames(int count) => count * Frame;
    }

    /// <summary>
    /// Event names raised on StoryEvents. String keys are the seam between
    /// gameplay and StoryKit, so they live in one place instead of being typed
    /// out at every call site where a typo would fail silently.
    /// </summary>
    public static class GameEvents
    {
        public const string StanceSwitched = "Combat.StanceSwitched";
        public const string PlayerDamaged = "Combat.PlayerDamaged";
        public const string PlayerDied = "Combat.PlayerDied";
        public const string PlayerRevived = "Combat.PlayerRevived";

        /// <summary>Carries the lane index landed on.</summary>
        public const string PlayerBlinkedLane = "Combat.PlayerBlinkedLane";
        public const string ParrySuccess = "Combat.ParrySuccess";
        public const string EnemyDefeated = "Combat.EnemyDefeated";
        public const string EncounterStarted = "Combat.EncounterStarted";
        public const string EncounterEnded = "Combat.EncounterEnded";

        /// <summary>Carries the shot mode name.</summary>
        public const string ShotFired = "Combat.ShotFired";
        /// <summary>Raised once when a charge shot reaches full. Audio plays the ping off this.</summary>
        public const string ShotChargeFull = "Combat.ShotChargeFull";
        public const string AimSlowStarted = "Combat.AimSlowStarted";
        public const string AimSlowEnded = "Combat.AimSlowEnded";

        /// <summary>Carries the amount healed.</summary>
        public const string HealedOnKill = "Combat.HealedOnKill";

        public const string WallJumped = "Move.WallJumped";
        public const string Dashed = "Move.Dashed";

        /// <summary>Carries the stance name that was refused because it is not learned yet.</summary>
        public const string StanceLocked = "Combat.StanceLocked";

        /// <summary>Carries the stance name that was just learned.</summary>
        public const string StanceLearned = "Progress.StanceLearned";

        /// <summary>Carries the checkpoint key.</summary>
        public const string CheckpointClaimed = "World.CheckpointClaimed";
        public const string GameSaved = "Save.Saved";
        public const string GameLoaded = "Save.Loaded";

        /// <summary>Boss events carry the boss display name.</summary>
        public const string BossStarted = "Boss.Started";
        public const string BossPhaseChanged = "Boss.PhaseChanged";
        public const string BossForcedDeath = "Boss.ForcedDeath";
        public const string BossDefeated = "Boss.Defeated";

        /// <summary>The player died in a normal way mid fight: the boss goes back to sleep at full health.</summary>
        public const string BossReset = "Boss.Reset";

        /// <summary>World events carry the room id.</summary>
        public const string RoomEntered = "World.RoomEntered";
        public const string RoomLoaded = "World.RoomLoaded";
        public const string GateUsed = "World.GateUsed";
        public const string MapOpened = "UI.MapOpened";
    }

    /// <summary>Blackboard keys game code writes for graphs and conditions to read.</summary>
    public static class StateKeys
    {
        public const string Stance = "player.stance";
        public const string HealthNormalized = "player.hp";
        public const string InCombat = "player.inCombat";
    }

    /// <summary>Physics layer names. Set these up in Project Settings once.</summary>
    public static class Layers
    {
        public const string Solid = "Solid";
        public const string Player = "Player";
        public const string Enemy = "Enemy";
        public const string PlayerHitbox = "PlayerHitbox";
        public const string EnemyHitbox = "EnemyHitbox";
    }

    /// <summary>Which side an attack came from, used for knockback and flinch.</summary>
    public enum Facing
    {
        Left = -1,
        Right = 1,
    }

    public static class FacingExtensions
    {
        public static float Sign(this Facing facing) => (float)(int)facing;

        public static Facing FromSign(float value) =>
            value < 0f ? Facing.Left : Facing.Right;
    }
}
