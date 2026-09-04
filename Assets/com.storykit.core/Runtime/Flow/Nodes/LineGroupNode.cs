using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using StoryKit.Quest;
using UnityEngine;

namespace StoryKit.Flow
{
    /// <summary>
    /// Picks the most fitting line from a set, instead of always saying the
    /// same thing. Write several variants, give each one conditions, and the
    /// node says the one that best matches the current game state.
    ///
    /// This is how an NPC can greet the player differently after a boss dies,
    /// during rain, on their birthday, or the tenth time you talk to them,
    /// without a tree of branch nodes.
    ///
    /// Selection rules:
    ///   1. Drop every variant whose conditions fail.
    ///   2. Keep the ones with the highest specificity (most conditions met).
    ///   3. Among those, prefer ones the player has heard least.
    ///   4. Break remaining ties at random.
    /// </summary>
    [Serializable]
    [NodeMenu("Dialogue",
        "Say the most fitting line from a set of variants. Best match wins, least-heard breaks ties.")]
    public sealed class LineGroupNode : ActionNode
    {
        [Serializable]
        public sealed class Variant
        {
            [Tooltip("Who says it. Leave empty to use the group speaker.")]
            public string speakerId;

            public string emotion;
            public string lineKey;

            [TextArea] public string text;

            [Tooltip("All of these must pass for this line to be eligible. " +
                     "More conditions means more specific, which wins ties.")]
            public RequirementSet conditions = new();

            [Tooltip("Say this at most once per save.")]
            public bool onceOnly;

            [Tooltip("Manual priority. Higher wins over specificity when set.")]
            public int priority;
        }

        [SerializeField] private string _speakerId;
        [SerializeField] private List<Variant> _variants = new();

        [Tooltip("Line said when nothing matches. Leave empty to say nothing.")]
        [SerializeField, TextArea] private string _fallbackText;

        public List<Variant> Variants => _variants;
        public override NodeEffect Effect => NodeEffect.Presentation;

        public override string GetSummary()
            => $"{_speakerId}: {_variants.Count} variant(s)";

        public override async UniTask ExecuteAsync(FlowContext context, CancellationToken ct)
        {
            var ui = StoryServices.Get<IDialogueUI>();
            if (ui == null) return;

            var chosen = Select(context.Board);
            if (chosen == null)
            {
                if (!string.IsNullOrWhiteSpace(_fallbackText))
                    await ui.SayAsync(_speakerId, null,
                        SayNode.Interpolate(_fallbackText, context.Board), ct);
                return;
            }

            // Remember it was said, both for once-only and for least-heard ties.
            string countKey = HeardKey(chosen);
            StoryState.Global.Set(countKey, StoryState.Global.Get<float>(countKey) + 1f);

            string text = StoryServices.Get<ILocalizationService>()?.Resolve(chosen.lineKey)
                          ?? chosen.lineKey;
            if (string.IsNullOrEmpty(text) || text == chosen.lineKey)
                text = chosen.text;

            string speaker = string.IsNullOrEmpty(chosen.speakerId)
                ? _speakerId : chosen.speakerId;

            await ui.SayAsync(speaker, chosen.emotion,
                SayNode.Interpolate(text, context.Board), ct);
        }

        /// <summary>
        /// Runs the selection rules. Public so a bark pool or a custom UI can
        /// reuse the same logic.
        /// </summary>
        public Variant Select(Blackboard board)
        {
            List<Variant> eligible = null;
            int bestScore = int.MinValue;

            foreach (var variant in _variants)
            {
                if (variant == null) continue;
                if (variant.onceOnly && StoryState.Global.Get<float>(HeardKey(variant)) > 0f)
                    continue;
                if (variant.conditions != null && !variant.conditions.Evaluate(board))
                    continue;

                // Specificity: a line with three conditions describes a narrower
                // situation than one with none, so it should win when both fit.
                int score = variant.priority * 1000 + CountConditions(variant.conditions);

                if (score > bestScore)
                {
                    bestScore = score;
                    eligible ??= new List<Variant>();
                    eligible.Clear();
                    eligible.Add(variant);
                }
                else if (score == bestScore)
                {
                    eligible.Add(variant);
                }
            }

            if (eligible == null || eligible.Count == 0) return null;
            if (eligible.Count == 1) return eligible[0];

            // Prefer whatever the player has heard least, so repeated visits
            // cycle through the pool instead of repeating one line.
            float fewest = float.MaxValue;
            var leastHeard = new List<Variant>();

            foreach (var variant in eligible)
            {
                float heard = StoryState.Global.Get<float>(HeardKey(variant));
                if (heard < fewest)
                {
                    fewest = heard;
                    leastHeard.Clear();
                    leastHeard.Add(variant);
                }
                else if (Mathf.Approximately(heard, fewest))
                {
                    leastHeard.Add(variant);
                }
            }

            return leastHeard[UnityEngine.Random.Range(0, leastHeard.Count)];
        }

        private static int CountConditions(RequirementSet set)
        {
            if (set == null) return 0;
            int count = set.requirements.Count;
            foreach (var group in set.groups)
                count += CountConditions(group);
            return count;
        }

        private string HeardKey(Variant variant)
        {
            // Key on the line key when present so the count survives edits to
            // the text; otherwise fall back to the node id plus the index.
            string suffix = !string.IsNullOrEmpty(variant.lineKey)
                ? variant.lineKey
                : Id + "." + _variants.IndexOf(variant);
            return "_heard." + suffix;
        }
    }

    /// <summary>
    /// Same selection logic as a line group, but shown as a floating bark
    /// above a scene object and without blocking the flow. Guards let a
    /// patrolling enemy say something different when alerted, wounded, or bored.
    /// </summary>
    [Serializable]
    [NodeMenu("Dialogue",
        "Floating bark chosen from variants by context. Never blocks gameplay.")]
    public sealed class BarkGroupNode : ActionNode
    {
        [SerializeField] private string _stageId;
        [SerializeField] private LineGroupNode _group = new();
        [SerializeField, Min(0.5f)] private float _duration = 2.2f;

        [Tooltip("Seconds before this object can bark again.")]
        [SerializeField, Min(0f)] private float _cooldown = 4f;

        public override NodeEffect Effect => NodeEffect.Presentation;
        public override string GetSummary() => $"{_stageId}: bark pool";

        public override UniTask ExecuteAsync(FlowContext context, CancellationToken ct)
        {
            string cooldownKey = "_barkReady." + _stageId;
            if (StoryTime.Time < StoryState.Global.Get<float>(cooldownKey))
                return UniTask.CompletedTask;

            var variant = _group.Select(context.Board);
            if (variant == null) return UniTask.CompletedTask;

            StoryState.Global.Set(cooldownKey, StoryTime.Time + _cooldown);

            string text = StoryServices.Get<ILocalizationService>()?.Resolve(variant.lineKey)
                          ?? variant.lineKey;
            if (string.IsNullOrEmpty(text) || text == variant.lineKey) text = variant.text;

            StoryEvents.Raise("Dialogue.Bark", $"{_stageId}|{text}|{_duration}");
            return UniTask.CompletedTask;
        }
    }
}
