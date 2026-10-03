using PrimeTween;
using Sirenix.OdinInspector;
using UnityEngine;

namespace Weizza.Narrative
{
    /// <summary>
    /// A real outline around the sprite, pulsing. The marker for "this can be
    /// picked up", separate from NpcInteractable's glow: an NPC darkens to
    /// pulse because it already reads as a character, an item sitting among
    /// the Mossy scenery does not read as anything without motion, a static
    /// white rim was tried first and still got lost against the foliage.
    ///
    /// Used to be a second SpriteRenderer, same sprite, scaled up, drawn one
    /// order behind, because a real outline shader was a job worth doing once
    /// for every sprite rather than once for a handful of pickups. That job
    /// is `Weizza/SpriteOutline` now (`Assets/Scripts/Shaders/SpriteOutline.shader`),
    /// so this swaps the art's own material onto it instead of building a
    /// second renderer: one draw call, one transform, no child object whose
    /// scale has to be kept in step with the pulse.
    /// </summary>
    [AddComponentMenu("Weizza/Interactable Outline")]
    public sealed class InteractableOutline : MonoBehaviour
    {
        [BoxGroup("Look", centerLabel: true)]
        [Tooltip("Found on this object if left empty.")]
        [SerializeField] private SpriteRenderer art;

        [BoxGroup("Look")]
        [PropertyRange(0f, 8f)]
        [Tooltip("How many real texels the outline reaches outward. Keep " +
                 "this inside the padding Unity's own sprite packer leaves " +
                 "around a packed sprite, or the outline starts reading " +
                 "pixels from whatever sprite is packed next door.")]
        [SerializeField] private float width = 2f;

        [BoxGroup("Look")]
        [ColorUsage(false)]
        [SerializeField] private Color colour = Color.white;

        [BoxGroup("Pulse", centerLabel: true)]
        [InfoBox("A still white rim reads as part of the art within a few " +
                 "seconds. Motion is what says 'this one is different', the " +
                 "same reason NpcInteractable pulses instead of sitting at one " +
                 "brightness.")]
        [ToggleLeft]
        [SerializeField] private bool pulse = true;

        [BoxGroup("Pulse")]
        [ShowIf(nameof(pulse))]
        [PropertyRange(0f, 1f)]
        [Tooltip("Alpha at the dimmest point of the pulse. 1 turns the pulse " +
                 "into pure width with no flicker.")]
        [SerializeField] private float minAlpha = 0.15f;

        [BoxGroup("Pulse")]
        [ShowIf(nameof(pulse))]
        [PropertyRange(0f, 4f)]
        [Tooltip("How many extra texels the outline gains on top of Width at " +
                 "the brightest point. Zero pulses alpha only.")]
        [SerializeField] private float swell = 1f;

        [BoxGroup("Pulse")]
        [ShowIf(nameof(pulse))]
        [PropertyRange(0.2f, 4f)]
        [Tooltip("Pulses per second.")]
        [SerializeField] private float speed = 1.4f;

        private static readonly int OutlineColorId = Shader.PropertyToID("_OutlineColor");
        private static readonly int OutlineWidthId = Shader.PropertyToID("_OutlineWidth");
        private static readonly int OutlineAlphaId = Shader.PropertyToID("_OutlineAlpha");

        private static Shader _shader;
        private static Material _sharedMaterial;

        private MaterialPropertyBlock _block;
        private Tween _pulseTween;
        private bool _visible = true;

        private void Awake()
        {
            if (art == null) art = GetComponent<SpriteRenderer>();
            if (art == null) return;

            art.sharedMaterial = SharedMaterial();
            _block = new MaterialPropertyBlock();

            // Randomised so two pickups placed near each other do not pulse in
            // lock step, the same reason Plant Animations wants a different
            // fps per plant.
            var phase = Random.Range(0f, 1f / speed);

            if (pulse)
            {
                _pulseTween = Tween.Custom(0f, 1f, 1f / speed, OnPulse,
                    ease: Ease.InOutSine, cycles: -1, cycleMode: CycleMode.Yoyo,
                    startDelay: phase, useUnscaledTime: true);
            }
            else
            {
                ApplyOutline(width, colour.a);
            }
        }

        private void OnDestroy() => _pulseTween.Stop();

        private void OnPulse(float t)
        {
            if (!_visible) return;
            var alpha = Mathf.Lerp(minAlpha, 1f, t);
            ApplyOutline(width + swell * t, alpha);
        }

        private void ApplyOutline(float outlineWidth, float alpha)
        {
            if (art == null) return;

            // Width reaches the shader in real screen pixels (fwidth based,
            // see SpriteOutline.shader), not raw texels, so it reads the
            // same thickness whether the source art is a 512px sheet or a
            // 2048px scan. A fixed texel count was tried first and went
            // sub-pixel on Inscription's 2048px texture, which looked
            // exactly like the pulse animation not running when the alpha
            // was in fact cycling correctly the whole time.
            art.GetPropertyBlock(_block);
            _block.SetColor(OutlineColorId, colour);
            _block.SetFloat(OutlineWidthId, outlineWidth);
            _block.SetFloat(OutlineAlphaId, alpha);
            art.SetPropertyBlock(_block);
        }

        public void SetVisible(bool visible)
        {
            _visible = visible;
            if (!visible) ApplyOutline(width, 0f);
        }

        private static Material SharedMaterial()
        {
            if (_sharedMaterial != null) return _sharedMaterial;

            _shader = Shader.Find("Weizza/SpriteOutline");
            _sharedMaterial = new Material(_shader) { name = "SpriteOutline (shared)" };
            return _sharedMaterial;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _shader = null;
            _sharedMaterial = null;
        }
    }
}
