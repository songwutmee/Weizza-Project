using System.Linq;
using Cysharp.Threading.Tasks;
using StoryKit.Bridge;
using StoryKit.Defaults;
using StoryKit.Flow;
using StoryKit.Fsm;
using StoryKit.Bt;
using StoryKit.Quest;
using UnityEditor;
using UnityEngine;

namespace StoryKit.Editor
{
    /// <summary>
    /// Custom inspectors for the scene components. Each one answers the two
    /// questions a default inspector cannot: "is this set up correctly?" and
    /// "what is it doing right now?".
    /// </summary>

    // ---------------- Story Graph Trigger ----------------

    [CustomEditor(typeof(StoryGraphTrigger))]
    public sealed class StoryGraphTriggerInspector : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            var trigger = (StoryGraphTrigger)target;
            serializedObject.Update();

            var graph = serializedObject.FindProperty("_graph");
            var fireOn = serializedObject.FindProperty("_fireOn");
            var playerTag = serializedObject.FindProperty("_playerTag");

            // --- setup validation: the three things people always miss ---
            var collider3D = trigger.GetComponent<Collider>();
            var collider2D = trigger.GetComponent<Collider2D>();
            bool hasTrigger = (collider3D != null && collider3D.isTrigger)
                              || (collider2D != null && collider2D.isTrigger);
            bool needsCollider = (StoryGraphTrigger.FireMode)fireOn.enumValueIndex
                                 != StoryGraphTrigger.FireMode.SceneStart;

            if (graph.objectReferenceValue == null)
                StoryKitStyles.StatusLine(false, "", "No graph assigned: nothing will play.");
            else if (needsCollider && !hasTrigger)
                StoryKitStyles.StatusLine(false, "",
                    "Needs a Collider with Is Trigger ticked to detect the player.");
            else
                StoryKitStyles.StatusLine(true,
                    $"Ready: plays '{graph.objectReferenceValue.name}'", "");

            StoryKitStyles.SectionTitle("What to play", StoryKitStyles.Accent);
            EditorGUILayout.PropertyField(graph, new GUIContent("Flow graph"));
            if (graph.objectReferenceValue != null &&
                GUILayout.Button("Open graph editor", GUILayout.Height(20)))
                AssetDatabase.OpenAsset(graph.objectReferenceValue);

            StoryKitStyles.SectionTitle("When", new Color(0.40f, 0.70f, 0.95f));
            EditorGUILayout.PropertyField(fireOn, new GUIContent("Fire on"));
            EditorGUILayout.LabelField(
                (StoryGraphTrigger.FireMode)fireOn.enumValueIndex switch
                {
                    StoryGraphTrigger.FireMode.EnterTrigger =>
                        "Starts the moment the player walks into the collider.",
                    StoryGraphTrigger.FireMode.Interact =>
                        "Player must be inside the collider and press interact (E, or X on a pad).",
                    _ => "Starts immediately when the scene loads.",
                }, StoryKitStyles.Summary);

            if (needsCollider)
            {
                EditorGUILayout.PropertyField(playerTag, new GUIContent("Player tag"));
                if (!hasTrigger)
                {
                    EditorGUILayout.Space(2);
                    if (GUILayout.Button("Add Box Collider (trigger)", GUILayout.Height(22)))
                    {
                        var added = Undo.AddComponent<BoxCollider>(trigger.gameObject);
                        added.isTrigger = true;
                        added.size = Vector3.one * 3f;
                    }
                }
            }

            StoryKitStyles.SectionTitle("Behaviour", StoryKitStyles.Success);
            DrawIfPresent("_once", "Play once only");
            DrawIfPresent("_cooldown", "Cooldown (s)");
            DrawIfPresent("_prompt", "Prompt object");

            if (Application.isPlaying)
            {
                EditorGUILayout.Space(4);
                StoryKitStyles.StatusLine(trigger.IsPlaying || trigger.PlayerInRange,
                    trigger.IsPlaying ? "Playing now" : "Player in range",
                    "Player not in range");
            }

            serializedObject.ApplyModifiedProperties();
        }

        private void DrawIfPresent(string propertyName, string label)
        {
            var property = serializedObject.FindProperty(propertyName);
            if (property != null) EditorGUILayout.PropertyField(property, new GUIContent(label));
        }
    }

    // ---------------- Flow Runner ----------------

    [CustomEditor(typeof(FlowRunner))]
    public sealed class FlowRunnerInspector : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            var runner = (FlowRunner)target;
            serializedObject.Update();

            var graph = serializedObject.FindProperty("_graphAsset");

            StoryKitStyles.StatusLine(graph.objectReferenceValue != null,
                "Graph assigned", "No graph assigned: this runner will do nothing.");

            EditorGUILayout.PropertyField(graph, new GUIContent("Graph"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("_playOnStart"),
                new GUIContent("Play on start"));

            if (graph.objectReferenceValue != null &&
                GUILayout.Button("Open graph editor", GUILayout.Height(20)))
                AssetDatabase.OpenAsset(graph.objectReferenceValue);

            if (Application.isPlaying)
            {
                StoryKitStyles.SectionTitle("Live", StoryKitStyles.Success);

                if (!runner.IsPlaying)
                {
                    EditorGUILayout.LabelField("Not running.", StoryKitStyles.Summary);
                    if (graph.objectReferenceValue != null && GUILayout.Button("Play now"))
                        runner.PlayAsync().Forget();
                }
                else
                {
                    foreach (string nodeId in runner.ActiveNodeIds)
                    {
                        var node = runner.SourceAsset?.FindNode(nodeId);
                        EditorGUILayout.LabelField(node != null
                            ? $"▶ {ObjectNames.NicifyVariableName(node.GetType().Name).Replace(" Node", "")}"
                              + (string.IsNullOrEmpty(node.GetSummary()) ? "" : $"  ·  {node.GetSummary()}")
                            : $"▶ {nodeId}", StoryKitStyles.Summary);
                    }

                    using (new EditorGUILayout.HorizontalScope())
                    {
                        if (GUILayout.Button("Skip cutscene")) runner.SkipToEnd();
                        if (GUILayout.Button("Stop")) runner.Stop();
                    }
                }
            }

            serializedObject.ApplyModifiedProperties();
        }
    }

    // ---------------- Story Stage Id ----------------

    [CustomEditor(typeof(StoryStageId))]
    public sealed class StoryStageIdInspector : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            var stage = (StoryStageId)target;
            serializedObject.Update();

            var id = serializedObject.FindProperty("_id");
            EditorGUILayout.LabelField(
                "Names this object so graphs can reference it by id.",
                StoryKitStyles.Summary);

            EditorGUILayout.PropertyField(id, new GUIContent("Stage id"));

            if (string.IsNullOrWhiteSpace(id.stringValue))
            {
                StoryKitStyles.StatusLine(false, "",
                    $"Empty: will fall back to the object name '{stage.name}'.");
                if (GUILayout.Button($"Use \"{stage.name}\""))
                    id.stringValue = stage.name;
            }
            else
            {
                // Duplicate ids silently break lookups, so surface them here.
                var duplicates = Object.FindObjectsByType<StoryStageId>(FindObjectsSortMode.None)
                    .Where(other => other != stage && other.Id == id.stringValue)
                    .ToArray();

                StoryKitStyles.StatusLine(duplicates.Length == 0,
                    "Unique in this scene.",
                    $"Duplicate id: also on '{(duplicates.Length > 0 ? duplicates[0].name : "")}'.");
            }

            serializedObject.ApplyModifiedProperties();
        }
    }

    // ---------------- Health ----------------

    [CustomEditor(typeof(Health))]
    public sealed class HealthInspector : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            var health = (Health)target;
            serializedObject.Update();

            if (Application.isPlaying)
            {
                // Live health bar: instantly readable while testing.
                var rect = GUILayoutUtility.GetRect(0, 22, GUILayout.ExpandWidth(true));
                StoryKitStyles.DrawRoundedRect(rect, new Color(1f, 1f, 1f, 0.06f));

                float fraction = health.Fraction;
                var fill = new Rect(rect.x, rect.y, rect.width * fraction, rect.height);
                var color = fraction > 0.5f ? StoryKitStyles.Success
                    : fraction > 0.25f ? StoryKitStyles.Warning : StoryKitStyles.Danger;
                StoryKitStyles.DrawRoundedRect(fill, color * 0.75f);

                var style = new GUIStyle(EditorStyles.boldLabel)
                {
                    alignment = TextAnchor.MiddleCenter,
                    normal = { textColor = Color.white },
                };
                GUI.Label(rect, $"{health.Current:0} / {health.Max:0}", style);

                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("-10")) health.TakeDamage(10f);
                    if (GUILayout.Button("+10")) health.Heal(10f);
                    if (GUILayout.Button("Kill")) health.TakeDamage(health.Max);
                    if (GUILayout.Button("Revive")) health.Revive();
                }
                EditorGUILayout.Space(4);
            }

            DrawDefaultInspector();

            var key = serializedObject.FindProperty("_blackboardKey");
            if (key != null && !string.IsNullOrEmpty(key.stringValue))
                EditorGUILayout.LabelField(
                    $"Writes 0..1 to '{key.stringValue}': FSM guards and BT conditions read it.",
                    StoryKitStyles.Summary);
        }
    }

    // ---------------- Quest System ----------------

    [CustomEditor(typeof(QuestSystem))]
    public sealed class QuestSystemInspector : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            var system = (QuestSystem)target;
            serializedObject.Update();

            var quests = serializedObject.FindProperty("_quests");
            StoryKitStyles.StatusLine(quests.arraySize > 0,
                $"{quests.arraySize} quest(s) registered.",
                "No quests registered: Quest nodes will only track raw state.");

            EditorGUILayout.PropertyField(quests, new GUIContent("Quests"), true);
            EditorGUILayout.PropertyField(serializedObject.FindProperty("_trackedEvents"),
                new GUIContent("Counted events"), true);
            EditorGUILayout.LabelField(
                "Events listed here can be used by EventCount requirements.",
                StoryKitStyles.Summary);

            if (Application.isPlaying)
            {
                StoryKitStyles.SectionTitle("Active quests", StoryKitStyles.Success);

                if (system.ActiveQuestIds.Count == 0)
                    EditorGUILayout.LabelField("None active.", StoryKitStyles.Summary);

                foreach (string questId in system.ActiveQuestIds.ToArray())
                {
                    using (new EditorGUILayout.VerticalScope(StoryKitStyles.Card))
                    {
                        EditorGUILayout.LabelField(questId, EditorStyles.boldLabel);

                        foreach (var (objective, done, current, targetCount)
                                 in system.GetVisibleObjectives(questId))
                        {
                            string counter = targetCount > 0 ? $"  {current}/{targetCount}" : "";
                            EditorGUILayout.LabelField(
                                (done ? "✓ " : "○ ") +
                                QuestDefinition.ResolveObjectiveText(objective) + counter,
                                StoryKitStyles.Summary);
                        }

                        using (new EditorGUILayout.HorizontalScope())
                        {
                            if (GUILayout.Button("Complete", EditorStyles.miniButtonLeft))
                                system.CompleteQuest(questId);
                            if (GUILayout.Button("Fail", EditorStyles.miniButtonRight))
                                system.FailQuest(questId);
                        }
                    }
                }
            }

            serializedObject.ApplyModifiedProperties();
        }
    }

    // ---------------- State Machine Runner ----------------

    [CustomEditor(typeof(StateMachineRunner))]
    public sealed class StateMachineRunnerInspector : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            var runner = (StateMachineRunner)target;
            serializedObject.Update();

            var asset = serializedObject.FindProperty("_asset");
            var machine = asset.objectReferenceValue as StateMachineAsset;

            if (machine == null)
                StoryKitStyles.StatusLine(false, "",
                    "No state machine assigned: this object will not do anything.");
            else if (machine.States.Count == 0)
                StoryKitStyles.StatusLine(false, "",
                    $"'{machine.name}' has no states yet. Open it and add one.");
            else
                StoryKitStyles.StatusLine(true,
                    $"Starts in '{StartStateName(machine)}'", "");

            StoryKitStyles.SectionTitle("What to run", StoryKitStyles.Accent);
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.PropertyField(asset, new GUIContent("State machine"));
                using (new EditorGUI.DisabledScope(machine == null))
                    if (GUILayout.Button("Open", EditorStyles.miniButton, GUILayout.Width(50)))
                        Selection.activeObject = machine;
            }

            if (machine == null && GUILayout.Button("Create a new one", GUILayout.Height(22)))
                CreateMachineFor(runner, asset);

            EditorGUILayout.PropertyField(serializedObject.FindProperty("_playOnStart"),
                new GUIContent("Play on start"));

            StoryKitStyles.SectionTitle("Listen to global events",
                new Color(0.86f, 0.52f, 0.86f));
            EditorGUILayout.LabelField(
                "Names listed here arrive from StoryEvents and act like a local event, " +
                "so a graph can drive this machine without any code.",
                StoryKitStyles.Summary);
            EditorGUILayout.PropertyField(serializedObject.FindProperty("_listenGlobalEvents"),
                new GUIContent("Event names"), true);

            serializedObject.ApplyModifiedProperties();

            if (!Application.isPlaying || !runner.IsRunning) return;

            StoryKitStyles.SectionTitle("Live", StoryKitStyles.Warning);

            var current = runner.CurrentState;
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("Current state", GUILayout.Width(90));
                var pill = GUILayoutUtility.GetRect(140, 18, GUILayout.Width(140));
                StoryKitStyles.DrawPill(pill, current != null ? current.title : "-",
                    StoryKitStyles.Warning);
            }

            if (current == null) return;

            EditorGUILayout.LabelField("Ways out of this state:", StoryKitStyles.Summary);
            DrawTransitionButtons(runner, machine, current.transitions);

            if (machine != null && machine.GlobalTransitions.Count > 0)
            {
                EditorGUILayout.LabelField("From any state:", StoryKitStyles.Summary);
                DrawTransitionButtons(runner, machine, machine.GlobalTransitions);
            }

            Repaint();
        }

        private static void DrawTransitionButtons(StateMachineRunner runner,
            StateMachineAsset machine, System.Collections.Generic.List<FsmTransition> transitions)
        {
            foreach (var transition in transitions)
            {
                if (transition == null) continue;

                string target = machine?.FindState(transition.targetStateId)?.title ?? "?";
                string label = string.IsNullOrEmpty(transition.eventName)
                    ? $"auto  ·  {transition.guardKey} {transition.guardComparison} {transition.guardValue:0.##}"
                    : $"on \"{transition.eventName}\"";

                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField($"{label}  ->  {target}", StoryKitStyles.Summary);
                    if (!string.IsNullOrEmpty(transition.eventName) &&
                        GUILayout.Button("Send", EditorStyles.miniButton, GUILayout.Width(46)))
                        runner.SendEvent(transition.eventName);
                }
            }
        }

        private static string StartStateName(StateMachineAsset machine)
        {
            var state = machine.FindState(machine.ResolvedInitialStateId);
            return state != null ? state.title : "?";
        }

        /// <summary>Saves a machine next to the scene so the first one is one click.</summary>
        private static void CreateMachineFor(StateMachineRunner runner, SerializedProperty asset)
        {
            string path = EditorUtility.SaveFilePanelInProject("New State Machine",
                runner.name + " Machine", "asset", "Where should it live?");
            if (string.IsNullOrEmpty(path)) return;

            var created = ScriptableObject.CreateInstance<StateMachineAsset>();
            created.AddState("Idle");
            AssetDatabase.CreateAsset(created, path);
            AssetDatabase.SaveAssets();

            asset.objectReferenceValue = created;
            asset.serializedObject.ApplyModifiedProperties();
            Selection.activeObject = created;
        }
    }

    // ---------------- Behavior Tree Runner ----------------

    [CustomEditor(typeof(BehaviorTreeRunner))]
    public sealed class BehaviorTreeRunnerInspector : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            var runner = (BehaviorTreeRunner)target;
            serializedObject.Update();

            var asset = serializedObject.FindProperty("_asset");
            var tree = asset.objectReferenceValue as BehaviorTreeAsset;

            if (tree == null)
                StoryKitStyles.StatusLine(false, "",
                    "No tree assigned: this AI will not think.");
            else if (tree.Root == null)
                StoryKitStyles.StatusLine(false, "",
                    $"'{tree.name}' has no root node yet. Open it and add one.");
            else
                StoryKitStyles.StatusLine(true, $"Running '{tree.name}'", "");

            StoryKitStyles.SectionTitle("What to run", StoryKitStyles.Accent);
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.PropertyField(asset, new GUIContent("Behaviour tree"));
                using (new EditorGUI.DisabledScope(tree == null))
                    if (GUILayout.Button("Open", EditorStyles.miniButton, GUILayout.Width(50)))
                        Selection.activeObject = tree;
            }

            StoryKitStyles.SectionTitle("How often it thinks", StoryKitStyles.Success);
            var tickMode = serializedObject.FindProperty("_tickMode");
            EditorGUILayout.PropertyField(tickMode, new GUIContent("Tick mode"));
            EditorGUILayout.LabelField(
                (BtTickMode)tickMode.enumValueIndex switch
                {
                    BtTickMode.EveryFrame =>
                        "Every frame: most responsive, most expensive. Fine for a boss.",
                    BtTickMode.Interval =>
                        "On a timer: the right default once there are more than a few enemies.",
                    _ => "Only when a blackboard value changes: cheapest for idle crowds.",
                }, StoryKitStyles.Summary);

            if ((BtTickMode)tickMode.enumValueIndex != BtTickMode.EveryFrame)
                EditorGUILayout.PropertyField(serializedObject.FindProperty("_interval"),
                    new GUIContent("Seconds between ticks"));

            serializedObject.ApplyModifiedProperties();

            if (!Application.isPlaying) return;

            StoryKitStyles.SectionTitle("Live", new Color(0.45f, 0.85f, 0.55f));
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("Last result", GUILayout.Width(80));
                var pill = GUILayoutUtility.GetRect(90, 18, GUILayout.Width(90));
                StoryKitStyles.DrawPill(pill, runner.LastStatus.ToString(),
                    runner.LastStatus switch
                    {
                        BtStatus.Success => StoryKitStyles.Success,
                        BtStatus.Failure => StoryKitStyles.Danger,
                        _ => StoryKitStyles.Warning,
                    });
            }

            runner.IsPaused = EditorGUILayout.Toggle("Pause thinking", runner.IsPaused);
            EditorGUILayout.LabelField(
                "Select the tree asset to watch every node light up as it ticks.",
                StoryKitStyles.Summary);
            Repaint();
        }
    }

    // ---------------- Simple Quest Log ----------------

    [CustomEditor(typeof(SimpleQuestLog))]
    public sealed class SimpleQuestLogInspector : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            var log = (SimpleQuestLog)target;
            serializedObject.Update();

            bool hasQuests = QuestSystem.Instance != null
                             || Object.FindObjectsByType<QuestSystem>(FindObjectsSortMode.None)
                                 .Length > 0;

            StoryKitStyles.StatusLine(hasQuests,
                "Reading the Quest System in this scene",
                "No Quest System in the scene, so there is nothing to show.");

            EditorGUILayout.LabelField(
                "Draws a small tracker in the corner and a full panel on a key. " +
                "Delete this the moment you have your own quest UI.",
                StoryKitStyles.Summary);

            StoryKitStyles.SectionTitle("Tracker", StoryKitStyles.Accent);
            EditorGUILayout.PropertyField(serializedObject.FindProperty("_showTracker"),
                new GUIContent("Show tracker"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("_trackerLines"),
                new GUIContent("Max lines"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("_trackedQuestId"),
                new GUIContent("Follow quest"));
            EditorGUILayout.LabelField(
                "Leave empty to follow whichever quest started last.",
                StoryKitStyles.Summary);

            StoryKitStyles.SectionTitle("Panel", StoryKitStyles.Success);
            EditorGUILayout.LabelField(
                "Opens with J, or the Select button on a pad. Rebind by assigning " +
                "your own StoryInput.Source at startup.",
                StoryKitStyles.Summary);

            serializedObject.ApplyModifiedProperties();

            if (!Application.isPlaying) return;

            EditorGUILayout.Space(4);
            if (GUILayout.Button(log.IsOpen ? "Close log" : "Open log", GUILayout.Height(22)))
                log.Toggle();
        }
    }

    // ---------------- Pause Manager ----------------

    [CustomEditor(typeof(PauseManager))]
    public sealed class PauseManagerInspector : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            if (!Application.isPlaying) return;

            EditorGUILayout.Space(4);
            StoryKitStyles.StatusLine(!PauseManager.IsPaused, "Running", "Paused");
            if (GUILayout.Button(PauseManager.IsPaused ? "Resume" : "Pause"))
                ((PauseManager)target).Toggle();
        }
    }
}
