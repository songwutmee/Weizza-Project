using Sirenix.OdinInspector;
using UnityEngine;

namespace Weizza
{
    /// <summary>
    /// Old wood that holds everyone except Than. "Stands on wood and it
    /// breaks" on the Than - list: the same weight that opens plates is a
    /// liability here, so a Than player has to cross it as someone else.
    /// </summary>
    [AddComponentMenu("Weizza/Level/Weak Wood")]
    public sealed class WeakWood : MonoBehaviour
    {
        [BoxGroup("Rule")]
        [PropertyRange(0f, 2f)]
        [Tooltip("Seconds a Heavy body can stand on it before it gives. A small " +
                 "grace lets a Than player who walked on by mistake step back.")]
        [SerializeField] private float holdSeconds = 0.35f;

        [BoxGroup("Rule")]
        [PropertyRange(0f, 1f)]
        [SerializeField] private float warnSeconds = 0.2f;

        [BoxGroup("Rule")]
        [PropertyRange(0f, 30f)]
        [Tooltip("Seconds before the planks come back. Zero stays broken.")]
        [SerializeField] private float respawnSeconds = 4f;

        private float _stoodFor;
        private bool _breaking;

        private void Update()
        {
            if (_breaking) return;

            var player = PlayerLocator.Player;
            var standing = player != null && LevelPiece.IsHeavy(player) &&
                           LevelPiece.Owns(this, player.Motor.GroundCollider);

            _stoodFor = standing ? _stoodFor + Time.deltaTime : 0f;
            if (_stoodFor < holdSeconds) return;

            _breaking = true;
            BreakAsync().Forget();
        }

        private async Cysharp.Threading.Tasks.UniTaskVoid BreakAsync()
        {
            LevelPiece.CrumbleAsync(transform, warnSeconds, respawnSeconds, destroyCancellationToken).Forget();
            await Cysharp.Threading.Tasks.UniTask.Delay(
                System.TimeSpan.FromSeconds(warnSeconds + Mathf.Max(0.1f, respawnSeconds)),
                cancellationToken: destroyCancellationToken);
            _stoodFor = 0f;
            _breaking = respawnSeconds <= 0f;
        }
    }
}
