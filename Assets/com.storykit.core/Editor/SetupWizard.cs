using System.IO;
using System.Linq;
using StoryKit.Bridge;
using StoryKit.Defaults;
using StoryKit.Flow;
using StoryKit.Quest;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace StoryKit.Editor
{
    /// <summary>
    /// Gets a project from nothing to a scene you can press Play on.
    ///
    /// Tools > StoryKit > Setup
    ///
    /// It checks what is missing, says so in plain words, then builds a scene
    /// for the kind of game you say you are making: a camera set up the right
    /// way, a player that moves, and an NPC you can walk up to and talk to.
    /// Everything it makes is ordinary Unity objects you can pull apart.
    /// </summary>
    public sealed class SetupWizard : EditorWindow
    {
        /// <summary>The shape of the game, which decides the camera and the moves.</summary>
        private enum Genre
        {
            TopDownStory,
            SurvivalHorror,
            FirstPersonStory,
            SideScroller,
            VisualNovel,
        }

        private Genre _genre = Genre.TopDownStory;
        private string _folder = "Assets/StoryKit";
        private bool _createPlayer = true;
        private bool _createNpc = true;
        private bool _createQuest = true;
        private Vector2 _scroll;

        [MenuItem("Tools/StoryKit/Setup", priority = 0)]
        public static void Open()
        {
            var window = GetWindow<SetupWizard>(true, "StoryKit Setup", true);
            window.minSize = new Vector2(460, 520);
            window.Show();
        }

        private void OnGUI()
        {
            _scroll = EditorGUILayout.BeginScrollView(_scroll);

            EditorGUILayout.Space(6);
            GUILayout.Label("StoryKit Setup", EditorStyles.largeLabel);
            EditorGUILayout.LabelField(
                "Answer one question, get a scene that plays.", StoryKitStyles.Summary);

            EditorGUILayout.Space(6);
            bool ready = DrawRequirements();

            EditorGUILayout.Space(6);
            DrawGenre();

            EditorGUILayout.Space(6);
            StoryKitStyles.SectionTitle("What to build", StoryKitStyles.Success);
            _folder = EditorGUILayout.TextField("Asset folder", _folder);
            _createPlayer = EditorGUILayout.Toggle("Player you can move", _createPlayer);
            _createNpc = EditorGUILayout.Toggle("NPC you can talk to", _createNpc);
            _createQuest = EditorGUILayout.Toggle("Example quest", _createQuest);

            EditorGUILayout.Space(10);
            using (new EditorGUI.DisabledScope(!ready))
            {
                if (GUILayout.Button("Build this scene", GUILayout.Height(36)))
                    Build();
            }

            if (!ready)
                EditorGUILayout.LabelField(
                    "Fix the items marked above first.", StoryKitStyles.Summary);

            EditorGUILayout.Space(8);
            DrawSceneStatus();

            EditorGUILayout.EndScrollView();
        }

        // ---------------- Requirements ----------------

        /// <summary>Everything that has to be true before anything can work.</summary>
        private static bool DrawRequirements()
        {
            StoryKitStyles.SectionTitle("Requirements", StoryKitStyles.Accent);

            bool hasUniTask = HasAssembly("UniTask");
            Requirement(hasUniTask, "UniTask installed",
                "Package Manager > + > Add package from git URL, then paste the link below.",
                copy: "https://github.com/Cysharp/UniTask.git?path=src/UniTask/Assets/Plugins/UniTask");

            bool hasInput = HasInputBackend();
            Requirement(hasInput, "An input system this can read",
                "Project Settings > Player > Active Input Handling. " +
                "Either option works, and so does Both.");

            // Cinemachine is a nice to have, never a blocker: the built in
            // camera covers every camera node without it.
            bool hasCinemachine = HasAssembly("Unity.Cinemachine");
            Optional(hasCinemachine, "Cinemachine",
                hasCinemachine
                    ? "Installed, so Cinemachine cameras will be used."
                    : "Not installed, so the built in Simple Camera is used instead. " +
                      "Every camera node still works. Install Cinemachine when the shots " +
                      "start to matter and nothing in your graphs has to change.");

            return hasUniTask && hasInput;
        }

        private static void Requirement(bool ok, string label, string help, string copy = null)
        {
            StoryKitStyles.StatusLine(ok, label, label + " is missing");
            if (ok) return;

            EditorGUILayout.LabelField(help, StoryKitStyles.Summary);
            if (copy != null && GUILayout.Button("Copy the URL", GUILayout.Width(110)))
                EditorGUIUtility.systemCopyBuffer = copy;
        }

        private static void Optional(bool present, string label, string note)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                var box = GUILayoutUtility.GetRect(18, 16, GUILayout.Width(18));
                StoryKitStyles.DrawPill(box, present ? "+" : "-",
                    present ? StoryKitStyles.Success : StoryKitStyles.Muted);
                EditorGUILayout.LabelField(label, EditorStyles.miniBoldLabel);
            }
            EditorGUILayout.LabelField(note, StoryKitStyles.Summary);
        }

        // ---------------- Genre ----------------

        private void DrawGenre()
        {
            StoryKitStyles.SectionTitle("What kind of game", new Color(0.40f, 0.70f, 0.95f));

            _genre = (Genre)EditorGUILayout.EnumPopup("Genre", _genre);
            EditorGUILayout.LabelField(GenreHelp(_genre), StoryKitStyles.Summary);
        }

        private static string GenreHelp(Genre genre) => genre switch
        {
            Genre.TopDownStory =>
                "Angled camera following the player, like a life sim or a tactics game. " +
                "Walk with WASD, talk with E. The pointer stays visible but cannot " +
                "leave the window.",
            Genre.SurvivalHorror =>
                "The camera stays put and turns to watch, the way Resident Evil and " +
                "Signalis do. The pointer is hidden while playing and comes back for " +
                "the item screen.",
            Genre.FirstPersonStory =>
                "Camera at the player's eyes, and the pointer is hidden and locked " +
                "so mouse movement can look around. Dialogue and menus bring it back " +
                "on their own.",
            Genre.SideScroller =>
                "Flat side view following the player horizontally.",
            Genre.VisualNovel =>
                "No world camera and no player. The graph starts on its own and the " +
                "whole game is the dialogue box.",
            _ => string.Empty,
        };

        /// <summary>
        /// What the pointer does while playing this kind of game. Getting this
        /// wrong is one of the first things that feels broken: a first person
        /// game with a loose cursor cannot look around, and a point and click
        /// game with a hidden one cannot be played at all.
        /// </summary>
        private static StoryUi.CursorMode CursorFor(Genre genre) => genre switch
        {
            Genre.FirstPersonStory => StoryUi.CursorMode.Locked,
            Genre.TopDownStory => StoryUi.CursorMode.Confined,
            Genre.SurvivalHorror => StoryUi.CursorMode.Locked,
            Genre.SideScroller => StoryUi.CursorMode.Confined,
            _ => StoryUi.CursorMode.Visible,
        };

        private static SimpleCamera.Style StyleFor(Genre genre) => genre switch
        {
            Genre.TopDownStory => SimpleCamera.Style.Isometric,
            Genre.SurvivalHorror => SimpleCamera.Style.FixedAngle,
            Genre.FirstPersonStory => SimpleCamera.Style.FirstPerson,
            Genre.SideScroller => SimpleCamera.Style.SideScroller,
            _ => SimpleCamera.Style.ThirdPerson,
        };

        // ---------------- Build ----------------

        private void Build()
        {
            Directory.CreateDirectory(_folder);
            AssetDatabase.Refresh();

            Undo.SetCurrentGroupName("StoryKit Setup");
            int group = Undo.GetCurrentGroup();

            var localization = LoadOrCreate<LocalizationTableAsset>(
                $"{_folder}/LocalizationTable.asset");
            var characters = LoadOrCreate<CharacterDatabaseAsset>(
                $"{_folder}/CharacterDatabase.asset");
            var items = LoadOrCreate<ItemDatabaseAsset>($"{_folder}/ItemDatabase.asset");
            var graph = BuildGraph($"{_folder}/HelloConversation.asset");

            var kit = BuildGameKit(localization, characters, items, CursorFor(_genre));

            if (_createQuest) AttachQuest(kit);

            Transform player = _createPlayer && _genre != Genre.VisualNovel
                ? BuildPlayer()
                : null;

            if (_genre != Genre.VisualNovel) BuildCamera(player);
            if (_createNpc && _genre != Genre.VisualNovel) BuildNpc(graph);

            if (_genre == Genre.VisualNovel)
            {
                // Nothing to walk into, so the story has to start itself.
                var serialized = new SerializedObject(kit);
                Assign(serialized, "_startupGraph", graph);
                serialized.ApplyModifiedProperties();
            }

            Undo.CollapseUndoOperations(group);
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            AssetDatabase.SaveAssets();

            Selection.activeObject = graph;
            EditorGUIUtility.PingObject(graph);

            EditorUtility.DisplayDialog("StoryKit", Summary(), "Got it");
        }

        private string Summary() => _genre switch
        {
            Genre.VisualNovel =>
                "Scene is ready.\n\nPress Play: the conversation starts on its own.\n\n" +
                "Double-click HelloConversation to write the dialogue.",
            Genre.SurvivalHorror =>
                "Scene is ready.\n\nPress Play, walk with WASD, press E at the cube.\n" +
                "The camera stays put and turns to watch, like a horror room.\n\n" +
                "J opens the quest log.",
            _ =>
                "Scene is ready.\n\nPress Play, walk with WASD, press E at the cube.\n\n" +
                "Double-click HelloConversation to write the dialogue.",
        };

        // ---------------- Pieces ----------------

        private static GameKit BuildGameKit(LocalizationTableAsset localization,
            CharacterDatabaseAsset characters, ItemDatabaseAsset items,
            StoryUi.CursorMode cursorMode)
        {
            var kit = FindFirst<GameKit>();
            if (kit == null)
            {
                var host = new GameObject("StoryKit");
                Undo.RegisterCreatedObjectUndo(host, "Create StoryKit");
                kit = host.AddComponent<GameKit>();
                host.AddComponent<PauseManager>();
            }

            var serialized = new SerializedObject(kit);
            Assign(serialized, "_localization", localization);
            Assign(serialized, "_characters", characters);
            Assign(serialized, "_items", items);

            var cursor = serialized.FindProperty("_cursor");
            if (cursor != null) cursor.enumValueIndex = (int)cursorMode;

            serialized.ApplyModifiedProperties();
            return kit;
        }

        private void AttachQuest(GameKit kit)
        {
            var quest = BuildQuest($"{_folder}/ExampleQuest.asset");

            var system = kit.GetComponent<QuestSystem>()
                         ?? Undo.AddComponent<QuestSystem>(kit.gameObject);

            var serialized = new SerializedObject(system);
            var list = serialized.FindProperty("_quests");
            if (list == null || list.arraySize > 0) return;

            list.arraySize = 1;
            list.GetArrayElementAtIndex(0).objectReferenceValue = quest;
            serialized.ApplyModifiedProperties();
        }

        private static void Assign(SerializedObject serialized, string field, Object value)
        {
            var property = serialized.FindProperty(field);
            if (property != null) property.objectReferenceValue = value;
        }

        private Transform BuildPlayer()
        {
            var existing = GameObject.FindGameObjectWithTag("Player");
            if (existing != null) return existing.transform;

            var player = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            Undo.RegisterCreatedObjectUndo(player, "Create Player");
            player.name = "Player";
            player.transform.position = new Vector3(0, 1, -4);

            if (!UnityEditorInternal.InternalEditorUtility.tags.Contains("Player"))
                UnityEditorInternal.InternalEditorUtility.AddTag("Player");
            player.tag = "Player";

            var body = player.AddComponent<Rigidbody>();
            body.isKinematic = false;
            body.useGravity = true;
            body.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;

            var mover = player.AddComponent<AdvancedPlayerController>();
            if (_genre == Genre.TopDownStory || _genre == Genre.SideScroller)
                mover.Type = AdvancedPlayerController.MovementType.TopDown;
            else if (_genre == Genre.SurvivalHorror)
                mover.Type = AdvancedPlayerController.MovementType.TankControls;
            else
                mover.Type = AdvancedPlayerController.MovementType.FirstPerson;

            player.AddComponent<SimpleActor>();
            player.AddComponent<SimpleAnimationController>();

            return player.transform;
        }

        private void BuildCamera(Transform player)
        {
            var camera = Camera.main;
            if (camera == null)
            {
                var host = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener));
                Undo.RegisterCreatedObjectUndo(host, "Create Camera");
                host.tag = "MainCamera";
                camera = host.GetComponent<Camera>();
            }

            var simple = camera.GetComponent<SimpleCamera>()
                         ?? Undo.AddComponent<SimpleCamera>(camera.gameObject);

            var serialized = new SerializedObject(simple);
            var style = serialized.FindProperty("_style");
            if (style != null) style.enumValueIndex = (int)StyleFor(_genre);

            var target = serialized.FindProperty("_target");
            if (target != null && player != null) target.objectReferenceValue = player;
            serialized.ApplyModifiedProperties();

            // A fixed camera has to start somewhere sensible or it looks at the floor.
            if (StyleFor(_genre) == SimpleCamera.Style.FixedAngle)
            {
                camera.transform.position = new Vector3(6f, 5f, -6f);
                if (player != null) camera.transform.LookAt(player.position + Vector3.up);
            }
            else if (StyleFor(_genre) == SimpleCamera.Style.FirstPerson && player != null)
            {
                camera.transform.SetParent(player);
                camera.transform.localPosition = new Vector3(0f, 0.6f, 0f);
                camera.transform.localRotation = Quaternion.identity;
                
                var mover = player.GetComponent<AdvancedPlayerController>();
                if (mover != null) mover.CameraTransform = camera.transform;

                // Add FPS interaction & crosshair
                player.gameObject.AddComponent<FirstPersonInteractor>();
                
                var canvasObj = new GameObject("CrosshairCanvas", typeof(Canvas), typeof(UnityEngine.UI.CanvasScaler));
                canvasObj.transform.SetParent(player);
                var canvas = canvasObj.GetComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                
                var crosshairImgObj = new GameObject("Crosshair", typeof(UnityEngine.UI.Image));
                crosshairImgObj.transform.SetParent(canvasObj.transform, false);
                crosshairImgObj.AddComponent<DynamicCrosshair>();
            }
        }

        private static void BuildNpc(FlowGraphAsset graph)
        {
            var npc = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Undo.RegisterCreatedObjectUndo(npc, "Create NPC");
            npc.name = "NPC (talk to me)";
            npc.transform.position = new Vector3(0, 0.5f, 2);

            // Turning to face each other is the difference between a
            // conversation and two strangers talking at a wall.
            npc.AddComponent<ConversationStaging>();
            npc.AddComponent<SimpleActor>();
            npc.AddComponent<SimpleAnimationController>();

            var stage = npc.AddComponent<StoryStageId>();
            var stageSerialized = new SerializedObject(stage);
            var id = stageSerialized.FindProperty("_id");
            if (id != null) id.stringValue = "npc";
            stageSerialized.ApplyModifiedProperties();

            var range = new GameObject("TalkRange", typeof(SphereCollider));
            range.transform.SetParent(npc.transform, false);

            var collider = range.GetComponent<SphereCollider>();
            collider.isTrigger = true;
            collider.radius = 2.5f;

            var trigger = range.AddComponent<StoryGraphTrigger>();
            var serialized = new SerializedObject(trigger);
            Assign(serialized, "_graph", graph);

            var fireOn = serialized.FindProperty("_fireOn");
            if (fireOn != null)
                fireOn.enumValueIndex = (int)StoryGraphTrigger.FireMode.Interact;
            serialized.ApplyModifiedProperties();
        }

        // ---------------- Assets ----------------

        private FlowGraphAsset BuildGraph(string path)
        {
            var existing = AssetDatabase.LoadAssetAtPath<FlowGraphAsset>(path);
            if (existing != null) return existing;

            var graph = ScriptableObject.CreateInstance<FlowGraphAsset>();

            var start = graph.AddNode(new StartNode { Position = new Vector2(0, 0) });
            var hello = graph.AddNode(new SayNode
            {
                Position = new Vector2(240, 0),
                SpeakerId = "npc",
                FallbackText = "It works. Open this graph and write something better.",
                Shot = _genre == Genre.VisualNovel ? ShotFraming.Default : ShotFraming.Medium,
            });
            var end = graph.AddNode(new EndNode { Position = new Vector2(480, 0) });

            graph.SetEntry(start).Connect(start, hello).Connect(hello, end);

            AssetDatabase.CreateAsset(graph, path);
            return graph;
        }

        private static QuestDefinition BuildQuest(string path)
        {
            var existing = AssetDatabase.LoadAssetAtPath<QuestDefinition>(path);
            if (existing != null) return existing;

            var quest = ScriptableObject.CreateInstance<QuestDefinition>();
            quest.questId = "say_hello";
            quest.titleFallback = "Say hello";
            quest.descriptionFallback = "Walk up to the cube and press E.";
            quest.autoStart = true;
            quest.objectives.Add(new QuestDefinition.Objective
            {
                id = "talk",
                fallbackText = "Talk to the NPC",
            });

            AssetDatabase.CreateAsset(quest, path);
            return quest;
        }

        private static T LoadOrCreate<T>(string path) where T : ScriptableObject
        {
            var existing = AssetDatabase.LoadAssetAtPath<T>(path);
            if (existing != null) return existing;

            var created = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(created, path);
            return created;
        }

        // ---------------- Scene status ----------------

        private static void DrawSceneStatus()
        {
            StoryKitStyles.SectionTitle("This scene right now", StoryKitStyles.Muted);

            Line(FindFirst<GameKit>() != null, "Game Kit");
            Line(GameObject.FindGameObjectWithTag("Player") != null, "Something tagged Player");
            Line(FindFirst<SimpleCamera>() != null, "A camera StoryKit can drive");
            Line(FindFirst<StoryGraphTrigger>() != null || FindFirst<FlowRunner>() != null,
                "Something that plays a graph");
            Line(FindFirst<QuestSystem>() != null, "Quest System (only needed for quests)");
        }

        private static void Line(bool ok, string label)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                var box = GUILayoutUtility.GetRect(18, 16, GUILayout.Width(18));
                StoryKitStyles.DrawPill(box, ok ? "+" : "-",
                    ok ? StoryKitStyles.Success : StoryKitStyles.Muted);
                EditorGUILayout.LabelField(label);
            }
        }

        // ---------------- Helpers ----------------

        private static T FindFirst<T>() where T : Object =>
            Object.FindObjectsByType<T>(FindObjectsSortMode.None).FirstOrDefault();

        private static bool HasAssembly(string name) =>
            System.AppDomain.CurrentDomain.GetAssemblies()
                .Any(assembly => assembly.GetName().Name == name);

        private static bool HasInputBackend()
        {
#if ENABLE_INPUT_SYSTEM || ENABLE_LEGACY_INPUT_MANAGER
            return true;
#else
            return false;
#endif
        }
    }
}
