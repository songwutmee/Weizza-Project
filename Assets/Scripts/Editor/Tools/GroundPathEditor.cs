using Sirenix.OdinInspector.Editor;
using UnityEditor;
using UnityEngine;

namespace Weizza.EditorTools
{
    /// <summary>
    /// White dots on every point of a Ground Path: drag to reshape, the picture and the
    /// boxes follow as you drag. Snaps to 0.1 m so two paths can meet exactly.
    /// </summary>
    [CustomEditor(typeof(GroundPath))]
    public sealed class GroundPathEditor : OdinEditor
    {
        public override void OnInspectorGUI()
        {
            base.OnInspectorGUI();
            var path = (GroundPath)target;
            GUILayout.Space(6);
            using (new GUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Add Point", GUILayout.Height(28)))
                {
                    Undo.RecordObject(path, "Add ground point");
                    path.AddPoint();
                    EditorUtility.SetDirty(path);
                }
                if (GUILayout.Button("Remove Last Point", GUILayout.Height(28)))
                {
                    Undo.RecordObject(path, "Remove ground point");
                    path.RemoveLastPoint();
                    EditorUtility.SetDirty(path);
                }
            }
        }

        private void OnSceneGUI()
        {
            var path = (GroundPath)target;
            var t = path.transform;
            var e = Event.current;

            // Shift click on the line adds a point there; Ctrl click on a point removes it.
            if (e.type == EventType.MouseDown && e.button == 0 && (e.shift || e.control))
            {
                if (e.control)
                {
                    for (var i = 0; i < path.Points.Count; i++)
                    {
                        var screen = HandleUtility.WorldToGUIPoint(t.TransformPoint(path.Points[i]));
                        if (Vector2.Distance(screen, e.mousePosition) > 12f) continue;
                        Undo.RecordObject(path, "Remove ground point");
                        path.RemovePoint(i);
                        EditorUtility.SetDirty(path);
                        e.Use();
                        return;
                    }
                }
                else
                {
                    var best = -1;
                    var bestDistance = 14f;
                    for (var i = 1; i < path.Points.Count; i++)
                    {
                        var d = HandleUtility.DistanceToLine(t.TransformPoint(path.Points[i - 1]), t.TransformPoint(path.Points[i]));
                        if (d < bestDistance) { bestDistance = d; best = i; }
                    }
                    if (best > 0)
                    {
                        // Dropped onto the line itself, so adding a point never changes the shape.
                        var ray = HandleUtility.GUIPointToWorldRay(e.mousePosition);
                        var plane = new Plane(-t.forward, t.position);
                        if (plane.Raycast(ray, out var enter))
                        {
                            var local = (Vector2)t.InverseTransformPoint(ray.GetPoint(enter));
                            local.x = Mathf.Round(local.x * 10f) / 10f;
                            local.y = path.HeightAt(local.x);
                            Undo.RecordObject(path, "Add ground point");
                            path.InsertPoint(best, local);
                            EditorUtility.SetDirty(path);
                            e.Use();
                            return;
                        }
                    }
                }
            }
            if (e.type == EventType.Layout && (e.shift || e.control))
                HandleUtility.AddDefaultControl(GUIUtility.GetControlID(FocusType.Passive));

            Handles.Label(t.position + Vector3.down * 0.6f, "Drag points.  Shift click the line to add one.  Ctrl click a point to remove it.");
            DrawSlopes(path, t);
            for (var i = 0; i < path.Points.Count; i++)
            {
                var world = t.TransformPoint(path.Points[i]);
                var size = HandleUtility.GetHandleSize(world) * 0.09f;
                Handles.color = Color.white;
                EditorGUI.BeginChangeCheck();
                var moved = Handles.FreeMoveHandle(world, size, Vector3.zero, Handles.DotHandleCap);
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(path, "Move ground point");
                    var local = (Vector2)t.InverseTransformPoint(moved);
                    local.x = Mathf.Round(local.x * 10f) / 10f;
                    local.y = Mathf.Round(local.y * 10f) / 10f;
                    path.SetPoint(i, local);
                    EditorUtility.SetDirty(path);
                }
                Handles.Label(world + Vector3.up * size * 3f, (i + 1).ToString());
            }
        }

        /// <summary>Slope steeper than this reads as unnatural to walk; past the climb limit it cannot be walked.</summary>
        public const float ComfortSlope = 30f;
        public const float ClimbLimit = 40f;

        // Each stretch between two points coloured by how steep it is, with its angle,
        // so a slope that is too steep shows up while dragging instead of in Play.
        private static void DrawSlopes(GroundPath path, Transform t)
        {
            for (var i = 1; i < path.Points.Count; i++)
            {
                var a = t.TransformPoint(path.Points[i - 1]);
                var b = t.TransformPoint(path.Points[i]);
                var angle = Mathf.Abs(Mathf.Atan2(b.y - a.y, Mathf.Max(Mathf.Abs(b.x - a.x), 1e-4f)) * Mathf.Rad2Deg);
                var colour = angle > ClimbLimit ? new Color(1f, 0.3f, 0.25f) : angle > ComfortSlope ? new Color(1f, 0.65f, 0.2f) : new Color(0.45f, 0.95f, 0.55f);
                Handles.color = colour;
                Handles.DrawAAPolyLine(4f, a + Vector3.up * 0.02f, b + Vector3.up * 0.02f);
                if (angle < 3f) continue;
                var style = new GUIStyle(EditorStyles.miniBoldLabel) { normal = { textColor = colour } };
                Handles.Label((a + b) * 0.5f + Vector3.up * 0.35f, $"{angle:0}°", style);
            }
        }
    }
}
