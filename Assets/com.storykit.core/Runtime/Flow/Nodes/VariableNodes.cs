using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace StoryKit.Flow
{
    public enum VarComparison
    {
        Equal, NotEqual, Greater, GreaterOrEqual, Less, LessOrEqual,
    }

    public static class VarComparisonExtensions
    {
        public static bool Evaluate(this VarComparison op, float value, float target)
        {
            return op switch
            {
                VarComparison.Equal => Mathf.Approximately(value, target),
                VarComparison.NotEqual => !Mathf.Approximately(value, target),
                VarComparison.Greater => value > target,
                VarComparison.GreaterOrEqual => value >= target,
                VarComparison.Less => value < target,
                VarComparison.LessOrEqual => value <= target,
                _ => false,
            };
        }
    }

    /// <summary>Sets a blackboard value (float / string / bool by mode).</summary>
    [Serializable]
    [NodeMenu("Variable",
        "Write a value (number / text / on-off flag) into the blackboard.")]
    public sealed class SetVarNode : ActionNode
    {
        public enum ValueKind { Number, Text, Flag }

        [SerializeField] private string _key;
        [SerializeField] private ValueKind _kind = ValueKind.Number;
        [SerializeField] private float _numberValue;
        [SerializeField] private string _textValue;
        [SerializeField] private bool _flagValue;

        public string Key { get => _key; set => _key = value; }
        public ValueKind Kind { get => _kind; set => _kind = value; }
        public float NumberValue { get => _numberValue; set => _numberValue = value; }
        public string TextValue { get => _textValue; set => _textValue = value; }
        public bool FlagValue { get => _flagValue; set => _flagValue = value; }

        public override NodeEffect Effect => NodeEffect.Consequence;
        public override string GetSummary() => _kind switch
        {
            ValueKind.Number => $"{_key} = {_numberValue:0.##}",
            ValueKind.Text => $"{_key} = \"{_textValue}\"",
            _ => $"{_key} = {_flagValue}",
        };

        public override UniTask ExecuteAsync(FlowContext context, CancellationToken ct)
        {
            switch (_kind)
            {
                case ValueKind.Number: context.Board.Set(_key, _numberValue); break;
                case ValueKind.Text: context.Board.Set(_key, _textValue); break;
                case ValueKind.Flag: context.Board.Set(_key, _flagValue); break;
            }
            return UniTask.CompletedTask;
        }
    }

    /// <summary>Adds a delta to a numeric blackboard value.</summary>
    [Serializable]
    [NodeMenu("Variable",
        "Add or subtract from a number in the blackboard (counter +1, reputation -5).")]
    public sealed class ModifyVarNode : ActionNode
    {
        [SerializeField] private string _key;
        [SerializeField] private float _delta = 1f;

        public string Key { get => _key; set => _key = value; }
        public float Delta { get => _delta; set => _delta = value; }
        public override NodeEffect Effect => NodeEffect.Consequence;
        public override string GetSummary() => $"{_key} {(_delta >= 0 ? "+" : "")}{_delta:0.##}";

        public override UniTask ExecuteAsync(FlowContext context, CancellationToken ct)
        {
            float current = context.Board.Get<float>(_key);
            context.Board.Set(_key, current + _delta);
            return UniTask.CompletedTask;
        }
    }

    /// <summary>Branches on a numeric blackboard comparison.</summary>
    [Serializable]
    [NodeMenu("Variable",
        "Go True or False by comparing one blackboard number.")]
    public sealed class VarBranchNode : ConditionNode
    {
        [SerializeField] private string _key;
        [SerializeField] private VarComparison _comparison = VarComparison.GreaterOrEqual;
        [SerializeField] private float _target = 1f;

        public string Key { get => _key; set => _key = value; }
        public VarComparison Comparison { get => _comparison; set => _comparison = value; }
        public float Target { get => _target; set => _target = value; }
        public override string GetSummary() => $"{_key} {_comparison} {_target:0.##}";

        public override UniTask<bool> EvaluateAsync(FlowContext context, CancellationToken ct)
        {
            float value = context.Board.Get<float>(_key);
            return UniTask.FromResult(_comparison.Evaluate(value, _target));
        }
    }

    /// <summary>
    /// Holds until a blackboard comparison becomes true.
    /// Event-driven: wakes on KeyChanged instead of polling every frame.
    /// </summary>
    [Serializable]
    [NodeMenu("Variable",
        "Hold here until a blackboard number passes the comparison. Wakes instantly when the value changes.")]
    public sealed class WaitUntilVarNode : TaskNode
    {
        [SerializeField] private string _key;
        [SerializeField] private VarComparison _comparison = VarComparison.GreaterOrEqual;
        [SerializeField] private float _target = 1f;

        public string Key { get => _key; set => _key = value; }
        public VarComparison Comparison { get => _comparison; set => _comparison = value; }
        public float Target { get => _target; set => _target = value; }
        public override NodeEffect Effect => NodeEffect.Consequence;
        public override string GetSummary() => $"until {_key} {_comparison} {_target:0.##}";

        [NonSerialized] private UniTaskCompletionSource _signal;
        [NonSerialized] private Blackboard _observed;
        [NonSerialized] private Action<string> _handler;

        public override UniTask ExecuteAsync(FlowContext context, CancellationToken ct)
        {
            _signal = new UniTaskCompletionSource();
            _observed = context.Board;
            _handler = changedKey =>
            {
                if (changedKey == _key || string.IsNullOrEmpty(changedKey))
                    Check();
            };
            // A board only announces its own writes. Game code usually writes the
            // global board, which is this graph board's parent, so listening to the
            // graph board alone meant the node slept through the very change it was
            // waiting for. Every board up the chain is watched.
            for (var board = _observed; board != null; board = board.Parent)
                board.KeyChanged += _handler;
            Check();
            return UniTask.CompletedTask;
        }

        private void Check()
        {
            if (_observed == null || _signal == null) return;
            float value = _observed.Get<float>(_key);
            if (_comparison.Evaluate(value, _target))
                _signal.TrySetResult();
        }

        public override async UniTask OnRunningAsync(FlowContext context, CancellationToken ct)
        {
            await _signal.Task.AttachExternalCancellation(ct);
        }

        public override UniTask OnFinallyAsync(FlowContext context, CancellationToken ct)
        {
            if (_handler != null)
                for (var board = _observed; board != null; board = board.Parent)
                    board.KeyChanged -= _handler;
            _observed = null;
            _handler = null;
            _signal = null;
            return UniTask.CompletedTask;
        }
    }
}
