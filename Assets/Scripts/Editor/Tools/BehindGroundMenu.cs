using UnityEditor;
using UnityEngine;

namespace Weizza.EditorTools
{
    /// <summary>
    /// Moves the selected stumps and rocks behind the ground or back in front of it, so a
    /// piece placed before Map Painter's "Behind ground" switch existed is one click to fix.
    /// </summary>
    public static class BehindGroundMenu
    {
        private const int FrontOrder = -4;

        [MenuItem("Weizza/Level/Send Behind Ground", priority = 41)]
        private static void SendBehind() => SetOrder(MapPainterWindow.BehindGroundOrder);

        [MenuItem("Weizza/Level/Bring In Front Of Ground", priority = 42)]
        private static void BringFront() => SetOrder(FrontOrder);

        [MenuItem("Weizza/Level/Send Behind Ground", true)]
        [MenuItem("Weizza/Level/Bring In Front Of Ground", true)]
        private static bool HasSelection() => Selection.gameObjects.Length > 0;

        private static void SetOrder(int order)
        {
            var changed = 0;
            foreach (var go in Selection.gameObjects)
            {
                foreach (var r in go.GetComponentsInChildren<SpriteRenderer>(true))
                {
                    // Never the ground itself: it is what everything else is sorted against.
                    if (r.sprite != null && r.sprite.name.StartsWith("L51_ground")) continue;
                    Undo.RecordObject(r, "Change ground order");
                    r.sortingOrder = order;
                    changed++;
                }
            }
            Debug.Log($"[Weizza] {changed} piece(s) now at order {order}.");
        }
    }
}
