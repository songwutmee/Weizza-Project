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
