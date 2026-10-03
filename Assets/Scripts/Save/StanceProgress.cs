using System.Collections.Generic;
using Sirenix.OdinInspector;
using StoryKit;
using UnityEngine;

namespace Weizza
{
    /// <summary>
    /// Which schools the player has learned.
    ///
    /// Neva's best idea is a companion that grows with the story. Weizza's
    /// version is the stance roster: Act 0 starts on Normal and each school is
    /// learned from someone. Kept in the StoryKit blackboard so the save keeps
    /// it and a flow graph can grant a school with a plain Set node
    /// ("stance.learned.Than" = true) without any custom node.
    ///
    /// With no StanceProgressRules in the scene nothing is restricted, so test
    /// scenes and the debug panel keep switching freely.
    /// </summary>
    public static class StanceProgress
    {
        public const string KeyPrefix = "stance.learned.";

        /// <summary>True when a scene has opted in to restricting stances.</summary>
        public static bool Restricted { get; set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => Restricted = false;

        public static bool IsLearned(StanceId id)
        {
            if (!Restricted || id == StanceId.Normal) return true;
            return StoryState.Global.Get(KeyPrefix + id, false);
        }

        public static void Learn(StanceId id)
        {
            if (IsLearnedRaw(id)) return;
            StoryState.Global.Set(KeyPrefix + id, true);
            StoryEvents.Raise(GameEvents.StanceLearned, id.ToString());
        }

        public static void Forget(StanceId id) => StoryState.Global.Set(KeyPrefix + id, false);

        private static bool IsLearnedRaw(StanceId id) => StoryState.Global.Get(KeyPrefix + id, false);
    }

}
