using System;
using System.Collections.Generic;
using System.Linq;
using StoryKit.Flow;
using UnityEditor;
using UnityEditor.Experimental.GraphView;
using UnityEngine;
using UnityEngine.UIElements;

namespace StoryKit.Editor
{
    /// <summary>
    /// The graph canvas: drag, connect, right-click to add (searchable),
    /// copy/paste/duplicate, delete with automatic reference cleanup,
    /// undo/redo. Edits write straight into the FlowGraphAsset.
    /// </summary>
    public sealed class FlowGraphView : GraphView
    {
        private readonly FlowGraphAsset _asset;
        private readonly Action<StoryNode> _onSelect;
        private readonly Dictionary<string, FlowNodeView> _views = new(StringComparer.Ordinal);
        private NodeSearchWindow _search;

        private const string PasteDataPrefix = "StoryKitNodes:";

        public FlowGraphView(FlowGraphAsset asset, Action<StoryNode> onSelect)
        {
            _asset = asset;
            _onSelect = onSelect;

            SetupZoom(0.15f, 2.5f);
            this.AddManipulator(new ContentDragger());
            this.AddManipulator(new SelectionDragger());
            this.AddManipulator(new RectangleSelector());

            var grid = new GridBackground();
            Insert(0, grid);
            grid.StretchToParentSize();
            style.backgroundColor = new Color(0.09f, 0.10f, 0.12f);

            _search = ScriptableObject.CreateInstance<NodeSearchWindow>();
            _search.Configure(this);
            nodeCreationRequest = ctx =>
                SearchWindow.Open(new SearchWindowContext(ctx.screenMousePosition), _search);

            graphViewChanged = OnGraphChanged;
            serializeGraphElements = OnCopy;
            unserializeAndPaste = OnPaste;
            canPasteSerializedData = data => data != null && data.StartsWith(PasteDataPrefix, StringComparison.Ordinal);

            SetMinimapVisible(true);
            Rebuild();
        }

        // ---------------- Build ----------------

        public void Rebuild()
        {
            graphViewChanged = null;
            DeleteElements(graphElements.ToList());
            _views.Clear();

            foreach (var node in _asset.Nodes)
            {
                if (node == null) continue;
                var view = new FlowNodeView(node, _asset.ResolvedEntryNodeId == node.Id, OnViewSelected);
                _views[node.Id] = view;
                AddElement(view);
            }

            foreach (var node in _asset.Nodes)
            {
                if (node == null || !_views.TryGetValue(node.Id, out var from)) continue;

                foreach (var (portName, targetId) in EnumerateOutputs(node))
                {
                    if (string.IsNullOrEmpty(targetId)) continue;
                    if (!_views.TryGetValue(targetId, out var to)) continue;
                    var outPort = from.GetOutputPort(portName);
                    if (outPort == null) continue;
                    var edge = outPort.ConnectTo(to.InputPort);
                    AddElement(edge);
                }
            }

            graphViewChanged = OnGraphChanged;
        }

        /// <summary>Frames every node. Named distinctly so it does not hide
        /// GraphView.FrameAll(), and deferred a frame so layout is ready.</summary>
        public void FrameAllNodes() =>
            schedule.Execute(() => FrameAll()).ExecuteLater(10);

        public void RefreshNodeTitle(StoryNode node)
        {
            if (node != null && _views.TryGetValue(node.Id, out var view))
                view.RefreshDisplay();
        }

        /// <summary>
        /// Keeps a Choice node's ports in step with its option list. Rebuilds the
        /// whole graph only when the option count changed, because that is the
        /// only case where the number of ports is wrong.
        /// </summary>
        public void SyncChoicePorts(StoryNode node)
        {
            if (node is not IOptionOutputs options) return;
            if (!_views.TryGetValue(node.Id, out var view)) return;

            if (view.OptionPortCount != options.OptionBranches.Count)
            {
                var selectedId = node.Id;
                Rebuild();
                if (_views.TryGetValue(selectedId, out var rebuilt))
                {
                    ClearSelection();
                    AddToSelection(rebuilt);
                }
                return;
            }

            view.RefreshOptionLabels();
        }

        /// <summary>Play-mode debugger: glow the nodes currently executing. Null clears.</summary>
        public void HighlightActive(ICollection<string> activeIds)
        {
            foreach (var pair in _views)
                pair.Value.SetRuntimeActive(activeIds != null && activeIds.Contains(pair.Key));
        }

        /// <summary>Fired whenever the graph structure changes (toolbar status).</summary>
        public event Action GraphChanged;

        // ---------------- Search ----------------

        /// <summary>Dims nodes that do not match, frames the first that does.</summary>
        public void SearchNodes(string query)
        {
            bool empty = string.IsNullOrWhiteSpace(query);
            FlowNodeView first = null;

            foreach (var view in _views.Values)
            {
                bool match = empty || Matches(view.Data, query);
                view.SetDimmed(!empty && !match);
                if (match && !empty && first == null) first = view;
            }

            if (first != null)
            {
                ClearSelection();
                AddToSelection(first);
                FrameSelection();
            }
        }

        private static bool Matches(StoryNode node, string query)
        {
            query = query.Trim();
            return Contains(node.Title, query)
                || Contains(node.GetType().Name, query)
                || Contains(node.GetSummary(), query);
        }

        private static bool Contains(string text, string query) =>
            !string.IsNullOrEmpty(text) &&
            text.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;

        // ---------------- Minimap ----------------

        private MiniMap _miniMap;

        public void SetMinimapVisible(bool visible)
        {
            if (visible)
            {
                if (_miniMap == null)
                {
                    _miniMap = new MiniMap { anchored = true };
                    _miniMap.SetPosition(new Rect(12, 12, 200, 140));
                    Add(_miniMap);
                }
                _miniMap.style.display = DisplayStyle.Flex;
            }
            else if (_miniMap != null)
            {
                _miniMap.style.display = DisplayStyle.None;
            }
        }

        // ---------------- Auto layout ----------------

        /// <summary>
        /// Tidy Up: lays nodes out left-to-right in execution order using a
        /// breadth-first walk from the entry node, so a messy graph becomes
        /// readable in one click. Orphans go in a row underneath.
        /// </summary>
        public void AutoLayout()
        {
            if (_asset.Nodes.Count == 0) return;
            RecordUndo("Tidy Up Graph");

            const float columnWidth = 300f;
            const float rowHeight = 170f;

            var depth = new Dictionary<string, int>(StringComparer.Ordinal);
            var rowInColumn = new Dictionary<int, int>();
            var queue = new Queue<(string id, int level)>();

            if (!string.IsNullOrEmpty(_asset.ResolvedEntryNodeId))
                queue.Enqueue((_asset.ResolvedEntryNodeId, 0));

            while (queue.Count > 0)
            {
                var (id, level) = queue.Dequeue();
                if (string.IsNullOrEmpty(id) || depth.ContainsKey(id)) continue;

                var node = _asset.FindNode(id);
                if (node == null) continue;
                depth[id] = level;

                foreach (var (_, targetId) in EnumerateOutputs(node))
                    if (!string.IsNullOrEmpty(targetId) && !depth.ContainsKey(targetId))
                        queue.Enqueue((targetId, level + 1));
            }

            int orphanRow = 0;
            int maxLevel = 0;
            foreach (int level in depth.Values) maxLevel = Mathf.Max(maxLevel, level);

            foreach (var node in _asset.Nodes)
            {
                if (node == null) continue;

                int column, row;
                if (depth.TryGetValue(node.Id, out int level))
                {
                    column = level;
                    rowInColumn.TryGetValue(column, out row);
                    rowInColumn[column] = row + 1;
                }
                else
                {
                    column = 0;
                    row = ++orphanRow;
                    // Orphans sit below the main flow.
                    node.Position = new Vector2(-columnWidth, row * rowHeight);
                    if (_views.TryGetValue(node.Id, out var orphanView))
                        orphanView.SetPosition(new Rect(node.Position, Vector2.zero));
                    continue;
                }

                node.Position = new Vector2(column * columnWidth, row * rowHeight);
                if (_views.TryGetValue(node.Id, out var view))
                    view.SetPosition(new Rect(node.Position, Vector2.zero));
            }

            EditorUtility.SetDirty(_asset);
            FrameAllNodes();
        }

        /// <summary>Live debug: glow the nodes whose ids are in the set (null = clear).</summary>
        public void HighlightActive(HashSet<string> activeIds)
        {
            foreach (var pair in _views)
                pair.Value.SetRuntimeActive(activeIds != null && activeIds.Contains(pair.Key));
        }

        private static IEnumerable<(string port, string target)> EnumerateOutputs(StoryNode node)
        {
            switch (node)
            {
                case TaskNode task:
                    yield return (FlowNodeView.PortRunning, task.RunningNextId);
                    yield return (FlowNodeView.PortCompleted, task.CompletedNextId);
                    break;
                case ConditionNode condition:
                    yield return (FlowNodeView.PortTrue, condition.TrueNextId);
                    yield return (FlowNodeView.PortFalse, condition.FalseNextId);
                    break;
                case EndNode:
                    break;
                default:
                {
                    bool named = false;

                    if (node is IOptionOutputs options)
                    {
                        var branches = options.OptionBranches;
                        for (int i = 0; i < branches.Count; i++)
                            yield return (FlowNodeView.PortChoicePrefix + i, branches[i].TargetId);
                        named |= branches.Count > 0;
                    }

                    if (node is IFixedOutputs fixedOutputs)
                    {
                        int count = fixedOutputs.OutputNames.Length;
                        for (int i = 0; i < count; i++)
                            yield return (FlowNodeView.PortFixedPrefix + i, node.GetFixedOutput(i));
                        named |= count > 0;
                    }

                    if (!named)
                        foreach (string id in node.NextNodeIds)
                            yield return (FlowNodeView.PortOut, id);
                    break;
                }
            }
        }

        private void OnViewSelected(FlowNodeView view) => _onSelect?.Invoke(view?.Data);

        // ---------------- Ports ----------------

        public override List<Port> GetCompatiblePorts(Port startPort, NodeAdapter adapter)
        {
            return ports.Where(p =>
                p.direction != startPort.direction &&
                p.node != startPort.node).ToList();
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
                    ApplyEdge(edge, connect: true);
                    dirty = true;
                }
            }

            if (change.elementsToRemove != null)
            {
                RecordUndo("Delete Elements");
                foreach (var element in change.elementsToRemove)
                {
                    switch (element)
                    {
                        case Edge edge:
                            ApplyEdge(edge, connect: false);
                            dirty = true;
                            break;
                        case FlowNodeView view:
                            RemoveNodeWithCleanup(view.Data);
                            _views.Remove(view.Data.Id);
                            dirty = true;
                            break;
                    }
                }
            }

            if (change.movedElements != null)
            {
                RecordUndo("Move Nodes");
                foreach (var element in change.movedElements)
                    if (element is FlowNodeView view)
                    {
                        view.Data.Position = view.GetPosition().position;
                        dirty = true;
                    }
            }

            if (dirty)
            {
                EditorUtility.SetDirty(_asset);
                GraphChanged?.Invoke();
            }
            return change;
        }

        private void ApplyEdge(Edge edge, bool connect)
        {
            if (edge.output?.node is not FlowNodeView from ||
                edge.input?.node is not FlowNodeView to)
                return;

            string portName = (string)edge.output.userData;
            string targetId = connect ? to.Data.Id : null;
            var node = from.Data;

            switch (portName)
            {
                case FlowNodeView.PortRunning when node is TaskNode task:
                    task.RunningNextId = targetId;
                    break;
                case FlowNodeView.PortCompleted when node is TaskNode task:
                    task.CompletedNextId = targetId;
                    break;
                case FlowNodeView.PortTrue when node is ConditionNode condition:
                    condition.TrueNextId = targetId;
                    break;
                case FlowNodeView.PortFalse when node is ConditionNode condition:
                    condition.FalseNextId = targetId;
                    break;

                case not null when node is IOptionOutputs options
                                   && portName.StartsWith(FlowNodeView.PortChoicePrefix,
                                       StringComparison.Ordinal):
                {
                    string indexText = portName.Substring(FlowNodeView.PortChoicePrefix.Length);
                    if (int.TryParse(indexText, out int optionIndex))
                        options.SetOptionTarget(optionIndex, targetId);
                    break;
                }

                case not null when node is IFixedOutputs
                                   && portName.StartsWith(FlowNodeView.PortFixedPrefix,
                                       StringComparison.Ordinal):
                {
                    string indexText = portName.Substring(FlowNodeView.PortFixedPrefix.Length);
                    if (int.TryParse(indexText, out int slot))
                        node.SetFixedOutput(slot, targetId);
                    break;
                }

                default: // PortOut → NextNodeIds list
                    if (connect)
                    {
                        if (!node.NextNodeIds.Contains(to.Data.Id))
                            node.NextNodeIds.Add(to.Data.Id);
                    }
                    else
                    {
                        node.NextNodeIds.Remove(to.Data.Id);
                    }
                    break;
            }
        }

        /// <summary>Deletes a node and strips every reference to it from the graph.</summary>
        private void RemoveNodeWithCleanup(StoryNode removed)
        {
            _asset.Nodes.Remove(removed);
            string deadId = removed.Id;

            foreach (var node in _asset.Nodes)
            {
                if (node == null) continue;

                // A fixed-output node keeps its slots: removing the entry would
                // shift every later output up one and silently rewire the node.
                if (node is IFixedOutputs)
                {
                    for (int i = 0; i < node.NextNodeIds.Count; i++)
                        if (node.NextNodeIds[i] == deadId) node.NextNodeIds[i] = string.Empty;
                }
                else
                {
                    node.NextNodeIds.RemoveAll(id => id == deadId);
                }
                if (node is TaskNode task)
                {
                    if (task.RunningNextId == deadId) task.RunningNextId = null;
                    if (task.CompletedNextId == deadId) task.CompletedNextId = null;
                }
                if (node is ConditionNode condition)
                {
                    if (condition.TrueNextId == deadId) condition.TrueNextId = null;
                    if (condition.FalseNextId == deadId) condition.FalseNextId = null;
                }
                if (node is CancelNode cancel && cancel.TargetNodeId == deadId)
                    cancel.TargetNodeId = null;
                if (node is IOptionOutputs options)
                {
                    var branches = options.OptionBranches;
                    for (int i = 0; i < branches.Count; i++)
                        if (branches[i].TargetId == deadId) options.SetOptionTarget(i, null);
                }
            }

            if (_asset.EntryNodeId == deadId)
                _asset.EntryNodeId = null;
        }

        // ---------------- Add node ----------------

        public void CreateNode(Type nodeType, Vector2 screenPosition)
        {
            RecordUndo("Add Node");

            var node = (StoryNode)Activator.CreateInstance(nodeType);
            node.Title = ObjectNames.NicifyVariableName(nodeType.Name).Replace(" Node", "");

            var window = EditorWindow.focusedWindow;
            Vector2 worldPos = window != null
                ? contentViewContainer.WorldToLocal(screenPosition - window.position.position)
                : screenPosition;
            node.Position = worldPos;

            string entryBefore = _asset.ResolvedEntryNodeId;
            _asset.AddNode(node);
            EditorUtility.SetDirty(_asset);

            // Adding a Start node can move the automatic entry, and the badge
            // on the old node would go stale, so redraw instead of patching.
            if (_asset.ResolvedEntryNodeId != entryBefore && entryBefore != null)
            {
                Rebuild();
                return;
            }

            var view = new FlowNodeView(node, _asset.ResolvedEntryNodeId == node.Id, OnViewSelected);
            _views[node.Id] = view;
            AddElement(view);
        }

        /// <summary>
        /// Pins the entry. Choosing the node that is already the automatic entry
        /// unpins it again, so the option works as a toggle.
        /// </summary>
        public void SetAsEntry(StoryNode node)
        {
            RecordUndo("Set Entry");
            _asset.EntryNodeId = _asset.EntryNodeId == node.Id ? null : node.Id;
            EditorUtility.SetDirty(_asset);
            Rebuild();
        }

        // ---------------- Copy / Paste ----------------

        [Serializable]
        private sealed class ClipboardHolder : ScriptableObject
        {
            [SerializeReference] public List<StoryNode> nodes = new();
        }

        private string OnCopy(IEnumerable<GraphElement> elements)
        {
            var holder = ScriptableObject.CreateInstance<ClipboardHolder>();
            foreach (var element in elements)
                if (element is FlowNodeView view && view.Data is not StartNode)
                    holder.nodes.Add(view.Data);

            if (holder.nodes.Count == 0)
            {
                UnityEngine.Object.DestroyImmediate(holder);
                return null;
            }

            string json = PasteDataPrefix + EditorJsonUtility.ToJson(holder);
            UnityEngine.Object.DestroyImmediate(holder);
            return json;
        }

        private void OnPaste(string operation, string data)
        {
            if (string.IsNullOrEmpty(data) || !data.StartsWith(PasteDataPrefix, StringComparison.Ordinal))
                return;

            var holder = ScriptableObject.CreateInstance<ClipboardHolder>();
            EditorJsonUtility.FromJsonOverwrite(data.Substring(PasteDataPrefix.Length), holder);

            RecordUndo("Paste Nodes");
            ClearSelection();

            // Remap ids so internal connections between pasted nodes survive,
            // while references to non-copied nodes are dropped.
            var idMap = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var node in holder.nodes)
            {
                string oldId = node.Id;
                node.Id = Guid.NewGuid().ToString("N");
                idMap[oldId] = node.Id;
            }

            foreach (var node in holder.nodes)
            {
                bool keepsSlots = node is IFixedOutputs;
                for (int i = node.NextNodeIds.Count - 1; i >= 0; i--)
                {
                    if (idMap.TryGetValue(node.NextNodeIds[i], out string mapped))
                        node.NextNodeIds[i] = mapped;
                    else if (keepsSlots)
                        node.NextNodeIds[i] = string.Empty;   // blank, do not shift
                    else
                        node.NextNodeIds.RemoveAt(i);
                }
                if (node is TaskNode task)
                {
                    task.RunningNextId = idMap.GetValueOrDefault(task.RunningNextId);
                    task.CompletedNextId = idMap.GetValueOrDefault(task.CompletedNextId);
                }
                if (node is ConditionNode condition)
                {
                    condition.TrueNextId = idMap.GetValueOrDefault(condition.TrueNextId);
                    condition.FalseNextId = idMap.GetValueOrDefault(condition.FalseNextId);
                }
                if (node is CancelNode cancel)
                    cancel.TargetNodeId = idMap.GetValueOrDefault(cancel.TargetNodeId);
                if (node is IOptionOutputs options)
                {
                    var branches = options.OptionBranches;
                    for (int i = 0; i < branches.Count; i++)
                        options.SetOptionTarget(i,
                            idMap.GetValueOrDefault(branches[i].TargetId));
                }

                node.Position += new Vector2(40, 40);
                _asset.AddNode(node);
            }

            UnityEngine.Object.DestroyImmediate(holder);
            EditorUtility.SetDirty(_asset);
            Rebuild();

            foreach (var node in idMap.Values)
                if (_views.TryGetValue(node, out var view))
                    AddToSelection(view);
        }

        private void RecordUndo(string label) => Undo.RegisterCompleteObjectUndo(_asset, label);

        // ---------------- Context menu ----------------

        public override void BuildContextualMenu(ContextualMenuPopulateEvent evt)
        {
            if (evt.target is FlowNodeView view)
            {
                bool pinned = _asset.EntryNodeId == view.Data.Id;
                evt.menu.AppendAction(pinned ? "Unpin Entry (back to automatic)" : "Set as Entry",
                    _ => SetAsEntry(view.Data));
                evt.menu.AppendSeparator();
            }
            base.BuildContextualMenu(evt);
        }
    }

    /// <summary>Right-click search window listing every node type by [NodeMenu] path.</summary>
    public sealed class NodeSearchWindow : ScriptableObject, ISearchWindowProvider
    {
        private FlowGraphView _view;

        public void Configure(FlowGraphView view) => _view = view;

        public List<SearchTreeEntry> CreateSearchTree(SearchWindowContext context)
        {
            var entries = new List<SearchTreeEntry>
            {
                new SearchTreeGroupEntry(new GUIContent("Add Node"), 0),
            };

            var byGroup = TypeCache.GetTypesDerivedFrom<StoryNode>()
                .Where(t => !t.IsAbstract)
                .Select(t => (type: t, menu: t.GetCustomAttributes(typeof(NodeMenuAttribute), false)
                    .Cast<NodeMenuAttribute>().FirstOrDefault()?.Path ?? "Other"))
                .GroupBy(x => x.menu)
                .OrderBy(g => g.Key);

            foreach (var group in byGroup)
            {
                entries.Add(new SearchTreeGroupEntry(new GUIContent(group.Key), 1));
                foreach (var (type, _) in group.OrderBy(x => x.type.Name))
                {
                    string label = UnityEditor.ObjectNames.NicifyVariableName(type.Name).Replace(" Node", "");
                    entries.Add(new SearchTreeEntry(new GUIContent(label))
                    {
                        level = 2,
                        userData = type,
                    });
                }
            }
            return entries;
        }

        public bool OnSelectEntry(SearchTreeEntry entry, SearchWindowContext context)
        {
            if (entry.userData is Type type)
            {
                _view.CreateNode(type, context.screenMousePosition);
                return true;
            }
            return false;
        }
    }
}
