using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace StoryKit.Flow
{
    /// <summary>Waits N seconds using the mockable clock. Skipped on fast-forward.</summary>
    [Serializable]
    [NodeMenu("Flow",
        "Pause the flow for N seconds, then continue. Skipped instantly when the player skips a cutscene.")]
    public sealed class WaitNode : ActionNode
    {
        [SerializeField, Min(0f)] private float _seconds = 1f;

        public float Seconds { get => _seconds; set => _seconds = Mathf.Max(0f, value); }
        public override NodeEffect Effect => NodeEffect.Presentation;
        public override string GetSummary() => $"{_seconds:0.##}s";

        public override async UniTask ExecuteAsync(FlowContext context, CancellationToken ct)
        {
            float elapsed = 0f;
            while (elapsed < _seconds)
            {
                ct.ThrowIfCancellationRequested();
                await UniTask.Yield(PlayerLoopTiming.Update, ct);
                elapsed += StoryTime.DeltaTime;
            }
        }
    }

    /// <summary>
    /// Holds until a StoryEvents event fires. Optional payload filter:
    /// if PayloadFilter is set, only a payload whose ToString() matches passes.
    /// Consequence: on skip we still wait (skipping a gate would corrupt progression),
    /// callers that want skippable gates should cancel this node instead.
    /// </summary>
    [Serializable]
    [NodeMenu("Flow",
        "Hold here until the game raises this event (StoryEvents.Raise). The main gate for 'wait until the player does X'.")]
    public sealed class WaitForEventNode : TaskNode
    {
        [SerializeField] private string _eventName;
        [SerializeField] private string _payloadFilter;

        public string EventName { get => _eventName; set => _eventName = value; }
        public string PayloadFilter { get => _payloadFilter; set => _payloadFilter = value; }
        public override NodeEffect Effect => NodeEffect.Consequence;
        public override string GetSummary() => _eventName;

        [NonSerialized] private IDisposable _subscription;
        [NonSerialized] private UniTaskCompletionSource _signal;

        public override UniTask ExecuteAsync(FlowContext context, CancellationToken ct)
        {
            _signal = new UniTaskCompletionSource();
            _subscription = StoryEvents.Subscribe(_eventName, payload =>
            {
                if (string.IsNullOrEmpty(_payloadFilter) ||
                    string.Equals(payload?.ToString(), _payloadFilter, StringComparison.Ordinal))
                {
                    _signal.TrySetResult();
                }
            });
            return UniTask.CompletedTask;
        }

        public override async UniTask OnRunningAsync(FlowContext context, CancellationToken ct)
        {
            await _signal.Task.AttachExternalCancellation(ct);
        }

        public override UniTask OnFinallyAsync(FlowContext context, CancellationToken ct)
        {
            _subscription?.Dispose();
            _subscription = null;
            _signal = null;
            return UniTask.CompletedTask;
        }
    }

    /// <summary>Fans out to all connected outputs in parallel.</summary>
    [Serializable]
    [NodeMenu("Flow",
        "Split into every connected output at once. Use for 'play music AND start dialogue together'.")]
    public sealed class ParallelNode : ActionNode
    {
        public override NodeEffect Effect => NodeEffect.Consequence;
        public override UniTask ExecuteAsync(FlowContext context, CancellationToken ct)
            => UniTask.CompletedTask;
    }

    /// <summary>
    /// Cancels the branch running the target node. Cancelling a TaskNode
    /// fires its Completed output: cancel is a phase-transition signal, not a stop.
    /// </summary>
    [Serializable]
    [NodeMenu("Flow",
        "Stop the target Task node. Its Completed port then fires - use this to end a phase, not just to abort.")]
    public sealed class CancelNode : ActionNode
    {
        [Tooltip("Pick the Task node whose phase this ends.")]
        [SerializeField, NodeIdField(tasksOnly: true)] private string _targetNodeId;

        public string TargetNodeId { get => _targetNodeId; set => _targetNodeId = value; }
        public override NodeEffect Effect => NodeEffect.Consequence;

        public override async UniTask ExecuteAsync(FlowContext context, CancellationToken ct)
        {
            if (string.IsNullOrEmpty(_targetNodeId))
            {
                Debug.LogWarning($"[StoryKit] CancelNode '{Title}' has no target.", context.Runner);
                return;
            }
            bool ok = await context.Runner.TryCancelNodeAsync(_targetNodeId, ct);
            if (!ok)
                Debug.LogWarning($"[StoryKit] CancelNode: target '{_targetNodeId}' is not running.", context.Runner);
        }
    }

    /// <summary>Picks one output at random using weights. Weight list pairs with NextNodeIds by index.</summary>
    [Serializable]
    [NodeMenu("Flow",
        "Pick ONE random output. Weights are optional (equal odds by default).")]
    public sealed class RandomBranchNode : StoryNode
    {
        [SerializeField] private List<float> _weights = new();

        public List<float> Weights => _weights;
        public override NodeEffect Effect => NodeEffect.Consequence;

        public override UniTask ExecuteAsync(FlowContext context, CancellationToken ct)
        {
            // Selection happens by trimming NextNodeIds to the winner.
            // The runner reads NextNodeIds after ExecuteAsync, so we choose here.
            if (NextNodeIds.Count <= 1) return UniTask.CompletedTask;

            float total = 0f;
            for (int i = 0; i < NextNodeIds.Count; i++)
                total += GetWeight(i);

            float roll = UnityEngine.Random.value * total;
            int chosen = NextNodeIds.Count - 1;
            for (int i = 0; i < NextNodeIds.Count; i++)
            {
                roll -= GetWeight(i);
                if (roll <= 0f) { chosen = i; break; }
            }

            string winner = NextNodeIds[chosen];
            NextNodeIds.Clear();
            NextNodeIds.Add(winner);
            return UniTask.CompletedTask;
        }

        private float GetWeight(int index)
            => index < _weights.Count && _weights[index] > 0f ? _weights[index] : 1f;
    }

    /// <summary>Raises a StoryEvents event. The graph-to-everything bridge.</summary>
    [Serializable]
    [NodeMenu("Flow",
        "Broadcast an event to the whole game. FSMs, BTs and other graphs can react to it.")]
    public sealed class RaiseEventNode : ActionNode
    {
        [SerializeField] private string _eventName;
        [SerializeField] private string _payload;

        public string EventName { get => _eventName; set => _eventName = value; }
        public string Payload { get => _payload; set => _payload = value; }
        public override NodeEffect Effect => NodeEffect.Consequence;
        public override string GetSummary() => _eventName;

        public override UniTask ExecuteAsync(FlowContext context, CancellationToken ct)
        {
            StoryEvents.Raise(_eventName, string.IsNullOrEmpty(_payload) ? null : _payload);
            return UniTask.CompletedTask;
        }
    }

    /// <summary>Debug log. Presentation (never matters after skip).</summary>
    [Serializable]
    [NodeMenu("Debug",
        "Print a message to the Console. For testing a path is reached.")]
    public sealed class LogNode : ActionNode
    {
        [SerializeField] private string _message;

        public string Message { get => _message; set => _message = value; }
        public override NodeEffect Effect => NodeEffect.Presentation;
        public override string GetSummary() => _message;

        public override UniTask ExecuteAsync(FlowContext context, CancellationToken ct)
        {
            Debug.Log($"[StoryKit] {_message}", context.Runner);
            return UniTask.CompletedTask;
        }
    }

    /// <summary>Designer note. Executes nothing.</summary>
    [Serializable]
    [NodeMenu("Debug",
        "A sticky note on the graph. Does nothing at runtime.")]
    public sealed class CommentNode : ActionNode
    {
        [SerializeField, TextArea] private string _note;

        public string Note { get => _note; set => _note = value; }
        public override NodeEffect Effect => NodeEffect.Presentation;
        public override string GetSummary() => _note;

        public override UniTask ExecuteAsync(FlowContext context, CancellationToken ct)
            => UniTask.CompletedTask;
    }
}
