using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Weizza.EditorTools
{
    /// <summary>
    /// Edit time questions about chunks, shared by Map Painter, the chunk splitter
    /// and World Map: which chunk covers this X, open it if it is closed, and
    /// where inside it a painted band lives.
    ///
    /// Runtime code finds chunks through RoomRoot.All, which only fills in Play
    /// mode, so the Editor searches scene roots instead.
    /// </summary>
    public static class ChunkPainting
    {
        public const string AtlasPath = "Assets/Resources/World/WorldAtlas.asset";
        public const string SceneryGroup = "Scenery";

        public static WorldAtlas Atlas => AssetDatabase.LoadAssetAtPath<WorldAtlas>(AtlasPath);

        public static RoomDefinition DefinitionAt(float x)
        {
            var atlas = Atlas;
            if (atlas == null) return null;
            foreach (var room in atlas.rooms)
                if (room != null && x >= room.worldRect.xMin && x < room.worldRect.xMax) return room;
            return null;
        }

        /// <summary>The chunk root covering x. Opens its scene additively when asked and needed.</summary>
        public static RoomRoot ChunkAt(float x, bool openIfClosed)
        {
            var def = DefinitionAt(x);
            if (def == null) return null;

            var root = FindLoaded(def);
            if (root != null || !openIfClosed) return root;

            var path = ScenePath(def.sceneName);
            if (path == null) return null;
            var scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
            return FindIn(scene);
        }

        public static RoomRoot FindLoaded(RoomDefinition def)
        {
            for (var i = 0; i < SceneManager.sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                if (!scene.isLoaded) continue;
                var root = FindIn(scene);
                if (root != null && root.Definition == def) return root;
            }
            return null;
        }

        public static RoomRoot FindIn(Scene scene)
        {
            if (!scene.IsValid() || !scene.isLoaded) return null;
            foreach (var go in scene.GetRootGameObjects())
            {
                var root = go.GetComponent<RoomRoot>();
                if (root != null) return root;
            }
            return null;
        }

        /// <summary>chunk/Scenery/name, created (with undo) when missing.</summary>
        public static Transform GroupIn(RoomRoot chunk, string name)
        {
            var scenery = Child(chunk.transform, SceneryGroup);
            return Child(scenery, name);
        }

        public static Transform Child(Transform parent, string name)
        {
            var existing = parent.Find(name);
            if (existing != null) return existing;
            var go = new GameObject(name);
            Undo.RegisterCreatedObjectUndo(go, "Create chunk group");
            go.transform.SetParent(parent, false);
            EditorSceneManager.MarkSceneDirty(parent.gameObject.scene);
            return go.transform;
        }

        public static string ScenePath(string sceneName)
        {
            foreach (var guid in AssetDatabase.FindAssets($"t:Scene {sceneName}"))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (Path.GetFileNameWithoutExtension(path) == sceneName) return path;
            }
            return null;
        }
    }
}
