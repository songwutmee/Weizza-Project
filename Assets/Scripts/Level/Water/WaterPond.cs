using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;

namespace Weizza
{
    /// <summary>
    /// A shallow pond lying on the ground: a flat sheet the pitched camera sees from a
    /// little above, with a live surface the player wades through.
    ///
    /// The surface is a row of springs, one per column of the mesh, the classic 2D
    /// water from Michael Hoffman's Tuts+ article and the same shape At Fate's End's
    /// ocean wave controller has (two buffers, damping, a brush to disturb it). A step
    /// or a landing pushes the springs under the feet and the wave runs out to the banks.
    ///
    /// The near strip of the pond draws a second time over the player (Overlay), so
    /// wading feet sink into the water. Everything this makes is rebuilt on enable and
    /// never saved, so opening the chunk never rewrites the scene file.
    ///
    /// Put the transform at the pond's centre: x along the level, y the surface height,
    /// z the middle of its depth. Reflections come from WaterReflection beside it.
    /// </summary>
    [ExecuteAlways]
    [AddComponentMenu("Weizza/Level/Water Pond")]
    public sealed class WaterPond : MonoBehaviour
    {
        [BoxGroup("Shape", centerLabel: true)]
        [MinValue(0.3f), Tooltip("Half the pond's length along the level, metres.")]
        [SerializeField] private float halfWidth = 2f;

        [BoxGroup("Shape")]
        [MinValue(0.2f), Tooltip("Half its depth into the scene, metres. The camera is pitched only 16 degrees, so depth reads about a quarter as tall on screen.")]
        [SerializeField] private float halfDepth = 1.3f;

        [BoxGroup("Shape")]
        [PropertyRange(0f, 0.5f), Tooltip("Corner rounding, metres.")]
        [SerializeField] private float cornerRadius = 0.7f;

        [BoxGroup("Shape")]
        [PropertyRange(0f, 0.4f), Tooltip("How much the painted shore wanders in and out, metres.")]
        [SerializeField] private float shoreWobble = 0.14f;

        [BoxGroup("Shape")]
        [PropertyRange(0.03f, 0.3f), Tooltip("Width of one surface column. Smaller is smoother waves and more vertices.")]
        [SerializeField] private float columnSize = 0.07f;

        [BoxGroup("Shape")]
        [Tooltip("World z of the lane the player walks on. The overlay covers the pond from its near edge to just past this.")]
        [SerializeField] private float laneZ = 0f;

        [BoxGroup("Look", centerLabel: true)]
        [Required] [SerializeField] private Material surfaceMaterial;

        [BoxGroup("Look")]
        [Tooltip("Sorting order of the pond. Above the ground strips (-5) and the ground mesh (-4), under the player (0).")]
        [SerializeField] private int sortingOrder = -3;

        [BoxGroup("Look")]
        [Tooltip("Sorting order of the near strip drawn again over the player's feet.")]
        [SerializeField] private int overlayOrder = 1;

        [BoxGroup("Look")]
        [PropertyRange(0f, 1f), Tooltip("How much of the water shows over the wading feet.")]
        [SerializeField] private float overlayAlpha = 0.55f;

        [BoxGroup("Look")]
        [PropertyRange(0.02f, 0.3f), Tooltip("Displacement in metres that lights a crest fully.")]
        [SerializeField] private float crestHeight = 0.06f;

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
        [PropertyRange(0f, 0.03f), Tooltip("A slow idle swell so the pond is never glass still. Metres.")]
        [SerializeField] private float idleSwell = 0.008f;

        [BoxGroup("Splash", centerLabel: true)]
        [SerializeField] private Material dropMaterial;

        [BoxGroup("Splash")]
        [SerializeField] private Material ringMaterial;

        [BoxGroup("Splash")]
        [SerializeField] private Color dropColour = new(0.9f, 0.95f, 0.85f, 0.9f);

        [BoxGroup("Splash")]
        [Tooltip("Metres of wading between two footstep splashes.")]
        [SerializeField] private float stepEvery = 0.5f;

        [BoxGroup("Splash")]
        [SerializeField] private string stepSfx = "water.step";

        [BoxGroup("Splash")]
        [SerializeField] private string splashSfx = "water.splash";

        public float Level => transform.position.y;
        public float HalfWidth => halfWidth;
        public int SortingLayerId => _sortingLayerId;
        public int SortingOrder => sortingOrder;

        private static readonly List<WaterPond> Ponds = new();
        public static IReadOnlyList<WaterPond> All => Ponds;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => Ponds.Clear();

        private static readonly int RectId = Shader.PropertyToID("_WaterRect");
        private static readonly int LevelId = Shader.PropertyToID("_WaterLevel");
        private static readonly int OverlayId = Shader.PropertyToID("_Overlay");
        private static readonly int OverlayAlphaId = Shader.PropertyToID("_OverlayAlpha");

        private const float StepRate = 1f / 60f;

        private Sheet _surface, _overlay;
        private ParticleSystem _drops, _rings;
        private MaterialPropertyBlock _block;
        private float[] _height, _speed, _left, _right;
        private float _stepClock;
        private bool _awake;
        private int _sortingLayerId;
        private float _seed;

        // Wading
        private bool _wasGrounded = true;
        private float _lastVy, _lastX, _walked;

        private void OnEnable()
        {
            Ponds.Add(this);
            _block ??= new MaterialPropertyBlock();
            _sortingLayerId = SortingLayer.NameToID("Default");
            _seed = Mathf.Repeat(transform.position.x * 0.37f, 50f);
            Build();
        }

        private void OnDisable()
        {
            Ponds.Remove(this);
            _surface?.Dispose();
            _overlay?.Dispose();
            _surface = _overlay = null;
            Kill(_drops);
            Kill(_rings);
            _drops = _rings = null;
        }

        private void OnValidate()
        {
            if (!isActiveAndEnabled) return;
            // Rebuilding inside OnValidate is not allowed to create objects; do it next tick.
#if UNITY_EDITOR
            UnityEditor.EditorApplication.delayCall += () =>
            {
                if (this == null || !isActiveAndEnabled) return;
                OnDisable();
                OnEnable();
            };
#endif
        }

        private void Build()
        {
            var columns = Mathf.Max(4, Mathf.CeilToInt(Width / columnSize) + 1);
            _height = new float[columns];
            _speed = new float[columns];
            _left = new float[columns];
            _right = new float[columns];

            if (surfaceMaterial == null) return;
            var near = -halfDepth - Margin;
            var far = halfDepth + Margin;
            var laneLocal = laneZ - transform.position.z;
            _surface = new Sheet(transform, "Water Surface", surfaceMaterial, columns, -HalfSpan, HalfSpan, near, far, 10,
                _sortingLayerId, sortingOrder);
            if (laneLocal + 0.08f > near)
                _overlay = new Sheet(transform, "Water Over Feet", surfaceMaterial, columns, -HalfSpan, HalfSpan, near,
                    Mathf.Min(far, laneLocal + 0.08f), 3, _sortingLayerId, overlayOrder);

            if (Application.isPlaying)
            {
                _drops = MakeParticles("Water Drops", dropMaterial, overlayOrder + 1, 1.6f);
                _rings = MakeParticles("Water Rings", ringMaterial, sortingOrder, 0f);
            }
        }

        private float Width => halfWidth * 2f;
        private float HalfSpan => halfWidth + Margin;
        private float Margin => shoreWobble + 0.05f;

        // ---- Waves ------------------------------------------------------------------------

        /// <summary>Push the surface down (negative power) or up around a world x. Radius in metres.</summary>
        public void Disturb(float worldX, float radius, float power)
        {
            if (_speed == null) return;
            var columns = _speed.Length;
            var x0 = transform.position.x - HalfSpan;
            var step = HalfSpan * 2f / (columns - 1);
            for (var i = 0; i < columns; i++)
            {
                var d = Mathf.Abs(x0 + i * step - worldX) / Mathf.Max(radius, 0.01f);
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
            // Several passes so a wave crosses the pond at a believable speed.
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
            if (!Application.isPlaying || _height == null) return;
            Wade();
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
            if (_surface == null) return;
            var p = transform.position;
            _block.Clear();
            _block.SetVector(RectId, new Vector4(p.x, halfWidth, p.z, halfDepth));
            _block.SetVector(LevelId, new Vector4(p.y, shoreWobble, Mathf.Min(cornerRadius, Mathf.Min(halfWidth, halfDepth * 1.8f) - 0.01f), _seed));
            _block.SetFloat(OverlayId, 0f);
            _block.SetFloat(OverlayAlphaId, overlayAlpha);
            var t = Application.isPlaying ? Time.time : 0f;
            _surface.Shape(_height, idleSwell, crestHeight, t, p.x);
            _surface.Renderer.SetPropertyBlock(_block);
            if (_overlay != null)
            {
                _overlay.Shape(_height, idleSwell, crestHeight, t, p.x);
                _block.SetFloat(OverlayId, 1f);
                _overlay.Renderer.SetPropertyBlock(_block);
            }
        }

        /// <summary>The pond's shape and level, for the reflections drawn over it.</summary>
        public void FillBlock(MaterialPropertyBlock block)
        {
            var p = transform.position;
            block.SetVector(RectId, new Vector4(p.x, halfWidth, p.z, halfDepth));
            block.SetVector(LevelId, new Vector4(p.y, shoreWobble, Mathf.Min(cornerRadius, Mathf.Min(halfWidth, halfDepth * 1.8f) - 0.01f), _seed));
        }

        /// <summary>True when a world x sits over this pond, with a small margin in from the banks.</summary>
        public bool Covers(float worldX, float inset = 0.15f) => Mathf.Abs(worldX - transform.position.x) < halfWidth - inset;

        // ---- Wading -----------------------------------------------------------------------

        private void Wade()
        {
            var player = PlayerLocator.Player;
            if (player == null || player.Motor == null) return;
            var pos = player.transform.position;
            var motor = player.Motor;
            var grounded = motor.Grounded;
            var over = Covers(pos.x) && pos.y < Level + 0.4f && pos.y > Level - 1.5f;

            if (over)
            {
                if (!_wasGrounded && grounded && _lastVy < -2f)
                {
                    Splash(new Vector3(pos.x, Level, laneZ), Mathf.InverseLerp(2f, 14f, -_lastVy));
                    _walked = 0f;
                }
                else if (grounded)
                {
                    var moved = Mathf.Abs(pos.x - _lastX);
                    // A wake: the legs keep pushing the water aside while walking.
                    if (moved > 0.0005f) Disturb(pos.x, 0.35f, -Mathf.Clamp(moved * 0.06f, 0f, 0.004f));
                    _walked += moved;
                    if (_walked >= stepEvery)
                    {
                        _walked = 0f;
                        Step(new Vector3(pos.x, Level, laneZ), Mathf.Sign(motor.Velocity.x));
                    }
                }
            }
            else _walked = stepEvery * 0.6f;

            _wasGrounded = grounded;
            _lastVy = motor.Velocity.y;
            _lastX = pos.x;
        }

        /// <summary>A landing or anything dropped in. Strength 0 to 1.</summary>
        public void Splash(Vector3 at, float strength)
        {
            strength = Mathf.Clamp01(strength);
            Disturb(at.x, 0.45f + 0.4f * strength, -(0.05f + 0.1f * strength));
            EmitDrops(at, 10 + Mathf.RoundToInt(22 * strength), 2.2f + 2.8f * strength, 0f);
            EmitRing(at, 0.9f + 0.9f * strength, 0.9f);
            EmitRing(at, 0.5f + 0.5f * strength, 0.6f);
            AudioDirector.Instance?.PlayAt(splashSfx, at, 0.6f + 0.4f * strength);
        }

        private void Step(Vector3 at, float direction)
        {
            Disturb(at.x, 0.3f, -0.03f);
            EmitDrops(at, 4, 1.4f, direction);
            EmitRing(at, 0.55f, 0.7f);
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
                p.startSize = Random.Range(0.035f, 0.09f);
                p.startLifetime = Random.Range(0.35f, 0.65f);
                p.startColor = dropColour;
                _drops.Emit(p, 1);
            }
        }

        private void EmitRing(Vector3 at, float size, float life)
        {
            if (_rings == null) return;
            var p = new ParticleSystem.EmitParams
            {
                applyShapeToPosition = false,
                position = at + new Vector3(0f, 0.01f, 0.02f),
                velocity = Vector3.zero,
                // Flattened: a circle lying on the water reads as a wide ellipse at this pitch.
                startSize3D = new Vector3(size, size * 0.3f, 1f),
                startLifetime = life,
                startColor = new Color(dropColour.r, dropColour.g, dropColour.b, 0.75f),
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
                ? new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 0.25f, 1f, 1f))   // rings widen
                : new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0.4f));      // drops shrink
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = material;
            r.sortingLayerID = _sortingLayerId;
            r.sortingOrder = order;
            r.renderMode = ParticleSystemRenderMode.Billboard;
            ps.Play();
            return ps;
        }

        private static void Kill(Component c)
        {
            if (c == null) return;
            if (Application.isPlaying) Destroy(c.gameObject);
            else DestroyImmediate(c.gameObject);
        }

        /// <summary>A grid lying flat at the pond's level whose columns ride the spring row.</summary>
        private sealed class Sheet
        {
            public readonly MeshRenderer Renderer;
            private readonly GameObject _go;
            private readonly Mesh _mesh;
            private readonly Vector3[] _vertices;
            private readonly Color32[] _colours;
            private readonly int _columns, _rows;
            private readonly float _x0, _dx;

            public Sheet(Transform parent, string label, Material material, int columns, float x0, float x1,
                float z0, float z1, int rows, int layer, int order)
            {
                _columns = columns;
                _rows = Mathf.Max(2, rows);
                _x0 = x0;
                _dx = (x1 - x0) / (columns - 1);
                _go = new GameObject(label) { hideFlags = HideFlags.DontSave };
                _go.transform.SetParent(parent, false);
                _mesh = new Mesh { name = label, hideFlags = HideFlags.DontSave };
                _mesh.MarkDynamic();
                _vertices = new Vector3[_columns * _rows];
                _colours = new Color32[_vertices.Length];
                var triangles = new int[(_columns - 1) * (_rows - 1) * 6];
                for (int r = 0, k = 0; r < _rows; r++)
                {
                    var z = Mathf.Lerp(z0, z1, r / (float)(_rows - 1));
                    for (var c = 0; c < _columns; c++)
                        _vertices[r * _columns + c] = new Vector3(x0 + c * _dx, 0f, z);
                    if (r == _rows - 1) break;
                    for (var c = 0; c < _columns - 1; c++)
                    {
                        var a = r * _columns + c;
                        triangles[k++] = a; triangles[k++] = a + _columns; triangles[k++] = a + 1;
                        triangles[k++] = a + 1; triangles[k++] = a + _columns; triangles[k++] = a + _columns + 1;
                    }
                }
                _mesh.vertices = _vertices;
                _mesh.triangles = triangles;
                _mesh.bounds = new Bounds(new Vector3((x0 + x1) * 0.5f, 0f, (z0 + z1) * 0.5f),
                    new Vector3(x1 - x0, 1f, z1 - z0));
                _go.AddComponent<MeshFilter>().sharedMesh = _mesh;
                Renderer = _go.AddComponent<MeshRenderer>();
                Renderer.sharedMaterial = material;
                Renderer.sortingLayerID = layer;
                Renderer.sortingOrder = order;
                Renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                Renderer.receiveShadows = false;
            }

            public void Shape(float[] heights, float swell, float crest, float time, float worldX)
            {
                var n = Mathf.Min(heights.Length, _columns);
                for (var c = 0; c < n; c++)
                {
                    var x = _x0 + c * _dx + worldX;
                    var idle = (Mathf.Sin(x * 2.1f + time * 1.3f) * 0.65f + Mathf.Sin(x * 5.3f - time * 2.1f) * 0.35f) * swell;
                    var h = heights[c] + idle;
                    var shade = (byte)Mathf.Clamp(Mathf.RoundToInt(127.5f + 127.5f * Mathf.Clamp(h / crest, -1f, 1f)), 0, 255);
                    for (var r = 0; r < _rows; r++)
                    {
                        var i = r * _columns + c;
                        _vertices[i].y = h;
                        _colours[i] = new Color32(shade, 0, 0, 255);
                    }
                }
                _mesh.SetVertices(_vertices);
                _mesh.SetColors(_colours);
            }

            public void Dispose()
            {
                if (Application.isPlaying) { Object.Destroy(_go); Object.Destroy(_mesh); }
                else { Object.DestroyImmediate(_go); Object.DestroyImmediate(_mesh); }
            }
        }
    }
}
