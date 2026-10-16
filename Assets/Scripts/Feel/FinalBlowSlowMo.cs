using System.Threading;
using Cysharp.Threading.Tasks;
using StoryKit;
using UnityEngine;

namespace Weizza
{
    /// <summary>
    /// A short slow motion when the player drops the last enemy around them, so the
    /// end of a fight lands as an ending instead of just stopping. Dead Cells does the
    /// same on a critical: freeze, then a few tenths of a second slowed down.
    ///
    /// Waits out the hit stop first (a slowed base under a freeze changes nothing on
    /// screen and then arrives late), and only touches the time scale when nothing
    /// else has slowed it: the death beat and the aiming slow own it while they run.
    ///
    /// Builds itself on scene load, so no scene needs it placed.
    /// </summary>
    [AddComponentMenu("Weizza/Feel/Final Blow Slow Mo")]
    public sealed class FinalBlowSlowMo : MonoBehaviour
    {
        [Tooltip("Speed during the slow motion. 1 is normal speed.")]
        [Range(0.1f, 1f)] [SerializeField] private float scale = 0.35f;

        [Tooltip("Real seconds the slow motion lasts after a grunt.")]
        [Range(0f, 1.5f)] [SerializeField] private float seconds = 0.4f;

        [Tooltip("Real seconds after a boss, which deserves a longer breath.")]
        [Range(0f, 3f)] [SerializeField] private float bossSeconds = 1.1f;

        [Tooltip("Enemies still alive this far from the player (metres along x) mean the fight is not over.")]
        [SerializeField] private float fightRadius = 16f;

        private CancellationTokenSource _running;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void EnsureExists()
        {
            if (FindFirstObjectByType<FinalBlowSlowMo>() != null) return;
            var go = new GameObject("Final Blow Slow Mo");
            DontDestroyOnLoad(go);
            go.AddComponent<FinalBlowSlowMo>();
        }

        private void OnEnable() => EnemyController.AnyDied += OnEnemyDied;

        private void OnDisable()
        {
            EnemyController.AnyDied -= OnEnemyDied;
            Cancel();
        }

        private void OnEnemyDied(EnemyController enemy)
        {
            var player = PlayerLocator.Player;
            if (player == null || enemy == null) return;
            if (Mathf.Abs(enemy.transform.position.x - player.transform.position.x) > fightRadius) return;
            if (EnemyController.CountAliveNear(player.transform.position, fightRadius) > 0) return;

            var boss = enemy.GetComponent<MiniBossBrain>() != null;
            if (boss)
            {
                // The boss going down is the biggest beat in Act 0: a soft impact flash,
                // a ripple from where it fell and a heavy rumble, under the slow motion.
                ScreenFlash.Play();
                ScreenShockwave.Play(enemy.transform.position + Vector3.up, 1f, 0.05f, 1.8f);
                Rumble.Pulse(0.9f, 0.5f, 0.45f);
                WindField.Ripple(enemy.transform.position, 0.4f);
            }
            Cancel();
            _running = new CancellationTokenSource();
            RunAsync(boss ? bossSeconds : seconds, _running.Token).Forget();
        }

        // What this component last wrote, and what to put back. While Base still holds
        // the value written here, the slow motion is ours to undo; once anything else
        // writes it (the death beat, a cutscene), it is theirs and is left alone.
        private float _restoreTo = 1f;
        private float _lastSet = -1f;

        private bool StillOurs => _lastSet >= 0f && Mathf.Abs(StoryTimeScale.Base - _lastSet) < 0.001f;

        private void Write(float value)
        {
            StoryTimeScale.SetBase(value);
            _lastSet = value;
        }

        private async UniTaskVoid RunAsync(float duration, CancellationToken ct)
        {
            if (duration <= 0f) return;
            try
            {
                while (StoryTimeScale.Stopped) await UniTask.Yield(PlayerLoopTiming.Update, ct);

                // Only from normal speed. The normal speed is the assist game speed when it is set.
                if (Mathf.Abs(StoryTimeScale.Base - AssistOptions.Normal) > 0.001f) return;

                _restoreTo = StoryTimeScale.Base;
                Write(_restoreTo * scale);

                var t = 0f;
                while (t < duration)
                {
                    t += Time.unscaledDeltaTime;
                    await UniTask.Yield(PlayerLoopTiming.Update, ct);
                    if (!StillOurs) { _lastSet = -1f; return; }
                }

                // Ease back over a short tail, so speed returns rather than snapping.
                const float tail = 0.18f;
                var e = 0f;
                while (e < tail)
                {
                    e += Time.unscaledDeltaTime;
                    Write(Mathf.Lerp(_restoreTo * scale, _restoreTo, Mathf.Clamp01(e / tail)));
                    await UniTask.Yield(PlayerLoopTiming.Update, ct);
                    if (!StillOurs) { _lastSet = -1f; return; }
                }
                Write(_restoreTo);
                _lastSet = -1f;
            }
            catch (System.OperationCanceledException) { }
        }

        private void Cancel()
        {
            _running?.Cancel();
            _running?.Dispose();
            _running = null;
            // Cut short (another kill, the scene ending): put the speed back if it is still ours.
            if (StillOurs) StoryTimeScale.SetBase(_restoreTo);
            _lastSet = -1f;
        }
    }
}
