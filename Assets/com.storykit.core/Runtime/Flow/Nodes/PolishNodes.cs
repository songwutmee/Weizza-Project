using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using StoryKit.Bridge;
using UnityEngine;

namespace StoryKit.Flow
{
    /// <summary>
    /// Micro-polish nodes: the small things every shipped game has and every
    /// toolkit forgets: toasts, rumble, waypoints, confirm prompts, name entry,
    /// damage flash, audio ducking, hints, fast travel, and SFX subtitles.
    ///
    /// All of them degrade gracefully: if the game has not wired the matching
    /// UI, the node logs one warning and the flow continues.
    /// </summary>

    // ---------------- Toast / notification ----------------

    /// <summary>
    /// Unobtrusive corner popup that fades by itself: "Item acquired",
    /// "Quest updated", "Autosaving". Never blocks the flow, never needs
    /// dismissing (the defining property of a toast).
    /// </summary>
    [Serializable]
    [NodeMenu("UI",
        "Corner popup that fades by itself: 'Item acquired', 'Quest updated'. Never blocks gameplay.")]
    public sealed class ToastNode : ActionNode
    {
        public enum ToastKind { Info, Success, Warning, Reward }

        [SerializeField] private string _lineKey;
        [SerializeField, TextArea] private string _fallbackText = "Item acquired";
        [SerializeField] private ToastKind _kind = ToastKind.Info;
        [SerializeField, Min(0.5f)] private float _duration = 2.5f;
        [Tooltip("Optional icon id your toast UI knows how to draw.")]
        [SerializeField] private string _iconId;

        public override NodeEffect Effect => NodeEffect.Presentation;
        public override string GetSummary() => _fallbackText;

        public override UniTask ExecuteAsync(FlowContext context, CancellationToken ct)
        {
            string text = StoryServices.Get<ILocalizationService>()?.Resolve(_lineKey) ?? _lineKey;
            if (string.IsNullOrEmpty(text) || text == _lineKey) text = _fallbackText;
            text = SayNode.Interpolate(text, context.Board);

            // Payload is "kind|icon|text" so a simple listener can split it.
            StoryEvents.Raise("Ui.Toast", $"{_kind}|{_iconId}|{text}");
            SimpleToastUI.ShowIfPresent(text, _kind, _duration);
            return UniTask.CompletedTask;
        }
    }

    // ---------------- Haptics ----------------

    /// <summary>
    /// Controller rumble / device vibration. Uses the new Input System's
    /// Gamepad API when available and falls back to mobile Handheld.Vibrate.
    /// Always stops itself, so a cancelled cutscene can never leave the pad
    /// buzzing forever.
    /// </summary>
    [Serializable]
    [NodeMenu("Player",
        "Controller rumble / phone vibration. Stops itself, so it can never get stuck buzzing.")]
    public sealed class RumbleNode : ActionNode
    {
        [SerializeField, Range(0f, 1f)] private float _lowFrequency = 0.4f;
        [SerializeField, Range(0f, 1f)] private float _highFrequency = 0.7f;
        [SerializeField, Min(0.02f)] private float _duration = 0.2f;

        public override NodeEffect Effect => NodeEffect.Presentation;
        public override string GetSummary() => $"{_duration:0.##}s";

        public override async UniTask ExecuteAsync(FlowContext context, CancellationToken ct)
        {
            if (StoryPreferences.DisableHaptics) return;

            Haptics.Rumble(_lowFrequency, _highFrequency);
            try
            {
                float elapsed = 0f;
                while (elapsed < _duration)
                {
                    ct.ThrowIfCancellationRequested();
                    await UniTask.Yield(PlayerLoopTiming.Update, ct);
                    elapsed += Time.unscaledDeltaTime;
                }
            }
            finally
            {
                Haptics.Stop();
            }
        }
    }

    // ---------------- Waypoint / navigation ----------------

    /// <summary>
    /// Places or clears a quest marker. The compass / minimap / world arrow all
    /// read the same blackboard keys, so any navigation UI works.
    /// </summary>
    [Serializable]
    [NodeMenu("UI",
        "Set or clear the quest waypoint marker (compass arrow, minimap pin).")]
    public sealed class WaypointNode : ActionNode
    {
        [SerializeField] private bool _clear;
        [Tooltip("Story Stage Id of the place to point at.")]
        [SerializeField] private string _targetStageId;
        [SerializeField] private string _labelKey;
        [SerializeField] private string _labelFallback = "Objective";

        public override NodeEffect Effect => NodeEffect.Consequence;
        public override string GetSummary() => _clear ? "clear marker" : _targetStageId;

        public override UniTask ExecuteAsync(FlowContext context, CancellationToken ct)
        {
            if (_clear)
            {
                StoryState.Global.Set("waypoint.target", "");
                StoryEvents.Raise("Ui.WaypointCleared", null);
                return UniTask.CompletedTask;
            }

            string label = StoryServices.Get<ILocalizationService>()?.Resolve(_labelKey) ?? _labelKey;
            if (string.IsNullOrEmpty(label) || label == _labelKey) label = _labelFallback;

            StoryState.Global.Set("waypoint.target", _targetStageId);
            StoryState.Global.Set("waypoint.label", label);
            StoryEvents.Raise("Ui.WaypointSet", _targetStageId);
            return UniTask.CompletedTask;
        }
    }

    /// <summary>
    /// Fast travel: fades out, moves the player to a stage anchor, fades in.
    /// Consequence, so the teleport still happens if the player skips.
    /// </summary>
    [Serializable]
    [NodeMenu("Level",
        "Fast travel: fade out, move the player to a Story Stage Id, fade in.")]
    public sealed class TeleportPlayerNode : ActionNode
    {
        [SerializeField] private string _destinationStageId;
        [SerializeField] private bool _fade = true;
        [SerializeField, Min(0f)] private float _fadeDuration = 0.4f;

        public override NodeEffect Effect => NodeEffect.Consequence;
        public override string GetSummary() => _destinationStageId;

        public override async UniTask ExecuteAsync(FlowContext context, CancellationToken ct)
        {
            var screen = StoryServices.Get<IScreenUI>();
            bool doFade = _fade && screen != null && !context.FastForward;

            if (doFade) await screen.FadeAsync(1f, _fadeDuration, ct);

            var destination = StoryStageId.FindTransform(_destinationStageId);
            if (destination != null)
            {
                // The game may own a CharacterController that fights transform
                // writes, so announce it too and let gameplay code do the move.
                StoryEvents.Raise("Player.Teleport", _destinationStageId);

                var player = StoryPlayer.Transform;
                if (player != null)
                {
                    // A CharacterController overwrites transform writes, so it
                    // has to be off for the frame the teleport happens.
                    var controller = player.GetComponent<CharacterController>();
                    if (controller != null) controller.enabled = false;
                    player.SetPositionAndRotation(
                        destination.position, destination.rotation);
                    if (controller != null) controller.enabled = true;
                }
            }
            else
            {
                Debug.LogWarning($"[StoryKit] Teleport target '{_destinationStageId}' not found.",
                    context.Runner);
            }

            if (doFade) await screen.FadeAsync(0f, _fadeDuration, ct);
        }
    }

    // ---------------- Prompts that ask the player ----------------

    /// <summary>
    /// Yes/No confirmation: "Really sleep until morning?", "Abandon quest?".
    /// Output 0 = confirmed, output 1 = cancelled.
    /// </summary>
    [Serializable]
    [NodeMenu("Dialogue",
        "Yes/No confirmation prompt. The two outputs are labelled, so you cannot wire them the wrong way round.")]
    public sealed class ConfirmNode : StoryNode, IFixedOutputs
    {
        public string[] OutputNames => new[] { "Yes", "No" };

        [SerializeField] private string _questionKey;
        [SerializeField, TextArea] private string _questionFallback = "Are you sure?";
        [SerializeField] private string _yesFallback = "Yes";
        [SerializeField] private string _noFallback = "No";

        public override NodeEffect Effect => NodeEffect.Consequence;
        public override string GetSummary() => _questionFallback;

        public override async UniTask ExecuteAsync(FlowContext context, CancellationToken ct)
        {
            var ui = StoryServices.Get<IDialogueUI>();
            if (ui == null) { FollowFixedOutput(1); return; }

            var loca = StoryServices.Get<ILocalizationService>();
            string question = loca?.Resolve(_questionKey) ?? _questionKey;
            if (string.IsNullOrEmpty(question) || question == _questionKey)
                question = _questionFallback;

            await ui.SayAsync(null, null, SayNode.Interpolate(question, context.Board), ct);
            int picked = await ui.ChoiceAsync(new[] { _yesFallback, _noFallback }, ct);
            FollowFixedOutput(Mathf.Clamp(picked, 0, 1));
        }
    }

    /// <summary>
    /// Asks the player to type something (character name, farm name, save name)
    /// and stores it in the blackboard, where {playerName} interpolation picks
    /// it up everywhere. The game shows the keyboard UI and raises
    /// "Ui.TextEntered" with the result.
    /// </summary>
    [Serializable]
    [NodeMenu("Dialogue",
        "Ask the player to type a name. Stored in the blackboard for {key} interpolation.")]
    public sealed class TextInputNode : TaskNode
    {
        [SerializeField] private string _saveToKey = "playerName";
        [SerializeField] private string _promptFallback = "Enter your name";
        [SerializeField] private string _defaultValue = "Traveler";
        [SerializeField, Min(1)] private int _maxLength = 16;

        public override NodeEffect Effect => NodeEffect.Consequence;
        public override string GetSummary() => $"→ {_saveToKey}";

        [NonSerialized] private UniTaskCompletionSource _done;
        [NonSerialized] private IDisposable _subscription;
        [NonSerialized] private Blackboard _board;

        public override UniTask ExecuteAsync(FlowContext context, CancellationToken ct)
        {
            _board = context.Board;
            _done = new UniTaskCompletionSource();

            _subscription = StoryEvents.Subscribe("Ui.TextEntered", payload =>
            {
                string value = payload?.ToString();
                if (string.IsNullOrWhiteSpace(value)) value = _defaultValue;
                if (value.Length > _maxLength) value = value.Substring(0, _maxLength);
                _board.Set(_saveToKey, value);
                _done.TrySetResult();
            });

            StoryEvents.Raise("Ui.RequestTextInput", $"{_promptFallback}|{_defaultValue}|{_maxLength}");
            return UniTask.CompletedTask;
        }

        public override async UniTask OnRunningAsync(FlowContext context, CancellationToken ct)
            => await _done.Task.AttachExternalCancellation(ct);

        public override UniTask OnFinallyAsync(FlowContext context, CancellationToken ct)
        {
            // Never leave the key empty: downstream text would show a blank.
            if (_board != null && string.IsNullOrEmpty(_board.Get<string>(_saveToKey)))
                _board.Set(_saveToKey, _defaultValue);

            _subscription?.Dispose();
            _subscription = null;
            _done = null;
            _board = null;
            StoryEvents.Raise("Ui.CloseTextInput", null);
            return UniTask.CompletedTask;
        }
    }

    // ---------------- Feedback ----------------

    /// <summary>
    /// Full-screen colour flash: damage (red), heal (green), lightning (white).
    /// Skipped entirely when the player has reduced motion / flashing enabled.
    /// </summary>
    [Serializable]
    [NodeMenu("UI",
        "Quick full-screen colour flash: damage red, heal green, lightning white.")]
    public sealed class ScreenFlashNode : ActionNode
    {
        [SerializeField] private Color _color = new(1f, 0.15f, 0.15f, 0.55f);
        [SerializeField, Min(0.02f)] private float _duration = 0.18f;

        public override NodeEffect Effect => NodeEffect.Presentation;
        public override string GetSummary() => $"{_duration:0.##}s";

        public override UniTask ExecuteAsync(FlowContext context, CancellationToken ct)
        {
            if (StoryPreferences.ReduceTextMotion) return UniTask.CompletedTask;
            ScreenFlashOverlay.Flash(_color, _duration);
            return UniTask.CompletedTask;
        }
    }

    /// <summary>
    /// Lowers music/ambience while something important is heard, then restores.
    /// Cleanup is mandatory, so the mix can never stay ducked.
    /// </summary>
    [Serializable]
    [NodeMenu("Audio",
        "Duck the music down while this task runs (so dialogue is audible). Restores automatically.")]
    public sealed class DuckAudioNode : TaskNode
    {
        [SerializeField, Range(0f, 1f)] private float _toVolume = 0.3f;
        [SerializeField, Min(0f)] private float _fadeTime = 0.3f;

        public override NodeEffect Effect => NodeEffect.Presentation;
        public override string GetSummary() => $"→ {_toVolume:0.##}";

        public override UniTask ExecuteAsync(FlowContext context, CancellationToken ct)
        {
            StoryEvents.Raise("Audio.Duck", $"{_toVolume}|{_fadeTime}");
            return UniTask.CompletedTask;
        }

        public override UniTask OnRunningAsync(FlowContext context, CancellationToken ct)
            => UniTask.WaitUntilCanceled(ct);

        public override UniTask OnFinallyAsync(FlowContext context, CancellationToken ct)
        {
            StoryEvents.Raise("Audio.Unduck", _fadeTime.ToString("0.##"));
            return UniTask.CompletedTask;
        }
    }

    /// <summary>
    /// Accessibility: shows a caption for an important non-speech sound
    /// "[a door creaks open]", "[footsteps behind you]". Only appears when the
    /// player has SFX subtitles turned on.
    /// </summary>
    [Serializable]
    [NodeMenu("UI",
        "Accessibility caption for an important sound: '[a door creaks open]'.")]
    public sealed class SoundCaptionNode : ActionNode
    {
        [SerializeField] private string _lineKey;
        [SerializeField] private string _fallbackText = "[a door creaks open]";
        [SerializeField, Min(0.5f)] private float _duration = 2f;

        public override NodeEffect Effect => NodeEffect.Presentation;
        public override string GetSummary() => _fallbackText;

        public override UniTask ExecuteAsync(FlowContext context, CancellationToken ct)
        {
            if (!StoryPreferences.SoundCaptions) return UniTask.CompletedTask;

            string text = StoryServices.Get<ILocalizationService>()?.Resolve(_lineKey) ?? _lineKey;
            if (string.IsNullOrEmpty(text) || text == _lineKey) text = _fallbackText;

            StoryEvents.Raise("Ui.SoundCaption", text);
            SimpleToastUI.ShowIfPresent(text, ToastNode.ToastKind.Info, _duration);
            return UniTask.CompletedTask;
        }
    }

    // ---------------- Hints, achievements, cursor ----------------

    /// <summary>
    /// One-time hint: "Hold Shift to sprint". Shows only the first time, then
    /// remembers forever: the pattern every tutorial system reinvents.
    /// </summary>
    [Serializable]
    [NodeMenu("UI",
        "Show a hint only the FIRST time it is reached, then never again.")]
    public sealed class HintNode : ActionNode
    {
        [SerializeField] private string _hintId = "hint_sprint";
        [SerializeField] private string _lineKey;
        [SerializeField, TextArea] private string _fallbackText = "Hold Shift to sprint";
        [SerializeField, Min(1f)] private float _duration = 4f;

        public override NodeEffect Effect => NodeEffect.Presentation;
        public override string GetSummary() => _fallbackText;

        public override UniTask ExecuteAsync(FlowContext context, CancellationToken ct)
        {
            string key = "hint.seen." + _hintId;
            if (StoryState.Global.Get<float>(key) > 0f) return UniTask.CompletedTask;
            StoryState.Global.Set(key, 1f);

            string text = StoryServices.Get<ILocalizationService>()?.Resolve(_lineKey) ?? _lineKey;
            if (string.IsNullOrEmpty(text) || text == _lineKey) text = _fallbackText;

            StoryEvents.Raise("Ui.Hint", text);
            SimpleToastUI.ShowIfPresent(text, ToastNode.ToastKind.Info, _duration);
            return UniTask.CompletedTask;
        }
    }

    /// <summary>
    /// Unlocks a platform achievement. Deduplicated: firing it twice does
    /// nothing the second time, so it is safe inside repeatable content.
    /// </summary>
    [Serializable]
    [NodeMenu("Meta",
        "Unlock a platform achievement (Steam etc). Safe to call more than once.")]
    public sealed class AchievementNode : ActionNode
    {
        [SerializeField] private string _achievementId = "ACH_FIRST_BLOOD";

        public override NodeEffect Effect => NodeEffect.Consequence;
        public override string GetSummary() => _achievementId;

        public override UniTask ExecuteAsync(FlowContext context, CancellationToken ct)
        {
            string key = "achievement." + _achievementId;
            if (PlayerPrefs.GetInt(key, 0) == 1) return UniTask.CompletedTask;

            PlayerPrefs.SetInt(key, 1);
            PlayerPrefs.Save();
            StoryEvents.Raise("Meta.AchievementUnlocked", _achievementId);
            return UniTask.CompletedTask;
        }
    }

    /// <summary>
    /// Shows or hides the mouse cursor and locks it to the window. Restores on
    /// cleanup, so a cancelled menu can never leave the player without a cursor.
    /// </summary>
    [Serializable]
    [NodeMenu("Player",
        "Free the mouse cursor while this task runs (menus, puzzles). Re-locks automatically.")]
    public sealed class FreeCursorNode : TaskNode
    {
        public override NodeEffect Effect => NodeEffect.Presentation;

        // Borrowed through the count in StoryUi rather than snapshotted. A
        // snapshot taken here is stale the moment a dialogue opens and closes
        // during the puzzle: that close drops the count to zero, relocks the
        // cursor, and the player is left doing a mouse puzzle without a mouse.
        [NonSerialized] private bool _held;

        public override UniTask ExecuteAsync(FlowContext context, CancellationToken ct)
        {
            if (!_held)
            {
                _held = true;
                StoryUi.ReleaseCursor();
            }
            return UniTask.CompletedTask;
        }

        public override UniTask OnRunningAsync(FlowContext context, CancellationToken ct)
            => UniTask.WaitUntilCanceled(ct);

        public override UniTask OnFinallyAsync(FlowContext context, CancellationToken ct)
        {
            // Guarded because OnFinallyAsync can run after a cancel that never
            // reached Execute, and an unpaired restore would free the cursor for
            // the rest of the session.
            if (_held)
            {
                _held = false;
                StoryUi.RestoreCursor();
            }
            return UniTask.CompletedTask;
        }
    }
}

namespace StoryKit.Bridge
{
    /// <summary>
    /// Cross-platform rumble.
    ///
    /// Gamepad rumble needs the Input System assembly, which this one cannot
    /// reference: a hard reference would break every project that has not
    /// installed it. So the pad handler is plugged in from outside, by the
    /// optional StoryKit.InputSystem assembly, and this file stays dependency
    /// free. Mobile vibration needs nothing and lives here.
    /// </summary>
    public static class Haptics
    {
        /// <summary>
        /// Set by the Input System assembly when it is present. Assign your own
        /// to drive a platform pad API instead.
        /// Arguments are low and high frequency motor strength, 0 to 1.
        /// </summary>
        public static System.Action<float, float> PadHandler;

        public static void Rumble(float low, float high)
        {
            if (StoryPreferences.DisableHaptics) return;

            if (PadHandler != null)
            {
                PadHandler(Mathf.Clamp01(low), Mathf.Clamp01(high));
                return;
            }

#if UNITY_ANDROID || UNITY_IOS
            if (low + high > 0.05f) Handheld.Vibrate();
#endif
        }

        public static void Stop() => PadHandler?.Invoke(0f, 0f);

        /// <summary>Cleared between play sessions by StoryKitLifecycle.</summary>
        internal static void Reset() => PadHandler = null;
    }


}
