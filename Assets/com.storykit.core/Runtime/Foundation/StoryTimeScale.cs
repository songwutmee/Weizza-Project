using UnityEngine;

namespace StoryKit
{
    /// <summary>
    /// The only thing that writes Time.timeScale.
    ///
    /// Three features used to own it independently: the pause menu, hit stop,
    /// and the slow motion node. Each cached the value it found and put it back
    /// afterwards, which is correct alone and wrong together. Pause during a slow
    /// motion cutscene, let the cutscene get cancelled, and the node restores
    /// full speed while the pause menu is still up, so the game plays on behind
    /// the menu. Nobody finds that by reading one file.
    ///
    /// So a full stop is counted, and effects set a base speed underneath it.
    /// A stop always wins, and the last one to lift it decides when time moves.
    /// </summary>
    public static class StoryTimeScale
    {
        private static float _base = 1f;
        private static int _stopCount;

        /// <summary>Speed the game runs at when nothing has stopped it.</summary>
        public static float Base => _base;

        /// <summary>True while a pause or a hit stop is holding time still.</summary>
        public static bool Stopped => _stopCount > 0;

        /// <summary>
        /// Sets the speed effects run at: slow motion, a bullet time node, a
        /// difficulty option. Returns the previous value so a task can put it
        /// back without reading Time.timeScale, which may be zero for a reason
        /// that has nothing to do with the caller.
        /// </summary>
        public static float SetBase(float scale)
        {
            float previous = _base;
            _base = Mathf.Max(0f, scale);
            Apply();
            return previous;
        }

        /// <summary>Freezes time. Pairs with Resume.</summary>
        public static void Stop()
        {
            _stopCount++;
            Apply();
        }

        /// <summary>Lifts one freeze. Time moves again when the last one lifts.</summary>
        public static void Resume()
        {
            _stopCount = Mathf.Max(0, _stopCount - 1);
            Apply();
        }

        private static void Apply() => Time.timeScale = _stopCount > 0 ? 0f : _base;

        internal static void Reset()
        {
            // A stale stop count from the previous play session would start the
            // next one frozen, which reads as the game hanging on launch.
            _base = 1f;
            _stopCount = 0;
            Time.timeScale = 1f;
        }
    }
}
