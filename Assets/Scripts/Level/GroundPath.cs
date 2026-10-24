using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;

namespace Weizza
{
    /// <summary>
    /// Ground that follows a line of points: hills, dips, a slope down into a hollow.
    ///
    /// The art is the same ground strip the flat pieces use, sheared rather than
    /// rotated. Every column of the picture stays upright and only slides up or down
    /// to meet the line, so grass and roots keep standing straight on a slope the way
    /// they do on a real hillside, instead of the whole strip tipping over.
    ///
    /// Collision is one plain box per segment, laid along the line, the same as every
    /// other walkable surface here. The motor follows slopes up to its slope limit
    /// (45 degrees), so keep segments shallower than that or they read as walls.
    ///
    /// Drag the points in the Scene view. Points are in this object's local space, so
    /// moving the object moves the whole path.
    /// </summary>
    [ExecuteAlways]
    [AddComponentMenu("Weizza/Level/Ground Path")]
    public sealed class GroundPath : MonoBehaviour
    {
        [BoxGroup("Shape")]
        [InfoBox("Drag the white handles in the Scene view. Keep every segment under 40 degrees: the character's slope limit is 45, and steeper reads as a wall.")]
        [ListDrawerSettings(ShowFoldout = true, DefaultExpandedState = true)]
        [SerializeField] private List<Vector2> points = new() { new Vector2(0f, 0f), new Vector2(4f, 1.5f), new Vector2(7f, 1.5f), new Vector2(16f, 0f) };

        [BoxGroup("Shape")]
        [PropertyRange(0f, 1f), Tooltip("Rounds every bend. 0 keeps sharp corners, 1 turns each bend into a curve as wide as the shorter of its two sides. Hills read as mounds of earth instead of folded paper.")]
        [SerializeField] private float smooth = 0.6f;

        [BoxGroup("Art")]
        [Tooltip("The ground strip drawn along the line. Its top edge sits on the line.")]
        [SerializeField] private Sprite strip;

        [BoxGroup("Art")]
        [Tooltip("The same material the flat ground pieces use, so the slope is graded and lit exactly like them.")]
        [SerializeField] private Material material;

        [BoxGroup("Art")]
        [PropertyRange(0f, 1f), Tooltip("How far the top of the picture rises above the walkable line. The flat pieces' grass stands 0.34 m above their collider, so the same value matches them.")]
        [SerializeField] private float grassAbove = 0.34f;

        [BoxGroup("Art")]
        [PropertyRange(0f, 4f), Tooltip("Metres the picture runs on past each end, fading out, so the path melts into the flat ground instead of ending in a straight cut. Collision stops at the end points either way.")]
        [SerializeField] private float edgeFade = 1.5f;

        [BoxGroup("Art")]
        [Tooltip("Local height the soil under the strip is filled down to. Deep enough that no camera ever sees under it.")]
        [SerializeField] private float fillDownTo = -10f;

        [BoxGroup("Art")]
        [Tooltip("Multiplies the picture. A darker, hazier tint on a path drawn behind makes a far ridge.")]
        [SerializeField] private Color tint = Color.white;

        [BoxGroup("Art")]
        [SerializeField] private string sortingLayer = "Default";

        [BoxGroup("Art")]
        [Tooltip("Draws in front of the flat ground pieces (they use -5), so a hill covers the flat strip's grass line instead of having it run across its foot.")]
        [SerializeField] private int sortingOrder = -4;

        [BoxGroup("Collision")]
        [Tooltip("Off for a ridge that is only scenery: drawn, never stood on.")]
        [SerializeField] private bool walkable = true;

        [BoxGroup("Collision")]
        [PropertyRange(0.3f, 3f), Tooltip("Thickness of each box under the line. Around 1 m stops a fast fall from tunnelling through.")]
        [SerializeField] private float thickness = 1f;

        [BoxGroup("Collision")]
        [Tooltip("Layer for the boxes. Copy it from the flat ground's collider.")]
        [SerializeField] private int groundLayer;

        public IReadOnlyList<Vector2> Points => points;

        [ShowInInspector, ReadOnly, BoxGroup("Shape"), LabelText("Steepest segment")]
        private string Steepest
        {
            get
            {
                var worst = 0f;
                for (var i = 1; i < points.Count; i++)
                {
                    var d = points[i] - points[i - 1];
                    if (Mathf.Abs(d.x) < 0.001f) continue;
                    worst = Mathf.Max(worst, Mathf.Abs(Mathf.Atan2(d.y, Mathf.Abs(d.x)) * Mathf.Rad2Deg));
                }
                return worst > 40f ? $"{worst:0} degrees: too steep, the player will slide or stop" : $"{worst:0} degrees";
            }
        }

        private MeshFilter _filter;
        private MeshRenderer _renderer;
        private Mesh _mesh;

        private static readonly List<GroundPath> Live = new();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => Live.Clear();

        /// <summary>Height of the terrain at a world x, ignoring stones and anything else stood on. False where no path covers x.</summary>
        public static bool TryTerrainHeight(float worldX, out float height)
        {
            foreach (var path in Live)
            {
                if (path == null || !path.walkable || path.points.Count < 2) continue;
                var local = worldX - path.transform.position.x;
                if (local < path.points[0].x || local > path.points[^1].x) continue;
                height = path.transform.position.y + path.HeightAt(local);
                return true;
            }
            height = 0f;
            return false;
        }

        private void OnEnable()
        {
            if (!Live.Contains(this)) Live.Add(this);
            Rebuild();
        }

        private void OnDisable() => Live.Remove(this);

        private void OnValidate()
        {
            // Building colliders is not allowed inside OnValidate, so it waits a tick.
#if UNITY_EDITOR
            UnityEditor.EditorApplication.delayCall += () => { if (this != null) Rebuild(); };
#endif
        }

        public void SetPoint(int index, Vector2 value)
        {
            if (index < 0 || index >= points.Count) return;
            points[index] = value;
            Rebuild();
        }

        public void AddPoint()
        {
            var last = points.Count > 0 ? points[^1] : Vector2.zero;
            points.Add(last + new Vector2(4f, 0f));
            Rebuild();
        }

        public void RemoveLastPoint()
        {
            if (points.Count <= 2) return;
            points.RemoveAt(points.Count - 1);
            Rebuild();
        }

        public void InsertPoint(int index, Vector2 value)
        {
            points.Insert(Mathf.Clamp(index, 0, points.Count), value);
            Rebuild();
        }

        public void RemovePoint(int index)
        {
            // Two points is the shortest line there is.
            if (points.Count <= 2 || index < 0 || index >= points.Count) return;
            points.RemoveAt(index);
            Rebuild();
        }

        /// <summary>Fills the art and collision settings in one go, for tools that create paths.</summary>
        public void Setup(Sprite groundStrip, Material groundMaterial, int layer, string layerName, int order)
        {
            strip = groundStrip;
            material = groundMaterial;
            groundLayer = layer;
            sortingLayer = layerName;
            sortingOrder = order;
            Rebuild();
        }

        [Button("Rebuild", ButtonSizes.Large), GUIColor(0.6f, 0.85f, 0.6f)]
        public void Rebuild()
        {
            if (points == null || points.Count < 2) return;
            BuildShape();
            BuildMesh();
            BuildColliders();
        }

        // ---- Picture ------------------------------------------------------------

        private void BuildMesh()
        {
            // Explicit checks, not ??: a missing component comes back as Unity's fake
            // null, which ?? treats as a real object.
            if (_filter == null) _filter = GetComponent<MeshFilter>();
            if (_filter == null) _filter = gameObject.AddComponent<MeshFilter>();
            if (_renderer == null) _renderer = GetComponent<MeshRenderer>();
            if (_renderer == null) _renderer = gameObject.AddComponent<MeshRenderer>();
            if (_mesh == null)
            {
                _mesh = new Mesh { name = "Ground Path" };
                _mesh.hideFlags = HideFlags.DontSave;
            }

            _renderer.sharedMaterial = material;
            _renderer.sortingLayerName = sortingLayer;
            _renderer.sortingOrder = sortingOrder;
            _renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _renderer.receiveShadows = false;

            _mesh.Clear();
            if (strip == null) { _filter.sharedMesh = _mesh; return; }

            // One copy of the strip covers this many metres; the texture clamps, so
            // the strip is cut into whole copies and each copy gets its own quads.
            var tex = strip.texture;
            var rect = strip.textureRect;
            var width = rect.width / strip.pixelsPerUnit;
            var height = rect.height / strip.pixelsPerUnit;
            float u0 = rect.xMin / tex.width, u1 = rect.xMax / tex.width;
            float v0 = rect.yMin / tex.height, v1 = rect.yMax / tex.height;

            // Every point and every copy boundary becomes a column. The picture also
            // runs on flat for a little past each end and fades out there, so where the
            // path meets ordinary flat ground there is no hard vertical cut.
            var xs = new List<float>();
            var start = points[0].x;
            var end = points[^1].x;
            var fadeFrom = start - edgeFade;
            var fadeTo = end + edgeFade;
            foreach (var p in _shape) xs.Add(p.x);
            xs.Add(fadeFrom);
            xs.Add(fadeTo);
            // Copies are laid from world x 0, not from this path's start, so two paths that
            // meet at a chunk seam carry on the same grass without a jump in the pattern.
            var origin = -transform.position.x;
            for (var x = origin + Mathf.Ceil((fadeFrom - origin) / width) * width; x < fadeTo; x += width) xs.Add(x);
            xs.Sort();
            float Alpha(float x) => edgeFade <= 0f ? 1f
                : Mathf.Clamp01(Mathf.Min((x - fadeFrom) / edgeFade, (fadeTo - x) / edgeFade));
            var colours = new List<Color>();

            var verts = new List<Vector3>();
            var uvs = new List<Vector2>();
            var tris = new List<int>();
            for (var i = 0; i < xs.Count - 1; i++)
            {
                float a = xs[i], b = xs[i + 1];
                if (b - a < 0.0005f) continue;
                var copy = Mathf.Floor(((a + b) * 0.5f - origin) / width);
                var ua = Mathf.Lerp(u0, u1, (a - origin) / width - copy);
                var ub = Mathf.Lerp(u0, u1, (b - origin) / width - copy);
                var ya = HeightAt(a) + grassAbove;
                var yb = HeightAt(b) + grassAbove;

                var k = verts.Count;
                verts.Add(new Vector3(a, ya - height, 0f)); uvs.Add(new Vector2(ua, v0));
                verts.Add(new Vector3(a, ya, 0f));          uvs.Add(new Vector2(ua, v1));
                verts.Add(new Vector3(b, yb, 0f));          uvs.Add(new Vector2(ub, v1));
                verts.Add(new Vector3(b, yb - height, 0f)); uvs.Add(new Vector2(ub, v0));
                var ca = new Color(tint.r, tint.g, tint.b, tint.a * Alpha(a));
                var cb = new Color(tint.r, tint.g, tint.b, tint.a * Alpha(b));
                colours.Add(ca); colours.Add(ca); colours.Add(cb); colours.Add(cb);
                tris.Add(k); tris.Add(k + 1); tris.Add(k + 2);
                tris.Add(k); tris.Add(k + 2); tris.Add(k + 3);

                // Below the strip, its bottom row of soil stretched down, so a high hill
                // never shows sky under its picture.
                var fa = ya - height;
                var fb = yb - height;
                if (Mathf.Min(fa, fb) <= fillDownTo) continue;
                var vSoil = v0 + (v1 - v0) * 0.02f;
                k = verts.Count;
                verts.Add(new Vector3(a, fillDownTo, 0f)); uvs.Add(new Vector2(ua, v0));
                verts.Add(new Vector3(a, fa, 0f));         uvs.Add(new Vector2(ua, vSoil));
                verts.Add(new Vector3(b, fb, 0f));         uvs.Add(new Vector2(ub, vSoil));
                verts.Add(new Vector3(b, fillDownTo, 0f)); uvs.Add(new Vector2(ub, v0));
                colours.Add(ca); colours.Add(ca); colours.Add(cb); colours.Add(cb);
                tris.Add(k); tris.Add(k + 1); tris.Add(k + 2);
                tris.Add(k); tris.Add(k + 2); tris.Add(k + 3);
            }

            _mesh.SetVertices(verts);
            _mesh.SetUVs(0, uvs);
            _mesh.SetColors(colours);
            _mesh.SetTriangles(tris, 0);
            _mesh.RecalculateBounds();
            _filter.sharedMesh = _mesh;

            // The sprite material reads its texture from the renderer, the way a
            // SpriteRenderer feeds it, so the strip goes in through a property block.
            var block = new MaterialPropertyBlock();
            _renderer.GetPropertyBlock(block);
            block.SetTexture("_MainTex", tex);
            _renderer.SetPropertyBlock(block);
        }

        /// <summary>Height of the walkable line at a local x, flat beyond either end.</summary>
        public float HeightAt(float x)
        {
            if (_shape.Count < 2) BuildShape();
            if (x <= _shape[0].x) return _shape[0].y;
            for (var i = 1; i < _shape.Count; i++)
            {
                if (x > _shape[i].x) continue;
                var a = _shape[i - 1];
                var b = _shape[i];
                return Mathf.Lerp(a.y, b.y, Mathf.InverseLerp(a.x, b.x, x));
            }
            return _shape[^1].y;
        }

        private readonly List<Vector2> _shape = new();

        // The walked line: the points with every bend swapped for a short curve. Picture, boxes
        // and HeightAt all read this, so what you see is what you stand on.
        private void BuildShape()
        {
            _shape.Clear();
            if (points == null || points.Count < 2) return;
            _shape.Add(points[0]);
            for (var i = 1; i < points.Count - 1; i++)
            {
                Vector2 a = points[i - 1], p = points[i], c = points[i + 1];
                var r = smooth * 0.5f * Mathf.Min((p - a).magnitude, (c - p).magnitude);
                if (r < 0.05f) { _shape.Add(p); continue; }
                var from = p + (a - p).normalized * r;
                var to = p + (c - p).normalized * r;
                const int steps = 8;
                for (var k = 0; k <= steps; k++)
                {
                    var t = k / (float)steps;
                    _shape.Add((1f - t) * (1f - t) * from + 2f * (1f - t) * t * p + t * t * to);
                }
            }
            _shape.Add(points[^1]);
        }

        // ---- Collision ------------------------------------------------------------

        private const string ColliderPrefix = "Segment ";

        private void BuildColliders()
        {
            // Rebuilt from scratch each time; the boxes carry nothing anyone edits by hand.
            for (var i = transform.childCount - 1; i >= 0; i--)
            {
                var child = transform.GetChild(i);
                if (!child.name.StartsWith(ColliderPrefix)) continue;
                if (Application.isPlaying) Destroy(child.gameObject);
                else DestroyImmediate(child.gameObject);
            }

            if (!walkable) return;
            for (var i = 1; i < _shape.Count; i++)
            {
                var a = _shape[i - 1];
                var b = _shape[i];
                var along = b - a;
                var length = along.magnitude;
                if (length < 0.01f) continue;

                var go = new GameObject(ColliderPrefix + i);
                go.layer = groundLayer;
                go.transform.SetParent(transform, false);
                var angle = Mathf.Atan2(along.y, along.x) * Mathf.Rad2Deg;
                go.transform.localRotation = Quaternion.Euler(0f, 0f, angle);
                // Centred under the segment, so its top face is the line itself.
                var normal = new Vector2(-along.y, along.x) / length;
                go.transform.localPosition = (Vector3)((a + b) * 0.5f - normal * (thickness * 0.5f));
                var box = go.AddComponent<BoxCollider>();
                // A hair longer than the segment so neighbouring boxes overlap at a
                // bend and the capsule never finds a crack to catch on.
                box.size = new Vector3(length + 0.05f, thickness, 2f);
            }
        }

        private void OnDrawGizmos()
        {
            if (points == null || points.Count < 2) return;
            Gizmos.color = new Color(0.55f, 0.85f, 0.45f, 0.9f);
            for (var i = 1; i < points.Count; i++)
                Gizmos.DrawLine(transform.TransformPoint(points[i - 1]), transform.TransformPoint(points[i]));
        }
    }
}
