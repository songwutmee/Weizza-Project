using System.Threading;
using Cysharp.Threading.Tasks;
using PrimeTween;
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

        [BoxGroup("Telegraph", centerLabel: true)]
        [InfoBox("A wind up pulses instead of holding still, so it reads as a " +
                 "different message from a damage flash. Colour alone was not " +
                 "enough: an orange hold and a red blink look the same at a " +
                 "glance on a small sprite.")]
        [PropertyRange(0.5f, 8f)]
        [Tooltip("Pulses per second while a tint is held.")]
        [SerializeField] private float holdPulseHz = 3.5f;

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
        private Tween _squashTween;
        private bool _holding;
        private Color _heldTint = Color.white;
        private float _holdStarted;

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
            _holding = false;
            if (sprite == null) return;
            sprite.color = _baseColor;
        }

        private void OnDestroy() => CancelAll();

        private void CancelAll()
        {
            _flashLife?.Cancel();
            _flashLife?.Dispose();
            _flashLife = null;
            _squashTween.Stop();
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

        /// <summary>
        /// Holds a tint until released, for a telegraph that has to last as
        /// long as the wind up rather than a few frames.
        ///
        /// This exists so exactly one component ever writes the sprite's
        /// colour. An enemy brain setting the colour itself would be fighting
        /// this one every time a hit landed mid telegraph, and the loser of
        /// that fight is whichever wrote first, which changes frame to frame.
        /// </summary>
        public void HoldTint(Color tint)
        {
            if (sprite == null) return;
            _holding = true;
            _heldTint = tint;
            _holdStarted = Time.unscaledTime;
            _flashLife?.Cancel();
            _flashLife?.Dispose();
            _flashLife = null;
            sprite.color = tint;
        }

        /// <summary>
        /// Pulses the held tint, so a wind up cannot be mistaken for a damage
        /// flash.
        ///
        /// The two are different messages and were reading as the same one: a
        /// flash is a single blink that means something just happened, a
        /// telegraph is a sustained beat that means something is about to.
        /// Colour alone was not carrying that, and a still tint on a small
        /// sprite is easy to miss entirely.
        /// </summary>
        private void Update()
        {
            if (!_holding || sprite == null) return;

            // Unscaled, because a telegraph interrupted by hit stop should keep
            // pulsing rather than freezing into a solid colour.
            var beat = Mathf.PingPong((Time.unscaledTime - _holdStarted) * holdPulseHz * 2f, 1f);
            sprite.color = Color.Lerp(_baseColor, _heldTint, 0.35f + beat * 0.65f);
        }

        /// <summary>Drops a held tint and goes back to the sprite's own colour.</summary>
        public void ReleaseTint()
        {
            _holding = false;
            if (sprite != null && _flashLife == null) sprite.color = _baseColor;
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

            _flashLife = null;

            // A hit landing mid telegraph must not wipe the telegraph. The
            // flash is temporary, the held tint is the state underneath it.
            if (sprite != null) sprite.color = _holding ? _heldTint : _baseColor;
        }

        /// <summary>Squashes on landing, scaled by how hard the landing was.</summary>
        public void Land(float impactSpeed)
        {
            if (sprite == null || landSquash <= 0f) return;

            var strength = Mathf.Clamp01(impactSpeed / Mathf.Max(0.01f, fullSquashSpeed));
            if (strength <= 0.05f) return;

            var amount = strength * landSquash;
            var duration = Timing.Frames(squashFrames);

            // The sign is read once, not every frame, so a character that
            // turns around mid squash keeps stretching along the facing it
            // landed with rather than flipping the pulse halfway through.
            var facing = Mathf.Sign(sprite.transform.localScale.x);

            _squashTween.Stop();
            _squashTween = Tween.Custom(0f, 1f, duration, t =>
            {
                // Out and back in one curve, so it never needs a second pass.
                var pulse = Mathf.Sin(t * Mathf.PI) * amount;
                sprite.transform.localScale = new Vector3(
                    facing * Mathf.Abs(_baseScale.x) * (1f + pulse),
                    _baseScale.y * (1f - pulse),
                    _baseScale.z);
            }, useUnscaledTime: true);
        }
    }
}
