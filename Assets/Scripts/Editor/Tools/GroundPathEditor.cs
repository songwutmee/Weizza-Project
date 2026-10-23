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
    }
}
