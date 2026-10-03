using UnityEngine;

namespace Weizza
{
    /// <summary>
    /// Bends a still drawing in the wind through the Weizza/SpriteSway material.
    /// Tells the shader where the sprite sits on its sheet and which end is
    /// planted, the two things a shared material cannot know per piece.
    ///
    /// Cheap on purpose: it writes a property block when the sprite changes and
    /// does nothing per frame. The motion itself is all in the shader.
    /// </summary>
    [AddComponentMenu("Weizza/Level/Sprite Sway")]
    [ExecuteAlways]
    [RequireComponent(typeof(SpriteRenderer))]
    public sealed class SpriteSway : MonoBehaviour
    {
        private static readonly int UVRect = Shader.PropertyToID("_UVRect");
        private static readonly int Hanging = Shader.PropertyToID("_Hanging");
        private static readonly int Strength = Shader.PropertyToID("_Strength");

        [Tooltip("On for vines and anything hanging from above: the top stays put and the bottom swings.")]
        [SerializeField] private bool hanging;

        [Tooltip("How far the free end travels, in metres. Small props 0.06 to 0.12, long vines up to 0.3.")]
        [Range(0f, 0.6f)]
        [SerializeField] private float strength = 0.1f;

        private SpriteRenderer _renderer;
        private MaterialPropertyBlock _block;

        public void Configure(bool hangs, float metres)
        {
            hanging = hangs;
            strength = metres;
            Apply();
        }

        private void OnEnable() => Apply();
        private void OnValidate() => Apply();

        // Scenery never swaps its sprite at runtime, so there is no per-frame
        // check. Call this after changing the sprite from code.
        public void Apply()
        {
            if (_renderer == null) _renderer = GetComponent<SpriteRenderer>();
            if (_renderer == null || _renderer.sprite == null) return;
            _block ??= new MaterialPropertyBlock();

            var uv = _renderer.sprite.uv;
            float minY = 1f, maxY = 0f, minX = 1f, maxX = 0f;
            for (var i = 0; i < uv.Length; i++)
            {
                if (uv[i].y < minY) minY = uv[i].y;
                if (uv[i].y > maxY) maxY = uv[i].y;
                if (uv[i].x < minX) minX = uv[i].x;
                if (uv[i].x > maxX) maxX = uv[i].x;
            }

            _renderer.GetPropertyBlock(_block);
            _block.SetVector(UVRect, new Vector4(minX, minY, maxX - minX, maxY - minY));
            _block.SetFloat(Hanging, hanging ? 1f : 0f);
            _block.SetFloat(Strength, strength);
            _renderer.SetPropertyBlock(_block);
        }
    }
}
