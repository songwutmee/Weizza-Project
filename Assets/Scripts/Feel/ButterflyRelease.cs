using System.Collections.Generic;
using UnityEngine;

namespace Weizza
{
    /// <summary>
    /// When a creature dies its butterflies leave it. In the story every body holds a
    /// butterfly, and the monsters are bodies Sawgyi pinned wrong; a death here is the
    /// pinned butterflies getting out. So the body fades and, over the same moment,
    /// butterflies rise out of it one after another, flutter, drift up and are gone.
    ///
    /// The butterfly is drawn in code (two pairs of wings and a body), white, tinted per
    /// insect; it flaps by squeezing its width, the way wings seen face on open and close.
    /// One pool for the whole game, nothing allocated per death. Art can replace the
    /// drawing later by assigning a sprite to <see cref="Override"/>.
    /// </summary>
    [AddComponentMenu("")]
    public sealed class ButterflyRelease : MonoBehaviour
    {
        /// <summary>A drawn butterfly to use instead of the one made in code.</summary>
        public static Sprite Override;

        private const int PoolSize = 96;

        private sealed class Fly
        {
            public SpriteRenderer Renderer;
            public Vector3 Position, Velocity;
            public float Born, Life, Phase, FlapRate, Size, Sway;
            public Color Colour;
            public bool Alive;
        }

        private struct Pending
        {
            public Vector3 Min, Max;
            public float At;
            public Color Tint;
            public float Size;
        }

        private static ButterflyRelease _instance;
        private static Sprite _sprite;
        private readonly List<Fly> _flies = new();
        private readonly List<Pending> _pending = new();

        // A soft spread of light colours so a swarm is not one flat tint. Light on purpose:
        // the grade maps by brightness, and these read as pale wings in any mood.
        private static readonly Color[] Palette =
        {
            new(1f, 0.97f, 0.86f), new(1f, 0.9f, 0.62f), new(0.92f, 0.98f, 0.8f), new(1f, 0.82f, 0.7f),
        };

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _instance = null;
            _sprite = null;
            Override = null;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Hook()
        {
            EnemyController.AnyDied -= OnEnemyDied;
            EnemyController.AnyDied += OnEnemyDied;
        }

        private static void OnEnemyDied(EnemyController enemy)
        {
            if (enemy == null) return;
            // Bosses carry more butterflies: a swarm, not a handful.
            var boss = enemy.GetComponent<MiniBossBrain>() != null;
            Release(enemy.gameObject, boss ? 36 : 9, boss ? 1.6f : 0.9f);
        }

        /// <summary>Let butterflies out of everything drawn on this object, spread over a moment.</summary>
        public static void Release(GameObject body, int count, float overSeconds)
        {
            if (body == null || count <= 0) return;
            var renderers = body.GetComponentsInChildren<SpriteRenderer>(false);
            var has = false;
            var bounds = new Bounds();
            foreach (var r in renderers)
            {
                if (r == null || !r.enabled || r.sprite == null || r.name.Contains("Shadow")) continue;
                if (!has) { bounds = r.bounds; has = true; } else bounds.Encapsulate(r.bounds);
            }
            if (!has) bounds = new Bounds(body.transform.position + Vector3.up * 0.6f, Vector3.one);
            var size = Mathf.Clamp(Mathf.Sqrt(bounds.size.x * bounds.size.y) * 0.13f, 0.14f, 0.32f);

            var host = Instance();
            var now = Time.time;
            for (var i = 0; i < count; i++)
            {
                // Out from the middle first, then the edges, the way a body comes apart.
                var k = i / (float)Mathf.Max(1, count - 1);
                host._pending.Add(new Pending
                {
                    Min = Vector3.Lerp(bounds.center, bounds.min, 0.25f + 0.75f * k),
                    Max = Vector3.Lerp(bounds.center, bounds.max, 0.25f + 0.75f * k),
                    At = now + overSeconds * k * Random.Range(0.8f, 1f),
                    Tint = Palette[Random.Range(0, Palette.Length)],
                    Size = size * Random.Range(0.75f, 1.2f),
                });
            }
        }

        private static ButterflyRelease Instance()
        {
            if (_instance != null) return _instance;
            var go = new GameObject("Butterflies");
            DontDestroyOnLoad(go);
            _instance = go.AddComponent<ButterflyRelease>();
            for (var i = 0; i < PoolSize; i++)
            {
                var child = new GameObject("Butterfly");
                child.transform.SetParent(go.transform, false);
                var r = child.AddComponent<SpriteRenderer>();
                r.sprite = Override != null ? Override : DrawnButterfly();
                r.sortingLayerName = "Default";
                r.sortingOrder = 3;
                r.enabled = false;
                _instance._flies.Add(new Fly { Renderer = r });
            }
            return _instance;
        }

        private void Update()
        {
            var now = Time.time;
            for (var i = _pending.Count - 1; i >= 0; i--)
            {
                var p = _pending[i];
                if (p.At > now) continue;
                _pending.RemoveAt(i);
                Spawn(p, now);
            }

            var dt = Time.deltaTime;
            foreach (var f in _flies)
            {
                if (!f.Alive) continue;
                var age = now - f.Born;
                var k = age / f.Life;
                if (k >= 1f) { f.Alive = false; f.Renderer.enabled = false; continue; }

                // Fluttering flight: a steady lift, a lazy side to side sway, and a little
                // hop on every wing beat, slowing as it climbs away.
                var beat = Mathf.Sin(age * f.FlapRate + f.Phase);
                f.Velocity.x += Mathf.Sin(age * 2.3f + f.Phase) * f.Sway * dt;
                f.Velocity.y += (0.9f + Mathf.Max(0f, beat) * 2.2f - f.Velocity.y * 1.4f) * dt;
                f.Velocity.x *= 1f - 0.6f * dt;
                f.Position += f.Velocity * dt;

                var t = f.Renderer.transform;
                t.position = f.Position;
                // Wings open and close: width squeezes to a sliver and back.
                var open = 0.25f + 0.75f * Mathf.Abs(beat);
                var facing = f.Velocity.x >= 0f ? 1f : -1f;
                t.localScale = new Vector3(f.Size * open * facing, f.Size, 1f);
                t.localRotation = Quaternion.Euler(0f, 0f, -f.Velocity.x * 8f);

                // In quickly, out slowly, so each one seems to come out of the body.
                var c = f.Colour;
                c.a = Mathf.Min(1f, k * 8f) * (1f - k * k);
                f.Renderer.color = c;
            }
        }

        private void Spawn(Pending p, float now)
        {
            Fly free = null;
            foreach (var f in _flies) if (!f.Alive) { free = f; break; }
            if (free == null) return;
            free.Alive = true;
            free.Born = now;
            free.Life = Random.Range(1.6f, 2.6f);
            free.Phase = Random.Range(0f, 6.28f);
            free.FlapRate = Random.Range(16f, 24f);
            free.Sway = Random.Range(2f, 4f);
            free.Size = p.Size;
            free.Colour = p.Tint;
            free.Position = new Vector3(Random.Range(p.Min.x, p.Max.x), Random.Range(p.Min.y, p.Max.y), -0.1f);
            free.Velocity = new Vector3(Random.Range(-1.2f, 1.2f), Random.Range(0.4f, 1.4f), 0f);
            free.Renderer.enabled = true;
            free.Renderer.color = new Color(p.Tint.r, p.Tint.g, p.Tint.b, 0f);
        }

        // Two pairs of rounded wings, bigger on top, and a thin body, white on clear.
        private static Sprite DrawnButterfly()
        {
            if (_sprite != null) return _sprite;
            const int n = 64;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { name = "Butterfly", filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[n * n];
            for (var y = 0; y < n; y++)
            for (var x = 0; x < n; x++)
            {
                var u = (x + 0.5f) / n * 2f - 1f;          // -1..1 across
                var v = (y + 0.5f) / n * 2f - 1f;          // -1..1 up
                var ax = Mathf.Abs(u);
                // Upper wing: a tilted ellipse up and out; lower wing: a smaller one down and out.
                var up = Ellipse(ax - 0.48f, v - 0.28f, 0.46f, 0.5f, 0.5f);
                var low = Ellipse(ax - 0.36f, v + 0.36f, 0.32f, 0.36f, -0.5f);
                var body = Mathf.Clamp01(1f - (ax - 0.04f) / 0.03f) * Mathf.Clamp01(1f - (Mathf.Abs(v + 0.05f) - 0.55f) / 0.05f);
                var wing = Mathf.Max(up, low);
                // A lighter band near the edge of the wings, like scale colour thinning out.
                var edge = Mathf.Clamp01(1f - Mathf.Max(EllipseDist(ax - 0.48f, v - 0.28f, 0.46f, 0.5f, 0.5f), 0f) * 3f);
                var a = Mathf.Max(wing, body);
                var shade = Mathf.Lerp(0.82f, 1f, edge * 0.5f + 0.5f);
                var b = (byte)Mathf.RoundToInt(255f * Mathf.Lerp(shade, 0.35f, body * (1f - wing)));
                px[y * n + x] = new Color32(b, b, b, (byte)Mathf.RoundToInt(255f * a));
            }
            tex.SetPixels32(px);
            tex.Apply(false, true);
            _sprite = Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), n);
            _sprite.name = "Butterfly";
            return _sprite;
        }

        private static float EllipseDist(float x, float y, float rx, float ry, float tilt)
        {
            var c = Mathf.Cos(tilt);
            var s = Mathf.Sin(tilt);
            var px = (x * c + y * s) / rx;
            var py = (-x * s + y * c) / ry;
            return Mathf.Sqrt(px * px + py * py);
        }

        // Soft edged ellipse coverage, 1 inside, fading over the last tenth.
        private static float Ellipse(float x, float y, float rx, float ry, float tilt) =>
            Mathf.Clamp01((1f - EllipseDist(x, y, rx, ry, tilt)) / 0.1f);
    }
}
