using System;
using System.Collections.Generic;
using StoryKit.Flow;   // VarComparison + its Evaluate() extension method
using UnityEngine;

namespace StoryKit.Quest
{
    /// <summary>
    /// What a single requirement checks. Everything a real quest system needs:
    /// flags, counters, quest states, inventory, achievements, time, custom code.
    /// </summary>
    public enum RequirementKind
    {
        /// <summary>Blackboard number vs value (flags, counters, reputation...).</summary>
        Variable,
        /// <summary>A quest is in a given state (NotStarted/Active/Completed/Failed).</summary>
        QuestState,
        /// <summary>Player owns at least N of an item (via IInventoryService).</summary>
        HasItem,
        /// <summary>An event fired at least N times this save (auto-counted).</summary>
        EventCount,
        /// <summary>A dialogue/node was visited at least N times (Ink-style visit counts).</summary>
        VisitCount,
        /// <summary>Delegates to a game-registered predicate: StoryConditions.Register(...).</summary>
        Custom,
    }

    public enum QuestState { NotStarted = 0, Active = 1, Completed = 2, Failed = 3 }

    /// <summary>How multiple requirements combine.</summary>
    public enum RequirementLogic
    {
        /// <summary>Every requirement must pass.</summary>
        All,
        /// <summary>At least one must pass.</summary>
        Any,
        /// <summary>At least N must pass (see RequirementSet.MinimumToPass).</summary>
        AtLeast,
        /// <summary>None may pass (useful for "as long as the alarm is not raised").</summary>
        None,
    }

    /// <summary>
    /// One requirement line. Serializable and inspector-friendly: designers pick
    /// a kind, then only the relevant fields matter.
    /// </summary>
    [Serializable]
    public sealed class Requirement
    {
        public RequirementKind kind = RequirementKind.Variable;

        [Tooltip("Blackboard key / quest id / item id / event name / node id / custom id.")]
        public string target;

        public VarComparison comparison = VarComparison.GreaterOrEqual;
        public float value = 1f;

        [Tooltip("Only for QuestState.")]
        public QuestState questState = QuestState.Completed;

        [Tooltip("Inverts the result of this single requirement.")]
        public bool negate;

        public bool Evaluate(Blackboard board)
        {
            bool result = EvaluateCore(board);
            return negate ? !result : result;
        }

        private bool EvaluateCore(Blackboard board)
        {
            if (string.IsNullOrEmpty(target)) return true;

            switch (kind)
            {
                case RequirementKind.Variable:
                    return comparison.Evaluate(board.Get<float>(target), value);

                case RequirementKind.QuestState:
                {
                    var quests = StoryServices.Get<IQuestService>();
                    int state = quests?.GetQuestState(target)
                                ?? (int)board.Get<float>(QuestKeys.State(target));
                    return state == (int)questState;
                }

                case RequirementKind.HasItem:
                {
                    var inventory = StoryServices.Get<IInventoryService>();
                    int count = inventory?.CountItem(target) ?? 0;
                    return comparison.Evaluate(count, value);
                }

                case RequirementKind.EventCount:
                    return comparison.Evaluate(
                        board.Get<float>(QuestKeys.EventCount(target)), value);

                case RequirementKind.VisitCount:
                    return comparison.Evaluate(
                        board.Get<float>(QuestKeys.VisitCount(target)), value);

                case RequirementKind.Custom:
                    return StoryConditions.Evaluate(target, board);

                default:
                    return true;
            }
        }

        /// <summary>Human-readable line for the editor and the quest log UI.</summary>
        public string Describe()
        {
            string body = kind switch
            {
                RequirementKind.Variable => $"{target} {Symbol()} {value:0.##}",
                RequirementKind.QuestState => $"quest '{target}' is {questState}",
                RequirementKind.HasItem => $"have {value:0} × {target}",
                RequirementKind.EventCount => $"'{target}' happened {Symbol()} {value:0} times",
                RequirementKind.VisitCount => $"visited '{target}' {Symbol()} {value:0} times",
                RequirementKind.Custom => $"custom: {target}",
                _ => target,
            };
            return negate ? $"NOT ({body})" : body;
        }

        private string Symbol() => comparison switch
        {
            VarComparison.Equal => "==",
            VarComparison.NotEqual => "!=",
            VarComparison.Greater => ">",
            VarComparison.GreaterOrEqual => "≥",
            VarComparison.Less => "<",
            VarComparison.LessOrEqual => "≤",
            _ => "?",
        };
    }

    /// <summary>
    /// A group of requirements combined with AND / OR / AT-LEAST-N / NONE,
    /// and optionally nested groups: so "(A and B) or (C and D)" is one field
    /// in the inspector instead of a chain of Condition nodes.
    /// </summary>
    [Serializable]
    public sealed class RequirementSet
    {
        public RequirementLogic logic = RequirementLogic.All;

        [Tooltip("Used when logic is AtLeast.")]
        public int minimumToPass = 1;

        public List<Requirement> requirements = new();

        [Tooltip("Nested groups, evaluated with the same logic as the requirements above.")]
        [SerializeReference] public List<RequirementSet> groups = new();

        /// <summary>
        /// True when nothing has been authored here at all.
        ///
        /// Callers need this because an empty set evaluating to true is right
        /// in one place and disastrous in another: empty prerequisites mean
        /// "no restriction, go ahead", while empty failure conditions asked the
        /// same question would fail the quest on the frame it started. Whoever
        /// owns that distinction has to ask before evaluating, and everyone has
        /// to ask the same way, which is why this counts nested groups too.
        /// </summary>
        public bool IsEmpty => requirements.Count == 0 && groups.Count == 0;

        public bool Evaluate(Blackboard board)
        {
            int passed = 0;
            int total = 0;

            for (int i = 0; i < requirements.Count; i++)
            {
                if (requirements[i] == null) continue;
                total++;
                if (requirements[i].Evaluate(board)) passed++;
            }

            for (int i = 0; i < groups.Count; i++)
            {
                if (groups[i] == null) continue;
                total++;
                if (groups[i].Evaluate(board)) passed++;
            }

            if (total == 0) return true;   // empty set = no restriction

            return logic switch
            {
                RequirementLogic.All => passed == total,
                RequirementLogic.Any => passed > 0,
                RequirementLogic.AtLeast => passed >= Mathf.Max(1, minimumToPass),
                RequirementLogic.None => passed == 0,
                _ => false,
            };
        }

        /// <summary>Lists the requirements that are NOT yet satisfied: feeds
        /// "you still need..." UI and the validation window.</summary>
        public void CollectUnmet(Blackboard board, List<string> output)
        {
            foreach (var requirement in requirements)
                if (requirement != null && !requirement.Evaluate(board))
                    output.Add(requirement.Describe());

            foreach (var group in groups)
                group?.CollectUnmet(board, output);
        }

        public string Describe()
        {
            var parts = new List<string>();
            foreach (var requirement in requirements)
                if (requirement != null) parts.Add(requirement.Describe());
            foreach (var group in groups)
                if (group != null) parts.Add($"({group.Describe()})");

            string joiner = logic switch
            {
                RequirementLogic.All => " AND ",
                RequirementLogic.Any => " OR ",
                RequirementLogic.None => " NOR ",
                _ => $" /{minimumToPass}+ ",
            };
            return string.Join(joiner, parts);
        }
    }

    /// <summary>Canonical blackboard key names so every system agrees.</summary>
    public static class QuestKeys
    {
        public static string State(string questId) => "quest." + questId;
        public static string Step(string questId) => "quest." + questId + ".step";
        public static string Counter(string questId, string counterId) =>
            "quest." + questId + "." + counterId;
        public static string EventCount(string eventName) => "_count.evt." + eventName;
        public static string VisitCount(string nodeId) => "_count.visit." + nodeId;
    }

    /// <summary>
    /// Game-registered predicates for requirements the package cannot know:
    /// "player is wearing the red cloak", "it is raining", "party has a healer".
    ///
    ///   StoryConditions.Register("is_raining", board => Weather.IsRaining);
    /// then pick Custom + target "is_raining" in any requirement.
    /// </summary>
    public static class StoryConditions
    {
        private static readonly Dictionary<string, Func<Blackboard, bool>> _predicates =
            new(StringComparer.OrdinalIgnoreCase);

        public static void Register(string id, Func<Blackboard, bool> predicate)
        {
            if (!string.IsNullOrEmpty(id) && predicate != null)
                _predicates[id] = predicate;
        }

        public static void Unregister(string id) => _predicates.Remove(id);
        public static void Reset() => _predicates.Clear();

        public static bool Evaluate(string id, Blackboard board)
        {
            if (_predicates.TryGetValue(id, out var predicate))
            {
                try { return predicate(board); }
                catch (Exception ex) { Debug.LogException(ex); return false; }
            }
            Debug.LogWarning($"[StoryKit] Custom condition '{id}' is not registered.");
            return false;
        }
    }

    /// <summary>
    /// Counts events and node visits into the blackboard so requirements can
    /// ask "how many times". Auto-installs on first use; counts persist with
    /// the save file because they live in the Global board.
    /// </summary>
    public static class StoryCounters
    {
        private static readonly List<IDisposable> _subscriptions = new();
        private static readonly HashSet<string> _tracked =
            new(StringComparer.Ordinal);

        /// <summary>Starts counting an event name. Safe to call repeatedly.</summary>
        public static void Track(string eventName)
        {
            if (string.IsNullOrEmpty(eventName) || !_tracked.Add(eventName)) return;

            _subscriptions.Add(StoryEvents.Subscribe(eventName, _ =>
            {
                string key = QuestKeys.EventCount(eventName);
                StoryState.Global.Set(key, StoryState.Global.Get<float>(key) + 1f);
            }));
        }

        /// <summary>Records that a node/section was visited (Ink-style visit count).</summary>
        public static void Visit(string nodeId)
        {
            if (string.IsNullOrEmpty(nodeId)) return;
            string key = QuestKeys.VisitCount(nodeId);
            StoryState.Global.Set(key, StoryState.Global.Get<float>(key) + 1f);
        }

        public static int GetEventCount(string eventName) =>
            Mathf.RoundToInt(StoryState.Global.Get<float>(QuestKeys.EventCount(eventName)));

        public static int GetVisitCount(string nodeId) =>
            Mathf.RoundToInt(StoryState.Global.Get<float>(QuestKeys.VisitCount(nodeId)));

        public static void Reset()
        {
            foreach (var subscription in _subscriptions) subscription.Dispose();
            _subscriptions.Clear();
            _tracked.Clear();
        }
    }
}
