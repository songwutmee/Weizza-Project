using System;
using System.Collections.Generic;
using System.Linq;
using StoryKit.Flow;
using StoryKit.Fsm;
using UnityEditor;
using UnityEngine;

namespace StoryKit.Editor
{
    /// <summary>
    /// The state machine editor. The default inspector shows a list of
    /// "Element 0" with raw ids in it, which is unusable, so this replaces it
    /// with named states, dropdowns instead of ids, and a live view of which
    /// state a boss is in while the game runs.
    /// </summary>
    [CustomEditor(typeof(StateMachineAsset))]
    public sealed class StateMachineAssetInspector : UnityEditor.Editor
    {
        private static readonly Color StateColor = new(0.42f, 0.62f, 0.95f);
        private static readonly Color GlobalColor = new(0.86f, 0.52f, 0.86f);

        private readonly HashSet<string> _expanded = new();
        private string _search = string.Empty;

        private string[] _stateNames = Array.Empty<string>();
        private string[] _stateIds = Array.Empty<string>();

        public override void OnInspectorGUI()
        {
            var machine = (StateMachineAsset)target;

            // Repair before reading: ids come back blank or duplicated when
            // states are added through a plain list.
            if (machine.EnsureIds()) EditorUtility.SetDirty(machine);

            serializedObject.Update();
            RebuildStateLookup(machine);

            DrawSummary(machine);

            if (GUILayout.Button("Open state canvas", GUILayout.Height(28)))
                FsmGraphWindow.Open(machine);
            EditorGUILayout.LabelField(
                "The canvas is the easier way to see a machine with more than a few states.",
                StoryKitStyles.Summary);

            DrawStartState(machine);
            DrawLive(machine);
            DrawStates(machine);
            DrawGlobalTransitions(machine);

            serializedObject.ApplyModifiedProperties();
        }

        // ---------------- Header ----------------

        private static void DrawSummary(StateMachineAsset machine)
        {
            var problems = new List<StoryProblem>();
            StoryKitValidator.ValidateStateMachine(machine, problems);
            var errors = problems.Where(problem => problem.IsError).ToList();

            if (machine.States.Count == 0)
                StoryKitStyles.StatusLine(false, "",
                    "No states yet. Add one below and it becomes the start state.");
            else if (errors.Count > 0)
                StoryKitStyles.StatusLine(false, "", errors[0].Message);
            else
                StoryKitStyles.StatusLine(true,
                    $"{machine.States.Count} states, {CountTransitions(machine)} transitions", "");

            if (problems.Count > errors.Count)
                foreach (var problem in problems.Where(problem => !problem.IsError))
                    EditorGUILayout.LabelField("•  " + problem.Message, StoryKitStyles.Summary);
        }

        private static int CountTransitions(StateMachineAsset machine)
            => machine.States.Where(state => state != null).Sum(state => state.transitions.Count)
               + machine.GlobalTransitions.Count;

        // ---------------- Start state ----------------

        private void DrawStartState(StateMachineAsset machine)
        {
            if (machine.States.Count == 0) return;

            StoryKitStyles.SectionTitle("Start", StoryKitStyles.Success);

            // "Automatic" is index 0 so the common case needs no decision.
            var options = new string[_stateNames.Length + 1];
            options[0] = machine.States.Count > 0
                ? $"Automatic (first state: {_stateNames[0]})"
                : "Automatic";
            Array.Copy(_stateNames, 0, options, 1, _stateNames.Length);

            int current = machine.InitialIsAutomatic
                ? 0
                : Array.IndexOf(_stateIds, machine.InitialStateId) + 1;

            int picked = EditorGUILayout.Popup("Starts in", Mathf.Max(0, current), options);
            if (picked != current)
                serializedObject.FindProperty("_initialStateId").stringValue =
                    picked == 0 ? string.Empty : _stateIds[picked - 1];

            if (machine.InitialIsAutomatic)
                EditorGUILayout.LabelField(
                    "You do not have to pick one. The first state in the list is used.",
                    StoryKitStyles.Summary);
        }

        // ---------------- Live ----------------

        private void DrawLive(StateMachineAsset machine)
        {
            if (!Application.isPlaying) return;

            var runners = FindRunners(machine);
            if (runners.Count == 0) return;

            StoryKitStyles.SectionTitle("Running now", StoryKitStyles.Warning);
            foreach (var runner in runners.Take(6))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.ObjectField(runner.gameObject, typeof(GameObject), true);
                    var pill = GUILayoutUtility.GetRect(110, 18, GUILayout.Width(110));
                    StoryKitStyles.DrawPill(pill,
                        runner.CurrentState != null ? runner.CurrentState.title : "stopped",
                        StoryKitStyles.Warning);
                }
            }
            Repaint();
        }

        private static List<StateMachineRunner> FindRunners(StateMachineAsset machine)
        {
            return UnityEngine.Object
                .FindObjectsByType<StateMachineRunner>(FindObjectsSortMode.None)
                .Where(runner => runner.Asset == machine)
                .ToList();
        }

        // ---------------- States ----------------

        private void DrawStates(StateMachineAsset machine)
        {
            var states = serializedObject.FindProperty("_states");

            StoryKitStyles.SectionTitle($"States ({states.arraySize})", StateColor);

            using (new EditorGUILayout.HorizontalScope())
            {
                _search = EditorGUILayout.TextField(_search, EditorStyles.toolbarSearchField);
                if (GUILayout.Button("+ Add state", EditorStyles.miniButton, GUILayout.Width(86)))
                {
                    AddState(machine);
                    return;
                }
            }

            string liveStateId = LiveStateId(machine);

            for (int i = 0; i < states.arraySize; i++)
            {
                var element = states.GetArrayElementAtIndex(i);
                var state = i < machine.States.Count ? machine.States[i] : null;
                if (state == null) continue;

                if (!string.IsNullOrEmpty(_search) &&
                    state.title.IndexOf(_search, StringComparison.OrdinalIgnoreCase) < 0)
                    continue;

                DrawStateCard(machine, states, element, state, i,
                    isStart: state.Id == machine.ResolvedInitialStateId,
                    isLive: state.Id == liveStateId);
            }
        }

        private string LiveStateId(StateMachineAsset machine)
        {
            if (!Application.isPlaying) return null;
            var runner = FindRunners(machine).FirstOrDefault();
            return runner != null ? runner.CurrentStateId : null;
        }

        private void DrawStateCard(StateMachineAsset machine, SerializedProperty states,
            SerializedProperty element, FsmState state, int index, bool isStart, bool isLive)
        {
            string key = state.Id;
            bool open = _expanded.Contains(key);

            using (new EditorGUILayout.VerticalScope(StoryKitStyles.Card))
            {
                var barColor = isLive ? StoryKitStyles.Warning
                    : isStart ? StoryKitStyles.Success : StateColor;

                using (new EditorGUILayout.HorizontalScope())
                {
                    var bar = GUILayoutUtility.GetRect(3, 18, GUILayout.Width(3));
                    StoryKitStyles.DrawRect(bar, barColor);

                    if (GUILayout.Button(open ? "▾" : "▸", EditorStyles.label, GUILayout.Width(14)))
                    {
                        if (open) _expanded.Remove(key); else _expanded.Add(key);
                    }

                    EditorGUILayout.PropertyField(element.FindPropertyRelative("title"),
                        GUIContent.none);

                    if (isStart)
                    {
                        var pill = GUILayoutUtility.GetRect(46, 16, GUILayout.Width(46));
                        StoryKitStyles.DrawPill(pill, "START", StoryKitStyles.Success);
                    }
                    if (isLive)
                    {
                        var pill = GUILayoutUtility.GetRect(38, 16, GUILayout.Width(38));
                        StoryKitStyles.DrawPill(pill, "HERE", StoryKitStyles.Warning);
                    }

                    // The menu answers later, so the rest of the list still draws.
                    if (GUILayout.Button("⋮", EditorStyles.miniButton, GUILayout.Width(20)))
                        ShowStateMenu(machine, states, index);
                }

                EditorGUILayout.LabelField(SummarizeState(state), StoryKitStyles.Summary);
                if (!open) return;

                StoryKitStyles.Separator();

                NodeListDrawer.DrawActionList(element.FindPropertyRelative("onEnter"),
                    "On enter", _expanded, key + ":enter");
                NodeListDrawer.DrawActionList(element.FindPropertyRelative("onExit"),
                    "On exit", _expanded, key + ":exit");

                EditorGUILayout.Space(4);
                DrawTransitions(machine, element.FindPropertyRelative("transitions"),
                    "Leaves this state when", StateColor);

                EditorGUILayout.Space(4);
                string advancedKey = key + ":advanced";
                bool advanced = _expanded.Contains(advancedKey);
                if (GUILayout.Button(advanced ? "▾  Extras" : "▸  Extras", EditorStyles.miniButton))
                {
                    if (advanced) _expanded.Remove(advancedKey); else _expanded.Add(advancedKey);
                }

                if (advanced)
                {
                    EditorGUILayout.PropertyField(element.FindPropertyRelative("flowGraph"),
                        new GUIContent("Play flow graph"));
                    EditorGUILayout.LabelField(
                        "Runs while this state is active. Finishing it sends \"Fsm.FlowDone\".",
                        StoryKitStyles.Summary);
                    EditorGUILayout.PropertyField(element.FindPropertyRelative("subMachine"),
                        new GUIContent("Sub machine"));
                }
            }
        }

        private static string SummarizeState(FsmState state)
        {
            var parts = new List<string>();
            if (state.onEnter.Count > 0) parts.Add($"{state.onEnter.Count} on enter");
            if (state.onExit.Count > 0) parts.Add($"{state.onExit.Count} on exit");
            if (state.flowGraph != null) parts.Add($"plays {state.flowGraph.name}");
            if (state.subMachine != null) parts.Add($"sub machine {state.subMachine.name}");

            parts.Add(state.transitions.Count == 0
                ? "no way out"
                : $"{state.transitions.Count} way(s) out");

            return string.Join("  ·  ", parts);
        }

        /// <summary>
        /// Menu callbacks run after the GUI pass, so the property is looked up
        /// again inside each one instead of being captured and reused.
        /// </summary>
        private void ShowStateMenu(StateMachineAsset machine, SerializedProperty states, int index)
        {
            var menu = new GenericMenu();
            var owner = states.serializedObject;

            void Edit(Action<SerializedProperty> action)
            {
                owner.Update();
                action(owner.FindProperty("_states"));
                owner.ApplyModifiedProperties();
            }

            menu.AddItem(new GUIContent("Make this the start"), false, () =>
            {
                if (index >= machine.States.Count) return;
                string id = machine.States[index].Id;
                owner.Update();
                owner.FindProperty("_initialStateId").stringValue = id;
                owner.ApplyModifiedProperties();
            });

            if (index > 0)
                menu.AddItem(new GUIContent("Move up"), false,
                    () => Edit(list => list.MoveArrayElement(index, index - 1)));

            if (index < states.arraySize - 1)
                menu.AddItem(new GUIContent("Move down"), false,
                    () => Edit(list => list.MoveArrayElement(index, index + 1)));

            menu.AddItem(new GUIContent("Duplicate"), false, () =>
            {
                Edit(list => list.InsertArrayElementAtIndex(index));

                // The copy shares the original id until it is repaired.
                if (index + 1 >= machine.States.Count) return;
                machine.States[index + 1].AssignNewId();
                machine.States[index + 1].title = machine.States[index].title + " copy";
                EditorUtility.SetDirty(machine);
            });

            menu.AddSeparator(string.Empty);
            menu.AddItem(new GUIContent("Delete"), false, () =>
            {
                if (index >= machine.States.Count) return;
                string deadId = machine.States[index].Id;

                Undo.RecordObject(machine, "Delete State");
                machine.States.RemoveAt(index);

                // Transitions pointing at a deleted state would silently do
                // nothing, so they are cleared here rather than left to rot.
                foreach (var state in machine.States)
                    state?.transitions.RemoveAll(t => t != null && t.targetStateId == deadId);
                machine.GlobalTransitions.RemoveAll(t => t != null && t.targetStateId == deadId);

                if (machine.InitialStateId == deadId) machine.InitialStateId = null;
                EditorUtility.SetDirty(machine);
                owner.Update();
            });

            menu.ShowAsContext();
        }

        private void AddState(StateMachineAsset machine)
        {
            Undo.RecordObject(machine, "Add State");
            var state = machine.AddState($"State {machine.States.Count + 1}");
            _expanded.Add(state.Id);
            EditorUtility.SetDirty(machine);
            serializedObject.Update();
        }

        // ---------------- Global transitions ----------------

        private void DrawGlobalTransitions(StateMachineAsset machine)
        {
            StoryKitStyles.SectionTitle("From any state", GlobalColor,
                "Fires no matter which state the machine is in. Use it for death and stun.");
            DrawTransitions(machine, serializedObject.FindProperty("_globalTransitions"),
                null, GlobalColor);
        }

        // ---------------- Transition rows ----------------

        private void DrawTransitions(StateMachineAsset machine, SerializedProperty list,
            string title, Color color)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                if (!string.IsNullOrEmpty(title))
                    EditorGUILayout.LabelField($"{title}  ({list.arraySize})",
                        EditorStyles.miniBoldLabel);
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("+ Transition", EditorStyles.miniButton, GUILayout.Width(94)))
                {
                    list.arraySize++;
                    var added = list.GetArrayElementAtIndex(list.arraySize - 1);
                    added.FindPropertyRelative("eventName").stringValue = string.Empty;
                    added.FindPropertyRelative("guardKey").stringValue = string.Empty;
                    added.FindPropertyRelative("targetStateId").stringValue = string.Empty;
                    added.FindPropertyRelative("guardValue").floatValue = 1f;
                    return;
                }
            }

            if (list.arraySize == 0)
            {
                EditorGUILayout.LabelField("Nothing leaves here yet.", StoryKitStyles.Summary);
                return;
            }

            for (int i = 0; i < list.arraySize; i++)
            {
                var element = list.GetArrayElementAtIndex(i);
                var eventName = element.FindPropertyRelative("eventName");
                var guardKey = element.FindPropertyRelative("guardKey");
                var guardComparison = element.FindPropertyRelative("guardComparison");
                var guardValue = element.FindPropertyRelative("guardValue");
                var targetStateId = element.FindPropertyRelative("targetStateId");

                using (new EditorGUILayout.VerticalScope(StoryKitStyles.Card))
                {
                    // Row 1: what starts it, and where it goes.
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        var bar = GUILayoutUtility.GetRect(3, 18, GUILayout.Width(3));
                        StoryKitStyles.DrawRect(bar, color);

                        EditorGUILayout.LabelField("On event", GUILayout.Width(58));
                        EditorGUILayout.PropertyField(eventName, GUIContent.none);

                        EditorGUILayout.LabelField("go to", GUILayout.Width(36));
                        DrawStateDropdown(targetStateId);

                        if (GUILayout.Button("✕", EditorStyles.miniButton, GUILayout.Width(22)))
                        {
                            list.DeleteArrayElementAtIndex(i);
                            return;
                        }
                    }

                    // Row 2: the optional blackboard guard.
                    if (string.IsNullOrEmpty(guardKey.stringValue))
                    {
                        using (new EditorGUILayout.HorizontalScope())
                        {
                            GUILayout.Space(6);
                            if (GUILayout.Button("+ Only if a value matches",
                                    EditorStyles.miniButton, GUILayout.Width(170)))
                                guardKey.stringValue = "hp";
                            GUILayout.FlexibleSpace();
                        }
                    }
                    else
                    {
                        using (new EditorGUILayout.HorizontalScope())
                        {
                            GUILayout.Space(6);
                            EditorGUILayout.LabelField("Only if", GUILayout.Width(52));
                            EditorGUILayout.PropertyField(guardKey, GUIContent.none);
                            EditorGUILayout.PropertyField(guardComparison, GUIContent.none,
                                GUILayout.Width(70));
                            EditorGUILayout.PropertyField(guardValue, GUIContent.none,
                                GUILayout.Width(56));
                            if (GUILayout.Button("✕", EditorStyles.miniButton, GUILayout.Width(22)))
                                guardKey.stringValue = string.Empty;
                        }
                    }

                    string explanation = string.IsNullOrEmpty(eventName.stringValue)
                        ? "No event name, so this is checked every time a blackboard value changes."
                        : $"Call runner.SendEvent(\"{eventName.stringValue}\") from your code to fire this.";
                    EditorGUILayout.LabelField(explanation, StoryKitStyles.Summary);
                }
            }
        }

        private void DrawStateDropdown(SerializedProperty targetStateId)
        {
            var labels = new string[_stateNames.Length + 1];
            labels[0] = "(nowhere)";
            Array.Copy(_stateNames, 0, labels, 1, _stateNames.Length);

            int current = Array.IndexOf(_stateIds, targetStateId.stringValue) + 1;
            int picked = EditorGUILayout.Popup(current, labels);
            if (picked != current)
                targetStateId.stringValue = picked == 0 ? string.Empty : _stateIds[picked - 1];
        }

        private void RebuildStateLookup(StateMachineAsset machine)
        {
            var alive = machine.States.Where(state => state != null).ToList();
            _stateNames = alive.Select(state =>
                string.IsNullOrEmpty(state.title) ? "(unnamed)" : state.title).ToArray();
            _stateIds = alive.Select(state => state.Id).ToArray();
        }
    }
}
