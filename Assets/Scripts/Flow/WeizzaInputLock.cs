using StoryKit;

namespace Weizza
{
    /// <summary>
    /// StoryKit's IInputService, answered with the one thing the project
    /// already has: StoryControl. PlayerController.ReadInput already checks
    /// StoryControl.MovementLocked, so this is a one-line bridge rather than a
    /// second lock mechanism competing with the first.
    /// </summary>
    public sealed class WeizzaInputLock : IInputService
    {
        public System.IDisposable LockPlayerControl(string reason) => StoryControl.Lock(reason);
    }
}
