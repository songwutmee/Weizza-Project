using System;
using System.Collections.Generic;
using System.Linq;
using StoryKit.Bt;
using UnityEditor;
using UnityEngine;

namespace StoryKit.Editor
{
    /// <summary>
    /// The behaviour tree editor. A tree is only readable as a tree, so this
    /// draws it as indented rows you can add to, reorder and delete in place,
    /// and colours those rows with what each node returned on the last tick
    /// while the game is running.
    /// </summary>
    [CustomEditor(typeof(BehaviorTreeAsset))]
    public sealed class BehaviorTreeAssetInspector : UnityEditor.Editor
    {
        private static readonly Color CompositeColor = new(0.42f, 0.62f, 0.95f);
        private static readonly Color DecoratorColor = new(0.72f, 0.52f, 0.95f);
        private static readonly Color LeafColor = new(0.36f, 0.76f, 0.52f);

        private readonly HashSet<string> _open = new();
        private Dictionary<string, BtStatus> _live;

        public override void OnInspectorGUI()
        {
            var tree = (BehaviorTreeAsset)target;
            serializedObject.Update();

            _live = Application.isPlaying ? CollectLiveStatus(tree) : null;

            var root = serializedObject.FindProperty("_root");

            if (root.managedReferenceValue == null)
            {
                StoryKitStyles.StatusLine(false, "",
                    "No root node yet. Most trees start with a Selector.");
                DrawEmptyState(root);
                serializedObject.ApplyModifiedProperties();
                return;
            }

            StoryKitStyles.StatusLine(true,
                $"{CountNodes(tree.Root)} nodes", "");

            if (GUILayout.Button("Open tree canvas", GUILayout.Height(28)))
                BehaviorTreeGraphWindow.Open(tree);

            EditorGUILayout.LabelField(
                "The canvas is the easier way to read a tree. This list is here for " +
                "quick edits and for trees small enough not to need a diagram.",
                StoryKitStyles.Summary);

            StoryKitStyles.SectionTitle("Tree", CompositeColor);
            DrawNode(root, 0, () => root.managedReferenceValue = null, null, 0);

            if (_live != null) Repaint();
            serializedObject.ApplyModifiedProperties();
        }

        // ---------------- Empty state ----------------

        private static void DrawEmptyState(SerializedProperty root)
        {
            EditorGUILayout.LabelField(
                "A Selector picks the first thing that works: fight if you can, " +
                "otherwise chase, otherwise wander.",
                StoryKitStyles.Summary);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Start with a Selector", GUILayout.Height(26)))
                    root.managedReferenceValue = new BtSelector();
                if (GUILayout.Button("Start with a Sequence", GUILayout.Height(26)))
                    root.managedReferenceValue = new BtSequence();
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.FlexibleSpace();
                NodeListDrawer.ReplaceButton<BtNode>("Something else", root, 120f);
            }
        }

        // ---------------- Rows ----------------

        /// <summary>
        /// Draws one node and everything under it.
        /// <paramref name="remove"/> detaches this node from its parent.
        /// <paramref name="siblings"/> is set only for children of a composite,
        /// which are the only nodes that can be reordered.
        /// </summary>
        private void DrawNode(SerializedProperty nodeProperty, int indent,
            Action remove, SerializedProperty siblings, int siblingIndex)
        {
            object node = nodeProperty.managedReferenceValue;
            if (node == null)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.Space(indent * 16);
                    EditorGUILayout.LabelField("(empty slot)", StoryKitStyles.Summary);
                    NodeListDrawer.ReplaceButton<BtNode>("Pick a node", nodeProperty, 96f);
                }
                return;
            }

            var type = node.GetType();
            string id = nodeProperty.FindPropertyRelative("_id")?.stringValue ?? type.Name;
            bool open = _open.Contains(id);
            Color color = ColorFor(type);

            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Space(indent * 16);

                var bar = GUILayoutUtility.GetRect(3, 18, GUILayout.Width(3));
                StoryKitStyles.DrawRect(bar, color);

                if (GUILayout.Button(open ? "▾" : "▸", EditorStyles.label, GUILayout.Width(14)))
                {
                    if (open) _open.Remove(id); else _open.Add(id);
                }

                string title = nodeProperty.FindPropertyRelative("_title")?.stringValue;
                string label = string.IsNullOrEmpty(title)
                    ? NodeListDrawer.Nice(type)
                    : $"{NodeListDrawer.Nice(type)}  ·  {title}";

                var labelStyle = new GUIStyle(EditorStyles.label) { normal = { textColor = color } };
                EditorGUILayout.LabelField(label, labelStyle);

                if (_live != null && _live.TryGetValue(id, out var status))
                {
                    var pill = GUILayoutUtility.GetRect(58, 16, GUILayout.Width(58));
                    StoryKitStyles.DrawPill(pill, status.ToString(), status switch
                    {
                        BtStatus.Success => StoryKitStyles.Success,
                        BtStatus.Failure => StoryKitStyles.Danger,
                        _ => StoryKitStyles.Warning,
                    });
                }

                if (siblings != null)
                {
                    using (new EditorGUI.DisabledScope(siblingIndex == 0))
                        if (GUILayout.Button("↑", EditorStyles.miniButtonLeft, GUILayout.Width(20)))
                        {
                            siblings.MoveArrayElement(siblingIndex, siblingIndex - 1);
                            return;
                        }

                    using (new EditorGUI.DisabledScope(siblingIndex >= siblings.arraySize - 1))
                        if (GUILayout.Button("↓", EditorStyles.miniButtonRight, GUILayout.Width(20)))
                        {
                            siblings.MoveArrayElement(siblingIndex, siblingIndex + 1);
                            return;
                        }
                }

                if (GUILayout.Button("✕", EditorStyles.miniButton, GUILayout.Width(20)))
                {
                    remove?.Invoke();
                    return;
                }
            }

            if (open)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.Space((indent + 1) * 16);
                    using (new EditorGUILayout.VerticalScope(StoryKitStyles.Card))
                    {
                        string description = NodeListDrawer.DescriptionOf(node);
                        if (!string.IsNullOrEmpty(description))
                            EditorGUILayout.LabelField(description, StoryKitStyles.Summary);

                        EditorGUILayout.PropertyField(
                            nodeProperty.FindPropertyRelative("_title"), new GUIContent("Label"));
                        NodeListDrawer.DrawFields(nodeProperty,
                            "_id", "_title", "_position", "_children", "_child");
                    }
                }
            }

            DrawChildren(nodeProperty, node, indent);
        }

        private void DrawChildren(SerializedProperty nodeProperty, object node, int indent)
        {
            if (node is BtComposite)
            {
                var children = nodeProperty.FindPropertyRelative("_children");
                for (int i = 0; i < children.arraySize; i++)
                {
                    int captured = i;
                    DrawNode(children.GetArrayElementAtIndex(i), indent + 1,
                        () => children.DeleteArrayElementAtIndex(captured), children, i);
                }

                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.Space((indent + 1) * 16);
                    NodeListDrawer.AddButton<BtNode>("+ Add child", children, 100f);
                    GUILayout.FlexibleSpace();
                }
            }
            else if (node is BtDecorator)
            {
                var child = nodeProperty.FindPropertyRelative("_child");
                if (child.managedReferenceValue == null)
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        GUILayout.Space((indent + 1) * 16);
                        EditorGUILayout.LabelField(
                            "This decorator has nothing to wrap.", StoryKitStyles.Summary);
                        NodeListDrawer.ReplaceButton<BtNode>("+ Set child", child, 92f);
                    }
                }
                else
                {
                    DrawNode(child, indent + 1,
                        () => child.managedReferenceValue = null, null, 0);
                }
            }
        }

        // ---------------- Helpers ----------------

        private static Color ColorFor(Type type)
        {
            if (typeof(BtComposite).IsAssignableFrom(type)) return CompositeColor;
            if (typeof(BtDecorator).IsAssignableFrom(type)) return DecoratorColor;
            return LeafColor;
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

        /// <summary>
        /// Runners tick a clone of the asset, and the clone keeps the same node
        /// ids, so the live result of each node can be matched back to the row
        /// being drawn here.
        /// </summary>
        internal static Dictionary<string, BtStatus> CollectLiveStatus(BehaviorTreeAsset asset)
        {
            var runner = UnityEngine.Object
                .FindObjectsByType<BehaviorTreeRunner>(FindObjectsSortMode.None)
                .FirstOrDefault(candidate =>
                candidate.Asset == asset && candidate.RuntimeTree != null);
            if (runner == null) return null;

            var map = new Dictionary<string, BtStatus>(StringComparer.Ordinal);
            Walk(runner.RuntimeTree.Root, map);
            return map.Count > 0 ? map : null;
        }

        private static void Walk(BtNode node, Dictionary<string, BtStatus> into)
        {
            if (node == null) return;

            // Only report nodes that were reached recently, so a stale result
            // from ten seconds ago does not look like something that just ran.
            if (Time.frameCount - node.LastTickFrame <= 2)
                into[node.Id] = node.LastStatus;

            if (node is BtComposite composite)
                foreach (var child in composite.Children) Walk(child, into);
            else if (node is BtDecorator decorator)
                Walk(decorator.Child, into);
        }
    }
}
