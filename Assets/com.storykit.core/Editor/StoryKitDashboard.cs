using System.Collections.Generic;
using System.Linq;
using StoryKit.Flow;
using UnityEditor;
using UnityEngine;

namespace StoryKit.Editor
{
    /// <summary>
    /// StoryKit dashboard: one window to see the whole story layer at runtime.
    ///
    /// Window > StoryKit > Dashboard
    ///
    /// Tabs:
    ///   Blackboard: every live variable, searchable, editable while playing
    ///   Runners   : which graphs are running and which nodes are active
    ///   Events    : a rolling log of every StoryEvent raised
    ///   Perf      : pool usage and running node counts
    ///
    /// This replaces "add a Debug.Log and rebuild" for almost every question
    /// you can ask about a story-driven game.
    /// </summary>
    public sealed class StoryKitDashboard : EditorWindow
    {
        private enum Tab { Blackboard, Runners, Events, Perf }

        private Tab _tab = Tab.Blackboard;
        private string _search = "";
        private Vector2 _scroll;
        private double _nextRepaint;

        // Event log is captured through a global hook installed on first open.
        private static readonly List<(double time, string name, string payload)> _eventLog = new();
        private static bool _hooked;
        private const int MaxEventLog = 200;

        [MenuItem("Window/StoryKit/Dashboard %#d")]
        public static void Open()
        {
            var window = GetWindow<StoryKitDashboard>();
            window.titleContent = new GUIContent("StoryKit");
            window.minSize = new Vector2(340, 240);
            window.Show();
        }

        private void OnEnable()
        {
            EnsureEventHook();
            EditorApplication.update += Tick;
        }

        private void OnDisable() => EditorApplication.update -= Tick;

        /// <summary>Repaint at ~10 Hz while playing: enough to feel live,
        /// cheap enough not to slow the editor down.</summary>
        private void Tick()
        {
            if (!Application.isPlaying) return;
            if (EditorApplication.timeSinceStartup < _nextRepaint) return;
            _nextRepaint = EditorApplication.timeSinceStartup + 0.1;
            Repaint();
        }

        private static void EnsureEventHook()
        {
            if (_hooked) return;
            _hooked = true;
            StoryEvents.Monitor = (name, payload) =>
            {
                _eventLog.Add((EditorApplication.timeSinceStartup, name, payload?.ToString()));
                if (_eventLog.Count > MaxEventLog) _eventLog.RemoveAt(0);
            };
        }

        private void OnGUI()
        {
            DrawToolbar();

            if (!Application.isPlaying)
            {
                EditorGUILayout.Space(20);
                EditorGUILayout.LabelField("Enter Play mode to inspect the live story state.",
                    StoryKitStyles.Summary);
                return;
            }

            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            switch (_tab)
            {
                case Tab.Blackboard: DrawBlackboard(); break;
                case Tab.Runners: DrawRunners(); break;
                case Tab.Events: DrawEvents(); break;
                case Tab.Perf: DrawPerf(); break;
            }
            EditorGUILayout.EndScrollView();
        }

        private void DrawToolbar()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                foreach (Tab tab in System.Enum.GetValues(typeof(Tab)))
                {
                    bool active = _tab == tab;
                    if (GUILayout.Toggle(active, tab.ToString(), EditorStyles.toolbarButton) && !active)
                        _tab = tab;
                }
                GUILayout.FlexibleSpace();
                _search = GUILayout.TextField(_search, EditorStyles.toolbarSearchField,
                    GUILayout.Width(160));
            }
        }

        // ---------------- Blackboard ----------------

        private void DrawBlackboard()
        {
            var snapshot = StoryState.Global.Snapshot();
            var keys = snapshot.Keys
                .Where(Matches)
                .OrderBy(key => key)
                .ToList();

            StoryKitStyles.SectionTitle($"Global variables ({keys.Count})",
                StoryKitStyles.Accent, "Edit any value live: the game reacts instantly.");

            foreach (string key in keys)
            {
                object value = snapshot[key];
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField(Prettify(key), GUILayout.MinWidth(140));

                    switch (value)
                    {
                        case float number:
                        {
                            float edited = EditorGUILayout.FloatField(number, GUILayout.Width(90));
                            if (!Mathf.Approximately(edited, number))
                                StoryState.Global.Set(key, edited);
                            break;
                        }
                        case bool flag:
                        {
                            bool edited = EditorGUILayout.Toggle(flag, GUILayout.Width(90));
                            if (edited != flag) StoryState.Global.Set(key, edited);
                            break;
                        }
                        case string text:
                        {
                            string edited = EditorGUILayout.TextField(text, GUILayout.Width(160));
                            if (edited != text) StoryState.Global.Set(key, edited);
                            break;
                        }
                        default:
                            EditorGUILayout.LabelField(value?.ToString() ?? "null",
                                GUILayout.Width(160));
                            break;
                    }
                }
            }

            if (keys.Count == 0)
                EditorGUILayout.LabelField("No variables yet.", StoryKitStyles.Summary);
        }

        /// <summary>Turns internal keys into readable labels.</summary>
        private static string Prettify(string key) => key.StartsWith("_")
            ? key.Substring(1) : key;

        // ---------------- Runners ----------------

        private void DrawRunners()
        {
            DrawPlayerState();

            var runners = FlowRunner.ActiveRunners;
            StoryKitStyles.SectionTitle($"Flow graphs running ({runners.Count})",
                StoryKitStyles.Success);

            foreach (var runner in runners)
            {
                if (runner == null) continue;
                using (new EditorGUILayout.VerticalScope(StoryKitStyles.Card))
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        EditorGUILayout.LabelField(
                            runner.SourceAsset != null ? runner.SourceAsset.name : "(runtime graph)",
                            EditorStyles.boldLabel);

                        if (GUILayout.Button("Select", EditorStyles.miniButton, GUILayout.Width(56)))
                            Selection.activeGameObject = runner.gameObject;
                        if (GUILayout.Button("Skip", EditorStyles.miniButton, GUILayout.Width(46)))
                            runner.SkipToEnd();
                        if (GUILayout.Button("Stop", EditorStyles.miniButton, GUILayout.Width(46)))
                            runner.Stop();
                    }

                    foreach (string nodeId in runner.ActiveNodeIds)
                    {
                        var node = runner.SourceAsset != null
                            ? runner.SourceAsset.FindNode(nodeId) : null;
                        string label = node != null
                            ? $"▶ {ObjectNames.NicifyVariableName(node.GetType().Name).Replace(" Node", "")}"
                              + (string.IsNullOrEmpty(node.GetSummary()) ? "" : $"  ·  {node.GetSummary()}")
                            : $"▶ {nodeId}";
                        EditorGUILayout.LabelField(label, StoryKitStyles.Summary);
                    }
                }
            }

            var machines = Object.FindObjectsByType<Fsm.StateMachineRunner>(
                FindObjectsSortMode.None);
            if (machines.Length > 0)
            {
                StoryKitStyles.SectionTitle($"State machines ({machines.Length})",
                    StoryKitStyles.Warning);
                foreach (var machine in machines)
                {
                    if (machine == null || !machine.IsRunning) continue;
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        EditorGUILayout.LabelField(machine.name, GUILayout.MinWidth(120));
                        EditorGUILayout.LabelField(
                            machine.CurrentState != null ? machine.CurrentState.title : "-",
                            EditorStyles.miniBoldLabel);
                        if (GUILayout.Button("Select", EditorStyles.miniButton, GUILayout.Width(56)))
                            Selection.activeGameObject = machine.gameObject;
                    }
                }
            }
        }

        // ---------------- Events ----------------

        /// <summary>
        /// Whether the player can move, and who is stopping them. A frozen
        /// player with no visible cause is the single most confusing bug this
        /// package can produce, so it gets its own line and a way out.
        /// </summary>
        private static void DrawPlayerState()
        {
            bool locked = StoryControl.MovementLocked;

            StoryKitStyles.StatusLine(!locked,
                "Player can move",
                $"Player is held still by {StoryControl.LockCount} thing(s): " +
                "dialogue, a cutscene or an open menu");

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(
                    locked
                        ? "Cursor: " + (StoryUi.CursorReleased ? "released for UI" : "held by the game")
                        : "Cursor: " + StoryUi.GameplayCursor,
                    StoryKitStyles.Summary);

                using (new EditorGUI.DisabledScope(!locked))
                    if (GUILayout.Button("Force unlock", GUILayout.Width(100)))
                        StoryControl.ForceRelease();
            }

            EditorGUILayout.Space(4);
        }

        private void DrawEvents()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                StoryKitStyles.SectionTitle($"Event log ({_eventLog.Count})",
                    new Color(0.85f, 0.50f, 0.80f), "Newest first.");
                if (GUILayout.Button("Clear", EditorStyles.miniButton, GUILayout.Width(52)))
                    _eventLog.Clear();
            }

            for (int i = _eventLog.Count - 1; i >= 0; i--)
            {
                var entry = _eventLog[i];
                if (!Matches(entry.name)) continue;

                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField($"{entry.time:0.0}s", GUILayout.Width(52));
                    EditorGUILayout.LabelField(entry.name, EditorStyles.miniBoldLabel,
                        GUILayout.MinWidth(150));
                    if (!string.IsNullOrEmpty(entry.payload))
                        EditorGUILayout.LabelField(entry.payload, StoryKitStyles.Summary);
                }
            }
        }

        // ---------------- Perf ----------------

        private void DrawPerf()
        {
            StoryKitStyles.SectionTitle("Runtime cost", new Color(0.40f, 0.70f, 0.95f),
                "Numbers that usually explain a frame spike.");

            int activeNodes = FlowRunner.ActiveRunners.Sum(
                runner => runner != null ? runner.ActiveNodeIds.Count : 0);

            Row("Flow graphs running", FlowRunner.ActiveRunners.Count.ToString());
            Row("Active nodes", activeNodes.ToString());
            Row("Pooled objects live", StoryPool.LiveCount.ToString());
            Row("Behaviour trees",
                Object.FindObjectsByType<Bt.BehaviorTreeRunner>(FindObjectsSortMode.None)
                    .Length.ToString());
            Row("State machines",
                Object.FindObjectsByType<Fsm.StateMachineRunner>(FindObjectsSortMode.None)
                    .Length.ToString());

            EditorGUILayout.Space(6);
            StoryKitStyles.StatusLine(activeNodes < 60,
                "Node load is healthy.",
                "Many nodes active at once: check for graphs that never reach an End node.");

            EditorGUILayout.Space(4);
            if (GUILayout.Button("Clear object pools"))
                StoryPool.Clear();
        }

        private static void Row(string label, string value)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(label, GUILayout.MinWidth(180));
                EditorGUILayout.LabelField(value, EditorStyles.boldLabel, GUILayout.Width(70));
            }
        }

        private bool Matches(string text) =>
            string.IsNullOrEmpty(_search) ||
            (text != null && text.IndexOf(_search, System.StringComparison.OrdinalIgnoreCase) >= 0);
    }
}
