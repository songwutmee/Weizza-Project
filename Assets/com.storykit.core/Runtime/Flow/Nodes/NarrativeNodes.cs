using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace StoryKit.Flow
{
    /// <summary>
    /// Extra nodes that narrative games (Sorry We're Closed / Disco Elysium
    /// style) need but generic kits forget: relationship tracking, timed
    /// choices, one-shot choices, ending selection, journal entries, and
    /// gameplay-mode switching.
    /// </summary>

    // ---------------- Relationships ----------------

    /// <summary>
    /// Adjusts how a character feels about the player. Stored as
    /// "rel.&lt;characterId&gt;" in the global board, so it saves automatically
    /// and any condition can read it.
    /// </summary>
    [Serializable]
    [NodeMenu("Character",
        "Change how a character feels about the player (+/- affection). Saved automatically.")]
    public sealed class RelationshipNode : ActionNode
    {
        [SerializeField] private string _characterId;
        [SerializeField] private float _delta = 1f;

        [Tooltip("Optional: clamp the value so it cannot run away.")]
        [SerializeField] private bool _clamp = true;
        [SerializeField] private float _min = -10f;
        [SerializeField] private float _max = 10f;

        public string CharacterId { get => _characterId; set => _characterId = value; }
        public float Delta { get => _delta; set => _delta = value; }

        public override NodeEffect Effect => NodeEffect.Consequence;
        public override string GetSummary() =>
            $"{_characterId} {(_delta >= 0 ? "+" : "")}{_delta:0.##}";

        public static string KeyFor(string characterId) => "rel." + characterId;

        public override UniTask ExecuteAsync(FlowContext context, CancellationToken ct)
        {
            string key = KeyFor(_characterId);
            float value = StoryState.Global.Get<float>(key) + _delta;
            if (_clamp) value = Mathf.Clamp(value, _min, _max);
            StoryState.Global.Set(key, value);
            StoryEvents.Raise("Character.RelationshipChanged", _characterId);
            return UniTask.CompletedTask;
        }
    }

    /// <summary>Branches on how much a character likes the player.</summary>
    [Serializable]
    [NodeMenu("Character",
        "Go True/False by a character's affection level toward the player.")]
    public sealed class RelationshipBranchNode : ConditionNode
    {
        [SerializeField] private string _characterId;
        [SerializeField] private VarComparison _comparison = VarComparison.GreaterOrEqual;
        [SerializeField] private float _value = 1f;

        public string CharacterId { get => _characterId; set => _characterId = value; }
        public override string GetSummary() =>
            $"{_characterId} affection {_comparison} {_value:0.##}";

        public override UniTask<bool> EvaluateAsync(FlowContext context, CancellationToken ct)
        {
            float current = StoryState.Global.Get<float>(RelationshipNode.KeyFor(_characterId));
            return UniTask.FromResult(_comparison.Evaluate(current, _value));
        }
    }

    // ---------------- Choices with pressure ----------------

    /// <summary>
    /// A choice with a countdown. If the player does not answer in time the
    /// Timeout output fires: used for tense conversations and QTE-ish moments.
    /// Every option owns its own branch, and Timeout is a separate named port.
    /// </summary>
    [Serializable]
    [NodeMenu("Dialogue",
        "Choice with a countdown. Each option gets its own output port, plus a labelled Timeout port.")]
    public sealed class TimedChoiceNode : StoryNode, IOptionOutputs, IFixedOutputs
    {
        [SerializeField] private List<ChoiceNode.Option> _options = new();
        [SerializeField, Min(0.5f)] private float _seconds = 5f;

        public List<ChoiceNode.Option> Options => _options;
        public float Seconds { get => _seconds; set => _seconds = Mathf.Max(0.5f, value); }

        /// <summary>The timeout branch lives in output slot 0.</summary>
        public string[] OutputNames => new[] { "Timeout" };

        public IReadOnlyList<OptionBranch> OptionBranches => ChoiceNode.Describe(_options);

        public void SetOptionTarget(int index, string targetId)
            => ChoiceNode.SetTarget(_options, index, targetId);

        /// <summary>
        /// Old format: option i used NextNodeIds[i] and the last entry was the
        /// timeout. Moves the options onto themselves and leaves the timeout in
        /// slot 0, which is where the named port now reads it from.
        /// </summary>
        public bool MigrateConnections()
        {
            if (NextNodeIds.Count <= _options.Count) return false;

            string timeout = NextNodeIds[NextNodeIds.Count - 1];
            bool changed = ChoiceNode.Migrate(_options, NextNodeIds, _options.Count);

            NextNodeIds.Clear();
            NextNodeIds.Add(timeout ?? string.Empty);
            return changed || !string.IsNullOrEmpty(timeout);
        }

        public override NodeEffect Effect => NodeEffect.Consequence;
        public override string GetSummary() => $"{_options.Count} options · {_seconds:0.#}s";

        public override async UniTask ExecuteAsync(FlowContext context, CancellationToken ct)
        {
            var ui = StoryServices.Get<IDialogueUI>();
            if (ui == null) { FollowFixedOutput(0); return; }   // straight to Timeout

            var loca = StoryServices.Get<ILocalizationService>();
            var texts = new List<string>();
            var indices = new List<int>();

            for (int i = 0; i < _options.Count; i++)
            {
                var option = _options[i];
                bool visible = string.IsNullOrEmpty(option.guardKey) ||
                    option.guardComparison.Evaluate(
                        context.Board.Get<float>(option.guardKey), option.guardValue);
                if (!visible) continue;

                string text = loca?.Resolve(option.lineKey) ?? option.lineKey;
                if (text == option.lineKey && !string.IsNullOrEmpty(option.fallbackText))
                    text = option.fallbackText;
                texts.Add(SayNode.Interpolate(text, context.Board));
                indices.Add(i);
            }

            if (indices.Count == 0) { FollowFixedOutput(0); return; }

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            var choiceTask = ui.ChoiceAsync(texts, timeoutCts.Token);

            float elapsed = 0f;
            while (elapsed < _seconds && choiceTask.Status == UniTaskStatus.Pending)
            {
                ct.ThrowIfCancellationRequested();
                await UniTask.Yield(PlayerLoopTiming.Update, ct);
                elapsed += StoryTime.DeltaTime;
            }

            if (choiceTask.Status == UniTaskStatus.Succeeded)
            {
                int picked = Mathf.Clamp(await choiceTask, 0, indices.Count - 1);
                string winner = _options[indices[picked]].nextNodeId;

                NextNodeIds.Clear();
                if (!string.IsNullOrEmpty(winner)) NextNodeIds.Add(winner);
            }
            else
            {
                timeoutCts.Cancel();     // close the choice UI
                FollowFixedOutput(0);
            }
        }
    }

    // ---------------- Endings & journal ----------------

    /// <summary>
    /// Records which ending the player reached and unlocks it permanently
    /// (survives New Game), so a gallery / "endings seen 3/5" screen can read it.
    /// </summary>
    [Serializable]
    [NodeMenu("Meta",
        "Record the ending the player reached and unlock it permanently for an endings gallery.")]
    public sealed class EndingNode : ActionNode
    {
        [SerializeField] private string _endingId = "ending_a";
        [SerializeField] private string _titleKey;
        [SerializeField, TextArea] private string _titleFallback;

        public string EndingId { get => _endingId; set => _endingId = value; }
        public override NodeEffect Effect => NodeEffect.Consequence;
        public override string GetSummary() => _endingId;

        public override UniTask ExecuteAsync(FlowContext context, CancellationToken ct)
        {
            StoryState.Global.Set("ending.last", _endingId);
            // Unlock flags live in PlayerPrefs so they persist across saves.
            PlayerPrefs.SetInt("storykit.ending." + _endingId, 1);
            PlayerPrefs.Save();
            StoryEvents.Raise("Story.Ending", _endingId);
            return UniTask.CompletedTask;
        }

        public static bool IsUnlocked(string endingId) =>
            PlayerPrefs.GetInt("storykit.ending." + endingId, 0) == 1;
    }

    /// <summary>
    /// Adds an entry to the player's journal / notes / clue list. Entries are
    /// stored as counters so the UI can list everything unlocked so far.
    /// </summary>
    [Serializable]
    [NodeMenu("Meta",
        "Unlock a journal / notes / clue entry the player can read later.")]
    public sealed class JournalEntryNode : ActionNode
    {
        [SerializeField] private string _entryId;
        [SerializeField] private string _bodyKey;
        [SerializeField, TextArea] private string _bodyFallback;

        public string EntryId { get => _entryId; set => _entryId = value; }
        public string BodyKey { get => _bodyKey; set => _bodyKey = value; }
        public string BodyFallback { get => _bodyFallback; set => _bodyFallback = value; }

        public override NodeEffect Effect => NodeEffect.Consequence;
        public override string GetSummary() => _entryId;

        public static string KeyFor(string entryId) => "journal." + entryId;

        public override UniTask ExecuteAsync(FlowContext context, CancellationToken ct)
        {
            StoryState.Global.Set(KeyFor(_entryId), 1f);
            StoryEvents.Raise("Journal.Unlocked", _entryId);
            return UniTask.CompletedTask;
        }

        public static bool IsUnlocked(string entryId) =>
            StoryState.Global.Get<float>(KeyFor(entryId)) > 0f;
    }

    // ---------------- Gameplay mode ----------------

    /// <summary>
    /// Switches the game between modes (exploration / combat / puzzle /
    /// conversation) by raising an event and setting "mode" on the board.
    /// Your controller reads it; enemies and UI react.
    /// </summary>
    [Serializable]
    [NodeMenu("Player",
        "Switch gameplay mode (exploration / combat / conversation). Your controller reads 'mode' from the board.")]
    public sealed class SetGameModeNode : ActionNode
    {
        [SerializeField] private string _mode = "exploration";

        public string Mode { get => _mode; set => _mode = value; }
        public override NodeEffect Effect => NodeEffect.Consequence;
        public override string GetSummary() => _mode;

        public override UniTask ExecuteAsync(FlowContext context, CancellationToken ct)
        {
            StoryState.Global.Set("mode", _mode);
            StoryEvents.Raise("Game.ModeChanged", _mode);
            return UniTask.CompletedTask;
        }
    }

    /// <summary>
    /// Slows or freezes time for dramatic beats. Always restores on cleanup,
    /// so a cancelled cutscene can never leave the game in slow motion.
    /// </summary>
    [Serializable]
    [NodeMenu("Player",
        "Slow down or freeze time while this task runs. Restores automatically when it ends.")]
    public sealed class TimeScaleNode : TaskNode
    {
        [SerializeField, Range(0f, 1f)] private float _timeScale = 0.2f;

        public float TimeScale { get => _timeScale; set => _timeScale = Mathf.Clamp01(value); }
        public override NodeEffect Effect => NodeEffect.Presentation;
        public override string GetSummary() => $"×{_timeScale:0.##}";

        [NonSerialized] private float _previous = 1f;

        [NonSerialized] private bool _applied;

        public override UniTask ExecuteAsync(FlowContext context, CancellationToken ct)
        {
            // Skipping a cutscene should not leave it in slow motion, and it
            // should not spend real seconds getting through it either.
            if (context.FastForward) return UniTask.CompletedTask;

            _previous = StoryTimeScale.SetBase(_timeScale);
            _applied = true;
            return UniTask.CompletedTask;
        }

        public override UniTask OnRunningAsync(FlowContext context, CancellationToken ct)
            => UniTask.WaitUntilCanceled(ct);

        public override UniTask OnFinallyAsync(FlowContext context, CancellationToken ct)
        {
            // Restores the speed that was set before this node, not whatever the
            // clock reads now, which may be zero because the player is paused.
            if (_applied)
            {
                _applied = false;
                StoryTimeScale.SetBase(_previous);
            }
            return UniTask.CompletedTask;
        }
    }
}
