using System;
using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;

namespace Weizza
{
    /// <summary>
    /// The beetle's idle: it sleeps inside a flower bed and its shell blends in, so
    /// while it is dormant all the player sees is a clump of the forest's own
    /// bushes. When the fight wakes it the plants are torn up and thrown
    /// aside and the beetle is there.
    ///
    /// The bushes are the forest pack's own fern clusters, swaying like the rest, so the
    /// clump does not stand out by style. What gives it away is the light: a warm
    /// glow sits under it and spores drift up out of it, the way Ori's forest marks
    /// something alive. A player who looks twice can tell this one bush is breathing.
    /// </summary>
    [AddComponentMenu("Weizza/Boss/Flower Disguise")]
    [RequireComponent(typeof(MiniBossBrain))]
    public sealed class FlowerDisguise : MonoBehaviour
    {
        [BoxGroup("Plants", centerLabel: true)]
        [InfoBox("Bush drawings from the Forest Silhouette pack (the fern clusters). The Fill button below loads them.")]
        [SerializeField] private Sprite[] bushes = Array.Empty<Sprite>();

        [BoxGroup("Plants")]
        [Tooltip("Weizza/SpriteSway material, so the bushes sway in the wind like the rest of the forest.")]
        [SerializeField] private Material swayMaterial;

        [BoxGroup("Plants")]
        [PropertyRange(3, 16)]
        [SerializeField] private int plantCount = 9;

        [BoxGroup("Plants")]
        [Tooltip("Metres the clump spreads across.")]
        [SerializeField] private float clumpWidth = 7f;

        [BoxGroup("Plants")]
        [Tooltip("Smallest and largest plant, as a scale of the art.")]
        [SerializeField] private Vector2 plantScale = new(0.5f, 0.85f);

        [BoxGroup("Light", centerLabel: true)]
        [SerializeField] private Color glowColour = new(1f, 0.78f, 0.55f);

        [BoxGroup("Light")]
        [PropertyRange(0, 50)]
        [SerializeField] private int sporeCount = 26;

        [BoxGroup("Light")]
        [SerializeField] private Color sporeColour = new(1f, 0.9f, 0.65f);

        [BoxGroup("Reveal", centerLabel: true)]
        [PropertyRange(0.3f, 2f)]
        [SerializeField] private float burstSeconds = 0.9f;

        private sealed class Plant
        {
            public SpriteRenderer sr;
            public float scale;
            public Vector2 home, fling;
            public float spin;
        }

        private sealed class Spore
        {
            public Transform t;
            public SpriteRenderer sr;
            public Vector2 home, burstVelocity;
            public float age, life, speed, drift, phase;
        }

        private MiniBossBrain _brain;
        private SpriteRenderer _body;
        private Transform _clump;
        private readonly List<Plant> _plants = new();
        private readonly List<Spore> _spores = new();
        private readonly List<SpriteRenderer> _glows = new();
        private bool _hidden;
        private float _burstAt = -1f;
        private System.Random _rng;

        private void Awake()
        {
            _brain = GetComponent<MiniBossBrain>();
            var feel = GetComponentInChildren<SpriteFeel>();
            _body = feel != null ? feel.GetComponent<SpriteRenderer>() : null;
        }

        private void OnEnable()
        {
            Build();
            Hide(true);
        }

        private void OnDisable()
        {
            Clear();
            if (_body != null) _body.enabled = true;
        }

        private void Update()
        {
            var dormant = _brain.State == MiniBossBrain.Phase.Dormant;
            if (dormant && !_hidden) { Build(); Hide(true); }
            if (!dormant && _hidden) Reveal();
            if (_clump == null) return;

            if (_burstAt < 0f) TickAlive(Time.time);
            else TickBurst(Time.time);
        }

        private void TickAlive(float t)
        {
            var dt = Time.deltaTime;
            for (var i = 0; i < _glows.Count; i++)
            {
                // The light under the clump breathes slowly, each pool on its own beat.
                var breath = 0.5f + 0.5f * Mathf.Sin(t * 0.9f + i * 1.7f);
                _glows[i].color = WithAlpha(glowColour, 0.16f + breath * 0.14f);
            }

            foreach (var s in _spores)
            {
                s.age += dt;
                if (s.age > s.life) RespawnSpore(s);
                var k = s.age / s.life;
                s.t.localPosition = s.home + new Vector2(Mathf.Sin(s.age * s.drift + s.phase) * 0.3f, s.age * s.speed);
                var fade = Mathf.Clamp01(k * 4f) * Mathf.Clamp01((1f - k) * 3f);
                var twinkle = 0.65f + 0.35f * Mathf.Sin(t * 6f + s.phase * 5f);
                s.sr.color = WithAlpha(sporeColour, fade * twinkle * 0.9f);
            }
        }

        private void TickBurst(float t)
        {
            var dt = Time.deltaTime;
            var k = (t - _burstAt) / burstSeconds;

            foreach (var p in _plants)
            {
                // Torn up and thrown aside, spinning, shrinking as it goes.
                p.fling.y -= 14f * dt;
                p.sr.transform.localPosition += (Vector3)(p.fling * dt);
                p.sr.transform.Rotate(0f, 0f, p.spin * dt);
                p.sr.transform.localScale = Vector3.one * p.scale * Mathf.Lerp(1f, 0.6f, k);
                p.sr.color = WithAlpha(Color.white, Mathf.Clamp01(1.2f - k * 1.2f));
            }

            foreach (var g in _glows)
            {
                g.transform.localScale *= 1f + dt * 3f;
                g.color = WithAlpha(glowColour, 0.45f * (1f - k));
            }

            foreach (var s in _spores)
            {
                s.burstVelocity *= 1f - dt * 2.2f;
                s.t.localPosition += (Vector3)(s.burstVelocity * dt);
                s.sr.color = WithAlpha(sporeColour, Mathf.Clamp01(1f - k) * 0.95f);
            }

            if (k >= 1f) Clear();
        }

        private void Hide(bool hide)
        {
            _hidden = hide;
            if (_body != null) _body.enabled = !hide;
        }

        private void Reveal()
        {
            Hide(false);
            _burstAt = Time.time;
            foreach (var p in _plants)
            {
                var side = p.home.x >= 0f ? 1f : -1f;
                p.fling = new Vector2(side * Range(3f, 7f), Range(5f, 9f));
                p.spin = side * -Range(120f, 360f);
            }
            foreach (var s in _spores)
            {
                var away = (Vector2)s.t.localPosition - new Vector2(0f, 1f);
                s.burstVelocity = away.normalized * Range(3f, 8f) + Vector2.up * Range(0.5f, 2f);
            }
            CameraFeel.Instance?.Shake(0.35f, Vector2.up);
            GetComponentInChildren<SpriteFeel>()?.Land(10f);
        }

        private void Clear()
        {
            if (_clump != null) Destroy(_clump.gameObject);
            _clump = null;
            _plants.Clear();
            _spores.Clear();
            _glows.Clear();
        }

        private void Build()
        {
            if (_clump != null) return;
            _burstAt = -1f;
            _clump = new GameObject("Flower Disguise").transform;
            _clump.SetParent(transform, false);
            _rng = new System.Random(GetInstanceID());
            var order = _body != null ? _body.sortingOrder : 0;
            var layer = _body != null ? _body.sortingLayerName : "Default";

            // Pools of light first, so every plant stands in front of them.
            for (var i = 0; i < 3; i++)
            {
                var glow = Make("Glow", _clump, FloraSprites.Glow(), layer, order - 1);
                glow.transform.localPosition = new Vector3(Lerp(-0.3f, 0.3f, i / 2f) * clumpWidth, Range(0.8f, 1.4f), 0.02f);
                glow.transform.localScale = new Vector3(clumpWidth * 0.55f, Range(2.6f, 3.4f), 1f);
                _glows.Add(glow);
            }

            for (var i = 0; bushes.Length > 0 && i < plantCount; i++)
            {
                var sprite = bushes[_rng.Next(bushes.Length)];
                if (sprite == null) continue;
                // Taller plants toward the middle, so the clump has a crown like a real bush.
                var spread = (i + Range(0.2f, 0.8f)) / plantCount - 0.5f;
                var middle = 1f - Mathf.Abs(spread) * 1.4f;
                var p = new Plant
                {
                    scale = Mathf.Lerp(plantScale.x, plantScale.y, Mathf.Clamp01(middle + Range(-0.2f, 0.2f))),
                    sr = Make("Bush", _clump, sprite, layer, order + _rng.Next(3)),
                };
                p.home = new Vector2(spread * clumpWidth, GroundOffset(sprite) * p.scale);
                p.sr.transform.localPosition = p.home;
                p.sr.transform.localScale = Vector3.one * p.scale;
                p.sr.flipX = _rng.NextDouble() < 0.5;
                // Sways in the same wind as the rest of the forest, so it hides in plain sight.
                if (swayMaterial != null)
                {
                    p.sr.sharedMaterial = swayMaterial;
                    p.sr.gameObject.AddComponent<SpriteSway>().Configure(false, 0.14f);
                }
                _plants.Add(p);
            }

            for (var i = 0; i < sporeCount; i++)
            {
                var s = new Spore { sr = Make("Spore", _clump, FloraSprites.Glow(), layer, order + 5) };
                s.t = s.sr.transform;
                RespawnSpore(s);
                s.age = Range(0f, s.life);
                _spores.Add(s);
            }
        }

        // The plant frames carry empty space under the drawing, so standing a plant on the
        // ground means lifting it by how far its lowest painted vertex sits below the pivot.
        private static float GroundOffset(Sprite sprite)
        {
            var lowest = float.MaxValue;
            foreach (var v in sprite.vertices) lowest = Mathf.Min(lowest, v.y);
            return lowest == float.MaxValue ? 0f : -lowest;
        }

        private void RespawnSpore(Spore s)
        {
            s.home = new Vector2(Range(-0.5f, 0.5f) * clumpWidth, Range(0.3f, 2.6f));
            s.age = 0f;
            s.life = Range(2.5f, 5f);
            s.speed = Range(0.15f, 0.45f);
            s.drift = Range(0.8f, 1.8f);
            s.phase = Range(0f, 6.28f);
            s.t.localScale = Vector3.one * Range(0.08f, 0.18f);
        }

        private static SpriteRenderer Make(string name, Transform parent, Sprite sprite, string layer, int order)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.sortingLayerName = layer;
            sr.sortingOrder = order;
            return sr;
        }

        private float Range(float a, float b) => a + (float)(_rng ?? new System.Random()).NextDouble() * (b - a);
        private static float Lerp(float a, float b, float t) => a + (b - a) * t;
        private static Color WithAlpha(Color c, float a) => new(c.r, c.g, c.b, a);

#if UNITY_EDITOR
        private const string PackFolder = "Assets/Mock_Asset/Forest_Silhouette_01/Lv4 nearBG/";
        private const string SwayMaterialPath = "Assets/GameData/Atmosphere/SpriteSway.mat";

        [BoxGroup("Plants")]
        [Button("Fill from the Forest Silhouette ferns", ButtonSizes.Large), GUIColor(0.55f, 0.85f, 0.55f)]
        public void LoadBushes()
        {
            var list = new List<Sprite>();
            for (var i = 1; i <= 7; i++)
            {
                var sprite = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>(PackFolder + "L4_fernCluster" + i + ".png");
                if (sprite != null) list.Add(sprite);
            }
            bushes = list.ToArray();
            swayMaterial = UnityEditor.AssetDatabase.LoadAssetAtPath<Material>(SwayMaterialPath);
            UnityEditor.EditorUtility.SetDirty(this);
        }
#endif
    }

    /// <summary>A gaussian glow: no edge at all, so stacked pools read as light, not as discs.</summary>
    internal static class FloraSprites
    {
        private static Sprite _glow;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => _glow = null;

        public static Sprite Glow()
        {
            if (_glow != null) return _glow;
            const int size = 64;
            var pixels = new Color[size * size];
            for (var y = 0; y < size; y++)
            for (var x = 0; x < size; x++)
            {
                var dx = ((x + 0.5f) / size - 0.5f) * 2f;
                var dy = ((y + 0.5f) / size - 0.5f) * 2f;
                pixels[y * size + x] = new Color(1f, 1f, 1f, Mathf.Exp(-(dx * dx + dy * dy) * 4f));
            }
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "FloraGlow" };
            texture.SetPixels(pixels);
            texture.Apply();
            _glow = Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
            _glow.name = "FloraGlow";
            return _glow;
        }
    }
}
