using System;
using System.Collections.Generic;
using System.Linq;
using StoryKit.Flow;
using StoryKit.Fsm;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEditor.Experimental.GraphView;
using UnityEngine;
using UnityEngine.UIElements;

namespace StoryKit.Editor
{
    /// <summary>
    /// State machine canvas. Double-click a State Machine asset to open it.
    ///
    /// A boss with five phases is a picture, not a list. Drag a line from one
    /// state to another to make a transition, then say what fires it. While the
    /// game runs the state the boss is actually in lights up.
    /// </summary>
    public sealed class FsmGraphWindow : EditorWindow
    {
        private StateMachineAsset _asset;
        private FsmGraphView _graphView;
        private ScrollView _inspector;
        private Label _inspectorTitle;
        private Label _status;
        private double _nextPoll;

        [OnOpenAsset]
        public static bool OnOpenAsset(int instanceId, int line)
        {
            if (EditorUtility.InstanceIDToObject(instanceId) is StateMachineAsset asset)
            {
                Open(asset);
                return true;
            }
            return false;
        }

        public static void Open(StateMachineAsset asset)
        {
            var window = GetWindow<FsmGraphWindow>();
            window.titleContent = new GUIContent($"States · {asset.name}");
            window.minSize = new Vector2(760, 420);
            window._asset = asset;
            window.BuildUI();
            window.Show();
        }

        private void OnEnable()
        {
            Undo.undoRedoPerformed += Rebuild;
            EditorApplication.update += PollLive;
        }

        private void OnDisable()
        {
            Undo.undoRedoPerformed -= Rebuild;
            EditorApplication.update -= PollLive;
        }

        private void Rebuild()
        {
            if (_asset != null) _graphView?.Rebuild();
        }

        private void PollLive()
        {
            if (_graphView == null || _asset == null) return;

            if (!Application.isPlaying)
            {
                _graphView.ShowLive(null);
                return;
            }

            if (EditorApplication.timeSinceStartup < _nextPoll) return;
            _nextPoll = EditorApplication.timeSinceStartup + 0.1;

            var runner = UnityEngine.Object
                .FindObjectsByType<StateMachineRunner>(FindObjectsSortMode.None)
                .FirstOrDefault(candidate => candidate.Asset == _asset && candidate.IsRunning);

            _graphView.ShowLive(runner != null ? runner.CurrentStateId : null);
        }

        private void BuildUI()
        {
            rootVisualElement.Clear();
            if (_asset == null)
            {
                rootVisualElement.Add(new Label("  Open a State Machine asset to edit."));
                return;
            }

            var toolbar = new UnityEditor.UIElements.Toolbar();
            toolbar.Add(new UnityEditor.UIElements.ToolbarButton(() => _graphView?.FrameStates())
                { text = "Frame All" });
            toolbar.Add(new UnityEditor.UIElements.ToolbarButton(() => _graphView?.AutoLayout())
                { text = "Tidy Up" });
            toolbar.Add(new UnityEditor.UIElements.ToolbarButton(() => _graphView?.AddState())
                { text = "+ State" });
            toolbar.Add(new UnityEditor.UIElements.ToolbarButton(SaveAsset)
                { text = "Save" });
            toolbar.Add(new UnityEditor.UIElements.ToolbarSpacer());

            _status = new Label
            {
                style =
                {
                    unityTextAlign = TextAnchor.MiddleLeft,
                    color = new Color(0.6f, 0.65f, 0.75f),
                    paddingLeft = 6,
                }
            };
            toolbar.Add(_status);
            rootVisualElement.Add(toolbar);

            var split = new TwoPaneSplitView(1, 320, TwoPaneSplitViewOrientation.Horizontal);
            split.style.flexGrow = 1;
            rootVisualElement.Add(split);

            _graphView = new FsmGraphView(_asset, OnSelected);
            _graphView.style.flexGrow = 1;
            _graphView.Changed += RefreshStatus;
            split.Add(_graphView);

            var right = new VisualElement { style = { minWidth = 260 } };
            _inspectorTitle = new Label("  Nothing selected")
            {
                style =
                {
                    unityFontStyleAndWeight = FontStyle.Bold,
                    paddingTop = 8, paddingBottom = 6, paddingLeft = 4,
                }
            };
            right.Add(_inspectorTitle);

            _inspector = new ScrollView { style = { flexGrow = 1 } };
            right.Add(_inspector);
            split.Add(right);

            OnSelected(null, null);
            RefreshStatus();
        }

        private void SaveAsset()
        {
            if (_asset == null) return;
            EditorUtility.SetDirty(_asset);
            AssetDatabase.SaveAssets();
            RefreshStatus();
        }

        private void RefreshStatus()
        {
            if (_status == null || _asset == null) return;

            var problems = new List<StoryProblem>();
            StoryKitValidator.ValidateStateMachine(_asset, problems);
            int errors = problems.Count(problem => problem.IsError);

            _status.text = errors == 0
                ? $"{_asset.name}  ·  {_asset.States.Count} states  ·  no issues"
                : $"{_asset.name}  ·  {_asset.States.Count} states  ·  {errors} problem(s)";
            _status.style.color = errors == 0
                ? new Color(0.45f, 0.85f, 0.55f)
                : new Color(0.95f, 0.75f, 0.35f);
        }

        // ---------------- Right hand inspector ----------------

        /// <summary>Draws the selected state, or the selected transition.</summary>
        private void OnSelected(FsmState state, FsmTransition transition)
        {
            _inspector.Clear();

            if (state == null && transition == null)
            {
                _inspectorTitle.text = "  Nothing selected";
                _inspector.Add(Hint(
                    "Right-click the canvas to add a state.\n\n" +
                    "Drag from the right edge of one state to the left edge of another " +
                    "to make a transition, then click the line to say what fires it."));
                return;
            }

            if (transition != null)
            {
                _inspectorTitle.text = "  Transition";
                _inspector.Add(new IMGUIContainer(() => DrawTransition(transition))
                    { style = { paddingLeft = 6, paddingRight = 6 } });
                return;
            }

            _inspectorTitle.text = "  State";
            _inspector.Add(new IMGUIContainer(() => DrawState(state))
                { style = { paddingLeft = 6, paddingRight = 6 } });
        }

        private static Label Hint(string text) => new(text)
        {
            style =
            {
                whiteSpace = WhiteSpace.Normal,
                paddingLeft = 8, paddingTop = 8, paddingRight = 8,
                color = new Color(0.62f, 0.67f, 0.75f),
            }
        };

        private void DrawState(FsmState state)
        {
            EditorGUI.BeginChangeCheck();

            state.title = EditorGUILayout.TextField("Name", state.title);

            bool isStart = _asset.ResolvedInitialStateId == state.Id;
            using (new EditorGUI.DisabledScope(isStart))
            {
                if (GUILayout.Button(isStart ? "This is the start state" : "Make this the start"))
                {
                    Undo.RegisterCompleteObjectUndo(_asset, "Set Start State");
                    _asset.InitialStateId = state.Id;
                }
            }

            StoryKitStyles.SectionTitle("Runs on enter", StoryKitStyles.Success);
            EditorGUILayout.LabelField(
                $"{state.onEnter.Count} action(s). Edit the list in the asset inspector.",
                StoryKitStyles.Summary);

            StoryKitStyles.SectionTitle("Extras", StoryKitStyles.Accent);
            state.flowGraph = (FlowGraphAsset)EditorGUILayout.ObjectField(
                "Play flow graph", state.flowGraph, typeof(FlowGraphAsset), false);
            EditorGUILayout.LabelField(
                "Runs while this state is active. Finishing it sends \"Fsm.FlowDone\".",
                StoryKitStyles.Summary);

            state.subMachine = (StateMachineAsset)EditorGUILayout.ObjectField(
                "Sub machine", state.subMachine, typeof(StateMachineAsset), false);

            if (EditorGUI.EndChangeCheck())
            {
                EditorUtility.SetDirty(_asset);
                _graphView?.RefreshLabels();
            }
        }

        private void DrawTransition(FsmTransition transition)
        {
            EditorGUI.BeginChangeCheck();

            EditorGUILayout.LabelField("Fires when", EditorStyles.boldLabel);
            transition.eventName = EditorGUILayout.TextField("On event", transition.eventName);
            EditorGUILayout.LabelField(
                string.IsNullOrEmpty(transition.eventName)
                    ? "No event name, so this is checked whenever a blackboard value changes."
                    : $"Call runner.SendEvent(\"{transition.eventName}\") to fire this.",
                StoryKitStyles.Summary);

            EditorGUILayout.Space(4);
            if (string.IsNullOrEmpty(transition.guardKey))
            {
                if (GUILayout.Button("+ Only if a value matches"))
                    transition.guardKey = "hp";
            }
            else
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField("Only if", GUILayout.Width(50));
                    transition.guardKey = EditorGUILayout.TextField(transition.guardKey);
                    transition.guardComparison = (VarComparison)EditorGUILayout.EnumPopup(
                        transition.guardComparison, GUILayout.Width(70));
                    transition.guardValue = EditorGUILayout.FloatField(
                        transition.guardValue, GUILayout.Width(56));
                    if (GUILayout.Button("x", GUILayout.Width(22)))
                        transition.guardKey = string.Empty;
                }
            }

            EditorGUILayout.Space(6);
            var target = _asset.FindState(transition.targetStateId);
            EditorGUILayout.LabelField("Goes to",
                target != null ? target.title : "(nowhere)");

            if (EditorGUI.EndChangeCheck())
            {
                EditorUtility.SetDirty(_asset);
                _graphView?.RefreshLabels();
            }
        }
    }
}
