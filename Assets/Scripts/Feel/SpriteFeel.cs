using System.Threading;
using Cysharp.Threading.Tasks;
using Sirenix.OdinInspector;
using UnityEngine;

namespace Weizza
{
    /// <summary>
    /// The two things done to a hand drawn sprite in code rather than in extra
    /// drawn frames: a tint flash on impact, and a squash on landing.
    ///
    /// The whole point of doing it this way is that the art team draws one set
    /// of frames and the feel is tuned afterwards without asking them for
    /// anything. Nothing here should ever require a new drawing.
    /// </summary>
    [AddComponentMenu("Weizza/Sprite Feel")]
    public sealed class SpriteFeel : MonoBehaviour
    {
        [BoxGroup("Setup")]
        [SerializeField] private SpriteRenderer sprite;

        [BoxGroup("Flash", centerLabel: true)]
        [SerializeField] private Color hitTint = Color.white;

        [BoxGroup("Flash")]
        [PropertyRange(1, 20)]
        [Tooltip("Frames the sprite is tinted. Runs on unscaled time so it is " +
                 "visible during hit stop, which is the whole point.")]
        [SerializeField] private int flashFrames = 4;

        [BoxGroup("Flash")]
        [SerializeField] private Color armorTint = new(1f, 0.85f, 0.4f);

        [BoxGroup("Flash")]
        [SerializeField] private Color ignoredTint = new(0.5f, 0.8f, 1f);

        [BoxGroup("Squash", centerLabel: true)]
        [PropertyRange(0f, 0.6f)]
        [Tooltip("How much the sprite flattens on a hard landing.")]
        [SerializeField] private float landSquash = 0.22f;

        [BoxGroup("Squash")]
        [Tooltip("Impact speed that produces the full squash.")]
        [SerializeField] private float fullSquashSpeed = 14f;

        [BoxGroup("Squash")]
        [PropertyRange(1, 30)]
        [SerializeField] private int squashFrames = 8;

        private Color _baseColor = Color.white;
        private Vector3 _baseScale = Vector3.one;
        private CancellationTokenSource _flashLife;
        private CancellationTokenSource _squashLife;

        private void Awake()
        {
            if (sprite == null) sprite = GetComponentInChildren<SpriteRenderer>();
            if (sprite != null)
            {
                _baseColor = sprite.color;
                _baseScale = sprite.transform.localScale;
            }
        }

        private void OnDisable()
        {
            CancelAll();
            if (sprite == null) return;
            sprite.color = _baseColor;
        }

        private void OnDestroy() => CancelAll();

        private void CancelAll()
        {
            _flashLife?.Cancel();
            _flashLife?.Dispose();
            _flashLife = null;
            _squashLife?.Cancel();
            _squashLife?.Dispose();
            _squashLife = null;
        }

        /// <summary>Tints for the result of one hit. Colour says what happened.</summary>
        public void FlashFor(HitResult result)
        {
            switch (result)
            {
                case HitResult.Hit:
                    Flash(hitTint);
                    break;
                case HitResult.Armored:
                    Flash(armorTint);
                    break;
                case HitResult.Ignored:
                case HitResult.Parried:
                    Flash(ignoredTint);
                    break;
            }
        }

        public void Flash(Color tint)
        {
            if (sprite == null) return;
            _flashLife?.Cancel();
            _flashLife?.Dispose();
            _flashLife = new CancellationTokenSource();
            FlashAsync(tint, _flashLife.Token).Forget();
        }

        private async UniTaskVoid FlashAsync(Color tint, CancellationToken token)
        {
            sprite.color = tint;
            try
            {
                await UniTask.Delay(
                    System.TimeSpan.FromSeconds(Timing.Frames(flashFrames)),
                    DelayType.UnscaledDeltaTime, PlayerLoopTiming.Update, token);
            }
            catch (System.OperationCanceledException)
            {
                return;
            }

            if (sprite != null) sprite.color = _baseColor;
        }

        /// <summary>Squashes on landing, scaled by how hard the landing was.</summary>
        public void Land(float impactSpeed)
        {
            if (sprite == null || landSquash <= 0f) return;

            var strength = Mathf.Clamp01(impactSpeed / Mathf.Max(0.01f, fullSquashSpeed));
            if (strength <= 0.05f) return;

            _squashLife?.Cancel();
            _squashLife?.Dispose();
            _squashLife = new CancellationTokenSource();
            SquashAsync(strength * landSquash, _squashLife.Token).Forget();
        }

        private async UniTaskVoid SquashAsync(float amount, CancellationToken token)
        {
            var duration = Timing.Frames(squashFrames);
            var elapsed = 0f;

            try
            {
                while (elapsed < duration)
                {
                    elapsed += Time.unscaledDeltaTime;
                    var t = Mathf.Clamp01(elapsed / duration);

                    // Out and back in one curve, so it never needs a second pass.
                    var pulse = Mathf.Sin(t * Mathf.PI) * amount;
                    var scale = _baseScale;
                    sprite.transform.localScale = new Vector3(
                        Mathf.Sign(sprite.transform.localScale.x) * Mathf.Abs(scale.x) * (1f + pulse),
                        scale.y * (1f - pulse),
                        scale.z);

                    await UniTask.Yield(PlayerLoopTiming.Update, token);
                }
            }
            catch (System.OperationCanceledException)
            {
                return;
            }

            if (sprite == null) return;
            sprite.transform.localScale = new Vector3(
                Mathf.Sign(sprite.transform.localScale.x) * Mathf.Abs(_baseScale.x),
                _baseScale.y, _baseScale.z);
        }
    }
}
