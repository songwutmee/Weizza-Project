using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using StoryKit.Quest;
using UnityEngine;

namespace StoryKit.Flow
{
    /// <summary>
    /// Branches on a full RequirementSet: the answer to
    /// "this path needs quest X done AND item Y AND gold ≥ 100".
    /// One node instead of a chain of comparisons.
    /// </summary>
    [Serializable]
    [NodeMenu("Condition",
        "Go True/False from a full condition set: quest states, items, variables, counters - AND/OR/nested.")]
    public sealed class RequirementBranchNode : ConditionNode
    {
        [SerializeField] private RequirementSet _requirements = new();

        public RequirementSet Requirements => _requirements;
        public override string GetSummary() => _requirements.Describe();

        public override UniTask<bool> EvaluateAsync(FlowContext context, CancellationToken ct)
            => UniTask.FromResult(_requirements.Evaluate(context.Board));
    }

    /// <summary>
    /// Holds until a RequirementSet becomes true. Event-driven: wakes on
    /// blackboard changes, never polls. Use it as a quest gate:
    /// "wait until the player has 5 herbs AND talked to the elder".
    /// </summary>
    [Serializable]
    [NodeMenu("Condition",
        "Hold here until the whole condition set passes (quest done AND has item AND...).")]
    public sealed class WaitForRequirementsNode : TaskNode
    {
        [SerializeField] private RequirementSet _requirements = new();

        public RequirementSet Requirements => _requirements;
        public override NodeEffect Effect => NodeEffect.Consequence;
        public override string GetSummary() => "until " + _requirements.Describe();

        [NonSerialized] private UniTaskCompletionSource _signal;
        [NonSerialized] private Blackboard _board;
        [NonSerialized] private Action<string> _handler;

        public override UniTask ExecuteAsync(FlowContext context, CancellationToken ct)
        {
            _signal = new UniTaskCompletionSource();
            _board = context.Board;
            _handler = _ => Check();
            _board.KeyChanged += _handler;
            // Global writes bubble to graph boards via the parent chain, but the
            // event fires on the board that was written; listen there too.
            StoryState.Global.KeyChanged += _handler;
            Check();
            return UniTask.CompletedTask;
        }

        private void Check()
        {
            if (_board != null && _requirements.Evaluate(_board))
                _signal?.TrySetResult();
        }

        public override async UniTask OnRunningAsync(FlowContext context, CancellationToken ct)
            => await _signal.Task.AttachExternalCancellation(ct);

        public override UniTask OnFinallyAsync(FlowContext context, CancellationToken ct)
        {
            if (_handler != null)
            {
                if (_board != null) _board.KeyChanged -= _handler;
                StoryState.Global.KeyChanged -= _handler;
            }
            _board = null;
            _handler = null;
            _signal = null;
            return UniTask.CompletedTask;
        }
    }

    /// <summary>Adds to a quest objective counter (collect 5 herbs → +1 each).</summary>
    [Serializable]
    [NodeMenu("Quest",
        "Add +N to a quest objective counter (collect 3/5 herbs).")]
    public sealed class QuestProgressNode : ActionNode
    {
        [SerializeField] private string _questId;
        [SerializeField] private string _objectiveId;
        [SerializeField] private float _amount = 1f;

        public string QuestId { get => _questId; set => _questId = value; }
        public string ObjectiveId { get => _objectiveId; set => _objectiveId = value; }
        public float Amount { get => _amount; set => _amount = value; }
        public override NodeEffect Effect => NodeEffect.Consequence;
        public override string GetSummary() => $"{_questId}/{_objectiveId} +{_amount:0.##}";

        public override UniTask ExecuteAsync(FlowContext context, CancellationToken ct)
        {
            if (QuestSystem.Instance != null)
                QuestSystem.Instance.AddProgress(_questId, _objectiveId, _amount);
            else
                Debug.LogWarning("[StoryKit] QuestProgress: no QuestSystem in the scene.",
                    context.Runner);
            return UniTask.CompletedTask;
        }
    }

    /// <summary>
    /// Branches on quest state: the classic "have you finished the intro?"
    /// gate for NPC dialogue.
    /// </summary>
    [Serializable]
    [NodeMenu("Quest",
        "Go True/False by one quest's state (NotStarted / Active / Completed / Failed).")]
    public sealed class QuestStateBranchNode : ConditionNode
    {
        [SerializeField] private string _questId;
        [SerializeField] private QuestState _expected = QuestState.Completed;

        public string QuestId { get => _questId; set => _questId = value; }
        public QuestState Expected { get => _expected; set => _expected = value; }
        public override string GetSummary() => $"{_questId} is {_expected}";

        public override UniTask<bool> EvaluateAsync(FlowContext context, CancellationToken ct)
        {
            var quests = StoryServices.Get<IQuestService>();
            int state = quests?.GetQuestState(_questId)
                        ?? (int)context.Board.Get<float>(QuestKeys.State(_questId));
            return UniTask.FromResult(state == (int)_expected);
        }
    }

    /// <summary>
    /// Records a visit to this point in the story (Ink-style visit counts).
    /// Requirements can then ask "has the player been here before?"
    /// perfect for "first time" vs "repeat" dialogue.
    /// </summary>
    [Serializable]
    [NodeMenu("Meta",
        "Remember the player passed this point. Conditions can then ask 'been here before?' for first-time vs repeat dialogue.")]
    public sealed class MarkVisitNode : ActionNode
    {
        [SerializeField] private string _visitId;

        public string VisitId { get => _visitId; set => _visitId = value; }
        public override NodeEffect Effect => NodeEffect.Consequence;
        public override string GetSummary() => _visitId;

        public override UniTask ExecuteAsync(FlowContext context, CancellationToken ct)
        {
            StoryCounters.Visit(string.IsNullOrEmpty(_visitId) ? Id : _visitId);
            return UniTask.CompletedTask;
        }
    }

    // ---------------- Quest log ----------------

    /// <summary>
    /// Points the corner tracker at a quest, or opens and closes the log.
    /// Lets a cutscene say "now follow this one" without any C#.
    /// </summary>
    [Serializable]
    [NodeMenu("Quest",
        "Point the quest tracker at a quest, or open and close the quest log.")]
    public sealed class QuestLogNode : ActionNode
    {
        public enum Action { Track, Open, Close, Toggle }

        [SerializeField] private Action _action = Action.Track;

        [Tooltip("Quest id to follow in the corner tracker. Only used by Track.")]
        [SerializeField] private string _questId;

        public override NodeEffect Effect => NodeEffect.Consequence;

        public override string GetSummary() =>
            _action == Action.Track ? $"track {_questId}" : _action.ToString().ToLowerInvariant();

        public override UniTask ExecuteAsync(FlowContext context, CancellationToken ct)
        {
            var log = StoryKit.Defaults.SimpleQuestLog.Active;
            if (log == null)
            {
                // No log in the scene is a normal setup, not an error: the game
                // may draw its own. Tell anyone listening either way.
                StoryEvents.Raise("Ui.QuestLog", $"{_action}|{_questId}");
                return UniTask.CompletedTask;
            }

            switch (_action)
            {
                case Action.Track: log.TrackedQuestId = _questId; break;
                case Action.Open: log.SetOpen(true); break;
                case Action.Close: log.SetOpen(false); break;
                case Action.Toggle: log.Toggle(); break;
            }
            return UniTask.CompletedTask;
        }
    }
}
