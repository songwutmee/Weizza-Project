using System.Collections.Generic;
using UnityEngine;

namespace Weizza
{
    /// <summary>
    /// Small shapes drawn in code: a soft dot, a diamond, a ring, a crosshair,
    /// a dash strip for lines.
    ///
    /// Every system that needs a placeholder visual takes one from here
    /// rather than each building its own texture, so there is one cache and a
    /// feature never has to wait for art to be playable. Art replaces these
    /// by being assigned on the data asset; nothing here has to change.
    /// </summary>
    public static class ProceduralSprites
    {
        private static readonly Dictionary<string, Sprite> Cache = new();
        private static Texture2D _dashTexture;
        private static Material _lineMaterial;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Cache.Clear();
            _dashTexture = null;
            _lineMaterial = null;
        }

        /// <summary>Round dot with a soft edge.</summary>
        public static Sprite Dot(int size = 32) => Get($"dot{size}", size, (x, y, r) =>
        {
            var d = Mathf.Sqrt(x * x + y * y);
            return Mathf.Clamp01((1f - d) * r * 0.5f);
        });

        /// <summary>A pointed diamond, longer than it is tall, for a thrown ward.</summary>
        public static Sprite Diamond(int size = 32) => Get($"diamond{size}", size, (x, y, r) =>
        {
            var d = Mathf.Abs(x) + Mathf.Abs(y) * 2f;
            return Mathf.Clamp01((1f - d) * r * 0.5f);
        });

        /// <summary>A thin ring, for charge meters and lock on marks.</summary>
        public static Sprite Ring(int size = 64, float thickness = 0.12f) =>
            Get($"ring{size}_{thickness:0.00}", size, (x, y, r) =>
            {
                var d = Mathf.Sqrt(x * x + y * y);
                var edge = Mathf.Abs(d - (1f - thickness));
                return Mathf.Clamp01((thickness - edge) * r * 0.5f);
            });

        /// <summary>A five petal flower, soft edged, for the blooms a defeated enemy leaves.</summary>
        public static Sprite Flower(int size = 48) => Get($"flower{size}", size, (x, y, r) =>
        {
            var d = Mathf.Sqrt(x * x + y * y);
            var a = Mathf.Atan2(y, x);
            // Petal edge: a radius that swells five times around the circle.
            var petal = 0.55f + 0.4f * Mathf.Abs(Mathf.Cos(a * 2.5f));
            return Mathf.Clamp01((petal - d) * r * 0.25f);
        });

        /// <summary>Four ticks around an empty centre. The cursor reticle.</summary>
        public static Sprite Crosshair(int size = 64) => Get($"cross{size}", size, (x, y, r) =>
        {
            var ax = Mathf.Abs(x);
            var ay = Mathf.Abs(y);
            const float thickness = 0.07f;
            var horizontal = ay < thickness && ax > 0.35f && ax < 0.95f;
            var vertical = ax < thickness && ay > 0.35f && ay < 0.95f;
            var dot = x * x + y * y < 0.012f;
            return horizontal || vertical || dot ? 1f : 0f;
        });

        /// <summary>Square corners only, the lock on bracket.</summary>
        public static Sprite Brackets(int size = 64) => Get($"brackets{size}", size, (x, y, r) =>
        {
            var ax = Mathf.Abs(x);
            var ay = Mathf.Abs(y);
            const float thickness = 0.08f;
            var nearEdgeX = ax > 1f - thickness * 2f;
            var nearEdgeY = ay > 1f - thickness * 2f;
            var inCornerX = ax > 0.55f;
            var inCornerY = ay > 0.55f;
            return (nearEdgeX && inCornerY) || (nearEdgeY && inCornerX) ? 1f : 0f;
        });

        /// <summary>
        /// A white and clear strip that repeats, for a dashed line. Scroll its
        /// offset to make the dashes run.
        /// </summary>
        public static Texture2D DashTexture()
        {
            if (_dashTexture != null) return _dashTexture;

            _dashTexture = new Texture2D(16, 4, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Bilinear,
                name = "Weizza Dash",
            };

            for (var x = 0; x < 16; x++)
            {
                var on = x < 9 ? 1f : 0f;
                for (var y = 0; y < 4; y++) _dashTexture.SetPixel(x, y, new Color(1f, 1f, 1f, on));
            }
            _dashTexture.Apply();
            return _dashTexture;
        }

        /// <summary>
        /// One shared unlit material for code-built lines and trails. Sprites
        /// Default ships with every Unity build because SpriteRenderer itself
        /// uses it, so Shader.Find cannot come back empty in a player.
        /// </summary>
        public static Material LineMaterial()
        {
            if (_lineMaterial != null) return _lineMaterial;
            _lineMaterial = new Material(Shader.Find("Sprites/Default")) { name = "Weizza Line" };
            return _lineMaterial;
        }

        /// <summary>A fresh copy of the line material, for a line that scrolls its own texture.</summary>
        public static Material NewLineMaterial(Texture texture)
        {
            var material = new Material(LineMaterial()) { mainTexture = texture };
            return material;
        }

        private delegate float Shape(float x, float y, float sharpness);

        private static Sprite Get(string key, int size, Shape shape)
        {
            if (Cache.TryGetValue(key, out var cached) && cached != null) return cached;

            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                name = "Weizza " + key,
            };

            var half = (size - 1) * 0.5f;
            for (var py = 0; py < size; py++)
            {
                for (var px = 0; px < size; px++)
                {
                    var x = (px - half) / half;
                    var y = (py - half) / half;
                    texture.SetPixel(px, py, new Color(1f, 1f, 1f, shape(x, y, size)));
                }
            }
            texture.Apply();

            var sprite = Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
            sprite.name = key;
            Cache[key] = sprite;
            return sprite;
        }
    }
}
