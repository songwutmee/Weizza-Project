using System.Collections.Generic;
using UnityEngine;

namespace Weizza
{
    /// <summary>
    /// The flash at the point of contact.
    ///
    /// Hit stop tells the player the game noticed. Screen shake tells them it
    /// mattered. Neither of them says where it happened, and without that a
    /// connect and a whiff look identical for the few frames that decide
    /// whether an attack feels like it landed on something solid.
    ///
    /// Runs on unscaled time, which is the whole trick. A spark that ticks on
    /// scaled time freezes along with the hit stop it is supposed to be
    /// punctuating, so the one moment it exists for is the one moment it is
    /// standing perfectly still.
    ///
    /// Draws itself, so it needs no art and no prefab. Replace the generated
    /// sprite with a drawn one later by assigning Sprite on the pooled
    /// renderer; nothing else has to change.
    /// </summary>
    public static class HitSpark
    {
        private static readonly List<HitSparkInstance> Pool = new(16);
        private static Sprite _sprite;
        private static Transform _root;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Pool.Clear();
            _sprite = null;
            _root = null;
        }

        /// <summary>Flashes once at a world point, stretched along the hit direction.</summary>
        public static void Play(Vector3 point, Vector2 direction, float strength, Color tint)
        {
            var spark = Take();
            if (spark == null) return;

            spark.Begin(point, direction, Mathf.Clamp(strength, 0.35f, 2.5f), tint, Sprite());
        }

        public static void Play(Vector3 point, Vector2 direction, float strength) =>
            Play(point, direction, strength, Color.white);

        private static HitSparkInstance Take()
        {
            foreach (var spark in Pool)
            {
                if (spark != null && !spark.Busy) return spark;
            }

            if (_root == null)
            {
                _root = new GameObject("Hit Sparks").transform;
                Object.DontDestroyOnLoad(_root.gameObject);
            }

            var made = new GameObject("Spark").AddComponent<HitSparkInstance>();
            made.transform.SetParent(_root, false);
            Pool.Add(made);
            return made;
        }

        /// <summary>
        /// A four point burst, built once. Round would read as a bubble; the
        /// spikes are what make it read as an impact.
        /// </summary>
        private static Sprite Sprite()
        {
            if (_sprite != null) return _sprite;

            const int size = 96;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = "HitSparkBurst",
                hideFlags = HideFlags.HideAndDontSave,
                wrapMode = TextureWrapMode.Clamp,
            };

            var centre = (size - 1) * 0.5f;
            var pixels = new Color32[size * size];

            for (var y = 0; y < size; y++)
            {
                for (var x = 0; x < size; x++)
                {
                    var dx = (x - centre) / centre;
                    var dy = (y - centre) / centre;
                    var distance = Mathf.Sqrt(dx * dx + dy * dy);

                    if (distance >= 1f)
                    {
                        pixels[y * size + x] = new Color32(255, 255, 255, 0);
                        continue;
                    }

                    var angle = Mathf.Atan2(dy, dx);
                    var spike = Mathf.Pow(Mathf.Abs(Mathf.Cos(angle * 2f)), 5f);

                    // A hot core that is always there, plus arms along the
                    // diagonals. Squared falloff so the edge is soft.
                    var body = Mathf.Pow(1f - distance, 2f);
                    var alpha = Mathf.Clamp01(body * (0.3f + 0.7f * spike));

                    pixels[y * size + x] = new Color32(255, 255, 255, (byte)(alpha * 255f));
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply();

            _sprite = UnityEngine.Sprite.Create(texture, new Rect(0, 0, size, size),
                new Vector2(0.5f, 0.5f), size);
            _sprite.name = "HitSparkBurst";
            _sprite.hideFlags = HideFlags.HideAndDontSave;
            return _sprite;
        }
    }

    /// <summary>One pooled spark. Owned by HitSpark, not placed by hand.</summary>
    [AddComponentMenu("")]
    public sealed class HitSparkInstance : MonoBehaviour
    {
        private SpriteRenderer _renderer;
        private float _life;
        private float _age;
        private float _scale;
        private Color _tint;

        public bool Busy { get; private set; }

        private void Awake()
        {
            _renderer = gameObject.AddComponent<SpriteRenderer>();
            // Drawn as light, past white, so bloom makes it sting.
            if (GlowFx.Material != null) _renderer.sharedMaterial = GlowFx.Material;

            // In front of characters, which are on Default. A spark drawn
            // behind the thing it is hitting is a spark nobody sees.
            _renderer.sortingLayerName = "Foreground";
            _renderer.sortingOrder = 100;
            _renderer.enabled = false;
        }

        public void Begin(Vector3 point, Vector2 direction, float strength, Color tint, Sprite sprite)
        {
            if (_renderer.sprite == null) _renderer.sprite = sprite;

            _tint = tint;
            _scale = 0.55f + strength * 0.5f;
            _life = Timing.Frames(9);
            _age = 0f;
            Busy = true;

            transform.position = point;

            // Leaned along the hit so the burst points the way the force went.
            var angle = direction.sqrMagnitude > 0.0001f
                ? Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg
                : 0f;
            transform.rotation = Quaternion.Euler(ViewRig.Pitch, 0f, angle);

            _renderer.enabled = true;
            Apply(0f);
        }

        private void Update()
        {
            if (!Busy) return;

            // Unscaled on purpose. This exists to be seen during hit stop.
            _age += Time.unscaledDeltaTime;

            if (_age >= _life)
            {
                Busy = false;
                _renderer.enabled = false;
                return;
            }

            Apply(_age / _life);
        }

        private void Apply(float t)
        {
            // Snaps open, then eases away. Opening slowly reads as a puff of
            // smoke rather than as something being struck.
            var open = 1f - Mathf.Pow(1f - t, 3f);
            var size = _scale * (0.45f + open * 0.75f);

            transform.localScale = new Vector3(size * 1.25f, size, 1f);

            var fade = 1f - t * t;
            _renderer.color = new Color(_tint.r, _tint.g, _tint.b, fade);
        }
    }
}
