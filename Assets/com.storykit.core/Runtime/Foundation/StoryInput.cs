using UnityEngine;

namespace StoryKit
{
    /// <summary>
    /// What StoryKit needs from the player, as five questions instead of key
    /// codes. Games with their own input map register an implementation and
    /// every trigger, dialogue box and menu follows it.
    /// </summary>
    public interface IStoryInput : IStoryService
    {
        /// <summary>Advance the current dialogue line. Click, Space or Enter by default.</summary>
        bool AdvancePressed { get; }

        /// <summary>Talk to what is in front of the player. E by default.</summary>
        bool InteractPressed { get; }

        /// <summary>Open and close the pause menu. Escape by default.</summary>
        bool PausePressed { get; }

        /// <summary>Open and close the quest log. J by default.</summary>
        bool ToggleLogPressed { get; }

        /// <summary>Movement, only used by the throwaway test mover.</summary>
        Vector2 Move { get; }

        /// <summary>Camera look delta for first person and third person movement.</summary>
        Vector2 Look { get; }

        /// <summary>True while the jump button is held.</summary>
        bool Jump { get; }

        /// <summary>True while the sprint button is held.</summary>
        bool Sprint { get; }

        /// <summary>True while the primary action (fire/attack) is held.</summary>
        bool Fire { get; }

        /// <summary>True while the secondary action (aim/block) is held.</summary>
        bool Aim { get; }
    }

    /// <summary>
    /// One place that knows how input works.
    ///
    /// Unity ships two input systems and a project uses one, the other, or
    /// both. Code written directly against `Input.GetKeyDown` silently stops
    /// compiling when a project is set to the new Input System, and the symptom
    /// is brutal: dialogue opens and never advances, because the key that would
    /// advance it was compiled out. Nothing errors, the game just sits there.
    ///
    /// So nothing in StoryKit reads a key. Everything asks this, and the right
    /// backend is installed automatically:
    ///   new Input System present  -> the StoryKit.InputSystem assembly wins
    ///   old Input Manager only    -> the legacy reader below
    ///   neither                   -> a stub that warns once and returns false
    ///
    /// To use your own bindings, assign Source at startup and stop thinking
    /// about it:
    ///   StoryInput.Source = new MyGameInput();
    /// </summary>
    public static class StoryInput
    {
        private static IStoryInput _source;
        private static bool _warned;

        /// <summary>
        /// The active reader. Assign your own to take over every binding.
        /// Assigning null falls back to whatever the project supports.
        /// </summary>
        public static IStoryInput Source
        {
            get => _source;
            set => _source = value;
        }

        public static bool Advance => _source != null && _source.AdvancePressed;
        public static bool Interact => _source != null && _source.InteractPressed;
        public static bool Pause => _source != null && _source.PausePressed;
        public static bool ToggleLog => _source != null && _source.ToggleLogPressed;
        public static Vector2 Move => _source != null ? _source.Move : Vector2.zero;
        public static Vector2 Look => _source != null ? _source.Look : Vector2.zero;
        public static bool Jump => _source != null && _source.Jump;
        public static bool Sprint => _source != null && _source.Sprint;
        public static bool Fire => _source != null && _source.Fire;
        public static bool Aim => _source != null && _source.Aim;

        /// <summary>Name of the active backend, for the setup checklist.</summary>
        public static string SourceName => _source?.GetType().Name ?? "none";

        /// <summary>
        /// Installs the default reader unless the game already set one. Called
        /// by StoryKitLifecycle before the first scene loads, and again by the
        /// optional Input System assembly, which takes priority.
        /// </summary>
        public static void InstallDefault(IStoryInput candidate, bool overrideExisting)
        {
            if (candidate == null) return;
            if (_source != null && !overrideExisting) return;
            _source = candidate;
        }

        internal static void Reset()
        {
            _source = null;
            _warned = false;

#if ENABLE_LEGACY_INPUT_MANAGER
            _source = new LegacyStoryInput();
#endif
        }

        /// <summary>
        /// Called by components on the frame they first need input. Warns once
        /// rather than every frame, and says exactly what to do about it.
        /// </summary>
        internal static void WarnIfMissing(Object context)
        {
            if (_source != null || _warned) return;
            _warned = true;

            Debug.LogWarning(
                "[StoryKit] No input reader is active, so dialogue cannot be advanced " +
                "and interact keys do nothing.\n" +
                "This project uses the new Input System, so install the optional " +
                "StoryKit.InputSystem assembly that ships with the package, or set " +
                "StoryInput.Source to your own reader at startup.", context);
        }
    }

#if ENABLE_LEGACY_INPUT_MANAGER
    /// <summary>
    /// Reads the old Input Manager. Keys are fields rather than constants so a
    /// game can rebind without replacing the whole reader.
    /// </summary>
    public sealed class LegacyStoryInput : IStoryInput
    {
        public KeyCode advanceKey = KeyCode.Space;
        public KeyCode advanceAltKey = KeyCode.Return;
        public KeyCode interactKey = KeyCode.E;
        public KeyCode pauseKey = KeyCode.Escape;
        public KeyCode logKey = KeyCode.J;

        public bool AdvancePressed =>
            Input.GetMouseButtonDown(0)
            || Input.GetKeyDown(advanceKey)
            || Input.GetKeyDown(advanceAltKey);

        public bool InteractPressed => Input.GetKeyDown(interactKey);
        public bool PausePressed => Input.GetKeyDown(pauseKey);
        public bool ToggleLogPressed => Input.GetKeyDown(logKey);

        public Vector2 Move =>
            new(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical"));

        public Vector2 Look =>
            new(Input.GetAxisRaw("Mouse X"), Input.GetAxisRaw("Mouse Y"));

        public bool Jump => Input.GetKey(KeyCode.Space);
        public bool Sprint => Input.GetKey(KeyCode.LeftShift);
        public bool Fire => Input.GetMouseButton(0);
        public bool Aim => Input.GetMouseButton(1);
    }
#endif
}
