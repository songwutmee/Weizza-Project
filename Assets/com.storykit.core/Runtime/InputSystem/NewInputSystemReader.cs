using UnityEngine;
using UnityEngine.InputSystem;

namespace StoryKit.InputSystemSupport
{
    /// <summary>
    /// Input reader for the new Input System.
    ///
    /// This whole assembly is skipped unless ENABLE_INPUT_SYSTEM is defined, so
    /// a project without the Input System package never sees it and never has a
    /// broken assembly reference. When it is present it installs itself before
    /// the first scene loads and takes priority over the legacy reader, which
    /// is what a project set to "Both" wants.
    ///
    /// It reads devices directly rather than an InputActionAsset on purpose: a
    /// package that demanded a specific action map would be one more thing to
    /// set up before anything worked. Point StoryInput.Source at your own
    /// reader the moment you have real bindings.
    /// </summary>
    public sealed class NewInputSystemReader : IStoryInput
    {
        public Key advanceKey = Key.Space;
        public Key advanceAltKey = Key.Enter;
        public Key interactKey = Key.E;
        public Key pauseKey = Key.Escape;
        public Key logKey = Key.J;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSplashScreen)]
        private static void Install()
        {
            // Runs after StoryKitLifecycle has installed the legacy reader, and
            // overrides it: if a project has both, the new one is the one the
            // player is actually using.
            StoryInput.InstallDefault(new NewInputSystemReader(), overrideExisting: true);
        }

        public bool AdvancePressed =>
            WasPressed(advanceKey)
            || WasPressed(advanceAltKey)
            || (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
            || (Gamepad.current != null && Gamepad.current.buttonSouth.wasPressedThisFrame);

        public bool InteractPressed =>
            WasPressed(interactKey)
            || (Gamepad.current != null && Gamepad.current.buttonWest.wasPressedThisFrame);

        public bool PausePressed =>
            WasPressed(pauseKey)
            || (Gamepad.current != null && Gamepad.current.startButton.wasPressedThisFrame);

        public bool ToggleLogPressed =>
            WasPressed(logKey)
            || (Gamepad.current != null && Gamepad.current.selectButton.wasPressedThisFrame);

        public Vector2 Move
        {
            get
            {
                var stick = Gamepad.current != null
                    ? Gamepad.current.leftStick.ReadValue()
                    : Vector2.zero;
                if (stick.sqrMagnitude > 0.04f) return stick;

                var keyboard = Keyboard.current;
                if (keyboard == null) return Vector2.zero;

                float x = (keyboard.dKey.isPressed || keyboard.rightArrowKey.isPressed ? 1f : 0f)
                          - (keyboard.aKey.isPressed || keyboard.leftArrowKey.isPressed ? 1f : 0f);
                float y = (keyboard.wKey.isPressed || keyboard.upArrowKey.isPressed ? 1f : 0f)
                          - (keyboard.sKey.isPressed || keyboard.downArrowKey.isPressed ? 1f : 0f);
                return new Vector2(x, y);
            }
        }

        public Vector2 Look
        {
            get
            {
                var stick = Gamepad.current != null
                    ? Gamepad.current.rightStick.ReadValue()
                    : Vector2.zero;
                if (stick.sqrMagnitude > 0.04f) return stick * 2f;

                var mouse = Mouse.current;
                if (mouse != null) return mouse.delta.ReadValue() * 0.05f;
                
                return Vector2.zero;
            }
        }

        public bool Jump =>
            IsPressed(Key.Space) || (Gamepad.current != null && Gamepad.current.buttonSouth.isPressed);

        public bool Sprint =>
            IsPressed(Key.LeftShift) || (Gamepad.current != null && Gamepad.current.leftStickButton.isPressed);

        public bool Fire =>
            (Mouse.current != null && Mouse.current.leftButton.isPressed) || 
            (Gamepad.current != null && Gamepad.current.rightTrigger.isPressed);

        public bool Aim =>
            (Mouse.current != null && Mouse.current.rightButton.isPressed) || 
            (Gamepad.current != null && Gamepad.current.leftTrigger.isPressed);

        private static bool WasPressed(Key key)
        {
            var keyboard = Keyboard.current;
            return keyboard != null && key != Key.None && keyboard[key].wasPressedThisFrame;
        }

        private static bool IsPressed(Key key)
        {
            var keyboard = Keyboard.current;
            return keyboard != null && key != Key.None && keyboard[key].isPressed;
        }
    }
}
