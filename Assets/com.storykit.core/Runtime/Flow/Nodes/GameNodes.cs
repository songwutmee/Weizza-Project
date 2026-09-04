using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using StoryKit.Bridge;
using UnityEngine;

namespace StoryKit.Flow
{
    /// <summary>One dialogue line. Resolves the loca key, then waits for advance/voice.</summary>
    [Serializable]
    [NodeMenu("Dialogue",
        "Show one dialogue line. Type the text in Fallback Text (no localization needed), or use a Line Key for translations + voice.")]
    public sealed class SayNode : ActionNode
    {
        [SerializeField] private string _speakerId;

        [Tooltip("Portrait variant from the Character Database: happy, sad, angry... "
               + "Empty = default portrait.")]
        [SerializeField] private string _emotion;

        [SerializeField] private string _lineKey;

        [Tooltip("Author-language text. Used when the key has no translation yet, "
               + "so .story files play before any localization table exists.")]
        [SerializeField, TextArea] private string _fallbackText;

        [Tooltip("Animator trigger fired on the speaker in the world while this "
               + "line plays. Leave empty for no animation. The speaker is found "
               + "by its Story Stage Id, matching the Speaker Id above.")]
        [SerializeField] private string _speakerAnimation;

        [Tooltip("Move the camera onto the speaker for this line, then hand it "
               + "back. None leaves the camera exactly where it is.")]
        [SerializeField] private ShotFraming _shot = ShotFraming.Default;

        [Tooltip("Seconds the camera takes to reach the shot.")]
        [SerializeField, Min(0f)] private float _shotBlend = 0.5f;

        public string SpeakerId { get => _speakerId; set => _speakerId = value; }
        public string SpeakerAnimation { get => _speakerAnimation; set => _speakerAnimation = value; }
        public ShotFraming Shot { get => _shot; set => _shot = value; }
        public string Emotion { get => _emotion; set => _emotion = value; }
        public string LineKey { get => _lineKey; set => _lineKey = value; }
        public string FallbackText { get => _fallbackText; set => _fallbackText = value; }
        public override NodeEffect Effect => NodeEffect.Presentation;
        public override string GetSummary() =>
            $"{_speakerId}: {(string.IsNullOrEmpty(_fallbackText) ? _lineKey : _fallbackText)}";

        public override async UniTask ExecuteAsync(FlowContext context, CancellationToken ct)
        {
            var ui = StoryServices.Get<IDialogueUI>();
            if (ui == null) return;

            string text = StoryServices.Get<ILocalizationService>()?.Resolve(_lineKey) ?? _lineKey;
            if (text == _lineKey && !string.IsNullOrEmpty(_fallbackText))
                text = _fallbackText;   // untranslated: show author text, not the raw key
            text = Interpolate(text, context.Board);

            // Voice-over: the line key doubles as the voice clip id (Yarn/DSU
            // pattern). Fire-and-forget alongside the text; games without VO
            // simply have no IAudioService clip for the key.
            var audio = StoryServices.Get<IAudioService>();
            if (audio != null && !string.IsNullOrEmpty(_lineKey) && !context.FastForward)
                audio.PlayVoiceAsync(_lineKey, ct).Forget();

            // The speaker in the world reacts: an animation, a camera move, or
            // both. All optional, and all found from the speaker id, so a line
            // that wants a close up costs one dropdown rather than three nodes.
            var speaker = StoryStageId.FindTransform(_speakerId);
            PlaySpeakerAnimation(speaker);

            bool tookCamera = await TakeShot(speaker, context, ct);

            try
            {
                await ui.SayAsync(_speakerId, _emotion, text, ct);
            }
            finally
            {
                // Always hand the camera back, including when the line is
                // cancelled, or a skipped cutscene leaves the camera stuck.
                if (tookCamera)
                    StoryServices.Get<ICameraService>()?.ReturnToDefault(_shotBlend);
            }
        }

        private void PlaySpeakerAnimation(Transform speaker)
        {
            if (string.IsNullOrEmpty(_speakerAnimation) || speaker == null) return;

            var animator = speaker.GetComponentInChildren<Animator>();
            if (animator == null)
            {
                Debug.LogWarning(
                    $"[StoryKit] Say node wants animation '{_speakerAnimation}' on " +
                    $"'{_speakerId}', but that object has no Animator.");
                return;
            }
            animator.SetTrigger(_speakerAnimation);
        }

        private async UniTask<bool> TakeShot(Transform speaker, FlowContext context,
            CancellationToken ct)
        {
            if (_shot == ShotFraming.Default || speaker == null) return false;

            var camera = StoryServices.Get<ICameraService>();
            if (camera == null) return false;

            await camera.FrameAsync(speaker, _shot,
                context.FastForward ? 0f : _shotBlend, ct);
            return true;
        }

        /// <summary>Replaces {key} with blackboard values (Ink-style interpolation).</summary>
        internal static string Interpolate(string text, Blackboard board)
        {
            if (string.IsNullOrEmpty(text) || text.IndexOf('{') < 0) return text;

            var result = new System.Text.StringBuilder(text.Length + 16);
            int i = 0;
            while (i < text.Length)
            {
                char c = text[i];
                if (c == '{')
                {
                    int close = text.IndexOf('}', i + 1);
                    if (close > i + 1)
                    {
                        string key = text.Substring(i + 1, close - i - 1);
                        result.Append(board.TryGet(key, out object value) ? value?.ToString() : key);
                        i = close + 1;
                        continue;
                    }
                }
                result.Append(c);
                i++;
            }
            return result.ToString();
        }
    }

    /// <summary>
    /// Player choice. Every option owns its own outgoing connection, so moving
    /// an option up or down in the list moves its branch with it. Each option
    /// can be hidden by a blackboard guard. Consequence: skipping a choice is
    /// not allowed, the player must decide (industry standard for skip systems).
    /// </summary>
    [Serializable]
    [NodeMenu("Dialogue",
        "Show choices to the player. Each option gets its own output port, and can hide behind a condition.")]
    public sealed class ChoiceNode : StoryNode, IOptionOutputs
    {
        [Serializable]
        public struct Option
        {
            public string lineKey;
            public string fallbackText;             // author-language text
            public string guardKey;                 // empty = always visible
            public VarComparison guardComparison;
            public float guardValue;

            /// <summary>Where this option leads. One port per option in the editor.</summary>
            public string nextNodeId;
        }

        [SerializeField] private List<Option> _options = new();
        public List<Option> Options => _options;
        public override NodeEffect Effect => NodeEffect.Consequence;

        public override string GetSummary() => _options.Count switch
        {
            0 => "(no options)",
            1 => "1 option",
            _ => $"{_options.Count} options",
        };

        /// <summary>
        /// Older graphs paired option 0 with the first connection dragged,
        /// option 1 with the second, and so on. That silently swapped branches
        /// whenever options were reordered. This copies the old pairing onto the
        /// options once, after which the option owns its own connection.
        /// Returns true when something was moved.
        /// </summary>
        public bool MigrateConnections()
            => NextNodeIds.Count != 0 && Migrate(_options, NextNodeIds, NextNodeIds.Count);

        /// <summary>Clears a connection to a node that no longer exists.</summary>
        public bool ClearLinksTo(string deadId)
        {
            bool changed = false;
            for (int i = 0; i < _options.Count; i++)
            {
                if (_options[i].nextNodeId != deadId) continue;
                var option = _options[i];
                option.nextNodeId = null;
                _options[i] = option;
                changed = true;
            }
            return changed;
        }

        public void SetOptionTarget(int index, string targetId)
            => SetTarget(_options, index, targetId);

        public IReadOnlyList<OptionBranch> OptionBranches => Describe(_options);

        // ---- shared with TimedChoiceNode, which uses the same Option type ----

        internal static void SetTarget(List<Option> options, int index, string targetId)
        {
            if (index < 0 || index >= options.Count) return;
            var option = options[index];
            option.nextNodeId = targetId;
            options[index] = option;
        }

        internal static IReadOnlyList<OptionBranch> Describe(List<Option> options)
        {
            var result = new OptionBranch[options.Count];
            for (int i = 0; i < options.Count; i++)
            {
                var option = options[i];
                string label = !string.IsNullOrWhiteSpace(option.fallbackText)
                    ? option.fallbackText
                    : option.lineKey;
                if (string.IsNullOrWhiteSpace(label)) label = $"option {i + 1}";

                result[i] = new OptionBranch(label, option.nextNodeId,
                    !string.IsNullOrEmpty(option.guardKey));
            }
            return result;
        }

        /// <summary>
        /// Old format: branch i lived in NextNodeIds[i]. Copies it onto the
        /// option and reports whether anything moved. Shared with TimedChoice.
        /// </summary>
        internal static bool Migrate(List<Option> options, List<string> oldLinks, int count)
        {
            bool changed = false;
            for (int i = 0; i < options.Count && i < count && i < oldLinks.Count; i++)
            {
                if (!string.IsNullOrEmpty(options[i].nextNodeId)) continue;
                if (string.IsNullOrEmpty(oldLinks[i])) continue;

                var option = options[i];
                option.nextNodeId = oldLinks[i];
                options[i] = option;
                changed = true;
            }
            return changed;
        }

        public override async UniTask ExecuteAsync(FlowContext context, CancellationToken ct)
        {
            // Safety net for graphs authored before options owned their branch.
            MigrateConnections();

            var ui = StoryServices.Get<IDialogueUI>();
            if (ui == null || _options.Count == 0)
            {
                NextNodeIds.Clear();
                return;
            }

            var loca = StoryServices.Get<ILocalizationService>();
            var visibleTexts = new List<string>();
            var visibleIndices = new List<int>();

            for (int i = 0; i < _options.Count; i++)
            {
                Option option = _options[i];
                bool visible = string.IsNullOrEmpty(option.guardKey) ||
                    option.guardComparison.Evaluate(
                        context.Board.Get<float>(option.guardKey), option.guardValue);
                if (!visible) continue;

                string text = loca?.Resolve(option.lineKey) ?? option.lineKey;
                if (text == option.lineKey && !string.IsNullOrEmpty(option.fallbackText))
                    text = option.fallbackText;
                visibleTexts.Add(SayNode.Interpolate(text, context.Board));
                visibleIndices.Add(i);
            }

            if (visibleIndices.Count == 0)
            {
                // Every option was hidden by its guard. Warn once here rather
                // than let the graph stop dead with no explanation.
                Debug.LogWarning(
                    $"[StoryKit] Choice '{Title}' had no visible options, so the graph stops here. " +
                    "Give at least one option no guard, or an always true one.",
                    context.Runner);
                NextNodeIds.Clear();
                return;
            }

            int picked = await ui.ChoiceAsync(visibleTexts, ct);
            picked = Mathf.Clamp(picked, 0, visibleIndices.Count - 1);
            FollowOption(visibleIndices[picked]);
        }

        /// <summary>
        /// The runner reads NextNodeIds after a node runs, so the chosen branch
        /// is written there. This runs on the per-play clone, never on the asset.
        /// </summary>
        private void FollowOption(int index)
        {
            NextNodeIds.Clear();
            if (index < 0 || index >= _options.Count) return;

            string target = _options[index].nextNodeId;
            if (!string.IsNullOrEmpty(target)) NextNodeIds.Add(target);
        }
    }

    /// <summary>Screen fade via IScreenUI.</summary>
    [Serializable]
    [NodeMenu("UI",
        "Fade the screen to black (alpha 1) or back to clear (alpha 0).")]
    public sealed class FadeNode : ActionNode
    {
        [SerializeField, Range(0f, 1f)] private float _toAlpha = 1f;
        [SerializeField, Min(0f)] private float _duration = 0.5f;

        public float ToAlpha { get => _toAlpha; set => _toAlpha = Mathf.Clamp01(value); }
        public float Duration { get => _duration; set => _duration = Mathf.Max(0f, value); }
        public override NodeEffect Effect => NodeEffect.Presentation;
        public override string GetSummary() => _toAlpha > 0.5f ? "fade out" : "fade in";

        public override async UniTask ExecuteAsync(FlowContext context, CancellationToken ct)
        {
            var ui = StoryServices.Get<IScreenUI>();
            if (ui != null)
                await ui.FadeAsync(_toAlpha, _duration, ct);
        }
    }

    /// <summary>
    /// Locks player control for the task's lifetime. The IDisposable handle
    /// pattern + mandatory OnFinally guarantees input can never stay stuck.
    /// </summary>
    [Serializable]
    [NodeMenu("Player",
        "Freeze player input while this task is active. GUARANTEED to unlock when it ends - no softlocks.")]
    public sealed class LockControlNode : TaskNode
    {
        [SerializeField] private string _reason = "cutscene";

        public string Reason { get => _reason; set => _reason = value; }
        public override NodeEffect Effect => NodeEffect.Presentation;
        public override string GetSummary() => _reason;

        [NonSerialized] private IDisposable _handle;

        public override UniTask ExecuteAsync(FlowContext context, CancellationToken ct)
        {
            _handle = StoryServices.Get<IInputService>()?.LockPlayerControl(_reason);
            return UniTask.CompletedTask;
        }

        public override UniTask OnRunningAsync(FlowContext context, CancellationToken ct)
            => UniTask.WaitUntilCanceled(ct);

        public override UniTask OnFinallyAsync(FlowContext context, CancellationToken ct)
        {
            _handle?.Dispose();
            _handle = null;
            return UniTask.CompletedTask;
        }
    }

    /// <summary>Give items / currency. Always runs on skip.</summary>
    [Serializable]
    [NodeMenu("Inventory",
        "Give the player items (through your IInventoryService). Always runs even when the cutscene is skipped.")]
    public sealed class GiveItemNode : ActionNode
    {
        [SerializeField] private string _itemId;
        [SerializeField, Min(1)] private int _amount = 1;

        public string ItemId { get => _itemId; set => _itemId = value; }
        public int Amount { get => _amount; set => _amount = Mathf.Max(1, value); }
        public override NodeEffect Effect => NodeEffect.Consequence;
        public override string GetSummary() => $"{_itemId} x{_amount}";

        public override UniTask ExecuteAsync(FlowContext context, CancellationToken ct)
        {
            StoryServices.Get<IInventoryService>()?.GiveItem(_itemId, _amount);
            return UniTask.CompletedTask;
        }
    }

    [Serializable]
    [NodeMenu("Quest",
        "Start / advance / complete / fail a quest by id.")]
    public sealed class QuestNode : ActionNode
    {
        public enum Op { Start, AdvanceStep, Complete, Fail }

        [SerializeField] private Op _operation = Op.Start;
        [SerializeField] private string _questId;
        [SerializeField] private string _stepId;

        public Op Operation { get => _operation; set => _operation = value; }
        public string QuestId { get => _questId; set => _questId = value; }
        public string StepId { get => _stepId; set => _stepId = value; }
        public override NodeEffect Effect => NodeEffect.Consequence;
        public override string GetSummary() => $"{_operation} {_questId}";

        public override UniTask ExecuteAsync(FlowContext context, CancellationToken ct)
        {
            var quests = StoryServices.Get<IQuestService>();
            if (quests == null) return UniTask.CompletedTask;

            switch (_operation)
            {
                case Op.Start: quests.StartQuest(_questId); break;
                case Op.AdvanceStep: quests.AdvanceStep(_questId, _stepId); break;
                case Op.Complete: quests.CompleteQuest(_questId); break;
                case Op.Fail: quests.FailQuest(_questId); break;
            }
            return UniTask.CompletedTask;
        }
    }

    [Serializable]
    [NodeMenu("Audio",
        "Change the background music with a crossfade.")]
    public sealed class PlayBgmNode : ActionNode
    {
        [SerializeField] private string _bgmId;
        [SerializeField, Min(0f)] private float _crossfade = 1f;

        public string BgmId { get => _bgmId; set => _bgmId = value; }
        public float Crossfade { get => _crossfade; set => _crossfade = Mathf.Max(0f, value); }
        public override NodeEffect Effect => NodeEffect.Presentation;
        public override string GetSummary() => _bgmId;

        public override UniTask ExecuteAsync(FlowContext context, CancellationToken ct)
        {
            StoryServices.Get<IAudioService>()?.PlayBgm(_bgmId, _crossfade);
            return UniTask.CompletedTask;
        }
    }

    [Serializable]
    [NodeMenu("Meta",
        "Ask the game to autosave right now.")]
    public sealed class AutoSaveNode : ActionNode
    {
        public override NodeEffect Effect => NodeEffect.Consequence;

        public override UniTask ExecuteAsync(FlowContext context, CancellationToken ct)
        {
            StoryServices.Get<ISaveService>()?.RequestAutoSave();
            return UniTask.CompletedTask;
        }
    }
}
