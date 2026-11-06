using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Weizza.EditorTools
{
    /// <summary>
    /// Switches placed pieces between a wall the player bumps into and a rock they walk
    /// through at ground level but can land on (a One Way Platform on the collider).
    /// </summary>
    public static class StandOnTopMenu
    {
        [MenuItem("Weizza/Level/Make Stand On Top (walk through)", priority = 43)]
        private static void MakeStandOnTop()
        {
            var changed = 0;
            foreach (var go in Selection.gameObjects)
                foreach (var c in go.GetComponentsInChildren<Collider>(true))
                    if (Apply(c, true)) changed++;
            Debug.Log($"[Weizza] {changed} piece(s) now stand on top.");
        }

        [MenuItem("Weizza/Level/Make Solid Wall", priority = 44)]
        private static void MakeSolid()
        {
            var changed = 0;
            foreach (var go in Selection.gameObjects)
                foreach (var c in go.GetComponentsInChildren<Collider>(true))
                    if (Apply(c, false)) changed++;
            Debug.Log($"[Weizza] {changed} piece(s) are solid walls again.");
        }

        [MenuItem("Weizza/Level/Make Stand On Top (walk through)", true)]
        [MenuItem("Weizza/Level/Make Solid Wall", true)]
        private static bool HasSelection() => Selection.gameObjects.Length > 0;

        [MenuItem("Weizza/Level/Make Every Stone Stand On Top (open scenes)", priority = 45)]
        private static void AllStones()
        {
            var changed = 0;
            foreach (var c in Object.FindObjectsByType<Collider>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                var r = c.GetComponent<SpriteRenderer>();
                if (r == null || r.sprite == null || !r.sprite.name.Contains("stone")) continue;
                if (Apply(c, true)) { changed++; EditorSceneManager.MarkSceneDirty(c.gameObject.scene); }
            }
            Debug.Log($"[Weizza] {changed} stone(s) now stand on top. Save the scenes to keep it.");
        }

        private static bool Apply(Collider c, bool on)
        {
            // Ground and anything already a trigger is never a platform.
            if (c.isTrigger || c.GetComponent<GroundPath>() != null || c.GetComponentInParent<GroundPath>() != null) return false;
            var has = c.GetComponent<OneWayPlatform>();
            if (on && has == null) { Undo.AddComponent<OneWayPlatform>(c.gameObject); return true; }
            if (!on && has != null) { Undo.DestroyObjectImmediate(has); return true; }
            return false;
        }
    }
}
