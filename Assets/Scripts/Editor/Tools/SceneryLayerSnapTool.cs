using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Weizza.EditorTools
{
    /// <summary>
    /// Sets a sprite's Sorting Layer, Sorting Order and Z to the standard
    /// numbers for its role, read off `SceneryRoleData` rather than typed
    /// from memory. A new prop dropped at some arbitrary Z used to mean
    /// checking an existing neighbour by hand to copy its numbers.
    ///
    /// Map Painter's own Role field does the same lookup at the moment a
    /// piece is placed; this is the equivalent command for something already
    /// in the scene, an old prop being reassigned or something dragged in
    /// from Project by hand.
    /// </summary>
    public static class SceneryLayerSnapTool
    {
        [MenuItem("Weizza/Scenery/Snap Selected To Far Fog")]
        private static void SnapFarFog() => Apply(SceneryRole.FarFog);

        [MenuItem("Weizza/Scenery/Snap Selected To Dark Things")]
        private static void SnapDarkThings() => Apply(SceneryRole.DarkThings);

        [MenuItem("Weizza/Scenery/Snap Selected To Earth Fill")]
        private static void SnapEarthFill() => Apply(SceneryRole.EarthFill);

        [MenuItem("Weizza/Scenery/Snap Selected To Ground Moss")]
        private static void SnapGroundMoss() => Apply(SceneryRole.GroundMoss);

        [MenuItem("Weizza/Scenery/Snap Selected To Vines")]
        private static void SnapVines() => Apply(SceneryRole.Vines);

        [MenuItem("Weizza/Scenery/Snap Selected To Player Plane")]
        private static void SnapPlayerPlane() => Apply(SceneryRole.PlayerPlane);

        [MenuItem("Weizza/Scenery/Snap Selected To Foreground Silhouette")]
        private static void SnapForeground() => Apply(SceneryRole.ForegroundSilhouette);

        private static void Apply(SceneryRole role)
        {
            var renderers = Selection.gameObjects
                .SelectMany(go => go.GetComponentsInChildren<SpriteRenderer>(true))
                .ToArray();

            if (renderers.Length == 0)
            {
                Debug.LogWarning("[Weizza] Select one or more objects with a Sprite " +
                                  "Renderer first, then pick a role from Weizza > Scenery.");
                return;
            }

            var numbers = SceneryRoleData.Get(role);
            Undo.RecordObjects(renderers.Select(r => (Object)r).ToArray(), "Snap Scenery Layer");
            Undo.RecordObjects(renderers.Select(r => (Object)r.transform).ToArray(), "Snap Scenery Layer");

            foreach (var sr in renderers)
            {
                sr.sortingLayerName = numbers.Layer;
                sr.sortingOrder = numbers.Order;
                var pos = sr.transform.position;
                sr.transform.position = new Vector3(pos.x, pos.y, numbers.Z);
            }

            Debug.Log($"[Weizza] Snapped {renderers.Length} sprite(s) to {role}: " +
                      $"layer {numbers.Layer}, order {numbers.Order}, z {numbers.Z}.");
        }
    }
}
