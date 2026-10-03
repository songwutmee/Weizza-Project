using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Weizza.EditorTools
{
    /// <summary>
    /// Cuts a map that was painted as one scene into streamable chunks, the way an
    /// open world engine partitions its level (Unreal calls it World Partition,
    /// Unity people call it additive scene streaming).
    ///
    /// Works on a copy: the source scene is copied to Prefix_Core, and every piece
    /// of level content is moved out of that copy into the chunk scene covering its
    /// X position, under the same group path it had (so "Scenery/Map (GroundMoss)"
    /// stays "Scenery/Map (GroundMoss)" inside the chunk). What is not level content
    /// (player, camera, systems, UI) stays in the core copy. The source scene is
    /// never modified, so the whole thing can be thrown away and run again.
    ///
    /// Pieces wider than a chunk and a half (a long backdrop, a parallax strip) stay
    /// in the core, because cut by its centre such a piece would vanish while its
    /// far end is still on screen. Each one is listed.
    /// </summary>
    public sealed class ChunkSplitterWindow : EditorWindow
    {
        // Roots that are the game's machinery, never level content.
        private static readonly string[] KeepHints =
        {
            "Player", "Camera", "CM_", "View Rig", "HUD", "Canvas", "Systems", "Narrative", "EventSystem",
            "Encounter Director", "Bootstrap", "Volume", "Grade", "Director", "Feel", "Fade", "Stage",
            "Directional Light", "World Streamer",
        };

        private SceneAsset _source;
        private string _prefix = "Act0";
        private string _area = "forest";
        private float _chunkWidth = 40f;
        private bool _replaceAtlasRooms = true;
        private readonly List<(string name, bool split)> _roots = new();
        private string _preview = "";
        private Vector2 _scroll;

        // Set by automation (tests, editor scripts) to skip the closing dialog, which is modal
        // and would freeze anything driving the Editor from outside.
        private bool _quiet;

        [MenuItem("Weizza/World/Split Scene Into Chunks", priority = 3)]
        private static void Open() => GetWindow<ChunkSplitterWindow>("Chunk Splitter");

        private void OnEnable()
        {
            if (_source == null) _source = AssetDatabase.LoadAssetAtPath<SceneAsset>("Assets/Scenes/SampleScene.unity");
            ReadRoots();
        }

        private void OnGUI()
        {
            EditorGUILayout.HelpBox(
                "Cuts a painted map into chunks the game streams in and out. Runs on a COPY of the scene: " +
                "the source is never touched. Ticked roots are level content and get cut; unticked roots " +
                "stay in the core scene with the player.", MessageType.Info);

            EditorGUI.BeginChangeCheck();
            _source = (SceneAsset)EditorGUILayout.ObjectField("Source scene", _source, typeof(SceneAsset), false);
            if (EditorGUI.EndChangeCheck()) ReadRoots();
            _prefix = EditorGUILayout.TextField("Name prefix", _prefix);
            _area = EditorGUILayout.TextField("Area id", _area);
            _chunkWidth = EditorGUILayout.Slider("Chunk width (m)", _chunkWidth, 20f, 80f);
            EditorGUILayout.LabelField($"About {_chunkWidth / (3.6f * 2f * 16f / 9f):0.0} screens per chunk. 30 to 60 m is the target.",
                EditorStyles.miniLabel);
            _replaceAtlasRooms = EditorGUILayout.ToggleLeft("Replace the rooms in the World Atlas with these chunks", _replaceAtlasRooms);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Roots (ticked = cut into chunks)", EditorStyles.boldLabel);
            _scroll = EditorGUILayout.BeginScrollView(_scroll, GUILayout.MaxHeight(220));
            for (var i = 0; i < _roots.Count; i++)
            {
                var (name, split) = _roots[i];
                var now = EditorGUILayout.ToggleLeft(name, split);
                if (now != split) _roots[i] = (name, now);
            }
            EditorGUILayout.EndScrollView();

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Preview", GUILayout.Height(28))) Preview();
                GUI.backgroundColor = new Color(0.55f, 0.85f, 0.6f);
                if (GUILayout.Button("Split into chunks", GUILayout.Height(28))) Split();
                GUI.backgroundColor = Color.white;
            }

            if (!string.IsNullOrEmpty(_preview)) EditorGUILayout.HelpBox(_preview, MessageType.None);
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
            foreach (var root in scene.GetRootGameObjects())
                _roots.Add((root.name, !KeepHints.Any(h => root.name.IndexOf(h, System.StringComparison.OrdinalIgnoreCase) >= 0)));
            if (opened) EditorSceneManager.CloseScene(scene, true);
        }

        // ---- Pieces ------------------------------------------------------------------

        private struct Piece
        {
            public GameObject go;
            public Bounds bounds;
        }

        /// <summary>
        /// A piece is the first object down a branch that does something: has a
        /// renderer, collider or script of its own. Plain group objects above it are
        /// folders and get recreated by name inside each chunk.
        /// </summary>
        private static void Collect(Transform t, List<Piece> into)
        {
            var isFolder = t.GetComponents<Component>().Length == 1 && t.childCount > 0;
            if (isFolder)
            {
                foreach (Transform child in t) Collect(child, into);
                return;
            }
            into.Add(new Piece { go = t.gameObject, bounds = BoundsOf(t.gameObject) });
        }

        private static Bounds BoundsOf(GameObject go)
        {
            var any = false;
            var b = new Bounds(go.transform.position, Vector3.zero);
            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
            {
                if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
            }
            foreach (var c in go.GetComponentsInChildren<Collider>(true))
            {
                if (!any) { b = c.bounds; any = true; } else b.Encapsulate(c.bounds);
            }
            return b;
        }

        private List<Piece> Gather(Scene scene)
        {
            var pieces = new List<Piece>();
            foreach (var root in scene.GetRootGameObjects())
            {
                var entry = _roots.Find(r => r.name == root.name);
                if (entry.name == null || !entry.split) continue;
                Collect(root.transform, pieces);
            }
            return pieces;
        }

        private void Preview()
        {
            var path = AssetDatabase.GetAssetPath(_source);
            var scene = SceneManager.GetSceneByPath(path);
            var opened = false;
            if (!scene.isLoaded) { scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive); opened = true; }

            var pieces = Gather(scene);
            if (pieces.Count == 0) { _preview = "Nothing to cut: no ticked root holds any piece."; return; }
            Range(pieces, out var minX, out var maxX, out _, out _);
            var count = Mathf.Max(1, Mathf.CeilToInt((maxX - minX) / _chunkWidth));
            var wide = pieces.Count(p => p.bounds.size.x > _chunkWidth * 1.5f);
            _preview = $"{pieces.Count} pieces from x {minX:0} to {maxX:0} m, cut into {count} chunks of {_chunkWidth:0} m. " +
                       $"{wide} piece(s) are too wide to cut and stay in the core.";
            if (opened) EditorSceneManager.CloseScene(scene, true);
        }

        private static void Range(List<Piece> pieces, out float minX, out float maxX, out float minY, out float maxY)
        {
            minX = minY = float.MaxValue;
            maxX = maxY = float.MinValue;
            foreach (var p in pieces)
            {
                minX = Mathf.Min(minX, p.bounds.center.x);
                maxX = Mathf.Max(maxX, p.bounds.center.x);
                minY = Mathf.Min(minY, p.bounds.min.y);
                maxY = Mathf.Max(maxY, p.bounds.max.y);
            }
        }

        // ---- Splitting ---------------------------------------------------------------

        private void Split()
        {
            if (_source == null || string.IsNullOrWhiteSpace(_prefix)) return;
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            const string folder = "Assets/Scenes/World";
            Directory.CreateDirectory(folder);
            Directory.CreateDirectory("Assets/GameData/World/Rooms");
            var corePath = $"{folder}/{_prefix}_Core.unity";
            if (File.Exists(corePath) && !_quiet &&
                !EditorUtility.DisplayDialog("Chunk Splitter", $"{corePath} exists. Replace it and its chunks?", "Replace", "Cancel")) return;

            AssetDatabase.DeleteAsset(corePath);
            AssetDatabase.CopyAsset(AssetDatabase.GetAssetPath(_source), corePath);
            var core = EditorSceneManager.OpenScene(corePath, OpenSceneMode.Single);

            var pieces = Gather(core);
            if (pieces.Count == 0) { Debug.LogWarning("[Chunk Splitter] Nothing to cut."); return; }
            Range(pieces, out var minX, out var maxX, out var minY, out var maxY);
            var x0 = Mathf.Floor(minX);
            var count = Mathf.Max(1, Mathf.CeilToInt((maxX - x0 + 0.01f) / _chunkWidth));

            var defs = new RoomDefinition[count];
            var scenes = new Scene[count];
            var roots = new Transform[count];
            for (var i = 0; i < count; i++)
            {
                var id = $"{_prefix.ToLowerInvariant()}_{i + 1:00}";
                var sceneName = $"{_prefix}_{i + 1:00}";
                defs[i] = MakeDefinition(id, sceneName, new Rect(x0 + i * _chunkWidth, minY - 4f, _chunkWidth, (maxY - minY) + 8f));
                scenes[i] = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
                roots[i] = MakeRoot(scenes[i], defs[i]);

                // Saved straight away: Unity allows only one untitled scene open at
                // a time, so the next NewScene would fail while this one has no path.
                EditorSceneManager.SaveScene(scenes[i], $"{folder}/{sceneName}.unity");
            }

            var kept = new List<string>();
            var moved = 0;
            foreach (var piece in pieces)
            {
                if (piece.bounds.size.x > _chunkWidth * 1.5f)
                {
                    kept.Add($"{piece.go.name} ({piece.bounds.size.x:0} m wide)");
                    continue;
                }
                var index = Mathf.Clamp(Mathf.FloorToInt((piece.bounds.center.x - x0) / _chunkWidth), 0, count - 1);
                var group = GroupPath(piece.go.transform.parent, roots[index]);

                // An object moves between scenes only as a root, so it is lifted
                // out, moved, then hung back under the same path in the chunk.
                piece.go.transform.SetParent(null, true);
                SceneManager.MoveGameObjectToScene(piece.go, scenes[index]);
                piece.go.transform.SetParent(group, true);
                moved++;
            }

            RemoveEmptyFolders(core);
            var crossRefs = FindCrossSceneReferences(scenes);

            for (var i = 0; i < count; i++)
            {
                EditorSceneManager.SaveScene(scenes[i], $"{folder}/{defs[i].sceneName}.unity");
                EditorSceneManager.CloseScene(scenes[i], true);
            }
            EditorSceneManager.SaveScene(core);

            UpdateAtlas(defs, Path.GetFileNameWithoutExtension(corePath));

            var report = $"[Chunk Splitter] Moved {moved} pieces into {count} chunks ({_prefix}_01 to {_prefix}_{count:00}). " +
                         $"Core scene: {corePath}. Press Play in it to stream the map.";
            if (kept.Count > 0) report += "\nKept in the core because they are wider than a chunk and a half: " + string.Join(", ", kept);
            Debug.Log(report);
            foreach (var r in crossRefs) Debug.LogWarning("[Chunk Splitter] " + r);
            if (!_quiet) EditorUtility.DisplayDialog("Chunk Splitter",
                $"{moved} pieces into {count} chunks.\n{kept.Count} kept in the core (too wide).\n{crossRefs.Count} broken cross scene reference(s), listed in the Console.",
                "OK");
        }

        private RoomDefinition MakeDefinition(string id, string sceneName, Rect rect)
        {
            var path = $"Assets/GameData/World/Rooms/ROOM_{id}.asset";
            var def = AssetDatabase.LoadAssetAtPath<RoomDefinition>(path);
            if (def == null)
            {
                def = CreateInstance<RoomDefinition>();
                AssetDatabase.CreateAsset(def, path);
            }
            def.id = id;
            def.sceneName = sceneName;
            def.areaId = _area;
            def.displayNameKey = "ROOM_" + id.ToUpperInvariant();
            def.worldRect = rect;
            def.hiddenUntilVisited = true;
            def.mapColour = Color.HSVToRGB(Mathf.Abs(_area.GetHashCode() % 360) / 360f, 0.35f, 0.75f);
            def.neighbours = new RoomNeighbour[0];
            EditorUtility.SetDirty(def);
            return def;
        }

        private static Transform MakeRoot(Scene scene, RoomDefinition def)
        {
            var go = new GameObject("Chunk " + def.id, typeof(BoxCollider));
            SceneManager.MoveGameObjectToScene(go, scene);
            go.layer = LayerMask.NameToLayer("Ignore Raycast");
            var r = def.worldRect;
            go.transform.position = new Vector3(r.xMin, r.yMin, 0f);
            var box = go.GetComponent<BoxCollider>();
            box.isTrigger = true;
            // Half a metre past each side, so neighbouring chunks overlap and no
            // sliver of the line belongs to nothing.
            box.size = new Vector3(r.width + 1f, r.height, 12f);
            box.center = new Vector3(r.width * 0.5f, r.height * 0.5f, 0f);
            var root = go.AddComponent<RoomRoot>();
            var so = new SerializedObject(root);
            so.FindProperty("definition").objectReferenceValue = def;
            so.ApplyModifiedPropertiesWithoutUndo();
            return go.transform;
        }

        /// <summary>Recreates the folder path above a piece inside a chunk, by name.</summary>
        private static Transform GroupPath(Transform sourceParent, Transform chunkRoot)
        {
            var names = new List<string>();
            for (var t = sourceParent; t != null; t = t.parent) names.Add(t.name);
            names.Reverse();
            var at = chunkRoot;
            foreach (var n in names)
            {
                var next = at.Find(n);
                if (next == null)
                {
                    next = new GameObject(n).transform;
                    next.SetParent(at, false);
                }
                at = next;
            }
            return at;
        }

        private static void RemoveEmptyFolders(Scene scene)
        {
            foreach (var root in scene.GetRootGameObjects()) Prune(root.transform);
        }

        private static bool Prune(Transform t)
        {
            for (var i = t.childCount - 1; i >= 0; i--) Prune(t.GetChild(i));
            var empty = t.childCount == 0 && t.GetComponents<Component>().Length == 1;
            if (empty) DestroyImmediate(t.gameObject);
            return empty;
        }

        /// <summary>
        /// Unity cannot save a reference from one scene to an object in another; it
        /// silently becomes None. Listing them now means the designer can fix each
        /// one (usually by an id or an event instead) rather than discover it in play.
        /// </summary>
        private static List<string> FindCrossSceneReferences(Scene[] chunks)
        {
            var found = new List<string>();
            foreach (var scene in chunks)
            {
                foreach (var root in scene.GetRootGameObjects())
                {
                    foreach (var component in root.GetComponentsInChildren<Component>(true))
                    {
                        if (component == null) continue;
                        var so = new SerializedObject(component);
                        var p = so.GetIterator();
                        while (p.NextVisible(true))
                        {
                            if (p.propertyType != SerializedPropertyType.ObjectReference || p.objectReferenceValue == null) continue;
                            var target = p.objectReferenceValue;
                            var targetGo = target is Component c ? c.gameObject : target as GameObject;
                            if (targetGo == null || EditorUtility.IsPersistent(targetGo)) continue;
                            if (targetGo.scene != scene)
                                found.Add($"{scene.name}: {component.gameObject.name}.{component.GetType().Name}.{p.propertyPath} points at '{targetGo.name}' in {targetGo.scene.name}. It will be empty after saving.");
                        }
                    }
                }
            }
            return found;
        }

        private void UpdateAtlas(RoomDefinition[] defs, string coreScene)
        {
            var atlas = ChunkPainting.Atlas;
            if (atlas == null)
            {
                Directory.CreateDirectory("Assets/Resources/World");
                atlas = CreateInstance<WorldAtlas>();
                AssetDatabase.CreateAsset(atlas, ChunkPainting.AtlasPath);
            }
            Undo.RecordObject(atlas, "Chunk split");
            atlas.linear = true;
            atlas.coreScene = coreScene;
            atlas.startRoom = defs[0].id;
            atlas.rooms = _replaceAtlasRooms ? defs : atlas.rooms.Concat(defs).ToArray();
            EditorUtility.SetDirty(atlas);
            AssetDatabase.SaveAssets();

            var list = EditorBuildSettings.scenes.ToList();
            void Add(string sceneName)
            {
                var path = ChunkPainting.ScenePath(sceneName);
                if (path != null && list.All(s => s.path != path)) list.Add(new EditorBuildSettingsScene(path, true));
            }
            Add(coreScene);
            foreach (var d in defs) Add(d.sceneName);
            EditorBuildSettings.scenes = list.ToArray();
        }
    }
}
