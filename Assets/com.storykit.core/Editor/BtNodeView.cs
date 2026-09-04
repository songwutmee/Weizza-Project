using System;
using StoryKit.Bt;
using UnityEditor.Experimental.GraphView;
using UnityEngine;
using UnityEngine.UIElements;

namespace StoryKit.Editor
{
    /// <summary>
    /// One box on the behaviour tree canvas.
    ///
    /// Colour says what kind of node it is without reading the label:
    ///   blue   composite, decides between children
    ///   violet decorator, changes the answer of the one below it
    ///   green  leaf, actually does something
    ///
    /// While the game runs the border says what it returned last tick, so a
    /// branch that never fires is visible rather than something to work out.
    /// </summary>
    public sealed class BtNodeView : Node
    {
        public BtNode Data { get; }
        public Port InputPort { get; private set; }
        public Port OutputPort { get; private set; }

        private readonly Action<BtNodeView> _onSelected;
        private readonly bool _isRoot;
        private Label _summary;
        private BtStatus? _live;

        private static readonly Color CompositeColor = new(0.42f, 0.62f, 0.95f);
        private static readonly Color DecoratorColor = new(0.72f, 0.52f, 0.95f);
        private static readonly Color LeafColor = new(0.36f, 0.76f, 0.52f);
        private static readonly Color RootColor = new(0.15f, 0.75f, 0.45f);

        public BtNodeView(BtNode data, bool isRoot, Action<BtNodeView> onSelected)
        {
            Data = data;
            _isRoot = isRoot;
            _onSelected = onSelected;

            // Top to bottom reads like a tree, so the ports go on the vertical
            // edges rather than the sides.
            InputPort = InstantiatePort(Orientation.Vertical, Direction.Input,
                Port.Capacity.Single, typeof(bool));
            InputPort.portName = string.Empty;
            inputContainer.Add(InputPort);

            if (data is BtComposite)
            {
                OutputPort = InstantiatePort(Orientation.Vertical, Direction.Output,
                    Port.Capacity.Multi, typeof(bool));
                OutputPort.portName = "children";
            }
            else if (data is BtDecorator)
            {
                OutputPort = InstantiatePort(Orientation.Vertical, Direction.Output,
                    Port.Capacity.Single, typeof(bool));
                OutputPort.portName = "child";
            }

            if (OutputPort != null) outputContainer.Add(OutputPort);

            var tint = TintFor(data);
            titleContainer.style.backgroundColor = new StyleColor(tint * 0.55f);
            titleContainer.style.borderBottomWidth = 2;
            titleContainer.style.borderBottomColor = isRoot ? RootColor : tint;

            _summary = new Label
            {
                style =
                {
                    color = new Color(0.75f, 0.8f, 0.9f, 0.9f),
                    fontSize = 10,
                    paddingLeft = 8, paddingRight = 8, paddingBottom = 4,
                    whiteSpace = WhiteSpace.Normal,
                    maxWidth = 190,
                }
            };
            mainContainer.Insert(1, _summary);

            RefreshDisplay();
            SetPosition(new Rect(data.Position, Vector2.zero));
            RefreshExpandedState();
            RefreshPorts();
        }

        private static Color TintFor(BtNode node) => node switch
        {
            BtComposite => CompositeColor,
            BtDecorator => DecoratorColor,
            _ => LeafColor,
        };

        public void RefreshDisplay()
        {
            string typeName = NodeListDrawer.Nice(Data.GetType());
            title = _isRoot ? "* " + typeName : typeName;

            _summary.text = Describe(Data);
            _summary.style.display = string.IsNullOrEmpty(_summary.text)
                ? DisplayStyle.None
                : DisplayStyle.Flex;
        }

        /// <summary>The one line that says what this node is set to do.</summary>
        private static string Describe(BtNode node)
        {
            if (!string.IsNullOrWhiteSpace(node.Title)) return node.Title;

            return node switch
            {
                BtCondition condition => $"{condition.Key} {condition.Comparison} {condition.Target:0.##}",
                BtBlackboardGuard guard => $"while {guard.Key} {guard.Comparison} {guard.Target:0.##}",
                BtSetVar setter => $"{setter.Key} = {setter.Value:0.##}",
                BtWait wait => $"{wait.Seconds:0.##}s",
                BtCooldown cooldown => $"every {cooldown.Seconds:0.##}s",
                BtRepeat repeat => repeat.Times > 0 ? $"x{repeat.Times}" : "forever",
                BtRaiseEvent raise => raise.EventName,
                BtCustomLeaf leaf => leaf.LeafId,
                BtParallel parallel => parallel.SuccessPolicy.ToString(),
                _ => string.Empty,
            };
        }

        /// <summary>Border colour follows the last tick result. Null clears it.</summary>
        public void SetLive(BtStatus? status)
        {
            if (_live == status) return;
            _live = status;

            Color glow = status switch
            {
                BtStatus.Success => new Color(0.30f, 0.90f, 0.50f),
                BtStatus.Failure => new Color(0.90f, 0.35f, 0.35f),
                BtStatus.Running => new Color(0.95f, 0.75f, 0.30f),
                _ => Color.clear,
            };

            float width = status.HasValue ? 2.5f : 0f;
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
