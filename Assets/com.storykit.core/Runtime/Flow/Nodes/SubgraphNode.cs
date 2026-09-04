using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace StoryKit.Flow
{
    /// <summary>
    /// Runs another FlowGraphAsset as a child, like a Blueprint function call.
    /// Inputs copy parent blackboard keys into the child's graph scope before start;
    /// outputs copy child keys back to the parent after completion.
    /// Cancelling this node cancels the whole child graph (cleanup runs inside).
    /// </summary>
    [Serializable]
    [NodeMenu("Flow",
        "Run another Flow Graph inside this one, like calling a function. Reuse cutscene patterns.")]
    public sealed class SubgraphNode : TaskNode
    {
        [Serializable]
        public struct VarBinding
        {
            public string fromKey;
            public string toKey;
        }

        [SerializeField] private FlowGraphAsset _subgraph;
        [SerializeField] private List<VarBinding> _inputs = new();
        [SerializeField] private List<VarBinding> _outputs = new();

        /// <summary>Guards against runaway recursion (subgraph containing itself).</summary>
        private const int MaxDepth = 8;

        public FlowGraphAsset Subgraph { get => _subgraph; set => _subgraph = value; }
        public List<VarBinding> Inputs => _inputs;
        public List<VarBinding> Outputs => _outputs;
        public override NodeEffect Effect => NodeEffect.Consequence;
        public override string GetSummary() => _subgraph != null ? _subgraph.name : "(none)";

        [NonSerialized] private FlowRunner _childRunner;
        [NonSerialized] private Blackboard _childBoard;

        public override UniTask ExecuteAsync(FlowContext context, CancellationToken ct)
        {
            return UniTask.CompletedTask;
        }

        public override async UniTask OnRunningAsync(FlowContext context, CancellationToken ct)
        {
            if (_subgraph == null)
            {
                Debug.LogWarning($"[StoryKit] SubgraphNode '{Title}' has no graph assigned.", context.Runner);
                return;
            }

            int depth = context.Board.Get<int>("_storykit.depth");
            if (depth >= MaxDepth)
            {
                Debug.LogError($"[StoryKit] Subgraph depth exceeded {MaxDepth}. Possible recursion: '{_subgraph.name}'.", context.Runner);
                return;
            }

            // Child runner on the same GameObject; destroyed in OnFinally.
            _childRunner = FlowRunner.Rent(context.Runner.gameObject);

            // configureBoard runs before any child node executes: inputs are race-free.
            await _childRunner.PlayAsync(_subgraph, ct, childBoard =>
            {
                _childBoard = childBoard;
                childBoard.Set("_storykit.depth", depth + 1, writeThrough: false);
                foreach (var binding in _inputs)
                    CopyKey(context.Board, childBoard, binding.fromKey, binding.toKey);
            });

            foreach (var binding in _outputs)
                CopyKey(_childBoard, context.Board, binding.fromKey, binding.toKey);
        }

        private static void CopyKey(Blackboard from, Blackboard to, string fromKey, string toKey)
        {
            if (string.IsNullOrEmpty(fromKey) || string.IsNullOrEmpty(toKey)) return;
            if (from.TryGet(fromKey, out object value))
                to.Set(toKey, value, writeThrough: false);
        }

        public override UniTask OnFinallyAsync(FlowContext context, CancellationToken ct)
        {
            if (_childRunner != null)
            {
                FlowRunner.ReturnRented(_childRunner);
                _childRunner = null;
            }
            _childBoard = null;
            return UniTask.CompletedTask;
        }
    }
}
