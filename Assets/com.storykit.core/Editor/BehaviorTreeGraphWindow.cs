using System;
using System.Collections.Generic;
using System.Linq;
using StoryKit.Bt;
using StoryKit.Flow;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEditor.Experimental.GraphView;
using UnityEngine;
using UnityEngine.UIElements;

namespace StoryKit.Editor
{
    /// <summary>
    /// Behaviour tree canvas. Double-click a Behavior Tree asset to open it.
    ///
    /// A tree is a shape, and a shape is unreadable as a list. Eight nodes in an
    /// inspector is a wall of foldouts; eight boxes with lines between them is a
    /// plan you can read in a second. That is the whole reason this exists.
    ///
    /// While the game runs, every box shows what it returned on the last tick,
    /// so you watch the AI decide instead of guessing from logs.
    /// </summary>
    public sealed class BehaviorTreeGraphWindow : EditorWindow
    {
        private BehaviorTreeAsset _asset;
        private BtGraphView _graphView;
        private ScrollView _inspector;
        private Label _inspectorTitle;
        private Label _status;
        private double _nextPoll;

        [OnOpenAsset]
        public static bool OnOpenAsset(int instanceId, int line)
        {
            if (EditorUtility.InstanceIDToObject(instanceId) is BehaviorTreeAsset asset)
            {
                Open(asset);
                return true;
            }
            return false;
        }

        public static void Open(BehaviorTreeAsset asset)
        {
            var window = GetWindow<BehaviorTreeGraphWindow>();
            window.titleContent = new GUIContent($"Tree · {asset.name}");
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

        /// <summary>Colours every box with its last tick result, about 10 times a second.</summary>
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
            _graphView.ShowLive(BehaviorTreeAssetInspector.CollectLiveStatus(_asset));
        }

        private void BuildUI()
        {
            rootVisualElement.Clear();
            if (_asset == null)
            {
                rootVisualElement.Add(new Label("  Open a Behavior Tree asset to edit."));
                return;
            }

            var toolbar = new UnityEditor.UIElements.Toolbar();
            toolbar.Add(new UnityEditor.UIElements.ToolbarButton(() => _graphView?.FrameTree())
                { text = "Frame All" });
            toolbar.Add(new UnityEditor.UIElements.ToolbarButton(() => _graphView?.AutoLayout())
                { text = "Tidy Up" });
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

            var split = new TwoPaneSplitView(1, 300, TwoPaneSplitViewOrientation.Horizontal);
            split.style.flexGrow = 1;
            rootVisualElement.Add(split);

            _graphView = new BtGraphView(_asset, OnSelected);
            _graphView.style.flexGrow = 1;
            _graphView.Changed += RefreshStatus;
            split.Add(_graphView);

            var right = new VisualElement { style = { minWidth = 240 } };
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

            var problems = new List<string>();
            if (_asset.Root == null) problems.Add("no root node");

            int loose = _graphView != null ? _graphView.CountUnattached() : 0;
            if (loose > 0) problems.Add($"{loose} node(s) not attached to the root");

            int count = CountNodes(_asset.Root);
            _status.text = problems.Count == 0
                ? $"{_asset.name}  ·  {count} nodes  ·  no issues"
                : $"{_asset.name}  ·  {count} nodes  ·  {string.Join(", ", problems)}";
            _status.style.color = problems.Count == 0
                ? new Color(0.45f, 0.85f, 0.55f)
                : new Color(0.95f, 0.75f, 0.35f);
        }

        private static int CountNodes(BtNode node)
        {
            if (node == null) return 0;
            int total = 1;
            if (node is BtComposite composite)
                foreach (var child in composite.Children) total += CountNodes(child);
            else if (node is BtDecorator decorator)
                total += CountNodes(decorator.Child);
            return total;
        }

        // ---------------- Right hand inspector ----------------

        private void OnSelected(BtNode node)
        {
            _inspector.Clear();

            if (node == null)
            {
                _inspectorTitle.text = "  Nothing selected";
                _inspector.Add(new Label(
                    "  Right-click the canvas to add a node.\n" +
                    "  Drag from the bottom of a box to the top of another to connect them.")
                {
                    style = { whiteSpace = WhiteSpace.Normal, paddingLeft = 6, paddingTop = 6,
                              color = new Color(0.62f, 0.67f, 0.75f) }
                });
                return;
            }

            _inspectorTitle.text = "  " + NodeListDrawer.Nice(node.GetType());

            string description = NodeListDrawer.DescriptionOf(node);
            if (!string.IsNullOrEmpty(description))
            {
                _inspector.Add(new Label(description)
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

            // The fields are drawn with IMGUI because a SerializeReference node
            // inside a tree has no stable property path for UI Toolkit binding.
            var captured = node;
            _inspector.Add(new IMGUIContainer(() =>
            {
                EditorGUI.BeginChangeCheck();
                DrawNodeFields(captured);
                if (EditorGUI.EndChangeCheck())
                {
                    EditorUtility.SetDirty(_asset);
                    _graphView?.RefreshLabels();
                }
            })
            { style = { paddingLeft = 6, paddingRight = 6 } });
        }

        /// <summary>Draws a node's own fields through reflection, skipping plumbing.</summary>
        private static void DrawNodeFields(BtNode node)
        {
            var serialized = new SerializedObject(
                BtNodeProxy.Wrap(node));
            serialized.Update();

            var titleProperty = serialized.FindProperty("node").FindPropertyRelative("_title");
            if (titleProperty != null)
                EditorGUILayout.PropertyField(titleProperty, new GUIContent("Label"));

            NodeListDrawer.DrawFields(serialized.FindProperty("node"),
                "_id", "_title", "_position", "_children", "_child");

            if (serialized.ApplyModifiedProperties())
                BtNodeProxy.Commit();
        }
    }

    /// <summary>
    /// Holds one BT node so Unity's serialized property system can draw it.
    /// A node lives inside a tree by SerializeReference, and reaching it by
    /// property path from the asset is fragile once the tree is deep, so it is
    /// borrowed into this object for the duration of the inspector draw.
    /// </summary>
    internal sealed class BtNodeProxy : ScriptableObject
    {
        [SerializeReference] public BtNode node;

        private static BtNodeProxy _shared;

        public static BtNodeProxy Wrap(BtNode value)
        {
            if (_shared == null)
            {
                _shared = CreateInstance<BtNodeProxy>();
                _shared.hideFlags = HideFlags.HideAndDontSave;
            }
            _shared.node = value;
            return _shared;
        }

        /// <summary>
        /// Nodes are reference types, so edits made through the proxy already
        /// landed on the node inside the tree. Nothing to copy back.
        /// </summary>
        public static void Commit() { }
    }
}
