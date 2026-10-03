using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;

namespace Weizza.EditorTools
{
    /// <summary>
    /// Keeps the whole map visible while editing. Opening the core scene opens every
    /// chunk with it, so the level looks like one scene in Edit mode and Map Painter,
    /// the eraser and selection all reach everything.
    ///
    /// Play mode then starts the way a build does: WorldBoot unloads every chunk
    /// outside the window around the player, and streams from there. When Play
    /// stops, Unity restores the scenes that were open before, so the full map comes
    /// back by itself.
    ///
    /// Ctrl+S saves every open scene, so painting across several chunks needs no
    /// extra step. Turn the auto open off from the menu if a lighter Editor is wanted.
    /// </summary>
    [InitializeOnLoad]
    public static class ChunkEditorSync
    {
        private const string PrefKey = "Weizza.AutoOpenChunks";
        private const string MenuPath = "Weizza/World/Open Every Chunk With Core";

        static ChunkEditorSync()
        {
            EditorSceneManager.sceneOpened -= OnSceneOpened;
            EditorSceneManager.sceneOpened += OnSceneOpened;
        }

        private static bool Enabled
        {
            get => EditorPrefs.GetBool(PrefKey, true);
            set => EditorPrefs.SetBool(PrefKey, value);
        }

        [MenuItem(MenuPath, priority = 4)]
        private static void Toggle()
        {
            Enabled = !Enabled;
            if (Enabled) OpenAll();
        }

        [MenuItem(MenuPath, true)]
        private static bool ToggleCheck()
        {
            Menu.SetChecked(MenuPath, Enabled);
            return true;
        }

        [MenuItem("Weizza/World/Open All Chunks", priority = 5)]
        public static void OpenAll()
        {
            var atlas = ChunkPainting.Atlas;
            if (atlas == null) return;
            foreach (var room in atlas.rooms)
            {
                if (room == null) continue;
                var path = ChunkPainting.ScenePath(room.sceneName);
                if (path == null) continue;
                var scene = SceneManager.GetSceneByPath(path);
                if (scene.isLoaded) continue;
                EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
            }
        }

        [MenuItem("Weizza/World/Close All Chunks", priority = 6)]
        private static void CloseAll()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            var atlas = ChunkPainting.Atlas;
            if (atlas == null) return;
            foreach (var room in atlas.rooms)
            {
                if (room == null) continue;
                var scene = SceneManager.GetSceneByName(room.sceneName);
                if (scene.IsValid() && scene.isLoaded && SceneManager.sceneCount > 1)
                    EditorSceneManager.CloseScene(scene, true);
            }
        }

        private static void OnSceneOpened(Scene scene, OpenSceneMode mode)
        {
            // Only when the core itself is opened as the main scene, never in Play
            // mode, and never while a chunk is being opened additively (or every
            // chunk would try to open every other chunk).
            if (!Enabled || EditorApplication.isPlayingOrWillChangePlaymode) return;
            if (mode != OpenSceneMode.Single) return;
            var atlas = ChunkPainting.Atlas;
            if (atlas == null || scene.name != atlas.coreScene) return;

            // Straight away, not on delayCall: delayCall only runs when the Editor
            // ticks, and an unfocused Editor can sit for a long time without a tick,
            // which left the map empty until something else woke it.
            OpenAll();
        }
    }
}
