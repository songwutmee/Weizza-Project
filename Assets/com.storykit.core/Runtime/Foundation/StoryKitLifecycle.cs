using UnityEngine;

namespace StoryKit
{
    /// <summary>
    /// Resets every piece of static state when play starts.
    ///
    /// Unity can be told to skip the domain reload between play sessions
    /// (Edit > Project Settings > Editor > Enter Play Mode Options). It makes
    /// entering play mode almost instant, and most teams turn it on. The catch
    /// is that static fields keep their values from the previous session.
    ///
    /// Without this file, the second time you press Play you would get:
    ///   - event handlers from the last session still subscribed, so every
    ///     event fires twice
    ///   - services pointing at destroyed objects, so nodes throw null errors
    ///   - the stage id registry full of destroyed transforms
    ///   - global variables still holding last session's values, so quests
    ///     look already completed and dialogue skips itself
    ///
    /// That class of bug is miserable to track down because the first play
    /// always works. This runs before any scene loads and clears all of it.
    /// </summary>
    public static class StoryKitLifecycle
    {
        /// <summary>
        /// Raised right after everything is cleared, before the first scene
        /// loads. Register game-wide services here if you do not use GameKit.
        /// </summary>
        public static event System.Action Reset;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetAll()
        {
            // Order matters: clear listeners before the data they listen to.
            StoryEvents.Reset();
            StoryServices.Reset();
            StoryCommands.Reset();
            StoryPlayer.Invalidate();

            Quest.StoryConditions.Reset();
            Quest.StoryCounters.Reset();

            Bridge.StoryStageId.ResetRegistry();
            Bridge.StoryGraphTrigger.ResetRegistry();
            Flow.FlowRunner.ActiveRunners.Clear();
            StorySave.ResetWarnings();

            // Reinstalls the legacy reader. The optional Input System assembly
            // runs later and overrides it, so a project with both ends up on
            // the new one, which is what the player is actually using.
            StoryInput.Reset();
            Bridge.Haptics.Reset();
            StoryUi.Reset();
            StoryControl.Reset();
            StoryUi.ResetCursor();
            Defaults.SimpleQuestLog.ResetActive();
            Defaults.SimpleItemScreen.ResetActive();

            // A fresh board every session. Games that load a save overwrite
            // this immediately in their own startup.
            StoryState.SetGlobal(new Blackboard());

            StoryTime.Provider = new UnityTimeProvider();
            StoryTimeScale.Reset();

            Reset?.Invoke();
        }
    }
}
