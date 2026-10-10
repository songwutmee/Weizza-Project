using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Sirenix.OdinInspector;
using StoryKit;
using UnityEngine;

namespace Weizza
{
    /// <summary>
    /// What happens after the boss falls: a beat to let it sink in, then the foreground
    /// closes over the screen like a cut, the player is carried on to where the story
    /// continues, and the foreground opens again. Control and HUD are held back while it
    /// runs. Place it anywhere in the room and point it at the spot to land on.
    /// </summary>
    [AddComponentMenu("Weizza/Boss/Boss Aftermath")]
    public sealed class BossAftermath : MonoBehaviour
    {
        [BoxGroup("Where")]
        [Required, Tooltip("The player is put here while the screen is covered.")]
        [SerializeField] private Transform destination;

        [BoxGroup("Timing")]
        [PropertyRange(0f, 6f), Tooltip("Seconds the player gets to look at the fallen boss before the cut.")]
        [SerializeField] private float waitSeconds = 2.8f;

        [BoxGroup("Timing")]
        [PropertyRange(0.2f, 3f), Tooltip("Seconds the screen stays covered, long enough for the camera to arrive.")]
        [SerializeField] private float coveredSeconds = 1f;

        private IDisposable _subscription;
        private bool _running;

        private void OnEnable() =>
            _subscription = StoryEvents.Subscribe(GameEvents.BossDefeated, _ => RunAsync(destroyCancellationToken).Forget());

        private void OnDisable() => _subscription?.Dispose();

        private async UniTaskVoid RunAsync(CancellationToken ct)
        {
            if (_running || destination == null) return;
            _running = true;
            IDisposable hold = null;
            try
            {
                await UniTask.Delay(TimeSpan.FromSeconds(waitSeconds), DelayType.UnscaledDeltaTime,
                    PlayerLoopTiming.Update, ct);
                hold = StoryControl.Lock("Boss aftermath");
                HudScreen.Instance?.SetVisible(false);

                var wipe = ForegroundWipe.Instance;
                if (wipe != null) await wipe.PlayAsync(MovePlayer, coveredSeconds, ct);
                else MovePlayer();
            }
            catch (OperationCanceledException) { }
            finally
            {
                HudScreen.Instance?.SetVisible(true);
                hold?.Dispose();
                _running = false;
            }
        }

        private void MovePlayer()
        {
            var player = PlayerLocator.Player;
            if (player == null || destination == null) return;
            if (player.Motor != null)
            {
                player.Motor.Velocity = Vector3.zero;
                player.Motor.Teleport(destination.position);
            }
            else
            {
                player.transform.position = destination.position;
            }
            Physics.SyncTransforms();
        }
    }
}
