using System;
using System.Collections.Generic;
using UnityEngine;

namespace StoryKit.Quest
{
    /// <summary>
    /// A full quest as data: prerequisites, ordered/unordered objectives with
    /// counters, rewards, failure conditions, and an optional time limit.
    /// Designers author it in the inspector; QuestSystem evaluates it every
    /// time relevant data changes (event-driven, not polled).
    /// </summary>
    [CreateAssetMenu(fileName = "Quest", menuName = "StoryKit/Quest")]
    public class QuestDefinition : ScriptableObject
    {
        [Serializable]
        public sealed class Objective
        {
            public string id = "objective";

            [Tooltip("Localization key for the quest log line.")]
            public string lineKey;
            [TextArea] public string fallbackText;

            [Tooltip("Everything that must be true for this objective to count as done.")]
            public RequirementSet completion = new();

            [Tooltip("Hidden objectives do not show in the log until they are active.")]
            public bool hidden;

            [Tooltip("Optional objectives do not block quest completion.")]
            public bool optional;

            [Tooltip("Counter target shown as 3/5 in the log. 0 = no counter shown.")]
            public int counterTarget;

            [Tooltip("Blackboard key holding the counter value (defaults to quest.<id>.<objectiveId>).")]
            public string counterKey;
        }

        [Serializable]
        public sealed class Reward
        {
            public enum Kind { Item, Currency, Variable, Event, CustomCommand }

            public Kind kind = Kind.Item;
            public string target;
            public float amount = 1f;
            public string arguments;
        }

        public string questId = "new_quest";
        public string titleKey;
        [TextArea] public string titleFallback;
        public string descriptionKey;
        [TextArea] public string descriptionFallback;

        [Tooltip("Must pass before the quest can be offered/started. " +
                 "Example: quest 'intro' Completed AND have 'map' AND level ≥ 5.")]
        public RequirementSet prerequisites = new();

        [Tooltip("Start automatically as soon as prerequisites pass.")]
        public bool autoStart;

        [Tooltip("Can be taken again after completion.")]
        public bool repeatable;

        [Tooltip("Ordered: objectives unlock one at a time. Unordered: all active at once.")]
        public bool ordered = true;
        public List<Objective> objectives = new();

        [Tooltip("If this ever passes while the quest is active, the quest fails.")]
        public RequirementSet failConditions = new();

        [Tooltip("Seconds before the quest auto-fails. 0 = no limit.")]
        public float timeLimit;

        public List<Reward> rewards = new();

        [Tooltip("Quests offered automatically once this one completes.")]
        public List<QuestDefinition> nextQuests = new();

        [Tooltip("Event raised on completion (empty = Quest.Completed).")]
        public string completedEvent;

        public string ResolveTitle()
        {
            string text = StoryServices.Get<ILocalizationService>()?.Resolve(titleKey) ?? titleKey;
            return (string.IsNullOrEmpty(text) || text == titleKey) && !string.IsNullOrEmpty(titleFallback)
                ? titleFallback : text;
        }

        public string ResolveDescription()
        {
            string text = StoryServices.Get<ILocalizationService>()?.Resolve(descriptionKey) ?? descriptionKey;
            return (string.IsNullOrEmpty(text) || text == descriptionKey) && !string.IsNullOrEmpty(descriptionFallback)
                ? descriptionFallback : text;
        }

        public static string ResolveObjectiveText(Objective objective)
        {
            string text = StoryServices.Get<ILocalizationService>()?.Resolve(objective.lineKey)
                          ?? objective.lineKey;
            return (string.IsNullOrEmpty(text) || text == objective.lineKey)
                   && !string.IsNullOrEmpty(objective.fallbackText)
                ? objective.fallbackText : text;
        }

        public string CounterKeyFor(Objective objective) =>
            string.IsNullOrEmpty(objective.counterKey)
                ? QuestKeys.Counter(questId, objective.id)
                : objective.counterKey;
    }
}
