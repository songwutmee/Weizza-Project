using StoryKit.Bridge;
using UnityEditor;
using UnityEngine;

namespace Weizza.EditorTools
{
    /// <summary>
    /// A StoryStageId anchor is an empty GameObject with nothing to draw it,
    /// so a scene full of cutscene targets was invisible in the Scene view,
    /// which made picking one for ActorMoveNode's Target Id a guess. This
    /// puts a glowing white marker and its id on every one, always, not just
    /// when selected, because the whole point is to see them at a glance
    /// while placing a new one.
    ///
    /// Lives in Weizza.Editor rather than in com.storykit.core: DrawGizmo is
    /// an attribute on a static method anywhere, it does not need to be a
    /// member of the type it draws.
    /// </summary>
    internal static class StoryStageIdGizmo
    {
        [DrawGizmo(GizmoType.NonSelected | GizmoType.Selected | GizmoType.Pickable)]
        private static void Draw(StoryStageId anchor, GizmoType gizmoType)
        {
            var selected = (gizmoType & GizmoType.Selected) != 0;
            var position = anchor.transform.position;

            Gizmos.color = selected
                ? Color.white
                : new Color(1f, 1f, 1f, 0.65f);
            Gizmos.DrawSphere(position, selected ? 0.22f : 0.16f);

            Gizmos.color = new Color(1f, 1f, 1f, 0.25f);
            Gizmos.DrawWireSphere(position, 0.4f);

            var label = string.IsNullOrEmpty(anchor.Id) ? "(no id)" : anchor.Id;
            Handles.color = Color.white;
            Handles.Label(position + Vector3.up * 0.5f, label, EditorStyles.whiteBoldLabel);
        }
    }
}
