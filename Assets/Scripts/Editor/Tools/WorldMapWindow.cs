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
    /// The whole world on one page. Every room is drawn as its real rectangle,
    /// links between rooms as lines, and the buttons down the side do the jobs
    /// that otherwise take a dozen clicks each and fail silently when one is
    /// missed: a new room scene with its root, bounds, camera box, spawn and
    /// gates already wired; adding every room to Build Profiles; opening a room
    /// together with its neighbours so a seamless border can be built across.
    /// </summary>
    public sealed class WorldMapWindow : EditorWindow
    {
        private const string AtlasPath = "Assets/Resources/World/WorldAtlas.asset";
        private const string RoomFolder = "Assets/GameData/World/Rooms";
        private const string SceneFolder = "Assets/Scenes/World";

        private WorldAtlas _atlas;
        private RoomDefinition _selected;
        private Vector2 _pan = new(40f, 300f);
        private float _zoom = 4f;
        private Vector2 _sideScroll;

        private string _newId = "forest_02";
        private string _newArea = "forest";
        private Vector2 _newSize = new(40f, 16f);
        private RoomLink _newLink = RoomLink.Seamless;

        [MenuItem("Weizza/World/World Map", priority = 0)]
        private static void Open() => GetWindow<WorldMapWindow>("World Map").Show();

        private void OnEnable()
        {
            _atlas = AssetDatabase.LoadAssetAtPath<WorldAtlas>(AtlasPath);
            wantsMouseMove = false;
        }

        private void OnGUI()
        {
            const float side = 300f;
            var mapRect = new Rect(0f, 0f, position.width - side, position.height);
            var sideRect = new Rect(position.width - side, 0f, side, position.height);

            DrawMap(mapRect);
            GUILayout.BeginArea(sideRect, EditorStyles.helpBox);
            _sideScroll = GUILayout.BeginScrollView(_sideScroll);
            DrawSide();
            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        // ---- Map canvas ----------------------------------------------------------------

        private Vector2 ToScreen(Vector2 world) => new(_pan.x + world.x * _zoom, _pan.y - world.y * _zoom);
        private Vector2 ToWorld(Vector2 screen) => new((screen.x - _pan.x) / _zoom, (_pan.y - screen.y) / _zoom);

        private void DrawMap(Rect area)
        {
            EditorGUI.DrawRect(area, new Color(0.11f, 0.12f, 0.12f));
            GUI.BeginClip(area);

            DrawGrid(area);

            if (_atlas != null)
            {
                Handles.BeginGUI();
                foreach (var room in _atlas.rooms)
                {
                    if (room == null) continue;
                    foreach (var link in room.neighbours)
                    {
                        var other = link != null ? _atlas.Find(link.roomId) : null;
                        if (other == null) continue;
                        Handles.color = link.link == RoomLink.Gate ? new Color(1f, 0.8f, 0.3f, 0.9f) : new Color(0.6f, 0.9f, 1f, 0.6f);
                        Handles.DrawAAPolyLine(2f, ToScreen(room.worldRect.center), ToScreen(other.worldRect.center));
                    }
                }
                Handles.EndGUI();

                foreach (var room in _atlas.rooms)
                {
                    if (room == null) continue;
                    var r = room.worldRect;
                    var a = ToScreen(new Vector2(r.xMin, r.yMax));
                    var b = ToScreen(new Vector2(r.xMax, r.yMin));
                    var rect = Rect.MinMaxRect(a.x, a.y, b.x, b.y);
                    var fill = room.mapColour;
                    fill.a = room == _selected ? 0.6f : 0.3f;
                    EditorGUI.DrawRect(rect, fill);
                    Handles.BeginGUI();
                    Handles.color = room == _selected ? Color.white : new Color(1f, 1f, 1f, 0.5f);
                    Handles.DrawSolidRectangleWithOutline(rect, Color.clear, Handles.color);
                    Handles.EndGUI();
                    GUI.Label(new Rect(rect.x + 4, rect.y + 2, Mathf.Max(60, rect.width), 18), room.id, EditorStyles.whiteMiniLabel);
                    if (!SceneInBuild(room.sceneName))
                        GUI.Label(new Rect(rect.x + 4, rect.y + 16, 160, 18), "not in build", EditorStyles.miniBoldLabel);
                }
            }
            else
            {
                GUI.Label(new Rect(20, 20, 400, 20), "No World Atlas yet. Press Create Atlas on the right.", EditorStyles.whiteLabel);
            }

            HandleInput(area);
            GUI.EndClip();
        }

        private void DrawGrid(Rect area)
        {
            // A line every 10 m, heavier every 50, so room sizes can be read off the map.
            Handles.BeginGUI();
            var min = ToWorld(Vector2.zero);
            var max = ToWorld(new Vector2(area.width, area.height));
            for (var x = Mathf.Floor(min.x / 10f) * 10f; x < max.x; x += 10f)
            {
                Handles.color = Mathf.Approximately(x % 50f, 0f) ? new Color(1, 1, 1, 0.08f) : new Color(1, 1, 1, 0.03f);
                var sx = ToScreen(new Vector2(x, 0)).x;
                Handles.DrawLine(new Vector3(sx, 0), new Vector3(sx, area.height));
            }
            for (var y = Mathf.Floor(max.y / 10f) * 10f; y < min.y; y += 10f)
            {
                Handles.color = Mathf.Approximately(y % 50f, 0f) ? new Color(1, 1, 1, 0.08f) : new Color(1, 1, 1, 0.03f);
                var sy = ToScreen(new Vector2(0, y)).y;
                Handles.DrawLine(new Vector3(0, sy), new Vector3(area.width, sy));
            }
            Handles.EndGUI();
        }

        private void HandleInput(Rect area)
        {
            var e = Event.current;
            if (e.type == EventType.ScrollWheel)
            {
                var before = ToWorld(e.mousePosition);
                _zoom = Mathf.Clamp(_zoom * (e.delta.y > 0 ? 0.9f : 1.1f), 0.3f, 40f);
                var after = ToScreen(before);
                _pan += e.mousePosition - after;
                e.Use();
                Repaint();
            }
            else if (e.type == EventType.MouseDrag && (e.button == 2 || (e.button == 0 && e.alt)))
            {
                _pan += e.delta;
                e.Use();
                Repaint();
            }
            else if (e.type == EventType.MouseDown && e.button == 0 && !e.alt && _atlas != null)
            {
                var world = ToWorld(e.mousePosition);
                _selected = _atlas.FindAt(world);
                if (_selected != null)
                {
                    Selection.activeObject = _selected;
                    if (e.clickCount == 2) OpenRoom(_selected, withNeighbours: false);
                }
                e.Use();
                Repaint();
            }
        }

        // ---- Side panel ----------------------------------------------------------------

        private void DrawSide()
        {
            EditorGUILayout.LabelField("World Atlas", EditorStyles.boldLabel);
            _atlas = (WorldAtlas)EditorGUILayout.ObjectField(_atlas, typeof(WorldAtlas), false);
            if (_atlas == null)
            {
                if (GUILayout.Button("Create Atlas", GUILayout.Height(30))) CreateAtlas();
                return;
            }

            EditorGUILayout.HelpBox("Scroll to zoom. Middle drag or Alt drag to pan. Click a room to select, double click to open its scene.", MessageType.None);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Core scene", EditorStyles.boldLabel);
            EditorGUILayout.LabelField(_atlas.coreScene, SceneInBuild(_atlas.coreScene) ? "in build" : "NOT in build");
            if (GUILayout.Button("Use the open scene as Core"))
            {
                Undo.RecordObject(_atlas, "Core scene");
                _atlas.coreScene = SceneManager.GetActiveScene().name;
                EditorUtility.SetDirty(_atlas);
            }
            EditorGUILayout.HelpBox("Core holds the player, camera rig, HUD, dialogue canvas and services, and no level. Make it by saving a copy of SampleScene and deleting Level and Scenery from the copy.", MessageType.Info);

            EditorGUILayout.Space();
            if (_selected != null)
            {
                EditorGUILayout.LabelField("Selected: " + _selected.id, EditorStyles.boldLabel);
                EditorGUILayout.LabelField("Scene", _selected.sceneName);
                EditorGUILayout.LabelField("Size", $"{_selected.worldRect.width:0.#} x {_selected.worldRect.height:0.#} m");
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("Open")) OpenRoom(_selected, false);
                    if (GUILayout.Button("Open + neighbours")) OpenRoom(_selected, true);
                }
                if (GUILayout.Button("Start play here")) PlayFrom(_selected);
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("New room", EditorStyles.boldLabel);
            _newId = EditorGUILayout.TextField("Id", _newId);
            _newArea = EditorGUILayout.TextField("Area", _newArea);
            _newSize = EditorGUILayout.Vector2Field("Size (m)", _newSize);
            if (_atlas.linear)
            {
                var tail = LastChunk();
                EditorGUILayout.HelpBox(tail != null
                    ? $"Linear world: added to the end, right after {tail.id}, seamless. No gates."
                    : "Linear world: the first chunk, placed at x 0.", MessageType.None);
            }
            else
            {
                _newLink = (RoomLink)EditorGUILayout.EnumPopup("Link to selected", _newLink);
                EditorGUILayout.HelpBox(_selected != null
                    ? $"Placed to the right of {_selected.id} and linked to it."
                    : "Nothing selected: placed at the origin with no link.", MessageType.None);
            }
            GUI.backgroundColor = new Color(0.55f, 0.85f, 0.6f);
            if (GUILayout.Button("Create Room Scene", GUILayout.Height(32))) CreateRoom();
            GUI.backgroundColor = Color.white;

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Upkeep", EditorStyles.boldLabel);
            if (GUILayout.Button("Add Core and every room to Build Profiles")) FixBuildSettings(_atlas);
            if (GUILayout.Button("Find room definitions not in the atlas")) AdoptLooseRooms();
            if (GUILayout.Button("Validate")) Validate();
        }

        // ---- Actions -------------------------------------------------------------------

        private RoomDefinition LastChunk()
        {
            RoomDefinition last = null;
            foreach (var room in _atlas.rooms)
                if (room != null && (last == null || room.worldRect.xMax > last.worldRect.xMax)) last = room;
            return last;
        }

        private void CreateAtlas()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(AtlasPath)!);
            _atlas = CreateInstance<WorldAtlas>();
            AssetDatabase.CreateAsset(_atlas, AtlasPath);
            AssetDatabase.SaveAssets();
            Debug.Log("[World Map] Created " + AtlasPath + ". Streaming and WorldBoot switch on now that it exists.");
        }

        private void CreateRoom()
        {
            if (string.IsNullOrWhiteSpace(_newId)) return;
            if (_atlas.Find(_newId) != null) { EditorUtility.DisplayDialog("World Map", $"A room with id '{_newId}' already exists.", "OK"); return; }

            var sceneName = ToSceneName(_newId);
            Directory.CreateDirectory(RoomFolder);
            Directory.CreateDirectory(SceneFolder);

            // Linear: always the end of the line, whatever is selected, so a chunk can
            // never be inserted into the middle of the walk by accident.
            if (_atlas.linear)
            {
                _selected = LastChunk();
                _newLink = RoomLink.Seamless;
            }

            var origin = _selected != null
                ? new Vector2(_selected.worldRect.xMax, _selected.worldRect.yMin)
                : Vector2.zero;

            var def = CreateInstance<RoomDefinition>();
            def.id = _newId;
            def.areaId = _newArea;
            def.sceneName = sceneName;
            def.displayNameKey = "ROOM_" + _newId.ToUpperInvariant();
            def.worldRect = new Rect(origin, _newSize);
            def.mapColour = Color.HSVToRGB(Mathf.Abs(_newArea.GetHashCode() % 360) / 360f, 0.35f, 0.75f);
            AssetDatabase.CreateAsset(def, $"{RoomFolder}/ROOM_{_newId}.asset");

            if (_selected != null)
            {
                Undo.RecordObject(_selected, "Link rooms");
                _selected.neighbours = _selected.neighbours.Append(new RoomNeighbour { roomId = def.id, link = _newLink }).ToArray();
                def.neighbours = new[] { new RoomNeighbour { roomId = _selected.id, link = _newLink } };
                EditorUtility.SetDirty(_selected);
            }

            Undo.RecordObject(_atlas, "Add room");
            _atlas.rooms = _atlas.rooms.Append(def).ToArray();
            EditorUtility.SetDirty(_atlas);

            BuildRoomScene(def, sceneName, _selected);
            AssetDatabase.SaveAssets();
            FixBuildSettings(_atlas);
            _selected = def;
        }

        /// <summary>
        /// Adds one chunk to the end of a linear world: room asset, scene, atlas entry,
        /// build list. Map Painter's "Add chunk" button calls this, so extending the map
        /// never needs this window. Returns null when the world is not linear.
        /// </summary>
        public static RoomDefinition AppendChunk(string area, float width)
        {
            var atlas = AssetDatabase.LoadAssetAtPath<WorldAtlas>(AtlasPath);
            if (atlas == null || !atlas.linear) return null;

            RoomDefinition last = null;
            foreach (var room in atlas.rooms)
                if (room != null && (last == null || room.worldRect.xMax > last.worldRect.xMax)) last = room;

            area = string.IsNullOrWhiteSpace(area) ? "chunk" : area.Trim().Replace(' ', '_');
            var id = NextChunkId(atlas, area);

            Directory.CreateDirectory(RoomFolder);
            Directory.CreateDirectory(SceneFolder);
            var sceneName = ToSceneName(id);
            var y = last != null ? last.worldRect.yMin : -14f;
            var height = last != null ? last.worldRect.height : 40f;
            var x = last != null ? last.worldRect.xMax : 0f;

            var def = CreateInstance<RoomDefinition>();
            def.id = id;
            def.areaId = area;
            def.sceneName = sceneName;
            def.displayNameKey = "ROOM_" + id.ToUpperInvariant();
            def.worldRect = new Rect(x, y, width, height);
            def.mapColour = Color.HSVToRGB(Mathf.Abs(area.GetHashCode() % 360) / 360f, 0.35f, 0.75f);
            AssetDatabase.CreateAsset(def, $"{RoomFolder}/ROOM_{id}.asset");

            if (last != null)
            {
                Undo.RecordObject(last, "Link chunks");
                last.neighbours = last.neighbours.Append(new RoomNeighbour { roomId = id, link = RoomLink.Seamless }).ToArray();
                def.neighbours = new[] { new RoomNeighbour { roomId = last.id, link = RoomLink.Seamless } };
                EditorUtility.SetDirty(last);
            }

            Undo.RecordObject(atlas, "Add chunk");
            atlas.rooms = atlas.rooms.Append(def).ToArray();
            EditorUtility.SetDirty(atlas);

            BuildRoomScene(def, sceneName, last);
            AssetDatabase.SaveAssets();
            FixBuildSettings(atlas);
            AreaMoodSetup.EnsureArea(area);
            return def;
        }

        private static void BuildRoomScene(RoomDefinition def, string sceneName, RoomDefinition linkedFrom)
        {
            var path = $"{SceneFolder}/{sceneName}.unity";
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            var r = def.worldRect;

            var atlasForName = AssetDatabase.LoadAssetAtPath<WorldAtlas>(AtlasPath);
            var linearWorld = atlasForName != null && atlasForName.linear;
            var rootGo = new GameObject((linearWorld ? "Chunk " : "Room ") + def.id, typeof(BoxCollider));
            SceneManager.MoveGameObjectToScene(rootGo, scene);
            rootGo.transform.position = new Vector3(r.xMin, r.yMin, 0f);
            rootGo.layer = LayerMask.NameToLayer("Ignore Raycast");
            var box = rootGo.GetComponent<BoxCollider>();
            box.isTrigger = true;
            // A little extra width so the seams of neighbouring rooms overlap and
            // there is never a sliver of space that belongs to no room.
            box.size = new Vector3(r.width + 1f, r.height, 12f);
            box.center = new Vector3(r.width * 0.5f, r.height * 0.5f, 0f);
            var root = rootGo.AddComponent<RoomRoot>();

            // No ground is made here. The map is drawn with Map Painter, which paints
            // straight into this chunk once it exists, so a placeholder slab would
            // only be something to find and delete later.

            var spawn = new GameObject("Spawn").transform;
            spawn.SetParent(rootGo.transform, false);
            spawn.localPosition = new Vector3(3f, 2.1f, 0f);

            var levelRoot = new GameObject("Level").transform;
            levelRoot.SetParent(rootGo.transform, false);
            new GameObject("Scenery").transform.SetParent(rootGo.transform, false);
            new GameObject("Enemies").transform.SetParent(rootGo.transform, false);

            var so = new SerializedObject(root);
            so.FindProperty("definition").objectReferenceValue = def;
            so.FindProperty("defaultSpawn").objectReferenceValue = spawn;
            so.ApplyModifiedPropertiesWithoutUndo();

            // A linear world has one camera box for the whole line, made at runtime
            // (LinearCameraBounds). A box per chunk would stop the camera at every
            // seam and jump it when the next box takes over. Gates are not used
            // either: nothing in a linear world loads behind a fade except a death.
            var atlas = AssetDatabase.LoadAssetAtPath<WorldAtlas>(AtlasPath);
            if (atlas != null && atlas.linear)
            {
                EditorSceneManager.SaveScene(scene, path);
                EditorSceneManager.CloseScene(scene, true);
                Debug.Log($"[World Map] Built chunk {path}.");
                return;
            }

            // Camera bounds, half a screen inside on each side.
            var camGo = new GameObject("Camera Bounds", typeof(BoxCollider));
            camGo.transform.SetParent(rootGo.transform, false);
            camGo.layer = LayerMask.NameToLayer("Ignore Raycast");
            var camBox = camGo.GetComponent<BoxCollider>();
            camBox.isTrigger = true;
            var half = 3.6f * 16f / 9f;
            camBox.center = box.center;
            camBox.size = new Vector3(Mathf.Max(0.1f, r.width - half * 2f), 60f, 40f);
            so = new SerializedObject(root);
            so.FindProperty("cameraBounds").objectReferenceValue = camBox;
            so.ApplyModifiedPropertiesWithoutUndo();

            MakeGate(rootGo.transform, "left", RoomGate.Side.Left, new Vector3(0.5f, 2f, 0f), new Vector3(2f, 2.1f, 0f),
                linkedFrom != null ? linkedFrom.id : "", "right");
            MakeGate(rootGo.transform, "right", RoomGate.Side.Right, new Vector3(r.width - 0.5f, 2f, 0f), new Vector3(r.width - 2f, 2.1f, 0f), "", "left");

            EditorSceneManager.SaveScene(scene, path);
            EditorSceneManager.CloseScene(scene, true);
            Debug.Log($"[World Map] Built {path}. Gates are disabled until each names a real room; Seamless links need no gate.");
        }

        private static void MakeGate(Transform parent, string id, RoomGate.Side side, Vector3 local, Vector3 arrival, string targetRoom, string targetGate)
        {
            var go = new GameObject("Gate " + id, typeof(BoxCollider));
            go.transform.SetParent(parent, false);
            go.transform.localPosition = local;
            go.layer = LayerMask.NameToLayer("Ignore Raycast");
            var box = go.GetComponent<BoxCollider>();
            box.isTrigger = true;
            box.size = new Vector3(1f, 4f, 6f);
            box.center = new Vector3(0f, 0.1f, 0f);
            var gate = go.AddComponent<RoomGate>();
            var arrivalPoint = new GameObject("Arrival").transform;
            arrivalPoint.SetParent(parent, false);
            arrivalPoint.localPosition = arrival;
            arrivalPoint.SetParent(go.transform, true);

            var so = new SerializedObject(gate);
            so.FindProperty("gateId").stringValue = id;
            so.FindProperty("side").enumValueIndex = (int)side;
            so.FindProperty("arrivalPoint").objectReferenceValue = arrivalPoint;
            so.FindProperty("targetRoom").stringValue = targetRoom;
            so.FindProperty("targetGate").stringValue = targetGate;
            so.ApplyModifiedPropertiesWithoutUndo();

            // A gate with nowhere to go would fade to black and stay there.
            go.SetActive(!string.IsNullOrEmpty(targetRoom));
        }

        private static string ToSceneName(string id)
        {
            var parts = id.Split('_');
            for (var i = 0; i < parts.Length; i++)
                if (parts[i].Length > 0) parts[i] = char.ToUpperInvariant(parts[i][0]) + parts[i].Substring(1);
            return string.Join("_", parts);
        }

        private static string FindScenePath(string sceneName)
        {
            foreach (var guid in AssetDatabase.FindAssets($"t:Scene {sceneName}"))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (Path.GetFileNameWithoutExtension(path) == sceneName) return path;
            }
            return null;
        }

        private static bool SceneInBuild(string sceneName)
        {
            foreach (var s in EditorBuildSettings.scenes)
                if (s.enabled && Path.GetFileNameWithoutExtension(s.path) == sceneName) return true;
            return false;
        }

        /// <summary>
        /// The number after the highest one on the line, not the count of rooms.
        /// Counting meant a deleted chunk, or a stale atlas entry, pushed the next
        /// name up by one (forest_10 deleted, the next came out forest_12).
        /// </summary>
        public static string NextChunkId(WorldAtlas atlas, string area)
        {
            var highest = 0;
            foreach (var room in atlas.rooms)
            {
                if (room == null || string.IsNullOrEmpty(room.id)) continue;
                var cut = room.id.LastIndexOf('_');
                if (cut >= 0 && int.TryParse(room.id.Substring(cut + 1), out var n)) highest = Mathf.Max(highest, n);
            }
            var next = highest + 1;
            string id;
            do id = $"{area}_{next++:00}"; while (atlas.Find(id) != null || FindScenePath(ToSceneName(id)) != null);
            return id;
        }

        /// <summary>
        /// Removes a chunk completely: atlas entry, links from its neighbours, room
        /// asset, scene file and its place in the build list. Deleting only the scene
        /// file left the room in the atlas, which kept counting and kept streaming a
        /// scene that no longer existed.
        /// </summary>
        public static void DeleteChunk(RoomDefinition def)
        {
            var atlas = AssetDatabase.LoadAssetAtPath<WorldAtlas>(AtlasPath);
            if (atlas == null || def == null) return;

            var scenePath = FindScenePath(def.sceneName);
            if (scenePath != null)
            {
                var scene = SceneManager.GetSceneByPath(scenePath);
                if (scene.IsValid() && scene.isLoaded) EditorSceneManager.CloseScene(scene, true);
            }

            foreach (var room in atlas.rooms)
            {
                if (room == null || room == def || room.neighbours == null) continue;
                var kept = room.neighbours.Where(n => n == null || n.roomId != def.id).ToArray();
                if (kept.Length == room.neighbours.Length) continue;
                room.neighbours = kept;
                EditorUtility.SetDirty(room);
            }

            atlas.rooms = atlas.rooms.Where(r => r != null && r != def).ToArray();
            EditorUtility.SetDirty(atlas);

            AssetDatabase.DeleteAsset(AssetDatabase.GetAssetPath(def));
            if (scenePath != null) AssetDatabase.DeleteAsset(scenePath);
            AssetDatabase.SaveAssets();
            FixBuildSettings(atlas);
        }

        private static void FixBuildSettings(WorldAtlas atlas)
        {
            // Drop scenes whose file is gone first, so a deleted chunk does not
            // stay in the build list as a missing entry.
            var list = EditorBuildSettings.scenes.Where(s => File.Exists(s.path)).ToList();
            void Add(string sceneName)
            {
                if (string.IsNullOrEmpty(sceneName)) return;
                var path = FindScenePath(sceneName);
                if (path == null) return;
                var existing = list.FindIndex(s => s.path == path);
                if (existing >= 0) list[existing] = new EditorBuildSettingsScene(path, true);
                else list.Add(new EditorBuildSettingsScene(path, true));
            }
            Add(atlas.coreScene);
            foreach (var room in atlas.rooms) if (room != null) Add(room.sceneName);
            EditorBuildSettings.scenes = list.ToArray();
        }

        private void OpenRoom(RoomDefinition room, bool withNeighbours)
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            var path = FindScenePath(room.sceneName);
            if (path == null) { Debug.LogWarning($"[World Map] No scene named {room.sceneName}."); return; }
            EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
            if (!withNeighbours) return;
            foreach (var link in room.neighbours)
            {
                var other = link != null ? _atlas.Find(link.roomId) : null;
                var otherPath = other != null ? FindScenePath(other.sceneName) : null;
                if (otherPath != null) EditorSceneManager.OpenScene(otherPath, OpenSceneMode.Additive);
            }
        }

        private void PlayFrom(RoomDefinition room)
        {
            OpenRoom(room, false);
            EditorApplication.isPlaying = true;
        }

        private void AdoptLooseRooms()
        {
            var added = 0;
            foreach (var guid in AssetDatabase.FindAssets("t:RoomDefinition"))
            {
                var def = AssetDatabase.LoadAssetAtPath<RoomDefinition>(AssetDatabase.GUIDToAssetPath(guid));
                if (def == null || _atlas.rooms.Contains(def)) continue;
                Undo.RecordObject(_atlas, "Adopt rooms");
                _atlas.rooms = _atlas.rooms.Append(def).ToArray();
                added++;
            }
            EditorUtility.SetDirty(_atlas);
            Debug.Log($"[World Map] Added {added} room definition(s) to the atlas.");
        }

        /// <summary>
        /// A linear world is one unbroken walk: chunks sorted by X must meet edge to
        /// edge. A gap shows the void between two chunks; a big overlap means two
        /// chunks draw over the same ground and both load to cover it.
        /// </summary>
        private void ValidateLine(List<string> problems)
        {
            var line = new List<RoomDefinition>();
            foreach (var room in _atlas.rooms) if (room != null) line.Add(room);
            line.Sort((a, b) => a.worldRect.xMin.CompareTo(b.worldRect.xMin));

            for (var i = 1; i < line.Count; i++)
            {
                var prev = line[i - 1].worldRect;
                var next = line[i].worldRect;
                var gap = next.xMin - prev.xMax;
                if (gap > 0.6f) problems.Add($"Gap of {gap:0.0} m between {line[i - 1].id} and {line[i].id}. The player would see the void.");
                if (gap < -2f) problems.Add($"{line[i - 1].id} and {line[i].id} overlap by {-gap:0.0} m. Keep seams within half a metre.");
            }

            foreach (var room in line)
                foreach (var link in room.neighbours)
                    if (link != null && link.link == RoomLink.Gate)
                        problems.Add($"{room.id} has a Gate link to {link.roomId}. A linear world never fades to load; make it Seamless.");

            // Smaller chunks mean smaller loads: each one arrives in less time and
            // switches on in fewer frames. 30 to 60 m (two to five screens) is the target.
            foreach (var room in line)
                if (room.worldRect.width > 80f)
                    problems.Add($"{room.id} is {room.worldRect.width:0} m wide. Chunks over 80 m are heavy to load; split it in two.");
        }

        private void Validate()
        {
            var problems = new List<string>();
            if (!SceneInBuild(_atlas.coreScene)) problems.Add($"Core scene '{_atlas.coreScene}' is not in Build Profiles.");
            if (_atlas.Find(_atlas.startRoom) == null) problems.Add($"Start room '{_atlas.startRoom}' is not in the atlas.");
            var ids = new HashSet<string>();
            foreach (var room in _atlas.rooms)
            {
                if (room == null) { problems.Add("Empty slot in the room list."); continue; }
                if (!ids.Add(room.id)) problems.Add($"Duplicate room id {room.id}.");
                if (FindScenePath(room.sceneName) == null) problems.Add($"{room.id}: scene '{room.sceneName}' does not exist.");
                else if (!SceneInBuild(room.sceneName)) problems.Add($"{room.id}: scene not in Build Profiles.");
                foreach (var link in room.neighbours)
                {
                    var other = link != null ? _atlas.Find(link.roomId) : null;
                    if (other == null) { problems.Add($"{room.id}: neighbour '{link?.roomId}' missing."); continue; }
                    if (link.link == RoomLink.Seamless)
                    {
                        // Seamless rooms must actually touch, or the player walks into a void.
                        var a = room.worldRect; var b = other.worldRect;
                        var touch = a.xMax >= b.xMin - 0.6f && b.xMax >= a.xMin - 0.6f && a.yMax >= b.yMin - 0.6f && b.yMax >= a.yMin - 0.6f;
                        if (!touch) problems.Add($"{room.id} and {other.id} are linked Seamless but their rectangles do not touch. Make it a Gate link or move one.");
                    }
                }
            }

            if (_atlas.linear) ValidateLine(problems);

            if (problems.Count == 0) EditorUtility.DisplayDialog("World Map", "Everything checks out.", "OK");
            else
            {
                foreach (var p in problems) Debug.LogWarning("[World Map] " + p);
                EditorUtility.DisplayDialog("World Map", $"{problems.Count} problem(s), listed in the Console.", "OK");
            }
        }
    }
}
