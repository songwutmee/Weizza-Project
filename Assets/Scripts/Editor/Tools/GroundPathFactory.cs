using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Weizza.EditorTools
{
    /// <summary>
    /// Makes a ready to use Ground Path where the Scene view is looking, filed into the
    /// chunk that owns that x so it streams with the rest of the stretch. Map Painter's
    /// Ground Path button and the Weizza menu both come through here, so there is one
    /// way a path gets made.
    ///
    /// Art, material and layer are copied from the nearest flat ground piece, so a new
    /// path is graded, lit and walked on exactly like the ground around it.
    /// </summary>
    public static class GroundPathFactory
    {
        private const string StripPath = "Assets/Mock_Asset/Forest_Silhouette_01/Lv5.1 ground_/L51_ground_flat1920x512.png";

        // ---- Ready made shapes ------------------------------------------------------

        /// <summary>A stretch of ground to drop in: points as metres along and up from where it starts.</summary>
        public enum Shape { Flat, Hill, Dip, Steps, Ledge }

        private static Vector2[] PointsOf(Shape shape) => shape switch
        {
            Shape.Flat => new[] { new Vector2(0f, 0f), new Vector2(8f, 0f) },
            // Rises and falls at about 22 degrees, comfortable to walk.
            Shape.Hill => new[] { new Vector2(0f, 0f), new Vector2(3f, 1.2f), new Vector2(6f, 1.2f), new Vector2(9f, 0f) },
            Shape.Dip => new[] { new Vector2(0f, 0f), new Vector2(2.5f, -1.2f), new Vector2(5.5f, -1.2f), new Vector2(8f, 0f) },
            // Each riser is 1.2 m, too steep to walk and well under the 3.1 m jump: a climb by hops.
            Shape.Steps => new[] { new Vector2(0f, 0f), new Vector2(2f, 0f), new Vector2(2.6f, 1.2f), new Vector2(5f, 1.2f), new Vector2(5.6f, 2.4f), new Vector2(8f, 2.4f) },
            // One 2.4 m step up, a jump that needs to be meant.
            Shape.Ledge => new[] { new Vector2(0f, 0f), new Vector2(3f, 0f), new Vector2(3.4f, 2.4f), new Vector2(7f, 2.4f) },
            _ => new[] { new Vector2(0f, 0f), new Vector2(8f, 0f) },
        };

        /// <summary>
        /// Drops a shape where the Scene view is looking. If a path ends just to the left,
        /// the new one starts exactly on its last point, so the ground has no seam to fall in.
        /// </summary>
        public static GroundPath CreateShape(Shape shape)
        {
            var view = SceneView.lastActiveSceneView;
            var x = view != null ? Mathf.Round(view.pivot.x * 10f) / 10f - 4f : 0f;
            var start = new Vector2(x, GroundPath.TryTerrainHeight(x, out var h) ? h : 0f);
            var before = PathEndingNear(x, 3f);
            if (before != null) start = WorldEnd(before);
            var path = Create(start.x);
            if (path == null) return null;
            var points = PointsOf(shape);
            var local = new Vector2[points.Length];
            var origin = path.transform.position.x;
            for (var i = 0; i < points.Length; i++) local[i] = new Vector2(start.x - origin + points[i].x, start.y + points[i].y);
            SetPoints(path, local);
            path.name = "Ground Path (" + shape + ")";
            return path;
        }

        /// <summary>
        /// Lays flat ground across a new chunk, starting on the last point of the ground before
        /// it, so walking into the new chunk is seamless from the first step.
        /// </summary>
        public static GroundPath ContinueInto(float fromX, float toX)
        {
            var before = PathEndingNear(fromX + 0.5f, 6f);
            var start = before != null ? WorldEnd(before) : new Vector2(fromX, 0f);
            // Made just inside the new chunk so it is filed there, then reaching back to the seam.
            var path = Create(Mathf.Max(start.x, fromX) + 0.05f);
            if (path == null) return null;
            var origin = path.transform.position.x;
            SetPoints(path, new[] { new Vector2(start.x - origin, start.y), new Vector2(toX - origin, start.y) });
            return path;
        }

        /// <summary>
        /// Every place one path ends close to where the next begins but not on it gets the
        /// next one's first point moved onto the end of the first. Returns how many were joined.
        /// </summary>
        public static int JoinSeams(float within = 3f)
        {
            var paths = new System.Collections.Generic.List<GroundPath>(Object.FindObjectsByType<GroundPath>(FindObjectsSortMode.None));
            paths.RemoveAll(p => p == null || p.Points.Count < 2);
            paths.Sort((a, b) => WorldStart(a).x.CompareTo(WorldStart(b).x));
            var joined = 0;
            for (var i = 1; i < paths.Count; i++)
            {
                // The path that ends nearest before this one starts.
                var next = paths[i];
                var s = WorldStart(next);
                GroundPath prev = null;
                var best = within;
                for (var j = 0; j < paths.Count; j++)
                {
                    if (j == i) continue;
                    var e = WorldEnd(paths[j]);
                    var d = Vector2.Distance(e, s);
                    if (d < best && e.x <= s.x + 0.5f) { best = d; prev = paths[j]; }
                }
                if (prev == null || best < 0.01f) continue;
                var end = WorldEnd(prev);
                Undo.RecordObject(next, "Join ground seam");
                next.SetPoint(0, new Vector2(end.x - next.transform.position.x, end.y - next.transform.position.y));
                EditorUtility.SetDirty(next);
                EditorSceneManager.MarkSceneDirty(next.gameObject.scene);
                joined++;
            }
            return joined;
        }

        private static Vector2 WorldStart(GroundPath p) => (Vector2)p.transform.position + p.Points[0];
        private static Vector2 WorldEnd(GroundPath p) => (Vector2)p.transform.position + p.Points[p.Points.Count - 1];

        // The path whose last point is nearest to x from the left, within reach.
        private static GroundPath PathEndingNear(float x, float reach)
        {
            GroundPath best = null;
            var bestGap = reach;
            foreach (var p in Object.FindObjectsByType<GroundPath>(FindObjectsSortMode.None))
            {
                if (p == null || p.Points.Count < 2) continue;
                var gap = x - WorldEnd(p).x;
                if (gap < -0.5f || gap > bestGap) continue;
                bestGap = gap;
                best = p;
            }
            return best;
        }

        private static void SetPoints(GroundPath path, Vector2[] points)
        {
            var so = new SerializedObject(path);
            var list = so.FindProperty("points");
            list.arraySize = points.Length;
            for (var i = 0; i < points.Length; i++) list.GetArrayElementAtIndex(i).vector2Value = points[i];
            so.ApplyModifiedProperties();
            path.Rebuild();
            EditorUtility.SetDirty(path);
            EditorSceneManager.MarkSceneDirty(path.gameObject.scene);
        }

        [MenuItem("Weizza/Level/New Ground Path (hill or slope)", priority = 40)]
        public static void CreateAtSceneView()
        {
            var view = SceneView.lastActiveSceneView;
            var x = view != null ? view.pivot.x : 0f;
            Create(Mathf.Round(x * 10f) / 10f - 6f);
        }

        public static GroundPath Create(float startX)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                // Chunk scenes cannot be opened for editing in Play mode, and a path made
                // now would vanish when Play stops anyway.
                Debug.LogWarning("[Ground Path] Stop Play mode first: ground is laid out in edit mode.");
                return null;
            }

            var reference = NearestGround(startX);
            var parent = ParentAt(startX);

            var go = new GameObject("Ground Path");
            Undo.RegisterCreatedObjectUndo(go, "New ground path");
            if (parent != null)
            {
                EditorSceneManager.MoveGameObjectToScene(go, parent.gameObject.scene);
                go.transform.SetParent(parent, false);
            }
            var z = reference != null ? reference.transform.position.z + 0.05f : 0.3f;
            go.transform.position = new Vector3(startX, 0f, z);

            var path = go.AddComponent<GroundPath>();
            var strip = reference != null ? reference.sprite : AssetDatabase.LoadAssetAtPath<Sprite>(StripPath);
            var material = reference != null ? reference.sharedMaterial : null;
            var collider = reference != null ? reference.GetComponentInChildren<Collider>() : null;
            path.Setup(strip, material,
                collider != null ? collider.gameObject.layer : 0,
                reference != null ? reference.sortingLayerName : "Default",
                reference != null ? reference.sortingOrder + 1 : -4);

            if (reference == null)
                Debug.LogWarning("[Ground Path] No flat ground piece found nearby, so the material is empty. Drag the ground's material into the new path.", go);

            Selection.activeGameObject = go;
            EditorSceneManager.MarkSceneDirty(go.scene);
            return path;
        }

        /// <summary>The "Level/Art" folder of the chunk owning this x, the same place Map Painter files ground.</summary>
        private static Transform ParentAt(float x)
        {
            var chunk = ChunkPainting.ChunkAt(x, openIfClosed: true);
            if (chunk == null) return null;
            var at = ChunkPainting.Child(chunk.transform, "Level");
            return ChunkPainting.Child(at, "Art");
        }

        private static SpriteRenderer NearestGround(float x)
        {
            SpriteRenderer best = null;
            var bestDistance = float.MaxValue;
            foreach (var r in Object.FindObjectsByType<SpriteRenderer>(FindObjectsSortMode.None))
            {
                if (r.sprite == null || !r.sprite.name.StartsWith("L51_ground")) continue;
                var d = Mathf.Abs(r.bounds.center.x - x);
                if (d < bestDistance) { bestDistance = d; best = r; }
            }
            return best;
        }
    }
}
