using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Weizza.EditorTools
{
    /// <summary>
    /// Makes the Core scene out of an existing playable scene: copies it, then
    /// deletes the roots that are level content and keeps the ones that are the
    /// game's machinery. Every root is listed with a tick box and a guess, so
    /// nothing is deleted that was not looked at first. The source scene is
    /// never modified, only the copy.
    /// </summary>
    public sealed class CoreSceneBuilder : EditorWindow
    {
        // Roots that are machinery, not level. A name containing any of these is
        // ticked to keep. Everything else starts unticked.
        private static readonly string[] KeepHints =
        {
            "Player", "Camera", "CM_", "HUD", "Canvas", "Systems", "Narrative", "EventSystem",
            "Encounter Director", "Bootstrap", "Volume", "Grade", "Director", "Feel", "Fade",
        };

        private SceneAsset _source;
        private string _targetPath = "Assets/Scenes/Core.unity";
        private readonly List<(string name, bool keep)> _roots = new();
        private Vector2 _scroll;

        [MenuItem("Weizza/World/Build Core Scene", priority = 1)]
        private static void Open() => GetWindow<CoreSceneBuilder>("Core Scene");

        private void OnEnable()
        {
            _source = AssetDatabase.LoadAssetAtPath<SceneAsset>("Assets/Scenes/SampleScene.unity");
            ReadRoots();
        }

        private void ReadRoots()
        {
            _roots.Clear();
            if (_source == null) return;
            var path = AssetDatabase.GetAssetPath(_source);
            var scene = SceneManager.GetSceneByPath(path);
            var opened = false;
            if (!scene.isLoaded)
            {
                scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
                opened = true;
            }
            foreach (var root in scene.GetRootGameObjects()) _roots.Add((root.name, GuessKeep(root.name)));
            if (opened) EditorSceneManager.CloseScene(scene, true);
        }

        private static bool GuessKeep(string name)
        {
            foreach (var hint in KeepHints)
                if (name.IndexOf(hint, System.StringComparison.OrdinalIgnoreCase) >= 0) return true;
            return false;
        }

        private void OnGUI()
        {
            EditorGUILayout.HelpBox("Ticked roots are kept in Core. Unticked roots (level, scenery, enemies, cutscene props) are deleted from the COPY only.", MessageType.Info);
            EditorGUI.BeginChangeCheck();
            _source = (SceneAsset)EditorGUILayout.ObjectField("Copy from", _source, typeof(SceneAsset), false);
            if (EditorGUI.EndChangeCheck()) ReadRoots();
            _targetPath = EditorGUILayout.TextField("Save as", _targetPath);

            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            for (var i = 0; i < _roots.Count; i++)
            {
                var (name, keep) = _roots[i];
                var now = EditorGUILayout.ToggleLeft(name, keep);
                if (now != keep) _roots[i] = (name, now);
            }
            EditorGUILayout.EndScrollView();

            GUI.enabled = _source != null && _roots.Count > 0;
            GUI.backgroundColor = new Color(0.55f, 0.85f, 0.6f);
            if (GUILayout.Button("Build Core Scene", GUILayout.Height(32))) Build();
            GUI.backgroundColor = Color.white;
            GUI.enabled = true;
        }

        private void Build()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            if (File.Exists(_targetPath) &&
                !EditorUtility.DisplayDialog("Core Scene", $"{_targetPath} exists. Replace it?", "Replace", "Cancel")) return;

            var sourcePath = AssetDatabase.GetAssetPath(_source);
            Directory.CreateDirectory(Path.GetDirectoryName(_targetPath)!);
            AssetDatabase.DeleteAsset(_targetPath);
            if (!AssetDatabase.CopyAsset(sourcePath, _targetPath)) { Debug.LogError("[Core Scene] Copy failed."); return; }

            var scene = EditorSceneManager.OpenScene(_targetPath, OpenSceneMode.Single);
            var deleted = 0;
            foreach (var root in scene.GetRootGameObjects())
            {
                var entry = _roots.Find(r => r.name == root.name);
                if (entry.name != null && !entry.keep) { DestroyImmediate(root); deleted++; }
            }
            EditorSceneManager.SaveScene(scene);

            var atlas = AssetDatabase.LoadAssetAtPath<WorldAtlas>("Assets/Resources/World/WorldAtlas.asset");
            if (atlas != null)
            {
                atlas.coreScene = Path.GetFileNameWithoutExtension(_targetPath);
                EditorUtility.SetDirty(atlas);
                AssetDatabase.SaveAssets();
            }
            Debug.Log($"[Core Scene] Built {_targetPath}, removed {deleted} level root(s). Check the Player has nothing left to stand on, that is expected: rooms bring the ground.");
        }
    }
}
