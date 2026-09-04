using System;
using System.Collections.Generic;
using System.Linq;
using StoryKit.Fsm;
using UnityEditor;
using UnityEditor.Experimental.GraphView;
using UnityEngine;
using UnityEngine.UIElements;

namespace StoryKit.Editor
{
    /// <summary>
    /// The state machine canvas. A box per state, an arrow per transition.
    ///
    /// Clicking an arrow selects the transition, which is the part that carries
    /// the interesting information: what event fires it and what has to be true.
    /// </summary>
    public sealed class FsmGraphView : GraphView
    {
        private readonly StateMachineAsset _asset;
        private readonly Action<FsmState, FsmTransition> _onSelect;
        private readonly Dictionary<string, FsmStateView> _views = new(StringComparer.Ordinal);
        private readonly Dictionary<Edge, FsmTransition> _edges = new();

        public event Action Changed;

        public FsmGraphView(StateMachineAsset asset, Action<FsmState, FsmTransition> onSelect)
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

            RegisterCallback<MouseDownEvent>(OnMouseDown);
            Rebuild();
        }

        // ---------------- Build ----------------

        public void Rebuild()
        {
            graphViewChanged = null;
            DeleteElements(graphElements.ToList());
            _views.Clear();
            _edges.Clear();

            _asset.EnsureIds();

            foreach (var state in _asset.States)
            {
                if (state == null) continue;
                var view = new FsmStateView(state,
                    state.Id == _asset.ResolvedInitialStateId, OnStateSelected);
                _views[state.Id] = view;
                AddElement(view);
            }

            foreach (var state in _asset.States)
            {
                if (state == null || !_views.TryGetValue(state.Id, out var from)) continue;

                foreach (var transition in state.transitions)
                {
                    if (transition == null) continue;
                    if (!_views.TryGetValue(transition.targetStateId ?? string.Empty, out var to))
                        continue;

                    var edge = from.OutputPort.ConnectTo(to.InputPort);
                    edge.edgeControl.inputColor = EdgeColour(transition);
                    edge.edgeControl.outputColor = EdgeColour(transition);
                    _edges[edge] = transition;
                    AddElement(edge);
                }
            }

            graphViewChanged = OnGraphChanged;
            Changed?.Invoke();
        }

        /// <summary>Amber for an event, blue for a value-driven auto transition.</summary>
        private static Color EdgeColour(FsmTransition transition) =>
            string.IsNullOrEmpty(transition.eventName)
                ? new Color(0.40f, 0.70f, 0.95f)
                : new Color(0.95f, 0.75f, 0.35f);

        public void RefreshLabels()
        {
            foreach (var view in _views.Values) view.RefreshDisplay();
        }

        public void ShowLive(string currentStateId)
        {
            foreach (var pair in _views)
                pair.Value.SetLive(pair.Key == currentStateId);
        }

        public void FrameStates() => schedule.Execute(() => FrameAll()).ExecuteLater(10);

        // ---------------- Selection ----------------

        private void OnStateSelected(FsmStateView view) =>
            _onSelect?.Invoke(view?.Data, null);

        /// <summary>
        /// GraphView does not report a selected edge through the node callback,
        /// so the click is caught here. Selecting the arrow is the whole point:
        /// the transition holds the event name and the guard.
        /// </summary>
        private void OnMouseDown(MouseDownEvent evt)
        {
            if (evt.target is not Edge edge) return;
            if (_edges.TryGetValue(edge, out var transition))
                _onSelect?.Invoke(null, transition);
        }

        // ---------------- Ports ----------------

        public override List<Port> GetCompatiblePorts(Port startPort, NodeAdapter adapter)
        {
            // A state may transition to itself, so only the direction matters.
            return ports.Where(candidate => candidate.direction != startPort.direction).ToList();
        }

        // ---------------- Change handling ----------------

        private GraphViewChange OnGraphChanged(GraphViewChange change)
        {
            bool dirty = false;

            if (change.edgesToCreate != null)
            {
                RecordUndo("Add Transition");
                foreach (var edge in change.edgesToCreate)
                {
                    if (edge.output?.node is not FsmStateView from ||
                        edge.input?.node is not FsmStateView to) continue;

                    var transition = new FsmTransition
                    {
                        eventName = string.Empty,
                        guardKey = string.Empty,
                        guardValue = 1f,
                        targetStateId = to.Data.Id,
                    };
                    from.Data.transitions.Add(transition);
                    _edges[edge] = transition;

                    // Show it straight away: a new arrow with nothing on it is
                    // the moment you want to type the event name.
                    _onSelect?.Invoke(null, transition);
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
                        case Edge edge when _edges.TryGetValue(edge, out var transition):
                            foreach (var state in _asset.States)
                                state?.transitions.Remove(transition);
                            _edges.Remove(edge);
                            dirty = true;
                            break;

                        case FsmStateView view:
                            RemoveState(view.Data);
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
                    if (element is FsmStateView view)
                        view.Data.position = view.GetPosition().position;
                dirty = true;
            }

            if (dirty)
            {
                EditorUtility.SetDirty(_asset);
                Changed?.Invoke();
            }
            return change;
        }

        private void RemoveState(FsmState removed)
        {
            _asset.States.Remove(removed);

            // Transitions pointing at a deleted state would do nothing at all,
            // which is worse than being gone, so they go too.
            foreach (var state in _asset.States)
                state?.transitions.RemoveAll(t => t != null && t.targetStateId == removed.Id);
            _asset.GlobalTransitions.RemoveAll(t => t != null && t.targetStateId == removed.Id);

            if (_asset.InitialStateId == removed.Id) _asset.InitialStateId = null;
        }

        // ---------------- Adding ----------------

        public void AddState() => AddStateAt(new Vector2(60, 60) + ScatterOffset());

        private Vector2 ScatterOffset() =>
            new(_asset.States.Count % 4 * 220f, _asset.States.Count / 4 * 150f);

        public void AddStateAt(Vector2 position)
        {
            RecordUndo("Add State");

            var state = _asset.AddState($"State {_asset.States.Count + 1}");
            state.position = position;

            EditorUtility.SetDirty(_asset);
            Rebuild();

            if (_views.TryGetValue(state.Id, out var view))
            {
                ClearSelection();
                AddToSelection(view);
                _onSelect?.Invoke(state, null);
            }
        }

        // ---------------- Auto layout ----------------

        /// <summary>
        /// Lays states out left to right in the order they can first be reached,
        /// so the shape of the machine matches the order it runs in.
        /// </summary>
        public void AutoLayout()
        {
            if (_asset.States.Count == 0) return;
            RecordUndo("Tidy Up");

            const float columnWidth = 260f;
            const float rowHeight = 140f;

            var depth = new Dictionary<string, int>(StringComparer.Ordinal);
            var rowsUsed = new Dictionary<int, int>();
            var queue = new Queue<(string id, int level)>();

            string start = _asset.ResolvedInitialStateId;
            if (!string.IsNullOrEmpty(start)) queue.Enqueue((start, 0));

            while (queue.Count > 0)
            {
                var (id, level) = queue.Dequeue();
                if (string.IsNullOrEmpty(id) || depth.ContainsKey(id)) continue;

                var state = _asset.FindState(id);
                if (state == null) continue;
                depth[id] = level;

                foreach (var transition in state.transitions)
                    if (transition != null && !string.IsNullOrEmpty(transition.targetStateId))
                        queue.Enqueue((transition.targetStateId, level + 1));
            }

            int orphanRow = 0;
            foreach (var state in _asset.States)
            {
                if (state == null) continue;

                if (depth.TryGetValue(state.Id, out int level))
                {
                    rowsUsed.TryGetValue(level, out int row);
                    rowsUsed[level] = row + 1;
                    state.position = new Vector2(level * columnWidth, row * rowHeight);
                }
                else
                {
                    // Never reachable: park it below so it is obviously separate.
                    state.position = new Vector2(orphanRow * columnWidth, -rowHeight * 2f);
                    orphanRow++;
                }
            }

            foreach (var pair in _views)
                pair.Value.SetPosition(new Rect(pair.Value.Data.position, Vector2.zero));

            EditorUtility.SetDirty(_asset);
            FrameStates();
        }

        // ---------------- Context menu ----------------

        public override void BuildContextualMenu(ContextualMenuPopulateEvent evt)
        {
            if (evt.target is FsmStateView view)
            {
                evt.menu.AppendAction("Make this the start", _ =>
                {
                    RecordUndo("Set Start State");
                    _asset.InitialStateId = view.Data.Id;
                    EditorUtility.SetDirty(_asset);
                    Rebuild();
                });
                evt.menu.AppendSeparator();
            }
            else if (evt.target == this)
            {
                var local = contentViewContainer.WorldToLocal(evt.localMousePosition);
                evt.menu.AppendAction("Add state", _ => AddStateAt(local));
                evt.menu.AppendSeparator();
            }

            base.BuildContextualMenu(evt);
        }

        private void RecordUndo(string label) =>
            Undo.RegisterCompleteObjectUndo(_asset, label);
    }

    /// <summary>One state box on the canvas.</summary>
    public sealed class FsmStateView : Node
    {
        public FsmState Data { get; }
        public Port InputPort { get; private set; }
        public Port OutputPort { get; private set; }

        private readonly Action<FsmStateView> _onSelected;
        private readonly bool _isStart;
        private Label _summary;
        private bool _live;

        private static readonly Color StateColor = new(0.42f, 0.62f, 0.95f);
        private static readonly Color StartColor = new(0.15f, 0.75f, 0.45f);

        public FsmStateView(FsmState data, bool isStart, Action<FsmStateView> onSelected)
        {
            Data = data;
            _isStart = isStart;
            _onSelected = onSelected;

            InputPort = InstantiatePort(Orientation.Horizontal, Direction.Input,
                Port.Capacity.Multi, typeof(bool));
            InputPort.portName = "in";
            inputContainer.Add(InputPort);

            OutputPort = InstantiatePort(Orientation.Horizontal, Direction.Output,
                Port.Capacity.Multi, typeof(bool));
            OutputPort.portName = "out";
            outputContainer.Add(OutputPort);

            var tint = isStart ? StartColor : StateColor;
            titleContainer.style.backgroundColor = new StyleColor(tint * 0.55f);
            titleContainer.style.borderBottomWidth = 2;
            titleContainer.style.borderBottomColor = tint;

            _summary = new Label
            {
                style =
                {
                    color = new Color(0.75f, 0.8f, 0.9f, 0.9f),
                    fontSize = 10,
                    paddingLeft = 8, paddingRight = 8, paddingBottom = 4,
                    whiteSpace = WhiteSpace.Normal,
                    maxWidth = 200,
                }
            };
            mainContainer.Insert(1, _summary);

            RefreshDisplay();
            SetPosition(new Rect(data.position, Vector2.zero));
            RefreshExpandedState();
            RefreshPorts();
        }

        public void RefreshDisplay()
        {
            title = (_isStart ? "* " : string.Empty) +
                    (string.IsNullOrEmpty(Data.title) ? "(unnamed)" : Data.title);

            var parts = new List<string>();
            if (Data.onEnter.Count > 0) parts.Add($"{Data.onEnter.Count} on enter");
            if (Data.onExit.Count > 0) parts.Add($"{Data.onExit.Count} on exit");
            if (Data.flowGraph != null) parts.Add($"plays {Data.flowGraph.name}");
            if (Data.subMachine != null) parts.Add($"sub {Data.subMachine.name}");

            _summary.text = string.Join("  ·  ", parts);
            _summary.style.display = parts.Count == 0 ? DisplayStyle.None : DisplayStyle.Flex;
        }

        /// <summary>Highlights the state the running machine is actually in.</summary>
        public void SetLive(bool active)
        {
            if (_live == active) return;
            _live = active;

            var glow = active ? new Color(0.95f, 0.75f, 0.30f) : Color.clear;
            float width = active ? 3f : 0f;

            style.borderTopColor = glow;
            style.borderBottomColor = glow;
            style.borderLeftColor = glow;
            style.borderRightColor = glow;
            style.borderTopWidth = width;
            style.borderBottomWidth = width;
            style.borderLeftWidth = width;
            style.borderRightWidth = width;
        }

        public override void OnSelected()
        {
            base.OnSelected();
            _onSelected?.Invoke(this);
        }

        public override void OnUnselected()
        {
            base.OnUnselected();
            _onSelected?.Invoke(null);
        }
    }
}
