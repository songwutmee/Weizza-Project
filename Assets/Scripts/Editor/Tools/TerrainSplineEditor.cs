using System.Collections.Generic;
using Sirenix.OdinInspector.Editor;
using UnityEditor;
using UnityEngine;
using Weizza.Level;

namespace Weizza.EditorTools
{
    /// <summary>
    /// Lets the terrain be drawn in the scene rather than typed in the inspector.
    ///
    /// A list of coordinates is a terrible way to describe a hillside. The whole
    /// value of a spline is that the shape is edited where it is seen, so the
    /// handles here are the real interface and the table underneath is only for
    /// the rare moment somebody needs an exact number.
    ///
    /// The line is drawn in the colour of whichever set of art each stretch will
    /// use. Which face a slope counts as is the one thing about this tool that is
    /// decided rather than typed, so it is the one thing worth showing before the
    /// art is there to show it.
    /// </summary>
    [CustomEditor(typeof(TerrainSpline))]
    public sealed class TerrainSplineEditor : OdinEditor
    {
        private const float HandleScale = 0.05f;
        private const string SnapKey = "Weizza.Terrain.Snap";

        private static readonly Color TopColour = new(0.42f, 0.92f, 0.5f);
        private static readonly Color SideColour = new(0.98f, 0.68f, 0.26f);
        private static readonly Color BottomColour = new(0.45f, 0.7f, 1f);

        private static float Snap
        {
            get => EditorPrefs.GetFloat(SnapKey, 0f);
            set => EditorPrefs.SetFloat(SnapKey, value);
        }

        public override void OnInspectorGUI()
        {
            var spline = (TerrainSpline)target;
            var style = serializedObject.FindProperty("style");

            if (style.objectReferenceValue == null)
            {
                EditorGUILayout.HelpBox(
                    "Nothing is dressing this terrain yet, so it draws as flat magenta. " +
                    "A starter style gives it a plain colour to stand on, which is enough " +
                    "to block out a level and walk it before any art exists.",
                    MessageType.Info);

                if (GUILayout.Button("Create a starter style", GUILayout.Height(32f)))
                {
                    style.objectReferenceValue = CreateStarterStyle();
                    serializedObject.ApplyModifiedProperties();
                    spline.MarkDirty();
                }

                EditorGUILayout.Space(6f);
            }

            base.OnInspectorGUI();
        }

        /// <summary>
        /// Makes the smallest style that is not magenta.
        ///
        /// Flat colour and no edge art at all, because the point of it is to get
        /// out of the way. A blockout wants ground that reads as ground and
        /// nothing else, and every minute spent choosing a placeholder texture is
        /// a minute spent on art that is going to be thrown away.
        /// </summary>
        private static TerrainStyle CreateStarterStyle()
        {
            const string folder = "Assets/GameData/Terrain";
            EnsureFolder(folder);

            var material = new Material(Shader.Find("Universal Render Pipeline/Unlit")
                                        ?? Shader.Find("Sprites/Default"))
            {
                name = "TerrainBlockout",
            };

            // A muted earth tone. Dark enough to sit under bright characters,
            // desaturated enough that nobody mistakes it for finished art.
            var earth = new Color(0.30f, 0.27f, 0.24f);
            material.color = earth;
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", earth);

            AssetDatabase.CreateAsset(material, $"{folder}/TerrainBlockout.mat");

            var style = ScriptableObject.CreateInstance<TerrainStyle>();
            var so = new SerializedObject(style);
            so.FindProperty("fillMaterial").objectReferenceValue = material;
            so.ApplyModifiedProperties();

            AssetDatabase.CreateAsset(style, $"{folder}/BlockoutTerrain.asset");
            AssetDatabase.SaveAssets();

            Debug.Log($"[Weizza] Starter terrain style made in {folder}. Drop edge sprites " +
                      "into it when the art arrives, then press Build the edge material.");
            return style;
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;

            var parts = path.Split('/');
            var built = parts[0];
            for (var i = 1; i < parts.Length; i++)
            {
                var next = $"{built}/{parts[i]}";
                if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(built, parts[i]);
                built = next;
            }
        }

        private void OnSceneGUI()
        {
            var spline = (TerrainSpline)target;
            var points = spline.Points;
            if (points == null || points.Count == 0) return;

            DrawLine(spline, points);
            DrawHandles(spline, points);
            HandleShortcuts(spline, points);
            DrawOverlay(points.Count);
        }

        private static Color ColourOf(TerrainSide side) => side switch
        {
            TerrainSide.Top => TopColour,
            TerrainSide.Bottom => BottomColour,
            _ => SideColour,
        };

        private static TerrainSide FaceOf(TerrainSpline.ControlPoint a, TerrainSpline.ControlPoint b) =>
            a.side != SideOverride.Auto
                ? (TerrainSide)(a.side - 1)
                : TerrainStyle.SideOf(a.position, b.position);

        private static void DrawLine(TerrainSpline spline, List<TerrainSpline.ControlPoint> points)
        {
            if (points.Count < 2) return;

            var last = spline.ClosedLoop ? points.Count : points.Count - 1;

            for (var i = 0; i < last; i++)
            {
                var a = points[i];
                var b = points[(i + 1) % points.Count];

                Handles.color = ColourOf(FaceOf(a, b));
                Handles.DrawAAPolyLine(4f,
                    spline.transform.TransformPoint(a.position),
                    spline.transform.TransformPoint(b.position));
            }
        }

        private void DrawHandles(TerrainSpline spline, List<TerrainSpline.ControlPoint> points)
        {
            for (var i = 0; i < points.Count; i++)
            {
                var point = points[i];
                var world = spline.transform.TransformPoint(point.position);

                // Sized from distance to the camera, so a handle stays the same
                // size on screen whether the view is on one rock or on the whole
                // level. Fixed size handles vanish when zoomed out, which is
                // exactly when a level is being laid out.
                var size = HandleUtility.GetHandleSize(world) * HandleScale;

                Handles.color = point.sharp ? new Color(1f, 0.45f, 0.4f) : new Color(1f, 0.85f, 0.32f);
                if (point.sharp) Handles.RectangleHandleCap(0, world, Quaternion.identity, size, EventType.Repaint);
                else Handles.DotHandleCap(0, world, Quaternion.identity, size, EventType.Repaint);

                EditorGUI.BeginChangeCheck();
                var moved = Handles.FreeMoveHandle(world, size * 2.4f, Vector3.zero, Handles.CircleHandleCap);
                if (!EditorGUI.EndChangeCheck()) continue;

                Undo.RecordObject(spline, "Move terrain point");

                var local = spline.transform.InverseTransformPoint(moved);
                point.position = Grid(new Vector2(local.x, local.y));
                points[i] = point;

                spline.MarkDirty();
                EditorUtility.SetDirty(spline);
            }
        }

        private static Vector2 Grid(Vector2 where)
        {
            var step = Snap;
            if (step <= 0f) return where;

            return new Vector2(Mathf.Round(where.x / step) * step, Mathf.Round(where.y / step) * step);
        }

        /// <summary>
        /// Shift click to add a point, control click to take one away, alt click
        /// to make a corner sharp or round again.
        ///
        /// All three act on whatever is nearest the cursor rather than on a
        /// selection, because selecting a point and then choosing an operation is
        /// two steps for something that happens a hundred times while a level is
        /// being shaped.
        /// </summary>
        private void HandleShortcuts(TerrainSpline spline, List<TerrainSpline.ControlPoint> points)
        {
            var e = Event.current;
            if (e.type != EventType.MouseDown || e.button != 0) return;
            if (!e.shift && !e.control && !e.alt) return;

            var ray = HandleUtility.GUIPointToWorldRay(e.mousePosition);
            var plane = new Plane(spline.transform.forward, spline.transform.position);
            if (!plane.Raycast(ray, out var distance)) return;

            var hit = spline.transform.InverseTransformPoint(ray.GetPoint(distance));
            var where = new Vector2(hit.x, hit.y);

            if (e.shift)
            {
                Undo.RecordObject(spline, "Add terrain point");

                var at = NearestSegment(points, where, spline.ClosedLoop) + 1;
                var neighbour = points[Mathf.Clamp(at - 1, 0, points.Count - 1)];

                // The new point copies what its neighbour was set to. Dropping one
                // into a stretch that was thinned or forced to a face should carry
                // on that stretch, not punch a default sized hole in it.
                points.Insert(at, new TerrainSpline.ControlPoint
                {
                    position = Grid(where),
                    thickness = neighbour.thickness,
                    side = neighbour.side,
                });
            }
            else if (e.control && points.Count > 2)
            {
                Undo.RecordObject(spline, "Remove terrain point");
                points.RemoveAt(NearestPoint(points, where));
            }
            else if (e.alt)
            {
                Undo.RecordObject(spline, "Toggle sharp corner");

                var at = NearestPoint(points, where);
                var point = points[at];
                point.sharp = !point.sharp;
                points[at] = point;
            }

            spline.MarkDirty();
            EditorUtility.SetDirty(spline);
            e.Use();
        }

        private static int NearestSegment(List<TerrainSpline.ControlPoint> points, Vector2 where, bool loop)
        {
            var best = 0;
            var bestDistance = float.MaxValue;
            var last = loop ? points.Count : points.Count - 1;

            for (var i = 0; i < last; i++)
            {
                var a = points[i].position;
                var b = points[(i + 1) % points.Count].position;

                // Distance to the segment, not to either end. Clicking the middle
                // of a long span should add a point there, and comparing against
                // the endpoints would send it to whichever corner happened to be
                // closer.
                var along = Mathf.Clamp01(Vector2.Dot(where - a, b - a) / Mathf.Max(0.0001f, (b - a).sqrMagnitude));
                var distance = Vector2.Distance(where, a + (b - a) * along);

                if (distance >= bestDistance) continue;

                bestDistance = distance;
                best = i;
            }

            return best;
        }

        private static int NearestPoint(List<TerrainSpline.ControlPoint> points, Vector2 where)
        {
            var best = 0;
            var bestDistance = float.MaxValue;

            for (var i = 0; i < points.Count; i++)
            {
                var distance = Vector2.Distance(where, points[i].position);
                if (distance >= bestDistance) continue;

                bestDistance = distance;
                best = i;
            }

            return best;
        }

        private static void DrawOverlay(int count)
        {
            Handles.BeginGUI();

            var box = new Rect(12f, 12f, 244f, 132f);
            GUI.Box(box, GUIContent.none);

            var row = box.y + 6f;
            var x = box.x + 10f;
            var w = box.width - 20f;

            GUI.Label(new Rect(x, row, w, 18f), $"Terrain: {count} point(s)");
            row += 20f;

            Swatch(new Rect(x, row + 4f, 10f, 10f), TopColour);
            GUI.Label(new Rect(x + 16f, row, w, 18f), "top art");
            row += 17f;

            Swatch(new Rect(x, row + 4f, 10f, 10f), SideColour);
            GUI.Label(new Rect(x + 16f, row, w, 18f), "side art");
            row += 17f;

            Swatch(new Rect(x, row + 4f, 10f, 10f), BottomColour);
            GUI.Label(new Rect(x + 16f, row, w, 18f), "underside art");
            row += 21f;

            GUI.Label(new Rect(x, row, w, 18f), "Shift add   Ctrl remove   Alt sharp");
            row += 20f;

            EditorGUI.BeginChangeCheck();
            var snap = EditorGUI.FloatField(new Rect(x, row, w, 18f), "Snap (m)", Snap);
            if (EditorGUI.EndChangeCheck()) Snap = Mathf.Max(0f, snap);

            Handles.EndGUI();
        }

        private static void Swatch(Rect where, Color colour)
        {
            var was = GUI.color;
            GUI.color = colour;
            GUI.DrawTexture(where, Texture2D.whiteTexture);
            GUI.color = was;
        }
    }
}
