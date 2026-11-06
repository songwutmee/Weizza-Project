using System.Collections.Generic;
using UnityEngine;

namespace Weizza
{
    /// <summary>
    /// Breaks a drawing into pieces of itself: the sprite is cut into a grid of smaller
    /// sprites from the same texture, and each piece falls, tumbles and fades. A floor
    /// that gives way then reads as that floor breaking, not as a picture switched off.
    ///
    /// Also throws a little grit while something is only cracking. Pieces are pooled and
    /// the cut sprites are cached per source drawing, so a floor that breaks every few
    /// seconds costs nothing after the first time.
    /// </summary>
    public static class ShatterFx
    {
        private sealed class Shard
        {
            public SpriteRenderer renderer;
            public Vector3 velocity;
            public float spin, born, life;
            public Color colour;
        }

        private static readonly List<Shard> Shards = new(48);
        private static readonly Dictionary<Sprite, Sprite[]> Cuts = new();
        private static Runner _runner;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Shards.Clear();
            Cuts.Clear();
            _runner = null;
        }

        /// <summary>Breaks the drawing into <paramref name="columns"/> by <paramref name="rows"/> falling pieces.</summary>
        public static void Shatter(SpriteRenderer source, int columns = 5, int rows = 2, float force = 1f)
        {
            if (source == null || source.sprite == null) return;
            var pieces = CutsOf(source.sprite, columns, rows);
            var t = source.transform;
            var size = source.sprite.rect.size / source.sprite.pixelsPerUnit;
            var cell = new Vector2(size.x / columns, size.y / rows);
            var corner = (Vector2)source.sprite.bounds.min;

            for (var i = 0; i < pieces.Length; i++)
            {
                var c = i % columns;
                var r = i / columns;
                var local = corner + new Vector2((c + 0.5f) * cell.x, (r + 0.5f) * cell.y);
                if (source.flipX) local.x = -local.x;
                var world = t.TransformPoint(local);

                var shard = Free(source);
                shard.renderer.sprite = pieces[i];
                shard.renderer.flipX = source.flipX;
                shard.renderer.transform.SetPositionAndRotation(world, t.rotation);
                shard.renderer.transform.localScale = t.lossyScale;
                // Out from the middle and a little up, the top row thrown harder than the bottom.
                var outward = (c + 0.5f) / columns - 0.5f;
                shard.velocity = new Vector3(outward * 3.5f + Random.Range(-0.6f, 0.6f),
                    Random.Range(0.8f, 2.6f) * (r + 1) / rows, 0f) * force;
                shard.spin = Random.Range(-260f, 260f) * force;
                shard.life = Random.Range(0.8f, 1.2f);
                Launch(shard, source.color);
            }
        }

        /// <summary>A few crumbs from the top edge, for a floor that is cracking but still holding.</summary>
        public static void Grit(SpriteRenderer source, int count = 3)
        {
            if (source == null || source.sprite == null) return;
            var pieces = CutsOf(source.sprite, 8, 4);
            var b = source.bounds;
            for (var i = 0; i < count; i++)
            {
                var shard = Free(source);
                shard.renderer.sprite = pieces[Random.Range(0, pieces.Length)];
                shard.renderer.transform.SetPositionAndRotation(
                    new Vector3(Random.Range(b.min.x, b.max.x), b.max.y - 0.1f, source.transform.position.z - 0.01f),
                    Quaternion.identity);
                shard.renderer.transform.localScale = source.transform.lossyScale * 0.35f;
                shard.velocity = new Vector3(Random.Range(-0.6f, 0.6f), Random.Range(0.4f, 1.2f), 0f);
                shard.spin = Random.Range(-400f, 400f);
                shard.life = 0.55f;
                Launch(shard, source.color);
            }
        }

        private static void Launch(Shard shard, Color colour)
        {
            shard.colour = colour;
            shard.born = Time.time;
            shard.renderer.color = colour;
            shard.renderer.enabled = true;
            if (_runner == null)
            {
                var go = new GameObject("Shatter Fx");
                Object.DontDestroyOnLoad(go);
                _runner = go.AddComponent<Runner>();
            }
        }

        private static Sprite[] CutsOf(Sprite sprite, int columns, int rows)
        {
            if (Cuts.TryGetValue(sprite, out var cached) && cached.Length == columns * rows) return cached;
            var rect = sprite.textureRect;
            var cells = new Sprite[columns * rows];
            var w = rect.width / columns;
            var h = rect.height / rows;
            for (var r = 0; r < rows; r++)
                for (var c = 0; c < columns; c++)
                    cells[r * columns + c] = Sprite.Create(sprite.texture,
                        new Rect(rect.x + c * w, rect.y + r * h, w, h), new Vector2(0.5f, 0.5f), sprite.pixelsPerUnit);
            Cuts[sprite] = cells;
            return cells;
        }

        private static Shard Free(SpriteRenderer like)
        {
            Shard shard = null;
            foreach (var s in Shards) if (!s.renderer.enabled) { shard = s; break; }
            if (shard == null)
            {
                var go = new GameObject("Shard");
                Object.DontDestroyOnLoad(go);
                shard = new Shard { renderer = go.AddComponent<SpriteRenderer>() };
                Shards.Add(shard);
            }
            shard.renderer.sharedMaterial = like.sharedMaterial;
            shard.renderer.sortingLayerID = like.sortingLayerID;
            shard.renderer.sortingOrder = like.sortingOrder + 1;
            return shard;
        }

        private sealed class Runner : MonoBehaviour
        {
            private void Update()
            {
                var dt = Time.deltaTime;
                foreach (var s in Shards)
                {
                    if (!s.renderer.enabled) continue;
                    var k = (Time.time - s.born) / s.life;
                    if (k >= 1f) { s.renderer.enabled = false; continue; }
                    s.velocity.y -= 18f * dt;
                    var t = s.renderer.transform;
                    t.position += s.velocity * dt;
                    t.Rotate(0f, 0f, s.spin * dt);
                    var c = s.colour;
                    c.a *= 1f - k * k;
                    s.renderer.color = c;
                }
            }
        }
    }
}
