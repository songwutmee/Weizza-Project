using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;

namespace Weizza
{
    /// <summary>
    /// Water standing in a hollow of the ground, seen side on like the rest of the world.
    ///
    /// Put the transform where the surface should be, over a dip dug into a Ground Path.
    /// The pond reads the ground from the transform's x outward until the ground rises
    /// above the level, and fills exactly that: the water meets each bank where the bank
    /// meets the level, and its floor is the real floor. A puddle is the same thing in a
    /// shallow dip; a pool over a metre deep floats the player (see PlayerController).
    ///
    /// The surface is a row of springs, one per column (Michael Hoffman's 2D water on
    /// Tuts+, and the same shape At Fate's End's wave controller has): a step or a landing
    /// pushes the springs under the feet and the wave runs out to the banks.
    ///
    /// Everything this makes is rebuilt on enable and never saved, so opening the chunk
    /// never rewrites the scene file.
    /// </summary>
    [ExecuteAlways]
    [AddComponentMenu("Weizza/Level/Water Pond")]
    public sealed class WaterPond : MonoBehaviour
    {
        [BoxGroup("Shape", centerLabel: true)]
        [InfoBox("The water fills the ground's hollow under this transform's height. Move the transform up or down to fill it more or less.")]
        [MinValue(0.5f), Tooltip("Furthest the water may reach either side of the transform, metres. A safety stop if the ground never rises.")]
        [SerializeField] private float maxReach = 12f;

        [BoxGroup("Shape")]
        [PropertyRange(0.02f, 0.2f), Tooltip("Width of one surface column. Smaller is smoother waves.")]
        [SerializeField] private float columnSize = 0.05f;

        [BoxGroup("Shape")]
        [Tooltip("World z of the lane the player walks on, where splashes are thrown.")]
        [SerializeField] private float laneZ = 0f;

        [BoxGroup("Look", centerLabel: true)]
        [Required] [SerializeField] private Material bodyMaterial;

        [BoxGroup("Look")]
        [Tooltip("Sorting order of the water. Above the player (0) so a wading foot or a swimmer's body sits inside it.")]
        [SerializeField] private int sortingOrder = 1;

        [BoxGroup("Look")]
        [PropertyRange(0.01f, 0.2f), Tooltip("Displacement in metres that lights a crest fully.")]
        [SerializeField] private float crestHeight = 0.05f;

        [BoxGroup("Waves", centerLabel: true)]
        [PropertyRange(0.001f, 0.1f), Tooltip("How hard each column pulls back to flat. Higher is quicker, choppier waves.")]
        [SerializeField] private float tension = 0.03f;

        [BoxGroup("Waves")]
        [PropertyRange(0f, 0.2f), Tooltip("How fast waves die. Higher is a thicker, calmer pond.")]
        [SerializeField] private float dampening = 0.035f;

        [BoxGroup("Waves")]
        [PropertyRange(0f, 0.5f), Tooltip("How much each column passes on to its neighbours. Higher waves travel further and faster.")]
        [SerializeField] private float spread = 0.22f;

        [BoxGroup("Waves")]
        [PropertyRange(0f, 0.03f), Tooltip("A slow idle swell so the water is never glass still. Metres.")]
        [SerializeField] private float idleSwell = 0.006f;

        [BoxGroup("Splash", centerLabel: true)]
        [SerializeField] private Material dropMaterial;

        [BoxGroup("Splash")]
        [SerializeField] private Material ringMaterial;

        [BoxGroup("Splash")]
        [SerializeField] private Color dropColour = new(0.9f, 0.95f, 0.85f, 0.9f);

        [BoxGroup("Splash")]
        [PropertyRange(0.2f, 2f), Tooltip("Size of every splash, drop and ring. A puddle wants about half.")]
        [SerializeField] private float splashScale = 1f;

        [BoxGroup("Splash")]
        [Tooltip("Metres of wading between two footstep splashes.")]
        [SerializeField] private float stepEvery = 0.5f;

        [BoxGroup("Splash")]
        [MinValue(0f), Tooltip("Seconds between drips falling in from the leaves above, on average. 0 is none.")]
        [SerializeField] private float dripEvery;

        [BoxGroup("Splash")]
        [SerializeField] private string stepSfx = "water.step";

        [BoxGroup("Splash")]
        [SerializeField] private string splashSfx = "water.splash";

        [BoxGroup("Splash")]
        [SerializeField] private string swimSfx = "water.swim";

        [FoldoutGroup("Live"), ShowInInspector, ReadOnly] public float Left { get; private set; }
        [FoldoutGroup("Live"), ShowInInspector, ReadOnly] public float Right { get; private set; }
        [FoldoutGroup("Live"), ShowInInspector, ReadOnly] public float Deepest { get; private set; }

        public float Level => transform.position.y;
        public int SortingLayerId => _sortingLayerId;
        public int SortingOrder => sortingOrder;
        public bool Built => _floor != null;

        /// <summary>Deep enough to float in rather than wade.</summary>
        public bool Deep => Deepest > 1f;

        private static readonly List<WaterPond> Ponds = new();
        public static IReadOnlyList<WaterPond> All => Ponds;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => Ponds.Clear();

        private static readonly int SpanId = Shader.PropertyToID("_WaterSpan");
        private static readonly int FloorId = Shader.PropertyToID("_WaterFloor");

        /// <summary>How far below the surface the feet must be before the player floats instead of wading.</summary>
        public const float SwimFeetDepth = 0.55f;

        private const float StepRate = 1f / 60f;
        private const int MaskSamples = 64;

        private GameObject _bodyObject;
        private Mesh _mesh;
        private MeshRenderer _renderer;
        private Vector3[] _vertices;
        private Vector2[] _heights;
        private Color32[] _colours;
        private float[] _floor;              // ground under each column, world y
        private readonly float[] _mask = new float[MaskSamples];
        private ParticleSystem _drops, _rings;
        private MaterialPropertyBlock _block;
        private float[] _height, _speed, _left, _right;
        private float _stepClock;
        private bool _awake;
        private int _sortingLayerId;

        private bool _wasGrounded = true;
        private float _lastVy, _lastX, _walked, _lastFeetY = 99f;
        private float _nextDrip, _swum, _bobClock;

        /// <summary>True when feet here are deep enough in a pool to float. The level is the surface.</summary>
        public static bool SwimAt(Vector3 feet, out float level)
        {
            foreach (var p in Ponds)
            {
                if (p == null || !p.Built || !p.Deep || feet.x < p.Left + 0.3f || feet.x > p.Right - 0.3f) continue;
                if (feet.y > p.Level - SwimFeetDepth || feet.y < p.FloorAt(feet.x) - 0.5f) continue;
                if (p.Level - p.FloorAt(feet.x) < 1f) continue;
                level = p.Level;
                return true;
            }
            level = 0f;
            return false;
        }

        private void OnEnable()
        {
            Ponds.Add(this);
            _block ??= new MaterialPropertyBlock();
            _sortingLayerId = SortingLayer.NameToID("Default");
            Build();
        }

        private void OnDisable()
        {
            Ponds.Remove(this);
            Clear();
        }

        private void Clear()
        {
            Kill(_bodyObject);
            if (_mesh != null) { if (Application.isPlaying) Destroy(_mesh); else DestroyImmediate(_mesh); }
            Kill(_drops != null ? _drops.gameObject : null);
            Kill(_rings != null ? _rings.gameObject : null);
            _bodyObject = null;
            _mesh = null;
            _drops = _rings = null;
            _floor = null;
        }

        private void OnValidate()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.delayCall += () =>
            {
                if (this == null || !isActiveAndEnabled) return;
                Clear();
                Build();
            };
#endif
        }

        [Button("Refit to the ground"), BoxGroup("Shape")]
        private void Refit()
        {
            Clear();
            Build();
        }

        // ---- Shape ------------------------------------------------------------------------

        private static bool Ground(float x, out float y) => GroundPath.TryTerrainHeight(x, out y);

        /// <summary>Ground height under the water at a world x.</summary>
        public float FloorAt(float x)
        {
            if (_floor == null) return Level;
            var u = Mathf.Clamp01((x - Left) / Mathf.Max(Right - Left, 1e-3f)) * (_floor.Length - 1);
            var i = Mathf.Min((int)u, _floor.Length - 2);
            return Mathf.Lerp(_floor[i], _floor[i + 1], u - i);
        }

        // Walks out from the middle until the ground rises above the level, then narrows the
        // crossing down to a millimetre, so the water ends exactly where the bank does.
        private float FindBank(float from, float direction)
        {
            var level = Level;
            var x = from;
            const float probe = 0.05f;
            for (var travelled = 0f; travelled < maxReach; travelled += probe)
            {
                var next = x + direction * probe;
                if (!Ground(next, out var g)) return x;
                if (g >= level)
                {
                    var wet = x;
                    var dry = next;
                    for (var i = 0; i < 12; i++)
                    {
                        var mid = (wet + dry) * 0.5f;
                        if (Ground(mid, out var gm) && gm < level) wet = mid; else dry = mid;
                    }
                    return wet;
                }
                x = next;
            }
            return x;
        }

        private void Build()
        {
            if (bodyMaterial == null) return;
            // Anything a script reload orphaned: a second body would draw a second water.
            for (var i = transform.childCount - 1; i >= 0; i--)
            {
                var child = transform.GetChild(i).gameObject;
                if (child.name is "Water Body" or "Water Drops" or "Water Rings") Kill(child);
            }
            var centre = transform.position.x;
            // In play the chunk's ground may wake after the water; LateUpdate tries again.
            if (!Ground(centre, out var bottom) || bottom >= Level) return;

            Left = FindBank(centre, -1f);
            Right = FindBank(centre, 1f);
            var columns = Mathf.Max(4, Mathf.CeilToInt((Right - Left) / columnSize) + 1);
            _floor = new float[columns];
            Deepest = 0f;
            for (var c = 0; c < columns; c++)
            {
                var x = Mathf.Lerp(Left, Right, c / (float)(columns - 1));
                _floor[c] = Ground(x, out var g) ? Mathf.Min(g, Level) : Level;
                Deepest = Mathf.Max(Deepest, Level - _floor[c]);
            }
            // The very ends meet the bank at the level, so the water closes to nothing there.
            _floor[0] = _floor[columns - 1] = Level;
            for (var i = 0; i < MaskSamples; i++) _mask[i] = FloorAt(Mathf.Lerp(Left, Right, i / (float)(MaskSamples - 1)));

            _height = new float[columns];
            _speed = new float[columns];
            _left = new float[columns];
            _right = new float[columns];

            MakeBody(columns);
            if (Application.isPlaying)
            {
                _drops = MakeParticles("Water Drops", dropMaterial, sortingOrder + 1, 1.6f);
                _rings = MakeParticles("Water Rings", ringMaterial, sortingOrder, 0f);
            }
        }

        // Two vertices per column, the surface and the floor under it, all in world space
        // on an identity child so the body stays put whatever the pond's own transform does.
        private void MakeBody(int columns)
        {
            _bodyObject = new GameObject("Water Body") { hideFlags = HideFlags.DontSave };
            _bodyObject.transform.SetParent(transform, false);
            _bodyObject.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            _bodyObject.transform.localScale = new Vector3(1f / Mathf.Max(transform.lossyScale.x, 1e-4f),
                1f / Mathf.Max(transform.lossyScale.y, 1e-4f), 1f / Mathf.Max(transform.lossyScale.z, 1e-4f));
            _bodyObject.transform.position = Vector3.zero;

            _mesh = new Mesh { name = "Water Body", hideFlags = HideFlags.DontSave };
            _mesh.MarkDynamic();
            _vertices = new Vector3[columns * 2];
            _heights = new Vector2[columns * 2];
            _colours = new Color32[columns * 2];
            var triangles = new int[(columns - 1) * 6];
            var z = transform.position.z;
            for (var c = 0; c < columns; c++)
            {
                var x = Mathf.Lerp(Left, Right, c / (float)(columns - 1));
                _vertices[c * 2] = new Vector3(x, Level, z);
                _vertices[c * 2 + 1] = new Vector3(x, _floor[c], z);
                if (c == columns - 1) break;
                var k = c * 6;
                var a = c * 2;
                triangles[k] = a; triangles[k + 1] = a + 2; triangles[k + 2] = a + 1;
                triangles[k + 3] = a + 1; triangles[k + 4] = a + 2; triangles[k + 5] = a + 3;
            }
            _mesh.vertices = _vertices;
            _mesh.triangles = triangles;
            var minY = Level - Deepest - 0.5f;
            _mesh.bounds = new Bounds(new Vector3((Left + Right) * 0.5f, (Level + minY) * 0.5f, z),
                new Vector3(Right - Left + 0.5f, Level - minY + 1f, 1f));
            _bodyObject.AddComponent<MeshFilter>().sharedMesh = _mesh;
            _renderer = _bodyObject.AddComponent<MeshRenderer>();
            _renderer.sharedMaterial = bodyMaterial;
            _renderer.sortingLayerID = _sortingLayerId;
            _renderer.sortingOrder = sortingOrder;
            _renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _renderer.receiveShadows = false;
            Shape(0f);
        }

        private void Shape(float time)
        {
            var columns = _floor.Length;
            for (var c = 0; c < columns; c++)
            {
                var x = _vertices[c * 2].x;
                // The very ends stay pinned to the banks; waves die into the shore.
                var pin = Mathf.Min(c, columns - 1 - c) < 2 ? 0f : 1f;
                var idle = (Mathf.Sin(x * 2.1f + time * 1.3f) * 0.65f + Mathf.Sin(x * 5.3f - time * 2.1f) * 0.35f) * idleSwell;
                var h = (_height[c] + idle) * pin;
                var top = Level + h;
                _vertices[c * 2].y = top;
                var shade = (byte)Mathf.Clamp(Mathf.RoundToInt(127.5f + 127.5f * Mathf.Clamp(h / crestHeight, -1f, 1f)), 0, 255);
                _heights[c * 2] = _heights[c * 2 + 1] = new Vector2(top, _floor[c]);
                _colours[c * 2] = _colours[c * 2 + 1] = new Color32(shade, 0, 0, 255);
            }
            _mesh.SetVertices(_vertices);
            _mesh.SetUVs(0, _heights);
            _mesh.SetColors(_colours);
        }

        /// <summary>The water's span, level and floor, for the reflections drawn inside it.</summary>
        public void FillBlock(MaterialPropertyBlock block)
        {
            block.SetVector(SpanId, new Vector4(Left, Right, Level, MaskSamples));
            block.SetFloatArray(FloorId, _mask);
        }

        // ---- Waves ------------------------------------------------------------------------

        /// <summary>Push the surface down (negative power) or up around a world x. Radius in metres.</summary>
        public void Disturb(float worldX, float radius, float power)
        {
            if (_speed == null) return;
            var columns = _speed.Length;
            for (var i = 0; i < columns; i++)
            {
                var x = Mathf.Lerp(Left, Right, i / (float)(columns - 1));
                var d = Mathf.Abs(x - worldX) / Mathf.Max(radius, 0.01f);
                if (d >= 1f) continue;
                // A bell, not a box: the middle pushed hardest, so the wave leaves round.
                _speed[i] += power * (0.5f + 0.5f * Mathf.Cos(d * Mathf.PI));
            }
            _awake = true;
        }

        private void Simulate()
        {
            var n = _height.Length;
            for (var i = 0; i < n; i++)
            {
                _speed[i] += -tension * _height[i] - dampening * _speed[i];
                _height[i] += _speed[i];
            }
            // Several passes so a wave crosses the water at a believable speed.
            for (var pass = 0; pass < 6; pass++)
            {
                for (var i = 0; i < n; i++)
                {
                    if (i > 0) { _left[i] = spread * (_height[i] - _height[i - 1]); _speed[i - 1] += _left[i]; }
                    if (i < n - 1) { _right[i] = spread * (_height[i] - _height[i + 1]); _speed[i + 1] += _right[i]; }
                }
                for (var i = 0; i < n; i++)
                {
                    if (i > 0) _height[i - 1] += _left[i];
                    if (i < n - 1) _height[i + 1] += _right[i];
                }
            }

            var energy = 0f;
            for (var i = 0; i < n; i++) energy += Mathf.Abs(_height[i]) + Mathf.Abs(_speed[i]);
            if (energy < 1e-4f * n)
            {
                System.Array.Clear(_height, 0, n);
                System.Array.Clear(_speed, 0, n);
                _awake = false;
            }
        }

        // ---- Frame ------------------------------------------------------------------------

        private void Update()
        {
            if (!Application.isPlaying) return;
            if (_floor == null)
            {
                Build();
                if (_floor == null) return;
            }
            Wade();
            Drip();
            if (!_awake) return;
            _stepClock += Mathf.Min(Time.deltaTime, 0.1f);
            while (_stepClock >= StepRate)
            {
                _stepClock -= StepRate;
                Simulate();
                if (!_awake) break;
            }
        }

        private void LateUpdate()
        {
            // The ground may come up after the water, on a scene load or back from Play.
            if (_floor == null) Build();
            if (_floor == null || _renderer == null) return;
            // Off screen, nothing to shape: a dozen puddles along a level cost nothing until seen.
            if (Application.isPlaying && !_renderer.isVisible) return;
            Shape(Application.isPlaying ? Time.time : 0f);
            _block.Clear();
            FillBlock(_block);
            _renderer.SetPropertyBlock(_block);
        }

        // ---- Wading and swimming ------------------------------------------------------------

        private void Wade()
        {
            var player = PlayerLocator.Player;
            if (player == null || player.Motor == null) return;
            var pos = player.transform.position;
            var motor = player.Motor;
            var grounded = motor.Grounded;
            var over = pos.x > Left + 0.05f && pos.x < Right - 0.05f && pos.y < Level + 0.4f && pos.y > FloorAt(pos.x) - 0.5f;
            var inWater = over && pos.y < Level - 0.02f;
            var swimming = over && Level - FloorAt(pos.x) >= 1f && pos.y < Level - SwimFeetDepth + 0.05f;
            var at = new Vector3(pos.x, Level, laneZ);

            if (over && _lastFeetY > Level - 0.05f && pos.y <= Level - 0.05f && _lastVy < -1.5f)
            {
                // Dropping in, however the player got there.
                Splash(at, Mathf.InverseLerp(1.5f, 12f, -_lastVy) * Mathf.Clamp01(Deepest / 0.8f + 0.25f));
                _walked = 0f;
                _swum = 0f;
            }
            else if (swimming)
            {
                // Strokes push rings out as the swimmer moves; a still swimmer bobs and
                // sends out a slow ring now and then.
                var moved = Mathf.Abs(pos.x - _lastX);
                if (moved > 0.0005f) Disturb(pos.x, 0.5f, -Mathf.Clamp(moved * 0.08f, 0f, 0.006f));
                _swum += moved;
                _bobClock += Time.deltaTime;
                if (_swum >= 0.8f)
                {
                    _swum = 0f;
                    _bobClock = 0f;
                    Disturb(pos.x, 0.45f, -0.04f);
                    EmitDrops(at, 3, 1.2f * splashScale, Mathf.Sign(motor.Velocity.x));
                    EmitRing(at, 0.8f * splashScale, 0.9f);
                    AudioDirector.Instance?.PlayAt(swimSfx, at, 0.5f);
                }
                else if (_bobClock >= 1.4f)
                {
                    _bobClock = 0f;
                    EmitRing(at, 0.7f * splashScale, 1.2f);
                    Disturb(pos.x, 0.4f, -0.015f);
                }
            }
            else if (inWater && grounded)
            {
                var moved = Mathf.Abs(pos.x - _lastX);
                // A wake: the legs keep pushing the water aside while walking.
                if (moved > 0.0005f) Disturb(pos.x, 0.3f, -Mathf.Clamp(moved * 0.05f, 0f, 0.003f) * splashScale);
                _walked += moved;
                if (_walked >= stepEvery)
                {
                    _walked = 0f;
                    Step(at, Mathf.Sign(motor.Velocity.x));
                }
            }
            else if (!inWater) _walked = stepEvery * 0.6f;

            _wasGrounded = grounded;
            _lastVy = motor.Velocity.y;
            _lastX = pos.x;
            _lastFeetY = pos.y;
        }

        // A drop from the leaves now and then, so water in a wet forest is never quite still.
        private void Drip()
        {
            if (dripEvery <= 0f || Time.time < _nextDrip) return;
            var first = _nextDrip <= 0f;
            _nextDrip = Time.time + dripEvery * Random.Range(0.5f, 1.5f);
            if (first || _renderer == null || !_renderer.isVisible) return;
            var x = Mathf.Lerp(Left, Right, Random.Range(0.15f, 0.85f));
            EmitRing(new Vector3(x, Level, laneZ), Random.Range(0.25f, 0.45f) * splashScale, 1f);
            Disturb(x, 0.15f, -0.01f);
        }

        /// <summary>A landing or anything dropped in. Strength 0 to 1.</summary>
        public void Splash(Vector3 at, float strength)
        {
            strength = Mathf.Clamp01(strength);
            var s = splashScale;
            Disturb(at.x, (0.45f + 0.4f * strength) * s, -(0.05f + 0.1f * strength) * s);
            EmitDrops(at, Mathf.RoundToInt((10 + 22 * strength) * s), (2.2f + 2.8f * strength) * Mathf.Sqrt(s), 0f);
            EmitRing(at, (0.9f + 0.9f * strength) * s, 0.9f);
            EmitRing(at, (0.5f + 0.5f * strength) * s, 0.6f);
            AudioDirector.Instance?.PlayAt(splashSfx, at, (0.6f + 0.4f * strength) * Mathf.Sqrt(s));
        }

        private void Step(Vector3 at, float direction)
        {
            var s = splashScale;
            Disturb(at.x, 0.3f * s, -0.03f * s);
            // A step throws a little crown forward, the way the foot leaves.
            EmitDrops(at, Mathf.Max(3, Mathf.RoundToInt(5 * s)), 1.4f * Mathf.Sqrt(s), direction);
            EmitRing(at, 0.55f * s, 0.7f);
            AudioDirector.Instance?.PlayAt(stepSfx, at, 0.5f);
        }

        private void EmitDrops(Vector3 at, int count, float speed, float lean)
        {
            if (_drops == null) return;
            var p = new ParticleSystem.EmitParams { applyShapeToPosition = false };
            for (var i = 0; i < count; i++)
            {
                var side = Random.Range(-1f, 1f) + lean * 0.6f;
                p.position = at + new Vector3(Random.Range(-0.12f, 0.12f), 0.03f, -0.05f);
                p.velocity = new Vector3(side * speed * 0.45f, speed * Random.Range(0.55f, 1f), 0f);
                p.startSize = Random.Range(0.035f, 0.09f) * Mathf.Lerp(1f, splashScale, 0.5f);
                p.startLifetime = Random.Range(0.35f, 0.65f);
                p.startColor = dropColour;
                _drops.Emit(p, 1);
            }
        }

        // Side on, a ripple is a short bright arc riding along the surface line, not a ring.
        private void EmitRing(Vector3 at, float size, float life)
        {
            if (_rings == null) return;
            var p = new ParticleSystem.EmitParams
            {
                applyShapeToPosition = false,
                position = at + new Vector3(0f, 0.005f, -0.02f),
                velocity = Vector3.zero,
                startSize3D = new Vector3(size, size * 0.12f, 1f),
                startLifetime = life,
                startColor = new Color(dropColour.r, dropColour.g, dropColour.b, 0.7f),
            };
            _rings.Emit(p, 1);
        }

        private ParticleSystem MakeParticles(string label, Material material, int order, float gravity)
        {
            if (material == null) return null;
            var go = new GameObject(label) { hideFlags = HideFlags.DontSave };
            go.transform.SetParent(transform, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.playOnAwake = false;
            main.loop = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.gravityModifier = gravity;
            main.maxParticles = 256;
            main.startSize3D = gravity <= 0f;
            var emission = ps.emission;
            emission.enabled = false;
            var shape = ps.shape;
            shape.enabled = false;
            var fade = ps.colorOverLifetime;
            fade.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.8f, 0.5f), new GradientAlphaKey(0f, 1f) });
            fade.color = g;
            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = gravity <= 0f
                ? new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 0.25f, 1f, 1f))   // ripples widen
                : new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0.4f));      // drops shrink
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = material;
            r.sortingLayerID = _sortingLayerId;
            r.sortingOrder = order;
            r.renderMode = ParticleSystemRenderMode.Billboard;
            ps.Play();
            return ps;
        }

        private static void Kill(GameObject go)
        {
            if (go == null) return;
            if (Application.isPlaying) Destroy(go);
            else DestroyImmediate(go);
        }
    }
}
