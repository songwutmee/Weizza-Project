using PrimeTween;
using Sirenix.OdinInspector;
using UnityEngine;

namespace Weizza
{
    /// <summary>
    /// Fades a sprite in or out through a noise mask with a bright edge,
    /// rather than a plain alpha wipe. Built for `Common_Revive`, the body
    /// reassembling out of butterflies the art plan draws and nothing in
    /// code owned yet: `RespawnDirector` calls `DissolveIn` on the player
    /// once it has teleported them behind the black, timed to finish with
    /// the screen fade.
    ///
    /// General purpose, not player specific. Anything with a SpriteRenderer
    /// can use it: a mini boss phase change, an item vanishing when taken.
    /// </summary>
    [AddComponentMenu("Weizza/Dissolve Effect")]
    public sealed class DissolveEffect : MonoBehaviour
    {
        [BoxGroup("Setup")]
        [Tooltip("Found on this object if left empty.")]
        [SerializeField] private SpriteRenderer sprite;

        [BoxGroup("Look", centerLabel: true)]
        [PropertyRange(2f, 60f)]
        [Tooltip("How fine the noise pattern is. Low values dissolve in a " +
                 "few big blotches, high values in fine speckling.")]
        [SerializeField] private float noiseScale = 18f;

        [BoxGroup("Look")]
        [PropertyRange(0.01f, 0.4f)]
        [SerializeField] private float edgeWidth = 0.08f;

        [BoxGroup("Look")]
        [ColorUsage(true, true)]
        [Tooltip("HDR on purpose, the same reason the light shafts are: " +
                 "above one it blooms, which is what sells the edge as " +
                 "energy rather than as a flat coloured line.")]
        [SerializeField] private Color edgeColour = new(1.6f, 1.3f, 0.6f, 1f);

        private static readonly int DissolveAmountId = Shader.PropertyToID("_DissolveAmount");
        private static readonly int NoiseScaleId = Shader.PropertyToID("_NoiseScale");
        private static readonly int EdgeWidthId = Shader.PropertyToID("_EdgeWidth");
        private static readonly int EdgeColorId = Shader.PropertyToID("_EdgeColor");

        private static Material _sharedMaterial;

        private MaterialPropertyBlock _block;
        private Material _originalMaterial;
        private Tween _tween;

        [BoxGroup("Setup")]
        [ShowInInspector, ReadOnly]
        public float Amount { get; private set; }

        private void Awake()
        {
            if (sprite == null) sprite = GetComponent<SpriteRenderer>();
            _block = new MaterialPropertyBlock();
            if (sprite != null) _originalMaterial = sprite.sharedMaterial;
        }

        private void OnDestroy() => _tween.Stop();

        /// <summary>Sweeps from fully gone to fully visible.</summary>
        public void DissolveIn(float seconds, System.Action onComplete = null) =>
            Animate(1f, 0f, seconds, onComplete);

        /// <summary>Sweeps from fully visible to fully gone.</summary>
        public void DissolveOut(float seconds, System.Action onComplete = null) =>
            Animate(0f, 1f, seconds, onComplete);

        /// <summary>Sets the amount directly, no animation. 0 is solid, 1 is gone.</summary>
        public void SetAmount(float amount)
        {
            if (sprite == null) return;

            var material = SharedMaterial();
            if (material == null) return;
            sprite.sharedMaterial = material;
            Amount = Mathf.Clamp01(amount);
            Apply();
        }

        private void Animate(float from, float to, float seconds, System.Action onComplete)
        {
            var material = sprite != null ? SharedMaterial() : null;
            if (material == null)
            {
                // No dissolve available: finish at once so whoever waits on it carries on.
                onComplete?.Invoke();
                return;
            }

            sprite.sharedMaterial = material;
            Amount = from;
            Apply();

            _tween.Stop();
            _tween = Tween.Custom(from, to, Mathf.Max(0.01f, seconds), t =>
            {
                Amount = t;
                Apply();
            }, useUnscaledTime: true);

            if (onComplete != null)
            {
                _tween.OnComplete(() =>
                {
                    // Fully solid again, hand the renderer back to whatever
                    // material it started with rather than leaving every
                    // revived character permanently on the shared dissolve
                    // material for a shader pass it no longer needs.
                    if (Amount <= 0f && sprite != null && _originalMaterial != null)
                        sprite.sharedMaterial = _originalMaterial;
                    onComplete();
                });
            }
        }

        private void Apply()
        {
            if (sprite == null) return;

            sprite.GetPropertyBlock(_block);
            _block.SetFloat(DissolveAmountId, Amount);
            _block.SetFloat(NoiseScaleId, noiseScale);
            _block.SetFloat(EdgeWidthId, edgeWidth);
            _block.SetColor(EdgeColorId, edgeColour);
            sprite.SetPropertyBlock(_block);
        }

        private static Material SharedMaterial()
        {
            if (_sharedMaterial != null) return _sharedMaterial;
            // Missing from a build, the dissolve is skipped instead of throwing in the
            // middle of the respawn beat, which would leave the player dead on the floor.
            var shader = RuntimeShaders.Find("Weizza/Dissolve");
            if (shader == null) return null;
            _sharedMaterial = new Material(shader) { name = "Dissolve (shared)" };
            return _sharedMaterial;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => _sharedMaterial = null;
    }
}
