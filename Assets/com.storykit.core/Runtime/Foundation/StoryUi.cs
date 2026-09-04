using UnityEngine;
using UnityEngine.EventSystems;

namespace StoryKit
{
    /// <summary>
    /// The one place that makes a UI clickable.
    ///
    /// A Canvas with buttons does nothing without an EventSystem, and an
    /// EventSystem needs the input module that matches the project. Adding the
    /// legacy StandaloneInputModule to a project set to the new Input System
    /// throws every single frame:
    ///
    ///   InvalidOperationException: You are trying to read Input using the
    ///   UnityEngine.Input class, but you have switched active Input handling
    ///   to Input System package in Player Settings
    ///
    /// It never stops, it fills the console, and the UI still does not work.
    /// So no UI in this package builds its own EventSystem: they all ask here,
    /// and the right module is chosen for the project.
    /// </summary>
    public static class StoryUi
    {
        /// <summary>
        /// Adds the input module that suits the project. Set by the optional
        /// Input System assembly, which is the only one allowed to name
        /// InputSystemUIInputModule.
        /// </summary>
        public static System.Action<GameObject> ModuleInstaller;

        private static bool _warned;

        /// <summary>
        /// Makes sure the scene has a working EventSystem, and returns it.
        /// Safe to call from every UI: the second call finds the first one.
        /// </summary>
        public static EventSystem EnsureEventSystem()
        {
            var existing = Object.FindFirstObjectByType<EventSystem>(FindObjectsInactive.Include);
            if (existing != null) return existing;

            var host = new GameObject("StoryKit.EventSystem");
            Object.DontDestroyOnLoad(host);
            var system = host.AddComponent<EventSystem>();

            if (ModuleInstaller != null)
            {
                ModuleInstaller(host);
                return system;
            }

#if ENABLE_LEGACY_INPUT_MANAGER
            host.AddComponent<StandaloneInputModule>();
            return system;
#else
            WarnNoModule(host);
            return system;
#endif
        }

        private static void WarnNoModule(Object context)
        {
            if (_warned) return;
            _warned = true;

            Debug.LogWarning(
                "[StoryKit] Made an EventSystem with no input module, so on-screen " +
                "buttons will not respond to clicks. This project uses the new Input " +
                "System, so the optional StoryKit.InputSystem assembly should have " +
                "supplied one. Check that it is present and compiling.", context);
        }

        internal static void Reset()
        {
            ModuleInstaller = null;
            _warned = false;
        }

        // ---------------- Cursor ----------------

        /// <summary>How the mouse pointer behaves while playing.</summary>
        public enum CursorMode
        {
            /// <summary>Visible and free. Point and click, top down, strategy.</summary>
            Visible,
            /// <summary>Hidden and pinned to the centre. First person, third person action.</summary>
            Locked,
            /// <summary>Visible but kept inside the window. Fullscreen strategy.</summary>
            Confined,
        }

        private static CursorMode _gameplayMode = CursorMode.Visible;
        private static int _releaseCount;

        /// <summary>
        /// What the cursor does during normal play. Set once at startup, then
        /// forget: dialogue, menus and pausing all borrow and return it.
        /// </summary>
        public static CursorMode GameplayCursor
        {
            get => _gameplayMode;
            set { _gameplayMode = value; ApplyCursor(); }
        }

        /// <summary>
        /// Frees the cursor while some UI needs it, and counts how many things
        /// asked. A dialogue opening a menu that opens a confirm box would break
        /// a plain on/off flag: the innermost close would grab the cursor back
        /// while two screens were still open.
        /// </summary>
        public static void ReleaseCursor()
        {
            _releaseCount++;
            ApplyCursor();
        }

        /// <summary>Gives the cursor back. Pairs with one ReleaseCursor call.</summary>
        public static void RestoreCursor()
        {
            _releaseCount = Mathf.Max(0, _releaseCount - 1);
            ApplyCursor();
        }

        /// <summary>True while anything is holding the cursor open.</summary>
        public static bool CursorReleased => _releaseCount > 0;

        /// <summary>
        /// Writes the counted state back to the OS cursor.
        ///
        /// Needed after alt-tab: the platform frees a locked cursor when the
        /// window loses focus and Unity does not always put it back, so a first
        /// person game returns from the desktop able to see its own pointer.
        /// Nothing else should ever touch Cursor directly, or the count and the
        /// real cursor drift apart.
        /// </summary>
        public static void ReapplyCursor() => ApplyCursor();

        private static void ApplyCursor()
        {
            if (_releaseCount > 0)
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
                return;
            }

            switch (_gameplayMode)
            {
                case CursorMode.Locked:
                    Cursor.lockState = CursorLockMode.Locked;
                    Cursor.visible = false;
                    break;
                case CursorMode.Confined:
                    Cursor.lockState = CursorLockMode.Confined;
                    Cursor.visible = true;
                    break;
                default:
                    Cursor.lockState = CursorLockMode.None;
                    Cursor.visible = true;
                    break;
            }
        }

        internal static void ResetCursor()
        {
            // Leaving play mode with a locked cursor is miserable, and stale
            // release counts from the last session would do exactly that.
            _releaseCount = 0;
            _gameplayMode = CursorMode.Visible;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
    }
}
