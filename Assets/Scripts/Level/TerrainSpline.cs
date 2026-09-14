using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;

namespace Weizza.Level
{
    /// <summary>How the inside of a shape is treated.</summary>
    public enum TerrainFill
    {
        /// <summary>Filled body under the line, closed off flat below.</summary>
        Ground = 0,

        /// <summary>Filled all the way round. Islands, floating rocks, platforms.</summary>
        Island = 1,

        /// <summary>Filled outside the line instead of inside. Caves and tunnels.</summary>
        Cave = 2,

        /// <summary>No body at all, only the band of art. Rails, vines, wires.</summary>
        EdgeOnly = 3,
    }

    /// <summary>Forces a stretch of edge to use a face the slope would not pick.</summary>
    public enum SideOverride
    {
        Auto = 0,
        Top = 1,
        Left = 2,
        Right = 3,
        Bottom = 4,
    }

    /// <summary>
    /// Ground drawn as a line instead of assembled out of tiles.
    ///
    /// Drag a handful of points, and this builds the three things a piece of
    /// terrain actually is: a filled body, a band of surface art that knows
    /// which way it faces, and something solid to stand on. Move a point and all
    /// three follow on the same frame.
    ///
    /// The collision is deliberately 3D. This game walks on a
    /// CharacterController, which is 3D physics and cannot see a 2D collider at
    /// all, so a tool that only emitted 2D ones would draw beautiful ground the
    /// player falls straight through.
    /// </summary>
    [ExecuteAlways]
    [AddComponentMenu("Weizza/Terrain Spline")]
    public sealed class TerrainSpline : MonoBehaviour
    {
        /// <summary>One point the designer placed, and what they said about it.</summary>
        [System.Serializable]
        public struct ControlPoint
        {
            [HorizontalGroup("p", 150f), HideLabel]
            public Vector2 position;

            [HorizontalGroup("p", 76f), LabelText("Thick"), LabelWidth(38f)]
            [Tooltip("Multiplies how far the band reaches in at this point. " +
                     "Varying it is what stops a long stretch of grass reading as " +
                     "a ruler drawn along the ground.")]
            public float thickness;

            [HorizontalGroup("p"), LabelText("Face"), LabelWidth(34f)]
            [Tooltip("Auto works the face out from the slope. Set it by hand when " +
                     "the slope is right but the art is wrong.")]
            public SideOverride side;

            [HorizontalGroup("p", 58f), LabelText("Sharp"), LabelWidth(40f)]
            [Tooltip("Stops the curve rounding through this point. Man made " +
                     "corners want this, hillsides do not.")]
            public bool sharp;

            public static ControlPoint At(float x, float y) =>
                new() { position = new Vector2(x, y), thickness = 1f };
        }

        /// <summary>A point on the smoothed line, carrying what the ends said.</summary>
        private readonly struct LinePoint
        {
            public readonly Vector2 position;
            public readonly float thickness;
            public readonly SideOverride side;

            public LinePoint(Vector2 p, float t, SideOverride s)
            {
                position = p;
                thickness = t;
                side = s;
            }
        }

        // ---- Shape ----------------------------------------------------------

        [TitleGroup("Shape")]
        [InfoBox("Drag the yellow handles in the scene. Shift click the line to " +
                 "add a point, control click a point to take it away.")]
        [TableList(AlwaysExpanded = true, DrawScrollView = true, MaxScrollViewHeight = 220)]
        [OnValueChanged(nameof(MarkDirty), IncludeChildren = true)]
        [SerializeField]
        private List<ControlPoint> points = new()
        {
            ControlPoint.At(-6f, 0f),
            ControlPoint.At(-2f, 1.2f),
            ControlPoint.At(2f, 0.6f),
            ControlPoint.At(6f, 1.6f),
        };

        [TitleGroup("Shape")]
        [EnumToggleButtons]
        [OnValueChanged(nameof(MarkDirty))]
        [SerializeField] private TerrainFill fill = TerrainFill.Ground;

        [TitleGroup("Shape")]
        [PropertyRange(0, 12)]
        [Tooltip("Extra points inserted between each pair to round the corners. " +
                 "Zero gives straight segments, which is right for man made ledges " +
                 "and wrong for a hillside.")]
        [OnValueChanged(nameof(MarkDirty))]
        [SerializeField] private int smoothing = 6;

        [TitleGroup("Shape")]
        [PropertyRange(0.5f, 30f)]
        [ShowIf("@fill == TerrainFill.Ground")]
        [Tooltip("How far the body extends below the lowest point of the line.")]
        [OnValueChanged(nameof(MarkDirty))]
        [SerializeField] private float bodyDepth = 6f;

        [TitleGroup("Shape")]
        [PropertyRange(0.5f, 40f)]
        [ShowIf("@fill == TerrainFill.Cave")]
        [Tooltip("How far the rock extends outwards from the tunnel before it is " +
                 "cut off. Only has to reach past the edge of the screen.")]
        [OnValueChanged(nameof(MarkDirty))]
        [SerializeField] private float caveThickness = 10f;

        // ---- Look -----------------------------------------------------------

        [TitleGroup("Look")]
        [Required("Make one with Assets > Create > Weizza > Terrain Style.")]
        [InfoBox("Grass on the top, rock down the sides, dirt underneath. Which " +
                 "face a stretch of ground counts as is worked out from the way it " +
                 "leans, so nothing here needs marking up by hand.")]
        [OnValueChanged(nameof(MarkDirty))]
        [SerializeField] private TerrainStyle style;

        [TitleGroup("Look")]
        [Tooltip("Multiplied into the art. Drag it towards the haze colour of an " +
                 "area to push a piece of ground into the distance without a " +
                 "second set of textures.")]
        [OnValueChanged(nameof(MarkDirty))]
        [SerializeField] private Color tint = Color.white;

        [TitleGroup("Look")]
        [PropertyRange(-1f, 1f)]
        [Tooltip("Leans the band of art towards or away from the camera. A little " +
                 "of it reads as the ground having thickness rather than being a " +
                 "sticker.")]
        [OnValueChanged(nameof(MarkDirty))]
        [SerializeField] private float slant;

        [TitleGroup("Look")]
        [Tooltip("Draw order against everything else in the scene.")]
        [OnValueChanged(nameof(MarkDirty))]
        [SerializeField] private int sortingOrder = -10;

        // ---- Collision ------------------------------------------------------

        [TitleGroup("Collision")]
        [InfoBox("Boxes, not a mesh collider. A mesh collider built from a flat " +
                 "shape is paper thin across the screen, and a character falling " +
                 "fast goes straight through it.")]
        [OnValueChanged(nameof(MarkDirty))]
        [SerializeField] private bool generateCollision = true;

        [TitleGroup("Collision")]
        [PropertyRange(0.2f, 4f)]
        [ShowIf(nameof(generateCollision))]
        [Tooltip("Thickness across the screen. Never visible at a flat camera " +
                 "angle, so this is purely about footing.")]
        [OnValueChanged(nameof(MarkDirty))]
        [SerializeField] private float collisionThickness = 1f;

        [TitleGroup("Collision")]
        [PropertyRange(0.1f, 2f)]
        [ShowIf(nameof(generateCollision))]
        [Tooltip("How deep each box reaches below the surface.")]
        [OnValueChanged(nameof(MarkDirty))]
        [SerializeField] private float collisionDepth = 0.5f;

        [TitleGroup("Collision")]
        [PropertyRange(0f, 30f)]
        [ShowIf(nameof(generateCollision))]
        [Tooltip("Consecutive segments that differ by less than this angle are " +
                 "merged into one box. Zero gives a box per segment, which is " +
                 "accurate and wasteful on a gentle curve.")]
        [OnValueChanged(nameof(MarkDirty))]
        [SerializeField] private float collisionMergeAngle = 6f;

        [TitleGroup("Collision")]
        [ShowIf(nameof(generateCollision))]
        [Tooltip("Passes through, but still reports. Water, brambles, a trigger " +
                 "volume shaped like the ground.")]
        [OnValueChanged(nameof(MarkDirty))]
        [SerializeField] private bool isTrigger;

        [TitleGroup("Collision")]
        [ShowIf(nameof(generateCollision))]
        [Tooltip("Optional. Ice, mud, anything with a different friction.")]
        [OnValueChanged(nameof(MarkDirty))]
        [SerializeField] private PhysicsMaterial surfacePhysics;

        // ---- Readout --------------------------------------------------------

        [TitleGroup("Collision")]
        [ShowInInspector, DisplayAsString]
        [LabelText("Built")]
        private string Readout => _builtVerts == 0
            ? "nothing yet"
            : $"{points.Count} point(s) into {_builtVerts} vertices and {_builtBoxes} collider(s)";

        private int _builtVerts;
        private int _builtBoxes;
        private bool _dirty = true;

        public List<ControlPoint> Points => points;
        public bool ClosedLoop => fill is TerrainFill.Island or TerrainFill.Cave;

        /// <summary>Asks for a rebuild on the next frame rather than right now.</summary>
        public void MarkDirty() => _dirty = true;

        private void OnEnable() => _dirty = true;

        private void Update()
        {
            if (!_dirty) return;

            _dirty = false;
            Rebuild();
        }

        // ---- Build ----------------------------------------------------------

        public void Rebuild()
        {
            if (points == null || points.Count < 2) return;

            OrderPoints();

            var line = Subdivide();

            BuildBody(line);
            BuildSurface(line);
            BuildCollision(line);
        }

        /// <summary>
        /// Puts the points back in left to right order.
        ///
        /// Ground is a height along x: every x has one surface above it. Drag a
        /// point past its neighbour and the line doubles back, which the mesh
        /// builder renders as a spike and the collider renders as a wall, and
        /// neither looks like the ordering mistake that caused it. Sorting here
        /// rather than warning because there is no case where a designer wants
        /// ground that folds over itself.
        ///
        /// Left alone for Cave and Island, where the line is a closed shape and
        /// going backwards along x is the whole point.
        /// </summary>
        private void OrderPoints()
        {
            if (fill != TerrainFill.Ground && fill != TerrainFill.EdgeOnly) return;

            for (var i = 1; i < points.Count; i++)
            {
                if (points[i].position.x >= points[i - 1].position.x) continue;

                points.Sort((a, b) => a.position.x.CompareTo(b.position.x));
                return;
            }
        }

        /// <summary>
        /// Turns the control points into the line the terrain actually follows.
        ///
        /// Catmull-Rom, because it passes through every point it is given. A
        /// Bezier rounds more smoothly but stops touching the handles, and a
        /// designer dragging a point expects the ground to arrive exactly where
        /// they put it.
        ///
        /// Thickness is carried along the same walk rather than looked up later,
        /// so a point set thin blends into its neighbours over the curve instead
        /// of stepping at the join.
        /// </summary>
        private List<LinePoint> Subdivide()
        {
            var line = new List<LinePoint>();
            var loop = ClosedLoop;
            var count = points.Count;
            var last = loop ? count : count - 1;

            for (var i = 0; i < last; i++)
            {
                var c1 = points[Wrap(i, count, loop)];
                var c2 = points[Wrap(i + 1, count, loop)];

                line.Add(new LinePoint(c1.position, Mathf.Max(0f, c1.thickness), c1.side));

                // A corner marked sharp is not interpolated through. Smoothing a
                // man made ledge rounds off the very thing that made it read as
                // built rather than grown.
                if (smoothing <= 0 || c1.sharp || c2.sharp) continue;

                var p0 = points[Wrap(i - 1, count, loop)].position;
                var p3 = points[Wrap(i + 2, count, loop)].position;

                for (var s = 1; s <= smoothing; s++)
                {
                    var t = s / (float)(smoothing + 1);
                    line.Add(new LinePoint(
                        CatmullRom(p0, c1.position, c2.position, p3, t),
                        Mathf.Lerp(c1.thickness, c2.thickness, t),
                        c1.side));
                }
            }

            if (!loop)
            {
                var end = points[count - 1];
                line.Add(new LinePoint(end.position, Mathf.Max(0f, end.thickness), end.side));
            }
            else if (line.Count > 0)
            {
                line.Add(line[0]);
            }

            return line;
        }

        private static int Wrap(int index, int count, bool loop)
        {
            if (loop) return ((index % count) + count) % count;
            return Mathf.Clamp(index, 0, count - 1);
        }

        private static Vector2 CatmullRom(Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3, float t)
        {
            var t2 = t * t;
            var t3 = t2 * t;

            return 0.5f * ((2f * p1)
                           + (-p0 + p2) * t
                           + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2
                           + (-p0 + 3f * p1 - 3f * p2 + p3) * t3);
        }

        // ---- Body -----------------------------------------------------------

        private void BuildBody(List<LinePoint> line)
        {
            var body = Part("Body");
            var filter = body.GetComponent<MeshFilter>();
            var renderer = body.GetComponent<MeshRenderer>();

            if (fill == TerrainFill.EdgeOnly)
            {
                filter.sharedMesh = null;
                _builtVerts = 0;
                return;
            }

            var mesh = Fill(Outline(line), style != null ? style.FillTileSize : 4f, tint);
            _builtVerts = mesh.vertexCount;

            filter.sharedMesh = mesh;
            renderer.sharedMaterial = style != null ? style.FillMaterial : null;
            renderer.sortingOrder = sortingOrder;
        }

        /// <summary>
        /// The closed outline the body is filled inside.
        ///
        /// Three of the four modes are the same shape described differently. An
        /// island is the line itself. Ground is the line with a floor dropped
        /// under it, because an open line is a surface and a surface has no
        /// inside. A cave is the line pushed outwards into a ring, so the solid
        /// part becomes the rock around the tunnel rather than the air in it.
        /// </summary>
        private List<Vector2> Outline(List<LinePoint> line)
        {
            var shape = new List<Vector2>(line.Count + 4);
            foreach (var p in line) shape.Add(p.position);

            if (fill == TerrainFill.Cave)
            {
                // Walked back the other way at a distance, which turns the single
                // loop into a ring with a hole in it.
                for (var i = line.Count - 1; i >= 0; i--)
                {
                    shape.Add(line[i].position - NormalAt(line, i) * caveThickness);
                }

                return shape;
            }

            if (fill == TerrainFill.Island) return shape;

            var floor = float.MaxValue;
            foreach (var p in line) floor = Mathf.Min(floor, p.position.y);
            floor -= bodyDepth;

            shape.Add(new Vector2(line[^1].position.x, floor));
            shape.Add(new Vector2(line[0].position.x, floor));
            return shape;
        }

        // ---- Surface --------------------------------------------------------

        /// <summary>
        /// Lays the band of art along the edge, one tile at a time.
        ///
        /// Tiles rather than one stretched strip, because a stretched strip can
        /// only ever show one picture, and ground that shows one picture for
        /// sixty metres reads as wallpaper. Each tile picks its own piece, and
        /// each stretch picks from the set belonging to the face it is: grass
        /// going over the top of a hill, rock down the cliff at the end of it,
        /// changing wherever the slope tips past forty five degrees.
        /// </summary>
        private void BuildSurface(List<LinePoint> line)
        {
            var surface = Part("Surface");
            var filter = surface.GetComponent<MeshFilter>();
            var renderer = surface.GetComponent<MeshRenderer>();

            if (style == null || style.EdgeMaterial == null)
            {
                filter.sharedMesh = null;
                return;
            }

            var verts = new List<Vector3>();
            var uvs = new List<Vector2>();
            var colours = new List<Color>();
            var tris = new List<int>();

            var tileWidth = style.EdgeTileWidth;
            var travelled = 0f;

            for (var i = 0; i < line.Count - 1; i++)
            {
                var a = line[i];
                var b = line[i + 1];
                var length = Vector2.Distance(a.position, b.position);
                if (length <= 0.0001f) continue;

                var side = FaceOf(a, b);
                var face = style.Of(side);

                var from = travelled;
                travelled += length;

                if (!face.draw) continue;

                // The tile a segment belongs to is decided once, from where the
                // segment starts. A segment straddling a boundary is squashed
                // into the tile it began in, which at this subdivision is a
                // fraction of a pixel and far cheaper than splitting geometry.
                var tile = Mathf.FloorToInt(from / tileWidth);
                var piece = style.Piece(side, tile);
                if (piece == null) continue;

                var rect = UvRect(piece);
                var u0 = Mathf.Lerp(rect.xMin, rect.xMax, Mathf.Clamp01((from - tile * tileWidth) / tileWidth));
                var u1 = Mathf.Lerp(rect.xMin, rect.xMax, Mathf.Clamp01((travelled - tile * tileWidth) / tileWidth));

                var normal = Normal(a.position, b.position);
                var z = -face.depthSort;

                var outerA = a.position + normal * face.lift;
                var outerB = b.position + normal * face.lift;
                var innerA = outerA - normal * face.depth * a.thickness;
                var innerB = outerB - normal * face.depth * b.thickness;

                var v = verts.Count;
                verts.Add(new Vector3(outerA.x, outerA.y, z - slant));
                verts.Add(new Vector3(innerA.x, innerA.y, z + slant));
                verts.Add(new Vector3(outerB.x, outerB.y, z - slant));
                verts.Add(new Vector3(innerB.x, innerB.y, z + slant));

                uvs.Add(new Vector2(u0, rect.yMax));
                uvs.Add(new Vector2(u0, rect.yMin));
                uvs.Add(new Vector2(u1, rect.yMax));
                uvs.Add(new Vector2(u1, rect.yMin));

                for (var c = 0; c < 4; c++) colours.Add(tint);

                tris.Add(v); tris.Add(v + 2); tris.Add(v + 1);
                tris.Add(v + 1); tris.Add(v + 2); tris.Add(v + 3);
            }

            // Only an open line has ends that hang in mid air. A loop meets
            // itself, so a cap there would sit in the middle of a continuous run.
            if (!ClosedLoop && line.Count >= 2)
            {
                Cap(verts, uvs, colours, tris, line[1], line[0], true);
                Cap(verts, uvs, colours, tris, line[^2], line[^1], false);
            }

            var mesh = new Mesh { name = "Terrain Surface" };
            mesh.SetVertices(verts);
            mesh.SetUVs(0, uvs);
            mesh.SetColors(colours);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();

            filter.sharedMesh = mesh;
            renderer.sharedMaterial = style.EdgeMaterial;

            // One step in front of the body, so the band never fights the fill it
            // sits on for the same pixels.
            renderer.sortingOrder = sortingOrder + 1;
        }

        /// <summary>
        /// Finishes an end of the line with its own piece of art.
        ///
        /// A band of ground that simply stops is cut off square, and a square cut
        /// through drawn grass reads as a missing texture rather than as the edge
        /// of a ledge. Ferr2D solves it the same way, with a piece drawn for the
        /// end rather than a piece of the middle stretched to fit.
        ///
        /// Optional throughout: a style with no cap assigned draws nothing here
        /// and the end stays square, which is correct for ground that runs off
        /// the side of the screen anyway.
        /// </summary>
        private void Cap(List<Vector3> verts, List<Vector2> uvs, List<Color> colours,
                         List<int> tris, LinePoint inner, LinePoint outer, bool leftEnd)
        {
            var side = FaceOf(leftEnd ? outer : inner, leftEnd ? inner : outer);
            var face = style.Of(side);
            if (!face.draw) return;

            var piece = leftEnd ? face.leftCap : face.rightCap;
            if (piece == null) return;

            var along = (outer.position - inner.position).normalized;
            if (along.sqrMagnitude < 0.0001f) return;

            var normal = new Vector2(along.y, -along.x);
            if (leftEnd) normal = -normal;

            var near = outer.position;
            var far = outer.position + along * face.capWidth;

            var rect = UvRect(piece);
            var z = -face.depthSort;

            var outerNear = near + normal * face.lift;
            var outerFar = far + normal * face.lift;
            var innerNear = outerNear - normal * face.depth * outer.thickness;
            var innerFar = outerFar - normal * face.depth * outer.thickness;

            // A left cap reads outwards from the line, so its art runs the other
            // way. Flipping the U here rather than asking the artist for a
            // mirrored copy halves what has to be drawn.
            var u0 = leftEnd ? rect.xMax : rect.xMin;
            var u1 = leftEnd ? rect.xMin : rect.xMax;

            var v = verts.Count;
            verts.Add(new Vector3(outerNear.x, outerNear.y, z - slant));
            verts.Add(new Vector3(innerNear.x, innerNear.y, z + slant));
            verts.Add(new Vector3(outerFar.x, outerFar.y, z - slant));
            verts.Add(new Vector3(innerFar.x, innerFar.y, z + slant));

            uvs.Add(new Vector2(u0, rect.yMax));
            uvs.Add(new Vector2(u0, rect.yMin));
            uvs.Add(new Vector2(u1, rect.yMax));
            uvs.Add(new Vector2(u1, rect.yMin));

            for (var c = 0; c < 4; c++) colours.Add(tint);

            tris.Add(v); tris.Add(v + 2); tris.Add(v + 1);
            tris.Add(v + 1); tris.Add(v + 2); tris.Add(v + 3);
        }

        /// <summary>
        /// Which face a stretch of edge is drawn as.
        ///
        /// The slope decides unless somebody said otherwise. The override exists
        /// because automatic classification is right almost always and wrong
        /// memorably: the underside of an overhang is a ceiling by geometry and a
        /// cliff face by eye, and only a person can settle that.
        /// </summary>
        private TerrainSide FaceOf(LinePoint a, LinePoint b)
        {
            if (a.side != SideOverride.Auto) return (TerrainSide)(a.side - 1);

            var side = TerrainStyle.SideOf(a.position, b.position);

            // A cave is filled outside its line, so every face points the other
            // way from what the maths says. Flipping here keeps one set of art
            // working for both a hill and the tunnel through it.
            return fill == TerrainFill.Cave ? Opposite(side) : side;
        }

        private static TerrainSide Opposite(TerrainSide side) => side switch
        {
            TerrainSide.Top => TerrainSide.Bottom,
            TerrainSide.Bottom => TerrainSide.Top,
            TerrainSide.Left => TerrainSide.Right,
            _ => TerrainSide.Left,
        };

        /// <summary>Where a sprite lives inside its own texture, as UVs.</summary>
        private static Rect UvRect(Sprite sprite)
        {
            var texture = sprite.texture;
            var r = sprite.textureRect;
            return new Rect(r.x / texture.width, r.y / texture.height,
                            r.width / texture.width, r.height / texture.height);
        }

        /// <summary>Outward pointing normal of a segment, a quarter turn from it.</summary>
        private static Vector2 Normal(Vector2 from, Vector2 to)
        {
            var along = (to - from).normalized;
            return new Vector2(-along.y, along.x);
        }

        /// <summary>
        /// Normal at a point, averaged from the segments either side of it.
        ///
        /// Taking one segment's normal leaves a visible kink at every corner when
        /// the line is offset, because the two sides of the corner move in
        /// different directions and tear apart.
        /// </summary>
        private static Vector2 NormalAt(List<LinePoint> line, int i)
        {
            var before = i > 0 ? Normal(line[i - 1].position, line[i].position) : Vector2.zero;
            var after = i < line.Count - 1 ? Normal(line[i].position, line[i + 1].position) : Vector2.zero;

            var sum = before + after;
            return sum.sqrMagnitude < 0.0001f ? Vector2.up : sum.normalized;
        }

        // ---- Collision ------------------------------------------------------

        private void BuildCollision(List<LinePoint> line)
        {
            var holder = Child("Collision");

            // Cleared and rebuilt rather than reused. Matching old boxes to new
            // spans would need the spans to be stable, and they are not: moving
            // one point can change how many there are.
            for (var i = holder.transform.childCount - 1; i >= 0; i--)
            {
                DestroyPart(holder.transform.GetChild(i).gameObject);
            }

            if (!generateCollision)
            {
                _builtBoxes = 0;
                return;
            }

            var spans = MergeSpans(line, collisionMergeAngle);

            // Faces the style calls decoration get no collision. A cave ceiling
            // should be drawn and not stood on.
            if (style != null) spans.RemoveAll(span => !style.Of(span.side).collide);

            _builtBoxes = spans.Count;

            for (var i = 0; i < spans.Count; i++)
            {
                var span = spans[i];
                var mid = (span.a + span.b) * 0.5f;
                var length = Vector2.Distance(span.a, span.b);
                var angle = Mathf.Atan2(span.b.y - span.a.y, span.b.x - span.a.x) * Mathf.Rad2Deg;

                // One object per span, because a BoxCollider has no rotation of
                // its own: it can only be sized and offset inside whatever
                // transform holds it. A slope therefore needs a transform turned
                // to match it, or the box stays level and the player walks on an
                // invisible staircase.
                var piece = new GameObject($"{span.side} {i}");
                piece.transform.SetParent(holder.transform, false);
                piece.transform.localPosition = new Vector3(mid.x, mid.y, 0f);
                piece.transform.localRotation = Quaternion.Euler(0f, 0f, angle);
                piece.hideFlags = HideFlags.DontSave;

                var box = piece.AddComponent<BoxCollider>();
                box.center = new Vector3(0f, -collisionDepth * 0.5f, 0f);
                box.size = new Vector3(length, collisionDepth, collisionThickness);
                box.isTrigger = isTrigger;
                box.sharedMaterial = surfacePhysics;
            }
        }

        private readonly struct Span
        {
            public readonly Vector2 a;
            public readonly Vector2 b;
            public readonly TerrainSide side;

            public Span(Vector2 from, Vector2 to, TerrainSide face)
            {
                a = from;
                b = to;
                side = face;
            }
        }

        /// <summary>
        /// Joins consecutive segments that point roughly the same way and belong
        /// to the same face.
        ///
        /// A smoothed curve is a few hundred tiny segments, and one collider each
        /// would be hundreds of boxes for a shape the player reads as a single
        /// slope. Merging keeps the collision faithful where the ground actually
        /// turns and cheap where it does not, and it stops at a change of face so
        /// that turning collision off for one of them still works.
        /// </summary>
        private List<Span> MergeSpans(List<LinePoint> line, float toleranceDegrees)
        {
            var spans = new List<Span>();
            if (line.Count < 2) return spans;

            var start = line[0].position;
            var heading = Angle(line[0].position, line[1].position);
            var face = FaceOf(line[0], line[1]);

            for (var i = 1; i < line.Count - 1; i++)
            {
                var next = Angle(line[i].position, line[i + 1].position);
                var nextFace = FaceOf(line[i], line[i + 1]);

                var turned = Mathf.Abs(Mathf.DeltaAngle(heading, next)) > toleranceDegrees;
                if (!turned && nextFace == face) continue;

                spans.Add(new Span(start, line[i].position, face));
                start = line[i].position;
                heading = next;
                face = nextFace;
            }

            spans.Add(new Span(start, line[^1].position, face));
            return spans;
        }

        private static float Angle(Vector2 from, Vector2 to) =>
            Mathf.Atan2(to.y - from.y, to.x - from.x) * Mathf.Rad2Deg;

        // ---- Triangulation --------------------------------------------------

        /// <summary>
        /// Turns an outline into a solid, by ear clipping.
        ///
        /// The idea is small: find a corner whose triangle is inside the shape and
        /// holds no other corner, cut it off, and repeat on what is left. It
        /// handles the concave shapes terrain is full of, such as an overhang or a
        /// dip, which a naive fan triangulation folds inside out.
        /// </summary>
        private static Mesh Fill(List<Vector2> outline, float tileSize, Color colour)
        {
            var shape = new List<Vector2>(outline);

            // Ear clipping only works one way round, so the winding is forced
            // before anything else looks at it.
            if (SignedArea(shape) < 0f) shape.Reverse();

            var indices = new List<int>(shape.Count);
            for (var i = 0; i < shape.Count; i++) indices.Add(i);

            var tris = new List<int>((shape.Count - 2) * 3);
            var guard = shape.Count * shape.Count;

            while (indices.Count > 3 && guard-- > 0)
            {
                var clipped = false;

                for (var i = 0; i < indices.Count; i++)
                {
                    var prev = indices[(i - 1 + indices.Count) % indices.Count];
                    var here = indices[i];
                    var next = indices[(i + 1) % indices.Count];

                    if (!IsEar(shape, indices, prev, here, next)) continue;

                    tris.Add(prev); tris.Add(here); tris.Add(next);
                    indices.RemoveAt(i);
                    clipped = true;
                    break;
                }

                // Nothing was clippable, which means the outline crosses itself.
                // Stopping here leaves a partial fill rather than hanging, and the
                // gap on screen is the clearest possible report of the problem.
                if (!clipped) break;
            }

            if (indices.Count == 3)
            {
                tris.Add(indices[0]); tris.Add(indices[1]); tris.Add(indices[2]);
            }

            var verts = new List<Vector3>(shape.Count);
            var uvs = new List<Vector2>(shape.Count);
            var colours = new List<Color>(shape.Count);

            foreach (var p in shape)
            {
                verts.Add(new Vector3(p.x, p.y, 0f));

                // Planar UVs in world metres, so a rock texture keeps its scale
                // no matter how big the piece of ground is.
                uvs.Add(new Vector2(p.x / tileSize, p.y / tileSize));
                colours.Add(colour);
            }

            var mesh = new Mesh { name = "Terrain Body" };
            mesh.SetVertices(verts);
            mesh.SetUVs(0, uvs);
            mesh.SetColors(colours);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        private static float SignedArea(List<Vector2> shape)
        {
            var sum = 0f;
            for (var i = 0; i < shape.Count; i++)
            {
                var a = shape[i];
                var b = shape[(i + 1) % shape.Count];
                sum += (b.x - a.x) * (b.y + a.y);
            }
            return sum;
        }

        private static bool IsEar(List<Vector2> shape, List<int> indices, int prev, int here, int next)
        {
            var a = shape[prev];
            var b = shape[here];
            var c = shape[next];

            if (Cross(a, b, c) >= 0f) return false;

            foreach (var index in indices)
            {
                if (index == prev || index == here || index == next) continue;
                if (InTriangle(shape[index], a, b, c)) return false;
            }

            return true;
        }

        private static float Cross(Vector2 a, Vector2 b, Vector2 c) =>
            (b.x - a.x) * (c.y - a.y) - (b.y - a.y) * (c.x - a.x);

        private static bool InTriangle(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
        {
            var d1 = Cross(p, a, b);
            var d2 = Cross(p, b, c);
            var d3 = Cross(p, c, a);

            var negative = d1 < 0f || d2 < 0f || d3 < 0f;
            var positive = d1 > 0f || d2 > 0f || d3 > 0f;
            return !(negative && positive);
        }

        // ---- Plumbing -------------------------------------------------------

        private GameObject Part(string name)
        {
            var go = Child(name);
            if (go.GetComponent<MeshFilter>() == null) go.AddComponent<MeshFilter>();
            if (go.GetComponent<MeshRenderer>() == null) go.AddComponent<MeshRenderer>();
            return go;
        }

        private GameObject Child(string name)
        {
            var existing = transform.Find(name);
            if (existing != null) return existing.gameObject;

            var created = new GameObject(name);
            created.transform.SetParent(transform, false);

            // Generated, so it is not something anyone should be editing by hand
            // and not something worth saving into the scene file. Rebuilt from
            // the points on enable, in a build as well as in the editor.
            created.hideFlags = HideFlags.DontSave;
            return created;
        }

        private static void DestroyPart(Object part)
        {
            if (part == null) return;

            if (Application.isPlaying) Destroy(part);
            else DestroyImmediate(part);
        }
    }
}
