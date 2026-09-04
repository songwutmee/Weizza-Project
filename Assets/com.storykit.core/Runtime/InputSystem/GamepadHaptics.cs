using StoryKit.Bridge;
using UnityEngine;
using UnityEngine.InputSystem;

namespace StoryKit.InputSystemSupport
{
    /// <summary>
    /// Gamepad rumble, plugged into Haptics.
    ///
    /// This lives in the optional Input System assembly for the same reason the
    /// input reader does: the core runtime must compile in a project that has
    /// never heard of the Input System package. Referencing Gamepad from there
    /// is what produced "the type or namespace name InputSystem does not exist
    /// in the namespace UnityEngine", because a scripting define being set does
    /// not give an assembly a reference.
    /// </summary>
    public static class GamepadHaptics
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSplashScreen)]
        private static void Install()
        {
            Haptics.PadHandler = SetMotors;
        }

        private static void SetMotors(float low, float high)
        {
            var pad = Gamepad.current;
            if (pad == null) return;

            if (low <= 0f && high <= 0f) pad.ResetHaptics();
            else pad.SetMotorSpeeds(low, high);
        }
    }
}
