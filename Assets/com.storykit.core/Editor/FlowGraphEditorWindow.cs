using StoryKit.Flow;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace StoryKit.Editor
{
    /// <summary>
    /// Main editor window. Double-click a FlowGraphAsset to open.
    /// Left: graph canvas. Right: inspector for the selected node.
    /// </summary>
    public sealed class FlowGraphEditorWindow : EditorWindow
    {
        private FlowGraphAsset _asset;
        private FlowGraphView _graphView;
        private ScrollView _inspector;
        private Label _inspectorTitle;

        [OnOpenAsset]
        public static bool OnOpenAsset(int instanceId, int line)
        {
            if (EditorUtility.InstanceIDToObject(instanceId) is FlowGraphAsset asset)
            {
                Open(asset);
                return true;
            }
            return false;
        }

        public static void Open(FlowGraphAsset asset)
        {
            var window = GetWindow<FlowGraphEditorWindow>();
            window.titleContent = new GUIContent($"Flow · {asset.name}");
            window.LoadAsset(asset);
            window.Show();
        }

        private double _nextDebugPoll;

        private void OnEnable()
        {
            Undo.undoRedoPerformed += OnUndoRedo;
            EditorApplication.update += PollRuntimeDebug;
        }

        private void OnDisable()
        {
            Undo.undoRedoPerformed -= OnUndoRedo;
            EditorApplication.update -= PollRuntimeDebug;
        }

        /// <summary>
        /// Live debugger: while playing, nodes currently executing in any
        /// FlowRunner playing this asset glow on the canvas (~10 Hz poll).
        /// </summary>
        private void PollRuntimeDebug()
        {
            if (_graphView == null || _asset == null) return;
            if (!Application.isPlaying)
            {
                _graphView.HighlightActive(null);
                return;
            }
            if (EditorApplication.timeSinceStartup < _nextDebugPoll) return;
            _nextDebugPoll = EditorApplication.timeSinceStartup + 0.1;

            var active = new System.Collections.Generic.HashSet<string>();
            foreach (var runner in StoryKit.Flow.FlowRunner.ActiveRunners)
                if (runner != null && runner.SourceAsset == _asset)
                    foreach (string id in runner.ActiveNodeIds)
                        active.Add(id);

            _graphView.HighlightActive(active);
        }

        private void OnUndoRedo()
        {
            if (_asset != null)
                _graphView?.Rebuild();
        }

        private void LoadAsset(FlowGraphAsset asset)
        {
            _asset = asset;
            BuildUI();
        }

        private void BuildUI()
        {
            rootVisualElement.Clear();
            if (_asset == null)
            {
                rootVisualElement.Add(new Label("  Open a FlowGraphAsset to edit."));
                return;
            }

            // Toolbar
            var toolbar = new Toolbar();
            toolbar.Add(new ToolbarButton(() => _graphView?.FrameAllNodes()) { text = "⤢ Frame All" });
            toolbar.Add(new ToolbarButton(() => _graphView?.AutoLayout()) { text = "⇄ Tidy Up" });
            toolbar.Add(new ToolbarButton(SaveAsset) { text = "💾 Save" });
            toolbar.Add(new ToolbarSpacer());

            // Live search: type to highlight and jump to matching nodes.
            var search = new ToolbarSearchField();
            search.style.width = 220;
            search.RegisterValueChangedCallback(evt => _graphView?.SearchNodes(evt.newValue));
            toolbar.Add(search);

            var minimapToggle = new ToolbarToggle { text = "Minimap", value = true };
            minimapToggle.RegisterValueChangedCallback(evt => _graphView?.SetMinimapVisible(evt.newValue));
            toolbar.Add(minimapToggle);

            toolbar.Add(new ToolbarSpacer());
            _statusLabel = new Label
            {
                style =
                {
                    unityTextAlign = TextAnchor.MiddleLeft,
                    color = new Color(0.6f, 0.65f, 0.75f),
                    paddingLeft = 6,
                }
            };
            toolbar.Add(_statusLabel);
            rootVisualElement.Add(toolbar);

            // Split: graph | inspector
            var split = new TwoPaneSplitView(1, 320, TwoPaneSplitViewOrientation.Horizontal);
            rootVisualElement.Add(split);
            split.style.flexGrow = 1;

            _graphView = new FlowGraphView(_asset, OnNodeSelected);
            _graphView.style.flexGrow = 1;
            _graphView.GraphChanged += RefreshStatus;
            split.Add(_graphView);
            RefreshStatus();

            var right = new VisualElement();
            right.style.minWidth = 240;
            _inspectorTitle = new Label("  Nothing selected")
            {
                style =
                {
                    unityFontStyleAndWeight = FontStyle.Bold,
                    paddingTop = 8, paddingBottom = 6, paddingLeft = 4,
                }
            };
            right.Add(_inspectorTitle);
            _inspector = new ScrollView();
            _inspector.style.flexGrow = 1;
            right.Add(_inspector);
            split.Add(right);
        }

        private Label _statusLabel;

        /// <summary>Toolbar status: node count plus problems worth fixing.</summary>
        private void RefreshStatus()
        {
            if (_statusLabel == null || _asset == null) return;

            int nodes = _asset.Nodes.Count;
            int problems = 0;
            // The graph works out its own entry, so this only counts as a
            // problem when there is genuinely nothing to start from.
            if (nodes > 0 && string.IsNullOrEmpty(_asset.ResolvedEntryNodeId)) problems++;

            foreach (var node in _asset.Nodes)
            {
                if (node == null) continue;
                // Dead ends: a node that goes nowhere and is not an End node.
                bool goesNowhere = node is not EndNode
                    && node.NextNodeIds.Count == 0
                    && node is not TaskNode { CompletedNextId: not null and not "" }
                    && node is not ConditionNode { TrueNextId: not null and not "" };
                if (goesNowhere) problems++;
            }

            _statusLabel.text = problems == 0
                ? $"{_asset.name}  ·  {nodes} nodes  ·  ✓ no issues"
                : $"{_asset.name}  ·  {nodes} nodes  ·  ⚠ {problems} dead end(s)";
            _statusLabel.style.color = problems == 0
                ? new Color(0.45f, 0.85f, 0.55f)
                : new Color(0.95f, 0.75f, 0.35f);
        }

        private void SaveAsset()
        {
            if (_asset == null) return;
            EditorUtility.SetDirty(_asset);
            AssetDatabase.SaveAssets();
            RefreshStatus();
        }

        private void OnNodeSelected(StoryNode node)
        {
            _inspector.Clear();
            if (node == null)
            {
                _inspectorTitle.text = "  Nothing selected";
                return;
            }

            _inspectorTitle.text = $"  {ObjectNames.NicifyVariableName(node.GetType().Name)}";

            // Node description from [NodeMenu]: what this node does, right where
            // the designer is looking.
            var menuAttribute = (NodeMenuAttribute)System.Attribute.GetCustomAttribute(
                node.GetType(), typeof(NodeMenuAttribute));
            if (!string.IsNullOrEmpty(menuAttribute?.Description))
            {
                _inspector.Add(new Label(menuAttribute.Description)
                {
                    style =
                    {
                        whiteSpace = WhiteSpace.Normal,
                        color = new Color(0.65f, 0.72f, 0.82f),
                        backgroundColor = new Color(0.13f, 0.15f, 0.19f),
                        paddingTop = 6, paddingBottom = 6, paddingLeft = 8, paddingRight = 8,
                        marginBottom = 6, marginLeft = 2, marginRight = 2,
                        borderTopLeftRadius = 4, borderTopRightRadius = 4,
                        borderBottomLeftRadius = 4, borderBottomRightRadius = 4,
                        fontSize = 11,
                    }
                });
            }

            int index = _asset.Nodes.IndexOf(node);
            if (index < 0) return;

            var serialized = new SerializedObject(_asset);
            var listProp = serialized.FindProperty("_nodes");
            if (listProp == null || index >= listProp.arraySize) return;

            var nodeProp = listProp.GetArrayElementAtIndex(index);

            // Title field (stored on the node base)
            var titleProp = nodeProp.FindPropertyRelative("_title");
            if (titleProp != null)
            {
                var titleField = new PropertyField(titleProp, "Title");
                titleField.Bind(serialized);
                _inspector.Add(titleField);
            }

            // Draw every visible child except internal wiring fields.
            var iterator = nodeProp.Copy();
            var end = iterator.GetEndProperty();
            bool enterChildren = true;
            while (iterator.NextVisible(enterChildren) && !SerializedProperty.EqualContents(iterator, end))
            {
                enterChildren = false;
                string name = iterator.name;
                if (name is "_id" or "_title" or "_position" or "_nextNodeIds"
                    or "_runningNextId" or "_completedNextId" or "_trueNextId" or "_falseNextId")
                    continue;

                var field = new PropertyField(iterator.Copy());
                field.Bind(serialized);
                field.RegisterValueChangeCallback(_ => OnNodeEdited(node));
                _inspector.Add(field);
            }
        }

        /// <summary>
        /// A Choice node draws one output port per option, so adding or removing
        /// an option has to rebuild the ports. Renaming one only needs the label
        /// redrawn, which keeps the current selection and view.
        /// </summary>
        private void OnNodeEdited(StoryNode node)
        {
            if (_graphView == null) return;
            _graphView.RefreshNodeTitle(node);

            if (node is IOptionOutputs)
                _graphView.SyncChoicePorts(node);

            RefreshStatus();
        }
    }
}
