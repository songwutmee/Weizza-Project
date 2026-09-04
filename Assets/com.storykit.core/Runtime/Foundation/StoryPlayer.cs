using UnityEngine;

namespace StoryKit
{
    /// <summary>
    /// One cached reference to the player, shared by everything that needs it.
    ///
    /// Without this, every distance culler, teleport node and trigger would
    /// call FindGameObjectWithTag on its own. With two hundred enemies in a
    /// scene that is two hundred scene searches on load.
    ///
    /// The reference re-resolves automatically if the player is destroyed and
    /// respawned, which is normal in a souls-like death loop.
    /// </summary>
    public static class StoryPlayer
    {
        private static Transform _cached;
        private static float _nextRetry;

        /// <summary>The player transform, or null if none is tagged yet.</summary>
        public static Transform Transform
        {
            get
            {
                if (_cached != null) return _cached;

                // Retry at most a few times a second so a scene without a
                // player does not search every frame.
                if (Time.unscaledTime < _nextRetry) return null;
                _nextRetry = Time.unscaledTime + 0.25f;

                var found = GameObject.FindGameObjectWithTag("Player");
                _cached = found != null ? found.transform : null;
                return _cached;
            }
        }

        public static Vector3 Position =>
            Transform != null ? Transform.position : Vector3.zero;

        /// <summary>Call after deliberately swapping the player object.</summary>
        public static void Invalidate()
        {
            _cached = null;
            _nextRetry = 0f;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetOnPlay() => Invalidate();
    }
}
