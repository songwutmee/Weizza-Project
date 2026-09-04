using System;
using System.Collections.Generic;
using System.Linq;
using StoryKit.Bt;
using StoryKit.Flow;
using UnityEditor;
using UnityEditor.Experimental.GraphView;
using UnityEngine;
using UnityEngine.UIElements;

namespace StoryKit.Editor
{
    /// <summary>
    /// The behaviour tree canvas.
    ///
    /// Parent to child runs downward, and for a composite the order of its
    /// children is decided by their position on screen, left to right. That is
    /// the rule every behaviour tree editor uses, and it means reordering a
    /// Selector's priorities is a drag rather than an argument with a list.
    /// </summary>
    public sealed class BtGraphView : GraphView
    {
        private readonly BehaviorTreeAsset _asset;
        private readonly Action<BtNode> _onSelect;
        private readonly Dictionary<string, BtNodeView> _views = new(StringComparer.Ordinal);

        /// <summary>
        /// Nodes that exist on the canvas but hang off the root. Kept here
        /// because the tree itself has nowhere to store a detached node, and
        /// losing someone's work when they unplug a branch would be rude.
        /// </summary>
        private readonly List<BtNode> _detached = new();

        private BtSearchWindow _search;

        public event Action Changed;

        public BtGraphView(BehaviorTreeAsset asset, Action<BtNode> onSelect)
        {
            _asset = asset;
            _onSelect = onSelect;

            SetupZoom(0.2f, 2.2f);
            this.AddManipulator(new ContentDragger());
            this.AddManipulator(new SelectionDragger());
            this.AddManipulator(new RectangleSelector());

            var grid = new GridBackground();
            Insert(0, grid);
            grid.StretchToParentSize();
            style.backgroundColor = new Color(0.09f, 0.10f, 0.12f);

            _search = ScriptableObject.CreateInstance<BtSearchWindow>();
            _search.Configure(this);
            nodeCreationRequest = context =>
                SearchWindow.Open(new SearchWindowContext(context.screenMousePosition), _search);

            Rebuild();
        }

        // ---------------- Build ----------------

        public void Rebuild()
        {
            graphViewChanged = null;
            DeleteElements(graphElements.ToList());
            _views.Clear();

            foreach (var node in AllNodes())
                AddView(node);

            foreach (var node in AllNodes())
            {
                if (!_views.TryGetValue(node.Id, out var parentView)) continue;

                foreach (var child in ChildrenOf(node))
                {
                    if (child == null || !_views.TryGetValue(child.Id, out var childView)) continue;
                    var edge = parentView.OutputPort.ConnectTo(childView.InputPort);
                    AddElement(edge);
                }
            }

            graphViewChanged = OnGraphChanged;
            Changed?.Invoke();
        }

        private BtNodeView AddView(BtNode node)
        {
            var view = new BtNodeView(node, node == _asset.Root, OnViewSelected);
            _views[node.Id] = view;
            AddElement(view);
            return view;
        }

        /// <summary>Every node the canvas knows about, attached or not.</summary>
        private IEnumerable<BtNode> AllNodes()
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);

            foreach (var node in Walk(_asset.Root))
                if (seen.Add(node.Id)) yield return node;

            for (int i = _detached.Count - 1; i >= 0; i--)
            {
                foreach (var node in Walk(_detached[i]))
                    if (seen.Add(node.Id)) yield return node;
            }
        }

        private static IEnumerable<BtNode> Walk(BtNode node)
        {
            if (node == null) yield break;
            yield return node;

            foreach (var child in ChildrenOf(node))
                foreach (var deeper in Walk(child))
                    yield return deeper;
        }

        private static IEnumerable<BtNode> ChildrenOf(BtNode node)
        {
            if (node is BtComposite composite)
            {
                foreach (var child in composite.Children)
                    if (child != null) yield return child;
            }
            else if (node is BtDecorator decorator && decorator.Child != null)
            {
                yield return decorator.Child;
            }
        }

        public int CountUnattached() => _detached.Count(node => node != null);

        public void RefreshLabels()
        {
            foreach (var view in _views.Values) view.RefreshDisplay();
        }

        public void ShowLive(Dictionary<string, BtStatus> live)
        {
            foreach (var pair in _views)
            {
                BtStatus? status = null;
                if (live != null && live.TryGetValue(pair.Key, out var found)) status = found;
                pair.Value.SetLive(status);
            }
        }

        public void FrameTree() => schedule.Execute(() => FrameAll()).ExecuteLater(10);

        // ---------------- Ports ----------------

        public override List<Port> GetCompatiblePorts(Port startPort, NodeAdapter adapter)
        {
            return ports.Where(candidate =>
                candidate.direction != startPort.direction &&
                candidate.node != startPort.node &&
                !CreatesCycle(startPort, candidate)).ToList();
        }

        /// <summary>
        /// Stops a node being made its own ancestor. A cycle in a behaviour tree
        /// is an infinite tick, which locks the editor as hard as the game.
        /// </summary>
        private static bool CreatesCycle(Port from, Port to)
        {
            var parentView = (from.direction == Direction.Output ? from.node : to.node) as BtNodeView;
            var childView = (from.direction == Direction.Output ? to.node : from.node) as BtNodeView;
            if (parentView == null || childView == null) return false;

            foreach (var node in Walk(childView.Data))
                if (node == parentView.Data) return true;
            return false;
        }

        // ---------------- Change handling ----------------

        private GraphViewChange OnGraphChanged(GraphViewChange change)
        {
            bool dirty = false;

            if (change.edgesToCreate != null)
            {
                RecordUndo("Connect Nodes");
                foreach (var edge in change.edgesToCreate)
                {
                    Attach(edge, connect: true);
                    dirty = true;
                }
            }

            if (change.elementsToRemove != null)
            {
                RecordUndo("Delete");
                foreach (var element in change.elementsToRemove)
                {
                    switch (element)
                    {
                        case Edge edge:
                            Attach(edge, connect: false);
                            dirty = true;
                            break;
                        case BtNodeView view:
                            RemoveNode(view.Data);
                            _views.Remove(view.Data.Id);
                            dirty = true;
                            break;
                    }
                }
            }

            if (change.movedElements != null)
            {
                RecordUndo("Move");
                foreach (var element in change.movedElements)
                    if (element is BtNodeView view)
                        view.Data.Position = view.GetPosition().position;

                // A composite reads its children left to right, so moving a box
                // sideways is how priorities get reordered.
                ReorderChildrenByPosition();
                dirty = true;
            }

            if (dirty)
            {
                EditorUtility.SetDirty(_asset);
                Changed?.Invoke();
            }
            return change;
        }

        private void Attach(Edge edge, bool connect)
        {
            if (edge.output?.node is not BtNodeView parentView ||
                edge.input?.node is not BtNodeView childView)
                return;

            var parent = parentView.Data;
            var child = childView.Data;

            if (parent is BtComposite composite)
            {
                if (connect)
                {
                    if (!composite.Children.Contains(child)) composite.Children.Add(child);
                    _detached.Remove(child);
                }
                else
                {
                    composite.Children.Remove(child);
                    if (!_detached.Contains(child)) _detached.Add(child);
                }
            }
            else if (parent is BtDecorator decorator)
            {
                if (connect)
                {
                    // A decorator wraps one node. Whatever was there is not
                    // deleted, it just becomes detached and stays on the canvas.
                    if (decorator.Child != null && decorator.Child != child &&
                        !_detached.Contains(decorator.Child))
                        _detached.Add(decorator.Child);

                    decorator.Child = child;
                    _detached.Remove(child);
                }
                else if (decorator.Child == child)
                {
                    decorator.Child = null;
                    if (!_detached.Contains(child)) _detached.Add(child);
                }
            }

            ReorderChildrenByPosition();
        }

        /// <summary>Sorts each composite's children by their x position.</summary>
        private void ReorderChildrenByPosition()
        {
            foreach (var node in AllNodes().ToList())
                if (node is BtComposite composite)
                    composite.Children.Sort((a, b) =>
                        a == null ? 1 : b == null ? -1 : a.Position.x.CompareTo(b.Position.x));
        }

        private void RemoveNode(BtNode removed)
        {
            _detached.Remove(removed);

            if (_asset.Root == removed)
            {
                _asset.Root = null;
                return;
            }

            foreach (var node in AllNodes().ToList())
            {
                if (node is BtComposite composite) composite.Children.Remove(removed);
                else if (node is BtDecorator decorator && decorator.Child == removed)
                    decorator.Child = null;
            }

            // Its children are not deleted with it: they stay on the canvas so
            // a mis-click is one undo away rather than a lost subtree.
            foreach (var child in ChildrenOf(removed))
                if (!_detached.Contains(child)) _detached.Add(child);
        }

        // ---------------- Adding ----------------

        public void CreateNode(Type type, Vector2 screenPosition)
        {
            RecordUndo("Add Node");

            var node = (BtNode)Activator.CreateInstance(type);

            var window = EditorWindow.focusedWindow;
            node.Position = window != null
                ? contentViewContainer.WorldToLocal(screenPosition - window.position.position)
                : screenPosition;

            if (_asset.Root == null) _asset.Root = node;
            else _detached.Add(node);

            EditorUtility.SetDirty(_asset);
            Rebuild();

            if (_views.TryGetValue(node.Id, out var view))
            {
                ClearSelection();
                AddToSelection(view);
            }
        }

        public void MakeRoot(BtNode node)
        {
            if (node == null || node == _asset.Root) return;
            RecordUndo("Set Root");

            // Detach it from whoever holds it, then swap the old root out.
            foreach (var other in AllNodes().ToList())
            {
                if (other is BtComposite composite) composite.Children.Remove(node);
                else if (other is BtDecorator decorator && decorator.Child == node)
                    decorator.Child = null;
            }

            if (_asset.Root != null && !_detached.Contains(_asset.Root))
                _detached.Add(_asset.Root);

            _detached.Remove(node);
            _asset.Root = node;

            EditorUtility.SetDirty(_asset);
            Rebuild();
        }

        // ---------------- Auto layout ----------------

        /// <summary>
        /// Lays the tree out top down, giving every subtree exactly the width it
        /// needs. Siblings keep their left to right order, which is also their
        /// priority order, so tidying up never changes behaviour.
        /// </summary>
        public void AutoLayout()
        {
            if (_asset.Root == null) return;
            RecordUndo("Tidy Up");

            const float columnWidth = 220f;
            const float rowHeight = 150f;
            float cursor = 0f;

            float Place(BtNode node, int depth)
            {
                var children = ChildrenOf(node).ToList();
                float x;

                if (children.Count == 0)
                {
                    x = cursor;
                    cursor += columnWidth;
                }
                else
                {
                    float first = Place(children[0], depth + 1);
                    float last = first;
                    for (int i = 1; i < children.Count; i++)
                        last = Place(children[i], depth + 1);
                    x = (first + last) * 0.5f;
                }

                node.Position = new Vector2(x, depth * rowHeight);
                return x;
            }

            Place(_asset.Root, 0);

            // Anything unplugged goes in a row underneath, out of the way.
            float loose = 0f;
            foreach (var node in _detached.Where(n => n != null))
            {
                node.Position = new Vector2(loose, -rowHeight * 1.6f);
                loose += columnWidth;
            }

            foreach (var pair in _views)
            {
                var node = pair.Value.Data;
                pair.Value.SetPosition(new Rect(node.Position, Vector2.zero));
            }

            EditorUtility.SetDirty(_asset);
            FrameTree();
        }

        // ---------------- Context menu ----------------

        public override void BuildContextualMenu(ContextualMenuPopulateEvent evt)
        {
            if (evt.target is BtNodeView view)
            {
                evt.menu.AppendAction("Make this the root", _ => MakeRoot(view.Data));
                evt.menu.AppendSeparator();
            }
            base.BuildContextualMenu(evt);
        }

        private void OnViewSelected(BtNodeView view) => _onSelect?.Invoke(view?.Data);

        private void RecordUndo(string label) =>
            Undo.RegisterCompleteObjectUndo(_asset, label);
    }

    /// <summary>Right-click search listing every BT node by its [NodeMenu] group.</summary>
    public sealed class BtSearchWindow : ScriptableObject, ISearchWindowProvider
    {
        private BtGraphView _view;

        public void Configure(BtGraphView view) => _view = view;

        public List<SearchTreeEntry> CreateSearchTree(SearchWindowContext context)
        {
            var entries = new List<SearchTreeEntry>
            {
                new SearchTreeGroupEntry(new GUIContent("Add node"), 0),
            };

            foreach (var group in NodeListDrawer.BuildMenu<BtNode>()
                         .GroupBy(entry => entry.Group)
                         .OrderBy(group => group.Key))
            {
                entries.Add(new SearchTreeGroupEntry(new GUIContent(group.Key), 1));
                foreach (var entry in group.OrderBy(item => item.Label))
                    entries.Add(new SearchTreeEntry(new GUIContent("   " + entry.Label))
                    {
                        level = 2,
                        userData = entry.Type,
                    });
            }
            return entries;
        }

        public bool OnSelectEntry(SearchTreeEntry entry, SearchWindowContext context)
        {
            if (entry.userData is not Type type) return false;
            _view.CreateNode(type, context.screenMousePosition);
            return true;
        }
    }
}
