using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace StoryKit.Flow
{
    /// <summary>
    /// Where the node appears in the editor's add-node search window,
    /// plus a one-line description shown in the node inspector.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class)]
    public sealed class NodeMenuAttribute : Attribute
    {
        public string Path { get; }
        public string Description { get; }

        public NodeMenuAttribute(string path, string description = null)
        {
            Path = path;
            Description = description;
        }
    }

    /// <summary>
    /// Marks a string field that holds another node's id, so the inspector
    /// shows a readable dropdown of nodes instead of a raw GUID.
    /// </summary>
    [AttributeUsage(AttributeTargets.Field)]
    public sealed class NodeIdFieldAttribute : PropertyAttribute
    {
        /// <summary>Only list Task nodes (the only ones Cancel can target).</summary>
        public bool TasksOnly { get; }
        public NodeIdFieldAttribute(bool tasksOnly = false) { TasksOnly = tasksOnly; }
    }

    /// <summary>
    /// Skip classification. Presentation nodes are fast-forwarded when the player
    /// skips a cutscene; Consequence nodes always run so game state after a skip
    /// is identical to having watched the scene. Every node must declare this.
    /// </summary>
    public enum NodeEffect
    {
        Presentation,
        Consequence,
    }

    /// <summary>Execution context handed to every node.</summary>
    public sealed class FlowContext
    {
        public FlowRunner Runner { get; }
        public FlowGraphAsset Graph { get; }
        public Blackboard Board { get; }
        /// <summary>True while skipping: presentation nodes must return immediately.</summary>
        public bool FastForward { get; internal set; }

        public FlowContext(FlowRunner runner, FlowGraphAsset graph, Blackboard board)
        {
            Runner = runner;
            Graph = graph;
            Board = board;
        }
    }

    [Serializable]
    public abstract class StoryNode
    {
        [SerializeField] private string _id = Guid.NewGuid().ToString("N");
        [SerializeField] private string _title;
        [SerializeField] private Vector2 _position;
        [SerializeField] private List<string> _nextNodeIds = new();

        public string Id { get => _id; set => _id = value; }
        public string Title { get => _title; set => _title = value; }
        public Vector2 Position { get => _position; set => _position = value; }
        public List<string> NextNodeIds => _nextNodeIds;

        /// <summary>Skip classification: abstract on purpose so it can never be forgotten.</summary>
        public abstract NodeEffect Effect { get; }

        public abstract UniTask ExecuteAsync(FlowContext context, CancellationToken ct);

        /// <summary>Short text drawn on the node body in the editor.</summary>
        public virtual string GetSummary() => string.Empty;

        // ---- Fixed output helpers (see IFixedOutputs) ----

        /// <summary>Reads output slot i. Empty when nothing is connected there.</summary>
        public string GetFixedOutput(int index) =>
            index >= 0 && index < _nextNodeIds.Count ? _nextNodeIds[index] : null;

        /// <summary>
        /// Writes output slot i, padding with blanks so the slot after it keeps
        /// its own index. Blanks are skipped everywhere that walks connections.
        /// </summary>
        public void SetFixedOutput(int index, string targetId)
        {
            if (index < 0) return;
            while (_nextNodeIds.Count <= index) _nextNodeIds.Add(string.Empty);
            _nextNodeIds[index] = targetId ?? string.Empty;
        }

        /// <summary>
        /// Picks the branch a fixed-output node decided on. The runner reads
        /// NextNodeIds after the node runs, so the winner is written there.
        /// Runs on the per-play clone, never on the asset.
        /// </summary>
        protected void FollowFixedOutput(int index)
        {
            string winner = GetFixedOutput(index);
            _nextNodeIds.Clear();
            if (!string.IsNullOrEmpty(winner)) _nextNodeIds.Add(winner);
        }
    }

    /// <summary>
    /// A node whose outputs mean fixed things: Yes/No, Love/Like/Neutral, one
    /// per time of day. Without this the editor draws identical "Out" ports and
    /// the meaning of each wire is decided by the order you happened to drag
    /// them, which nobody can see. Implementing this gives every output a name.
    ///
    /// Output i is stored in NextNodeIds[i], so no extra data is serialized.
    /// </summary>
    public interface IFixedOutputs
    {
        /// <summary>Port labels, in order. Must be the same list every time.</summary>
        string[] OutputNames { get; }
    }

    /// <summary>
    /// A node with a player-facing option list, where each option owns its own
    /// branch. Reordering options in the inspector moves their wires with them.
    /// </summary>
    public interface IOptionOutputs
    {
        /// <summary>The options, in the order the player sees them.</summary>
        System.Collections.Generic.IReadOnlyList<OptionBranch> OptionBranches { get; }

        void SetOptionTarget(int index, string targetId);
    }

    /// <summary>The two things the editor needs to draw an option port.</summary>
    public readonly struct OptionBranch
    {
        public readonly string Label;
        public readonly string TargetId;
        public readonly bool HasGuard;

        public OptionBranch(string label, string targetId, bool hasGuard)
        {
            Label = label;
            TargetId = targetId;
            HasGuard = hasGuard;
        }
    }

    /// <summary>Fire-and-forget node. Runs, finishes, flow continues via NextNodeIds.</summary>
    [Serializable]
    public abstract class ActionNode : StoryNode { }

    /// <summary>
    /// Long-lived node with two outputs:
    /// Running fires immediately and executes a parallel branch while the task is alive;
    /// Completed fires after the task ends (normally or via CancelNode).
    /// OnFinallyAsync is abstract: every task MUST state its cleanup, even if empty.
    /// </summary>
    [Serializable]
    public abstract class TaskNode : StoryNode
    {
        [SerializeField] private string _runningNextId;
        [SerializeField] private string _completedNextId;

        public string RunningNextId { get => _runningNextId; set => _runningNextId = value; }
        public string CompletedNextId { get => _completedNextId; set => _completedNextId = value; }

        /// <summary>Held open while the task is active. Usually UniTask.WaitUntilCanceled(ct).</summary>
        public abstract UniTask OnRunningAsync(FlowContext context, CancellationToken ct);

        /// <summary>Cleanup. Always runs: normal completion, cancellation, or scenario stop.</summary>
        public abstract UniTask OnFinallyAsync(FlowContext context, CancellationToken ct);
    }

    /// <summary>Branching node. Evaluates and picks the true or false output.</summary>
    [Serializable]
    public abstract class ConditionNode : StoryNode
    {
        [SerializeField] private string _trueNextId;
        [SerializeField] private string _falseNextId;

        public string TrueNextId { get => _trueNextId; set => _trueNextId = value; }
        public string FalseNextId { get => _falseNextId; set => _falseNextId = value; }

        public override NodeEffect Effect => NodeEffect.Consequence;

        public sealed override UniTask ExecuteAsync(FlowContext context, CancellationToken ct)
            => UniTask.CompletedTask;

        public abstract UniTask<bool> EvaluateAsync(FlowContext context, CancellationToken ct);
    }

    [Serializable]
    [NodeMenu("Flow",
        "Where the graph begins. Right-click any node > Set as Entry to move the start.")]
    public sealed class StartNode : StoryNode
    {
        public override NodeEffect Effect => NodeEffect.Consequence;
        public override UniTask ExecuteAsync(FlowContext context, CancellationToken ct)
            => UniTask.CompletedTask;
    }

    [Serializable]
    [NodeMenu("Flow",
        "Stops the whole graph cleanly. Every path should eventually reach one.")]
    public sealed class EndNode : StoryNode
    {
        public override NodeEffect Effect => NodeEffect.Consequence;
        public override UniTask ExecuteAsync(FlowContext context, CancellationToken ct)
            => UniTask.CompletedTask;
    }
}
