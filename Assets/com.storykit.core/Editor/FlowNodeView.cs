using System;
using StoryKit.Flow;
using UnityEditor.Experimental.GraphView;
using UnityEngine;
using UnityEngine.UIElements;

namespace StoryKit.Editor
{
    /// <summary>
    /// Visual node. Color-coded by kind, shows the node's GetSummary() live,
    /// and exposes the right ports for its type:
    ///   Action/Start  → Out (multi)
    ///   Task          → Running + Completed
    ///   Condition     → True + False
    ///   End           → input only
    /// </summary>
    public sealed class FlowNodeView : Node
    {
        public const string PortOut = "Out";
        public const string PortRunning = "Running";
        public const string PortCompleted = "Completed";
        public const string PortTrue = "True";
        public const string PortFalse = "False";

        /// <summary>Prefix for the per-option ports on a Choice node: "Choice:2".</summary>
        public const string PortChoicePrefix = "Choice:";

        /// <summary>Prefix for the named ports of an IFixedOutputs node: "Fixed:2".</summary>
        public const string PortFixedPrefix = "Fixed:";

        /// <summary>Number of option ports drawn, so the window can tell when
        /// the option list changed and the ports need rebuilding.</summary>
        public int OptionPortCount { get; private set; }

        public StoryNode Data { get; }
        public Port InputPort { get; private set; }

        private readonly Action<FlowNodeView> _onSelected;
        private readonly Label _summaryLabel;
        private readonly bool _isEntry;

        private static readonly Color TaskColor = new(0.42f, 0.32f, 0.85f);
        private static readonly Color ConditionColor = new(0.85f, 0.62f, 0.20f);
        private static readonly Color ActionColor = new(0.22f, 0.55f, 0.50f);
        private static readonly Color FlowColor = new(0.35f, 0.38f, 0.45f);
        private static readonly Color EntryColor = new(0.15f, 0.75f, 0.45f);

        public FlowNodeView(StoryNode data, bool isEntry, Action<FlowNodeView> onSelected)
        {
            Data = data;
            _isEntry = isEntry;
            _onSelected = onSelected;

            RefreshDisplay();
            SetPosition(new Rect(data.Position, Vector2.zero));

            // Header tint by node kind.
            Color tint = data switch
            {
                StartNode or EndNode => FlowColor,
                TaskNode => TaskColor,
                ConditionNode => ConditionColor,
                _ => ActionColor,
            };
            if (isEntry) tint = EntryColor;
            titleContainer.style.backgroundColor = new StyleColor(tint * 0.55f);
            titleContainer.style.borderBottomWidth = 2;
            titleContainer.style.borderBottomColor = tint;

            // Summary line under the title.
            _summaryLabel = new Label
            {
                style =
                {
                    color = new Color(0.75f, 0.8f, 0.9f, 0.9f),
                    fontSize = 10,
                    paddingLeft = 8, paddingRight = 8, paddingBottom = 4,
                    whiteSpace = WhiteSpace.Normal,
                    maxWidth = 220,
                }
            };
            mainContainer.Insert(1, _summaryLabel);

            BuildPorts();
            RefreshExpandedState();
            RefreshPorts();
        }

        public void RefreshDisplay()
        {
            string typeName = UnityEditor.ObjectNames
                .NicifyVariableName(Data.GetType().Name).Replace(" Node", "");
            title = string.IsNullOrEmpty(Data.Title) || Data.Title == typeName
                ? (_isEntry ? "▶ " : "") + typeName
                : (_isEntry ? "▶ " : "") + Data.Title;

            if (_summaryLabel != null)
            {
                string summary = Data.GetSummary();
                _summaryLabel.text = summary;
                _summaryLabel.style.display = string.IsNullOrEmpty(summary)
                    ? DisplayStyle.None : DisplayStyle.Flex;
            }
        }

        private void BuildPorts()
        {
            if (Data is not StartNode)
            {
                InputPort = InstantiatePort(Orientation.Horizontal, Direction.Input,
                    Port.Capacity.Multi, typeof(bool));
                InputPort.portName = "In";
                inputContainer.Add(InputPort);
            }

            switch (Data)
            {
                case EndNode:
                    return;
                case TaskNode:
                    AddOutput(PortRunning, Port.Capacity.Single, new Color(0.4f, 0.85f, 0.6f));
                    AddOutput(PortCompleted, Port.Capacity.Single, new Color(0.95f, 0.65f, 0.35f));
                    return;
                case ConditionNode:
                    AddOutput(PortTrue, Port.Capacity.Single, new Color(0.4f, 0.85f, 0.5f));
                    AddOutput(PortFalse, Port.Capacity.Single, new Color(0.9f, 0.4f, 0.4f));
                    return;
            }

            // A node can have both: a timed choice has one port per option plus
            // a Timeout port, so these are added rather than switched between.
            bool hasNamedPorts = false;

            if (Data is IOptionOutputs options)
            {
                var branches = options.OptionBranches;
                OptionPortCount = branches.Count;
                for (int i = 0; i < branches.Count; i++)
                    AddOutput(PortChoicePrefix + i, Port.Capacity.Single,
                        new Color(0.95f, 0.80f, 0.35f), OptionLabel(branches[i], i));
                hasNamedPorts = branches.Count > 0;
            }

            if (Data is IFixedOutputs fixedOutputs)
            {
                var names = fixedOutputs.OutputNames;
                for (int i = 0; i < names.Length; i++)
                    AddOutput(PortFixedPrefix + i, Port.Capacity.Single,
                        new Color(0.55f, 0.85f, 0.80f), names[i]);
                hasNamedPorts |= names.Length > 0;
            }

            if (!hasNamedPorts)
                AddOutput(PortOut, Port.Capacity.Multi, new Color(0.55f, 0.7f, 0.95f),
                    Data is IOptionOutputs ? "add an option first" : null);
        }

        private void AddOutput(string name, Port.Capacity capacity, Color color,
            string label = null)
        {
            var port = InstantiatePort(Orientation.Horizontal, Direction.Output, capacity, typeof(bool));
            port.portName = label ?? name;
            port.userData = name;
            port.portColor = color;
            outputContainer.Add(port);
        }

        /// <summary>Short readable label for an option port.</summary>
        private static string OptionLabel(OptionBranch branch, int index)
        {
            string text = branch.Label;
            if (string.IsNullOrWhiteSpace(text)) text = $"option {index + 1}";

            text = text.Replace("\n", " ").Trim();
            if (text.Length > 26) text = text.Substring(0, 25) + "...";

            return branch.HasGuard ? text + "  [if]" : text;
        }

        /// <summary>Refreshes the option port labels after an inspector edit.</summary>
        public void RefreshOptionLabels()
        {
            if (Data is not IOptionOutputs options) return;

            var branches = options.OptionBranches;
            for (int i = 0; i < branches.Count; i++)
            {
                var port = GetOutputPort(PortChoicePrefix + i);
                if (port != null) port.portName = OptionLabel(branches[i], i);
            }
        }

        public Port GetOutputPort(string name)
        {
            foreach (var child in outputContainer.Children())
                if (child is Port port && (string)port.userData == name)
                    return port;
            return null;
        }

        /// <summary>Search dimming: fade nodes that do not match the query.</summary>
        public void SetDimmed(bool dimmed)
        {
            style.opacity = dimmed ? 0.25f : 1f;
        }

        private bool _runtimeActive;

        /// <summary>Play-mode debugger glow.</summary>
        public void SetRuntimeActive(bool active)
        {
            if (_runtimeActive == active) return;
            _runtimeActive = active;

            var glow = active ? new Color(0.2f, 1f, 0.55f) : Color.clear;
            style.borderTopColor = glow;
            style.borderBottomColor = glow;
            style.borderLeftColor = glow;
            style.borderRightColor = glow;
            float width = active ? 2.5f : 0f;
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
