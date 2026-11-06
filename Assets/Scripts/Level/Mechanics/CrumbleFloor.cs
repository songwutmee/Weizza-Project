using Sirenix.OdinInspector;
using UnityEngine;

namespace Weizza
{
    /// <summary>
    /// A floor that holds anyone for a moment, then cracks and falls away under them,
    /// and grows back after a while. The classic "keep moving" piece: a run of these
    /// turns a plain walk into a timed one. Unlike Weak Wood it does not care who stands
    /// on it, only for how long.
    ///
    /// The time counts from the first touch and does not reset when the player hops off,
    /// so a floor once stepped on is going whatever happens next; that is what makes a
    /// chain of them a route to plan rather than a trap to stand still on.
    /// </summary>
    [AddComponentMenu("Weizza/Level/Crumble Floor")]
    public sealed class CrumbleFloor : MonoBehaviour
    {
        [BoxGroup("Rule")]
        [PropertyRange(0.1f, 3f), Tooltip("Seconds from the first step until it starts to crack.")]
        [SerializeField] private float holdSeconds = 0.45f;

        [BoxGroup("Rule")]
        [PropertyRange(0.1f, 2f), Tooltip("Seconds it trembles and sheds grit before it breaks.")]
        [SerializeField] private float warnSeconds = 0.5f;

        [BoxGroup("Rule")]
        [PropertyRange(0f, 30f), Tooltip("Seconds before it grows back. Zero stays broken.")]
        [SerializeField] private float respawnSeconds = 3f;

        private float _touchedAt = -1f;
        private bool _breaking;

        private void Update()
        {
            if (_breaking) return;

            var player = PlayerLocator.Player;
            var on = player != null && player.Motor.Grounded && LevelPiece.Owns(this, player.Motor.GroundCollider);
            if (on && _touchedAt < 0f) _touchedAt = Time.time;
            if (_touchedAt < 0f || Time.time - _touchedAt < holdSeconds) return;

            _breaking = true;
            BreakAsync().Forget();
        }

        private async Cysharp.Threading.Tasks.UniTaskVoid BreakAsync()
        {
            LevelPiece.CrumbleAsync(transform, warnSeconds, respawnSeconds, destroyCancellationToken).Forget();
            if (respawnSeconds <= 0f) return;
            await Cysharp.Threading.Tasks.UniTask.Delay(
                System.TimeSpan.FromSeconds(warnSeconds + respawnSeconds + 0.4f),
                cancellationToken: destroyCancellationToken);
            _touchedAt = -1f;
            _breaking = false;
        }
    }
}
