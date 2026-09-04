using UnityEngine;
using UnityEngine.InputSystem.UI;

namespace StoryKit.InputSystemSupport
{
    /// <summary>
    /// Supplies the UI input module for the new Input System.
    ///
    /// This is the other half of the fix for the every-frame
    /// InvalidOperationException about UnityEngine.Input: the core runtime is
    /// not allowed to name InputSystemUIInputModule, because that type lives in
    /// an assembly it must not depend on. So it asks, and this answers.
    /// </summary>
    public static class InputSystemUiModule
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSplashScreen)]
        private static void Install()
        {
            StoryUi.ModuleInstaller = host => host.AddComponent<InputSystemUIInputModule>();
        }
    }
}
