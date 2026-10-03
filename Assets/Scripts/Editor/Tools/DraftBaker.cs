using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Weizza.EditorTools
{
    /// <summary>
    /// Turns a stretch of draft painting into streamed chunks.
    ///
    /// The artist paints freely into a "Draft" root (Map Painter does this when
    /// "Paint straight into chunks" is off, or when painting past the end of the
    /// line), in any scene, without thinking about chunks at all. When a stretch
    /// looks right, they select it and bake: new chunk scenes are added to the end
    /// of the line to cover it, with the area id they choose (forest, mountain),
    /// and every piece moves into the chunk under it, into the same band folder it
    /// had in the draft. This is the same "draft, then commit" split level tools
    /// like LDtk and Tiled use: drawing and the streaming layout are two steps, so
    /// the drawing step never has to know about the second.
    /// </summary>
    public static class DraftBaker
    {
        public const string DraftRoot = "Draft";

        public struct Piece
        {
            public Transform transform;
            public string folder;   // path under Draft, e.g. "Scenery/6 Props"
            public Bounds bounds;
        }

        /// <summary>Every piece under every Draft root in the open scenes.</summary>
        public static List<Piece> FindPieces()
        {
            var found = new List<Piece>();
            for (var i = 0; i < SceneManager.sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                if (!scene.isLoaded) continue;
                foreach (var root in scene.GetRootGameObjects())
                    if (root.name == DraftRoot) Collect(root.transform, "", found);
            }
            return found;
        }

        /// <summary>Only the pieces the selection touches. Selecting the Draft root or a band folder takes everything under it.</summary>
        public static List<Piece> SelectedPieces()
        {
            var all = FindPieces();
            var picked = new List<Piece>();
            foreach (var p in all)
            {
                foreach (var go in Selection.gameObjects)
                {
                    if (p.transform == go.transform || p.transform.IsChildOf(go.transform))
                    {
                        picked.Add(p);
                        break;
                    }
                }
            }
            return picked;
        }

        public static bool Measure(List<Piece> pieces, out float xMin, out float xMax)
        {
            xMin = float.MaxValue;
            xMax = float.MinValue;
            foreach (var p in pieces)
            {
                xMin = Mathf.Min(xMin, p.bounds.min.x);
                xMax = Mathf.Max(xMax, p.bounds.max.x);
            }
            return pieces.Count > 0;
        }

        /// <summary>
        /// Bakes the pieces into chunks. With moveToLineEnd the stretch is slid along X
        /// so its left edge meets the end of the line (a draft painted at x 0 in an
        /// empty scene lands after the forest, not on top of it). With oneChunk the new
        /// chunk is exactly as wide as the stretch, otherwise it is cut every
        /// chunkWidth metres. Returns a line for the log.
        /// </summary>
        public static string Bake(List<Piece> pieces, string area, float chunkWidth, bool oneChunk, bool moveToLineEnd)
        {
            var atlas = ChunkPainting.Atlas;
            if (atlas == null || !atlas.linear) return "No linear World Atlas, nothing baked.";
            if (!Measure(pieces, out var xMin, out var xMax)) return "Nothing to bake: no draft pieces selected.";

            var lineStart = float.MaxValue;
            var lineEnd = float.MinValue;
            foreach (var room in atlas.rooms)
            {
                if (room == null) continue;
                lineStart = Mathf.Min(lineStart, room.worldRect.xMin);
                lineEnd = Mathf.Max(lineEnd, room.worldRect.xMax);
            }
            if (lineEnd < lineStart) { lineStart = 0f; lineEnd = 0f; }

            var shift = moveToLineEnd ? lineEnd - xMin : 0f;
            var stretchEnd = xMax + shift;

            // New chunks to cover whatever runs past the end of the line.
            var made = new List<string>();
            if (stretchEnd > lineEnd + 0.01f)
            {
                if (oneChunk)
                {
                    var width = Mathf.Max(10f, Mathf.Ceil(stretchEnd - lineEnd));
                    var def = WorldMapWindow.AppendChunk(area, width);
                    if (def != null) made.Add(def.id);
                }
                else
                {
                    var guard = 0;
                    while (lineEnd < stretchEnd - 0.01f && guard++ < 64)
                    {
                        var def = WorldMapWindow.AppendChunk(area, chunkWidth);
                        if (def == null) break;
                        made.Add(def.id);
                        lineEnd = def.worldRect.xMax;
                    }
                }
                ChunkEditorSync.OpenAll();
            }

            var moved = 0;
            var skipped = 0;
            var draftScenes = new HashSet<Scene>();
            foreach (var p in pieces)
            {
                var t = p.transform;
                if (t == null) continue;
                draftScenes.Add(t.gameObject.scene);

                var centre = p.bounds.center.x + shift;
                var chunk = ChunkPainting.ChunkAt(centre, openIfClosed: true);
                if (chunk == null)
                {
                    skipped++;
                    continue;
                }

                var folder = chunk.transform;
                if (!string.IsNullOrEmpty(p.folder))
                    foreach (var part in p.folder.Split('/'))
                        folder = ChunkPainting.Child(folder, part);

                Undo.SetTransformParent(t, null, "Bake draft");
                Undo.MoveGameObjectToScene(t.gameObject, chunk.gameObject.scene, "Bake draft");
                Undo.RecordObject(t, "Bake draft");
                t.position += Vector3.right * shift;
                Undo.SetTransformParent(t, folder, "Bake draft");
                EditorSceneManager.MarkSceneDirty(chunk.gameObject.scene);
                moved++;
            }

            foreach (var scene in draftScenes)
            {
                RemoveEmptyFolders(scene);
                EditorSceneManager.MarkSceneDirty(scene);
            }

            var madeText = made.Count > 0 ? $" New chunk(s): {string.Join(", ", made)}." : "";
            var skipText = skipped > 0 ? $" {skipped} piece(s) sit before the start of the line and stayed in the draft." : "";
            return $"Baked {moved} piece(s) into chunks.{madeText}{skipText} Press Ctrl+S to save every scene.";
        }

        // ---- Helpers ------------------------------------------------------------

        // A folder is an empty object that only groups things. Anything else under
        // the draft (a sprite, a collider box, a plant) is a piece.
        private static bool IsFolder(Transform t) => t.GetComponents<Component>().Length == 1 && t.childCount > 0;

        private static void Collect(Transform folder, string path, List<Piece> into)
        {
            foreach (Transform child in folder)
            {
                // An empty folder is not a piece, it is just left over.
                if (child.GetComponents<Component>().Length == 1 && child.childCount == 0) continue;
                if (IsFolder(child))
                {
                    Collect(child, string.IsNullOrEmpty(path) ? child.name : path + "/" + child.name, into);
                    continue;
                }
                into.Add(new Piece { transform = child, folder = path, bounds = BoundsOf(child) });
            }
        }

        private static Bounds BoundsOf(Transform t)
        {
            var has = false;
            var b = new Bounds(t.position, Vector3.zero);
            foreach (var r in t.GetComponentsInChildren<Renderer>())
            {
                if (has) b.Encapsulate(r.bounds); else { b = r.bounds; has = true; }
            }
            foreach (var c in t.GetComponentsInChildren<Collider>())
            {
                if (has) b.Encapsulate(c.bounds); else { b = c.bounds; has = true; }
            }
            return b;
        }

        private static void RemoveEmptyFolders(Scene scene)
        {
            if (!scene.IsValid() || !scene.isLoaded) return;
            foreach (var root in scene.GetRootGameObjects())
            {
                if (root.name != DraftRoot) continue;
                Prune(root.transform);
                if (root.transform.childCount == 0) Undo.DestroyObjectImmediate(root);
            }
        }

        private static void Prune(Transform t)
        {
            for (var i = t.childCount - 1; i >= 0; i--)
            {
                var child = t.GetChild(i);
                if (child.GetComponents<Component>().Length != 1) continue;
                Prune(child);
                if (child.childCount == 0) Undo.DestroyObjectImmediate(child.gameObject);
            }
        }
    }
}
