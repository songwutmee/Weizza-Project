using UnityEngine;

namespace Weizza
{
    /// <summary>
    /// The shared glowing material and the swing slash. Anything that has to read
    /// as light rather than paint (a hit flash, a spark, a slash) draws with this
    /// material, which goes past white so bloom catches it.
    /// </summary>
    public static class GlowFx
    {
        private static Material _material;
        private static Sprite _slash;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _material = null;
            _slash = null;
        }

        /// <summary>Lives in Resources so a build always carries the shader with it.</summary>
        public static Material Material => _material != null ? _material : _material = Resources.Load<Material>("GlowAdditive");

        /// <summary>A white crescent swept forward from where the swing starts.</summary>
        public static void Slash(Vector3 at, float facing, float size, Color tint)
        {
            var go = new GameObject("Slash");
            go.transform.position = at;
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = SlashSprite();
            if (Material != null) sr.sharedMaterial = Material;
            sr.sortingLayerName = "Foreground";
            sr.sortingOrder = 90;
            // Not flipX: the glow shader draws its own quad and never reads the sprite flip, so a
            // flipped slash still pointed right. A negative scale on x mirrors it for real.
            go.AddComponent<SlashLife>().Begin(sr, size, tint, facing < 0f ? -1f : 1f);
        }

        // A crescent: the band between two offset circles, thick in the middle of
        // the swing and sharp at both tips. Painted once.
        private static Sprite SlashSprite()
        {
            if (_slash != null) return _slash;
            const int w = 256, h = 192;
            var pixels = new Color32[w * h];
            for (var y = 0; y < h; y++)
            for (var x = 0; x < w; x++)
            {
                var px = (x - w * 0.3f) / (w * 0.5f);
                var py = (y - h * 0.5f) / (h * 0.5f);
                var outer = 1f - Mathf.Sqrt(px * px + py * py);
                var ix = px + 0.28f;
                var inner = Mathf.Sqrt(ix * ix + py * py) - 0.86f;
                var band = Mathf.Min(outer, inner) * 14f;
                // Brightest toward the leading edge, fading along the tips.
                var a = Mathf.Clamp01(band) * Mathf.Clamp01(px * 1.4f + 0.2f);
                pixels[y * w + x] = new Color32(255, 255, 255, (byte)(a * 255f));
            }
            var texture = new Texture2D(w, h, TextureFormat.RGBA32, false) { name = "Slash", wrapMode = TextureWrapMode.Clamp };
            texture.SetPixels32(pixels);
            texture.Apply();
            _slash = Sprite.Create(texture, new Rect(0, 0, w, h), new Vector2(0.3f, 0.5f), 140f);
            return _slash;
        }
    }

    [AddComponentMenu("")]
    public sealed class SlashLife : MonoBehaviour
    {
        private SpriteRenderer _sr;
        private float _age, _size, _side = 1f;
        private Color _tint;
        private const float Life = 0.16f;

        public void Begin(SpriteRenderer sr, float size, Color tint, float side)
        {
            _sr = sr;
            _side = side;
            _size = size;
            _tint = tint;
            transform.rotation = Quaternion.Euler(ViewRig.Pitch, 0f, 0f);
            Apply(0f);
        }

        private void Update()
        {
            // Unscaled, so the slash still plays through the hit stop it causes.
            _age += Time.unscaledDeltaTime;
            if (_age >= Life) { Destroy(gameObject); return; }
            Apply(_age / Life);
        }

        private void Apply(float t)
        {
            // Snaps out to full size, then thins and fades.
            var grow = 1f - Mathf.Pow(1f - t, 4f);
            transform.localScale = new Vector3(_side * _size * (0.7f + grow * 0.35f), _size * (1f - t * 0.35f), 1f);
            _sr.color = new Color(_tint.r, _tint.g, _tint.b, 1f - t * t);
        }
    }
}
