using UnityEngine;

namespace Weizza
{
    /// <summary>
    /// The beetle seen from below while it clings to the top of the screen: the
    /// underside of the head, two great curved pincers hanging down, and legs
    /// gripping out to the sides. No tail, because from down here the body hides it.
    /// Painted once in code as a soft edged ink silhouette and cached.
    /// </summary>
    public static class InkLoom
    {
        private const int W = 1024;
        private const int H = 560;
        private static Sprite _sprite;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => _sprite = null;

        /// <summary>Pivot is the top centre, so placing it at the top edge of the frame hangs it there.</summary>
        public static Sprite Get()
        {
            if (_sprite != null) return _sprite;

            var field = new float[W * H];

            // Body, mostly cut off by the top of the frame.
            Ellipse(field, 512, H + 40, 250, 170);
            // Head, the part that pushes down into view.
            Ellipse(field, 512, H - 150, 120, 95);
            Ellipse(field, 512, H - 215, 80, 60);

            // Pincers: out and down from the head, then hooking back in at the tips.
            for (var side = -1; side <= 1; side += 2)
            {
                var s = side;
                Stroke(field, P(512 + s * 70, H - 220), P(512 + s * 250, H - 300), P(512 + s * 180, H - 470), 34f, 9f);
                Stroke(field, P(512 + s * 180, H - 470), P(512 + s * 150, H - 520), P(512 + s * 95, H - 500), 11f, 3f);
                // A small inner tooth on each pincer.
                Stroke(field, P(512 + s * 225, H - 360), P(512 + s * 200, H - 375), P(512 + s * 175, H - 372), 10f, 3f);
            }

            // Legs: three a side, up and out to a knee, then down to a hooked foot.
            float[] rootY = { H - 70, H - 115, H - 160 };
            float[] kneeX = { 360, 420, 400 };
            float[] kneeY = { H - 10, H - 60, H - 150 };
            float[] footX = { 500, 470, 430 };
            float[] footY = { H - 120, H - 230, H - 330 };
            for (var side = -1; side <= 1; side += 2)
            for (var i = 0; i < 3; i++)
            {
                var root = P(512 + side * 200, rootY[i]);
                var knee = P(512 + side * kneeX[i], kneeY[i]);
                var foot = P(512 + side * footX[i], footY[i]);
                Stroke(field, root, Vector2.Lerp(root, knee, 0.5f) + new Vector2(0, 12), knee, 22f, 15f);
                Stroke(field, knee, Vector2.Lerp(knee, foot, 0.5f) + new Vector2(side * 20, 0), foot, 15f, 5f);
                Stroke(field, foot, foot + new Vector2(side * -18, -14), foot + new Vector2(side * -34, -6), 6f, 2f);
            }

            var pixels = new Color32[W * H];
            for (var y = 0; y < H; y++)
            for (var x = 0; x < W; x++)
            {
                // Grain along the edge, so the silhouette reads as ink and not as a vector shape.
                var grain = Mathf.PerlinNoise(x * 0.045f, y * 0.045f) * 0.35f + Mathf.PerlinNoise(x * 0.17f, y * 0.17f) * 0.2f;
                var a = Mathf.Clamp01((field[y * W + x] - 0.35f - grain * 0.4f) * 3.2f);
                pixels[y * W + x] = new Color32(255, 255, 255, (byte)(a * 255f));
            }

            var texture = new Texture2D(W, H, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "InkLoom" };
            texture.SetPixels32(pixels);
            texture.Apply();
            _sprite = Sprite.Create(texture, new Rect(0, 0, W, H), new Vector2(0.5f, 1f), 200f);
            _sprite.name = "InkLoom";
            return _sprite;
        }

        private static Vector2 P(float x, float y) => new(x, y);

        // Writes a soft coverage value: 1 deep inside, falling to 0 over a few pixels outside.
        private static void Splat(float[] field, float cx, float cy, float r)
        {
            var reach = r + 6f;
            for (var y = Mathf.Max(0, (int)(cy - reach)); y < Mathf.Min(H, (int)(cy + reach)); y++)
            for (var x = Mathf.Max(0, (int)(cx - reach)); x < Mathf.Min(W, (int)(cx + reach)); x++)
            {
                var d = Mathf.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy));
                var v = Mathf.Clamp01((r + 4f - d) / 8f) + 0.35f * Mathf.Clamp01((r - d) / r);
                if (v > field[y * W + x]) field[y * W + x] = v;
            }
        }

        private static void Ellipse(float[] field, float cx, float cy, float rx, float ry)
        {
            for (var y = Mathf.Max(0, (int)(cy - ry - 6)); y < Mathf.Min(H, (int)(cy + ry + 6)); y++)
            for (var x = Mathf.Max(0, (int)(cx - rx - 6)); x < Mathf.Min(W, (int)(cx + rx + 6)); x++)
            {
                var dx = (x - cx) / rx;
                var dy = (y - cy) / ry;
                var d = Mathf.Sqrt(dx * dx + dy * dy);
                var v = Mathf.Clamp01((1f - d) * Mathf.Min(rx, ry) / 8f + 0.5f) + 0.35f * Mathf.Clamp01(1f - d);
                if (v > field[y * W + x]) field[y * W + x] = v;
            }
        }

        // A tapering brush stroke along a curve, from thick at a to thin at c.
        private static void Stroke(float[] field, Vector2 a, Vector2 b, Vector2 c, float fromRadius, float toRadius)
        {
            var length = Vector2.Distance(a, b) + Vector2.Distance(b, c);
            var steps = Mathf.CeilToInt(length / 3f);
            for (var i = 0; i <= steps; i++)
            {
                var t = i / (float)steps;
                var p = (1 - t) * (1 - t) * a + 2 * (1 - t) * t * b + t * t * c;
                Splat(field, p.x, p.y, Mathf.Lerp(fromRadius, toRadius, t));
            }
        }
    }
}
