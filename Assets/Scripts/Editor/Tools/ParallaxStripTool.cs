using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Weizza.EditorTools
{
    /// <summary>
    /// Lines up several duplicated parallax tiles into one seamless strip.
    ///
    /// Wrap Horizontally needs the width of the whole strip, not the width of
    /// one tile, or every copy teleports three times too often and drifts out
    /// of formation with its neighbours. That number is easy to get wrong by
    /// hand and hard to notice is wrong until the background pops out of
    /// place mid walk, so this reads it off the actual sprites instead of
    /// asking for it.
    /// </summary>
    public static class ParallaxStripTool
    {
        [MenuItem("Weizza/Parallax/Arrange Selected As Strip")]
        private static void ArrangeSelectedAsStrip()
        {
            var layers = Selection.gameObjects
                .Select(go => go.GetComponent<ParallaxLayer>())
                .Where(layer => layer != null)
                .OrderBy(layer => layer.transform.position.x)
                .ToArray();

            if (layers.Length < 2)
            {
                Debug.LogWarning("Select at least two tiles that share a Parallax " +
                                  "Layer component (the duplicated bg/buildings pieces).");
                return;
            }

            var first = layers[0];
            var firstRenderer = first.GetComponent<SpriteRenderer>();
            if (firstRenderer == null || firstRenderer.sprite == null)
            {
                Debug.LogWarning("The leftmost selected tile has no sprite to measure.");
                return;
            }

            var width = firstRenderer.bounds.size.x;
            var anchor = first.transform.position;

            var transforms = layers.Select(layer => layer.transform).ToArray();
            Undo.RecordObjects(transforms, "Arrange Parallax Strip");

            for (var i = 0; i < transforms.Length; i++)
            {
                transforms[i].position = new Vector3(anchor.x + width * i, anchor.y, anchor.z);
            }

            var targets = layers.Cast<Object>().ToArray();
            var firstSo = new SerializedObject(first);
            var mode = firstSo.FindProperty("mode").enumValueIndex;
            var horizontalRate = firstSo.FindProperty("horizontalRate").floatValue;
            var verticalRate = firstSo.FindProperty("verticalRate").floatValue;

            var stripWidth = width * layers.Length;

            var so = new SerializedObject(targets);
            Undo.RecordObjects(targets, "Arrange Parallax Strip");
            so.FindProperty("mode").enumValueIndex = mode;
            so.FindProperty("horizontalRate").floatValue = horizontalRate;
            so.FindProperty("verticalRate").floatValue = verticalRate;
            so.FindProperty("wrapHorizontally").boolValue = true;
            so.FindProperty("tileWidth").floatValue = stripWidth;
            so.ApplyModifiedProperties();

            foreach (var layer in layers) layer.ReAnchor();

            Debug.Log($"Arranged {layers.Length} tiles, {width:0.00} m each, " +
                      $"strip width {stripWidth:0.00} m. Rate copied from {first.name} " +
                      $"to the rest, wrap turned on for the whole group.");
        }

        [MenuItem("Weizza/Parallax/Arrange Selected As Strip", true)]
        private static bool ValidateArrangeSelectedAsStrip()
        {
            return Selection.gameObjects.Count(go => go.GetComponent<ParallaxLayer>() != null) >= 2;
        }
    }
}
