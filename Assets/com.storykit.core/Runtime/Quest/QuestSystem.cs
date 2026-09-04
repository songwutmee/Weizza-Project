using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace StoryKit.Quest
{
    /// <summary>
    /// Runtime quest engine and the default IQuestService implementation.
    ///
    /// Evaluation is event-driven: it re-checks only when the blackboard
    /// changes (or a timer ticks), so hundreds of registered quests cost
    /// nothing while the player is just walking around.
    ///
    /// All state lives in the Global blackboard under "quest.*" keys, so
    /// StorySave persists quests with zero extra work.
    /// </summary>
    [AddComponentMenu("StoryKit/Quest System")]
    public sealed class QuestSystem : MonoBehaviour, IQuestService
    {
        [Tooltip("Quests known to the game. Auto-start ones begin as soon as " +
                 "their prerequisites pass.")]
        [SerializeField] private List<QuestDefinition> _quests = new();

        [Tooltip("Events that quest requirements can count (EventCount kind).")]
        [SerializeField] private List<string> _trackedEvents = new();

        public static QuestSystem Instance { get; private set; }

        /// <summary>Fired with the quest id whenever any quest state or objective changes.</summary>
        public event Action<string> QuestChanged;

        private readonly Dictionary<string, QuestDefinition> _byId =
            new(StringComparer.OrdinalIgnoreCase);
        private readonly List<string> _activeIds = new();
        private readonly Dictionary<string, float> _deadlines = new(StringComparer.Ordinal);
        private bool _dirty = true;

        public IReadOnlyList<string> ActiveQuestIds => _activeIds;

        private void Awake()
        {
            Instance = this;
            foreach (var quest in _quests)
                if (quest != null && !string.IsNullOrEmpty(quest.questId))
                    _byId[quest.questId] = quest;

            foreach (string eventName in _trackedEvents)
                StoryCounters.Track(eventName);

            BuildWatchedKeys();

            StoryServices.Register<IQuestService>(this);
            StoryState.Global.KeyChanged += OnBoardChanged;
        }

        private void OnDestroy()
        {
            StoryState.Global.KeyChanged -= OnBoardChanged;
            if (Instance == this) Instance = null;
        }

        /// <summary>
        /// Only wake up when a key some quest actually watches changes.
        /// Without this filter, every single variable write in the game would
        /// re-evaluate every quest, which is the classic quest-system stall.
        /// </summary>
        private void OnBoardChanged(string key)
        {
            if (string.IsNullOrEmpty(key)) { _dirty = true; return; }
            if (_watchedKeys.Count == 0 || _watchedKeys.Contains(key))
                _dirty = true;
        }

        private readonly HashSet<string> _watchedKeys = new(StringComparer.Ordinal);

        /// <summary>
        /// Walks every quest once at startup and records which blackboard keys
        /// can affect it. Item and quest-state requirements are covered by the
        /// canonical key names those systems write.
        /// </summary>
        private void BuildWatchedKeys()
        {
            _watchedKeys.Clear();

            foreach (var quest in _quests)
            {
                if (quest == null) continue;

                CollectKeys(quest.prerequisites);
                CollectKeys(quest.failConditions);

                foreach (var objective in quest.objectives)
                {
                    if (objective == null) continue;
                    CollectKeys(objective.completion);
                    _watchedKeys.Add(quest.CounterKeyFor(objective));
                }

                _watchedKeys.Add(QuestKeys.State(quest.questId));
                _watchedKeys.Add(QuestKeys.Step(quest.questId));
            }
        }

        private void CollectKeys(RequirementSet set)
        {
            if (set == null) return;

            foreach (var requirement in set.requirements)
            {
                if (requirement == null || string.IsNullOrEmpty(requirement.target)) continue;

                switch (requirement.kind)
                {
                    case RequirementKind.Variable:
                        _watchedKeys.Add(requirement.target);
                        break;
                    case RequirementKind.QuestState:
                        _watchedKeys.Add(QuestKeys.State(requirement.target));
                        break;
                    case RequirementKind.EventCount:
                        _watchedKeys.Add(QuestKeys.EventCount(requirement.target));
                        break;
                    case RequirementKind.VisitCount:
                        _watchedKeys.Add(QuestKeys.VisitCount(requirement.target));
                        break;
                    default:
                        // HasItem and Custom read outside the board, so those
                        // quests fall back to the periodic safety check.
                        _needsPeriodicCheck = true;
                        break;
                }
            }

            foreach (var group in set.groups) CollectKeys(group);
        }

        /// <summary>True when some requirement cannot be tracked by key alone.</summary>
        private bool _needsPeriodicCheck;
        private float _nextPeriodicCheck;

        private void Update()
        {
            // Timers are the only thing that can change without a board write.
            if (_deadlines.Count > 0) CheckDeadlines();

            // Inventory and custom conditions live outside the board, so those
            // quests get a slow safety sweep instead of a per-write check.
            if (_needsPeriodicCheck && StoryTime.Time >= _nextPeriodicCheck)
            {
                _nextPeriodicCheck = StoryTime.Time + 0.5f;
                _dirty = true;
            }

            if (!_dirty) return;
            _dirty = false;
            Evaluate();
        }

        // ---------------- IQuestService ----------------

        public void StartQuest(string questId)
        {
            if (!_byId.TryGetValue(questId, out var quest))
            {
                // Unknown id: still track state so simple graph-only quests work.
                SetState(questId, QuestState.Active);
                return;
            }

            var state = GetState(questId);
            if (state == QuestState.Active) return;
            if (state == QuestState.Completed && !quest.repeatable) return;

            if (!quest.prerequisites.Evaluate(StoryState.Global))
            {
                Debug.Log($"[StoryKit] Quest '{questId}' blocked. Missing: " +
                          string.Join(", ", GetUnmetPrerequisites(questId)));
                return;
            }

            SetState(questId, QuestState.Active);
            StoryState.Global.Set(QuestKeys.Step(questId), 0f);
            if (!_activeIds.Contains(questId)) _activeIds.Add(questId);

            if (quest.timeLimit > 0f)
                _deadlines[questId] = StoryTime.Time + quest.timeLimit;

            StoryEvents.Raise("Quest.Started", questId);
            QuestChanged?.Invoke(questId);
            _dirty = true;
        }

        public void AdvanceStep(string questId, string stepId)
        {
            if (GetState(questId) != QuestState.Active) return;

            if (_byId.TryGetValue(questId, out var quest))
            {
                int index = quest.objectives.FindIndex(o => o.id == stepId);
                if (index >= 0)
                {
                    StoryState.Global.Set(QuestKeys.Step(questId), index + 1f);
                    StoryEvents.Raise("Quest.StepAdvanced", questId + ":" + stepId);
                    QuestChanged?.Invoke(questId);
                    return;
                }
            }

            float step = StoryState.Global.Get<float>(QuestKeys.Step(questId));
            StoryState.Global.Set(QuestKeys.Step(questId), step + 1f);
            QuestChanged?.Invoke(questId);
        }

        public void CompleteQuest(string questId)
        {
            if (GetState(questId) == QuestState.Completed) return;

            SetState(questId, QuestState.Completed);
            _activeIds.Remove(questId);
            _deadlines.Remove(questId);

            if (_byId.TryGetValue(questId, out var quest))
            {
                GrantRewards(quest);
                StoryEvents.Raise(
                    string.IsNullOrEmpty(quest.completedEvent) ? "Quest.Completed" : quest.completedEvent,
                    questId);

                foreach (var next in quest.nextQuests)
                    if (next != null) OfferQuest(next.questId);
            }
            else
            {
                StoryEvents.Raise("Quest.Completed", questId);
            }

            QuestChanged?.Invoke(questId);
            _dirty = true;
        }

        public void FailQuest(string questId)
        {
            if (GetState(questId) != QuestState.Active) return;
            SetState(questId, QuestState.Failed);
            _activeIds.Remove(questId);
            _deadlines.Remove(questId);
            StoryEvents.Raise("Quest.Failed", questId);
            QuestChanged?.Invoke(questId);
        }

        public int GetQuestState(string questId) => (int)GetState(questId);

        // ---------------- Public helpers for UI ----------------

        public QuestState GetState(string questId) =>
            (QuestState)Mathf.RoundToInt(StoryState.Global.Get<float>(QuestKeys.State(questId)));

        public QuestDefinition GetDefinition(string questId) =>
            _byId.TryGetValue(questId, out var quest) ? quest : null;

        /// <summary>True when the quest may be offered right now.</summary>
        public bool CanStart(string questId)
        {
            if (!_byId.TryGetValue(questId, out var quest)) return true;
            var state = GetState(questId);
            if (state == QuestState.Active) return false;
            if (state == QuestState.Completed && !quest.repeatable) return false;
            return quest.prerequisites.Evaluate(StoryState.Global);
        }

        /// <summary>Human-readable list of what is still missing: for
        /// "You need to finish X first" dialogue and debugging.</summary>
        public List<string> GetUnmetPrerequisites(string questId)
        {
            var output = new List<string>();
            if (_byId.TryGetValue(questId, out var quest))
                quest.prerequisites.CollectUnmet(StoryState.Global, output);
            return output;
        }

        public bool IsObjectiveComplete(string questId, string objectiveId)
        {
            if (!_byId.TryGetValue(questId, out var quest)) return false;
            var objective = quest.objectives.Find(o => o.id == objectiveId);
            return objective != null && objective.completion.Evaluate(StoryState.Global);
        }

        /// <summary>Objectives the player should currently see, in log order.</summary>
        public List<(QuestDefinition.Objective objective, bool done, int current, int target)>
            GetVisibleObjectives(string questId)
        {
            var result = new List<(QuestDefinition.Objective, bool, int, int)>();
            if (!_byId.TryGetValue(questId, out var quest)) return result;

            int step = Mathf.RoundToInt(StoryState.Global.Get<float>(QuestKeys.Step(questId)));

            for (int i = 0; i < quest.objectives.Count; i++)
            {
                var objective = quest.objectives[i];
                if (quest.ordered && i > step) break;           // not unlocked yet
                if (objective.hidden && i != step) continue;

                bool done = objective.completion.Evaluate(StoryState.Global);
                int current = objective.counterTarget > 0
                    ? Mathf.RoundToInt(StoryState.Global.Get<float>(quest.CounterKeyFor(objective)))
                    : 0;
                result.Add((objective, done, current, objective.counterTarget));
            }
            return result;
        }

        /// <summary>Adds to an objective counter. Call from gameplay code:
        /// QuestSystem.Instance.AddProgress("herb_hunt", "gather", 1);</summary>
        public void AddProgress(string questId, string objectiveId, float amount = 1f)
        {
            if (!_byId.TryGetValue(questId, out var quest)) return;
            var objective = quest.objectives.Find(o => o.id == objectiveId);
            if (objective == null) return;

            string key = quest.CounterKeyFor(objective);
            StoryState.Global.Set(key, StoryState.Global.Get<float>(key) + amount);
        }

        // ---------------- Engine ----------------

        private void OfferQuest(string questId)
        {
            if (!_byId.TryGetValue(questId, out var quest)) return;
            if (!CanStart(questId)) return;
            if (quest.autoStart) StartQuest(questId);
            else StoryEvents.Raise("Quest.Available", questId);
        }

        private void Evaluate()
        {
            // Auto-start anything whose prerequisites just became true.
            foreach (var quest in _quests)
            {
                if (quest == null || !quest.autoStart) continue;
                if (GetState(quest.questId) != QuestState.NotStarted) continue;
                if (quest.prerequisites.Evaluate(StoryState.Global))
                    StartQuest(quest.questId);
            }

            // Progress / fail active quests. Iterate a copy: handlers mutate the list.
            for (int i = _activeIds.Count - 1; i >= 0; i--)
            {
                string questId = _activeIds[i];
                if (!_byId.TryGetValue(questId, out var quest)) continue;

                // Asking IsEmpty rather than counting requirements: a failure
                // condition built only out of nested groups used to slip past
                // the count here, so the quest could never fail and nothing
                // said so.
                if (!quest.failConditions.IsEmpty &&
                    quest.failConditions.Evaluate(StoryState.Global))
                {
                    FailQuest(questId);
                    continue;
                }

                if (quest.ordered) EvaluateOrdered(quest);
                else EvaluateUnordered(quest);
            }
        }

        private void EvaluateOrdered(QuestDefinition quest)
        {
            int step = Mathf.RoundToInt(StoryState.Global.Get<float>(QuestKeys.Step(quest.questId)));
            bool advanced = false;

            while (step < quest.objectives.Count)
            {
                var objective = quest.objectives[step];
                bool done = objective.optional || objective.completion.Evaluate(StoryState.Global);
                if (!done) break;
                step++;
                advanced = true;
            }

            if (advanced)
            {
                StoryState.Global.Set(QuestKeys.Step(quest.questId), (float)step);
                QuestChanged?.Invoke(quest.questId);
            }

            if (step >= quest.objectives.Count)
                CompleteQuest(quest.questId);
        }

        private void EvaluateUnordered(QuestDefinition quest)
        {
            foreach (var objective in quest.objectives)
            {
                if (objective.optional) continue;
                if (!objective.completion.Evaluate(StoryState.Global)) return;
            }
            CompleteQuest(quest.questId);
        }

        private void CheckDeadlines()
        {
            float now = StoryTime.Time;
            List<string> expired = null;
            foreach (var pair in _deadlines)
                if (now >= pair.Value)
                    (expired ??= new List<string>()).Add(pair.Key);

            if (expired == null) return;
            foreach (string questId in expired) FailQuest(questId);
        }

        private void SetState(string questId, QuestState state) =>
            StoryState.Global.Set(QuestKeys.State(questId), (float)(int)state);

        private void GrantRewards(QuestDefinition quest)
        {
            foreach (var reward in quest.rewards)
            {
                switch (reward.kind)
                {
                    case QuestDefinition.Reward.Kind.Item:
                        StoryServices.Get<IInventoryService>()
                            ?.GiveItem(reward.target, Mathf.RoundToInt(reward.amount));
                        break;
                    case QuestDefinition.Reward.Kind.Currency:
                        StoryServices.Get<IInventoryService>()
                            ?.GiveCurrency(Mathf.RoundToInt(reward.amount));
                        break;
                    case QuestDefinition.Reward.Kind.Variable:
                        StoryState.Global.Set(reward.target,
                            StoryState.Global.Get<float>(reward.target) + reward.amount);
                        break;
                    case QuestDefinition.Reward.Kind.Event:
                        StoryEvents.Raise(reward.target, reward.arguments);
                        break;
                    case QuestDefinition.Reward.Kind.CustomCommand:
                        StoryCommands.InvokeAsync(reward.target,
                            string.IsNullOrEmpty(reward.arguments)
                                ? Array.Empty<string>()
                                : reward.arguments.Split(' ', StringSplitOptions.RemoveEmptyEntries),
                            StoryState.Global, default).Forget();
                        break;
                }
            }
        }
    }
}
