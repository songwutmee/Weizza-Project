using System.Threading;
using Cysharp.Threading.Tasks;
using StoryKit;
using UnityEngine;

namespace Weizza
{
    /// <summary>
    /// The freeze on impact. More of what a hit feels like comes from here than
    /// from the damage number, which is why it is measured in frames and lives
    /// on the attack asset next to the damage.
    ///
    /// Time is never written directly. StoryTimeScale owns it, counts holders,
    /// and already knows how to survive a pause landing on top of a freeze.
    /// </summary>
    public static class HitStop
    {
        private static CancellationTokenSource _life;
        private static int _held;

        /// <summary>How many freezes are stacked right now. For the debug window.</summary>
        public static int Held => _held;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _life?.Cancel();
            _life?.Dispose();
            _life = new CancellationTokenSource();
            _held = 0;
        }

        /// <summary>Freezes everything for a number of design frames.</summary>
        public static void Freeze(int frames)
        {
            if (frames <= 0) return;
            FreezeAsync(Timing.Frames(frames)).Forget();
        }

        public static void FreezeSeconds(float seconds)
        {
            if (seconds <= 0f) return;
            FreezeAsync(seconds).Forget();
        }

        private static async UniTaskVoid FreezeAsync(float seconds)
        {
            var token = _life?.Token ?? CancellationToken.None;

            _held++;
            StoryTimeScale.Stop();
            try
            {
                await UniTask.Delay(
                    System.TimeSpan.FromSeconds(seconds),
                    DelayType.UnscaledDeltaTime,
                    PlayerLoopTiming.Update,
                    token);
            }
            finally
            {
                _held = Mathf.Max(0, _held - 1);
                StoryTimeScale.Resume();
            }
        }
    }
}
