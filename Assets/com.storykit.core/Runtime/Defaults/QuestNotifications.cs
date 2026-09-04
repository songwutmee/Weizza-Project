using System;
using StoryKit.Flow;
using StoryKit.Quest;
using UnityEngine;

namespace StoryKit.Defaults
{
    /// <summary>
    /// Telling the player something happened.
    ///
    /// The quest system was completely silent. A quest could start, an
    /// objective could tick, a whole questline could finish, and the only way
    /// to find out was to open the log and compare it to memory. Players do not
    /// open the log; they carry on and never learn the game noticed.
    ///
    /// This turns the quest events into corner toasts. It is deliberately a
    /// separate component: delete it the moment you have your own notification
    /// art, and nothing else changes.
    /// </summary>
    [AddComponentMenu("StoryKit/Quest Notifications")]
    public sealed class QuestNotifications : MonoBehaviour
    {
        [Tooltip("Say so when a quest becomes active.")]
        [SerializeField] private bool _onStarted = true;

        [Tooltip("Say so when a single objective ticks off. Off for quests with " +
                 "many small steps, where it becomes noise.")]
        [SerializeField] private bool _onObjective = true;

        [Tooltip("Say so when a quest finishes.")]
        [SerializeField] private bool _onCompleted = true;

        [Tooltip("Say so when a quest fails. Almost always worth keeping: a " +
                 "silent failure reads as a bug.")]
        [SerializeField] private bool _onFailed = true;

        [Tooltip("Seconds each message stays on screen.")]
        [SerializeField, Min(0.5f)] private float _duration = 2.8f;

        private readonly IDisposable[] _subscriptions = new IDisposable[4];

        private void OnEnable()
        {
            _subscriptions[0] = StoryEvents.Subscribe("Quest.Started",
                payload => Announce(_onStarted, payload as string,
                    "New quest: {0}", ToastNode.ToastKind.Info));

            _subscriptions[1] = StoryEvents.Subscribe("Quest.StepAdvanced",
                payload => AnnounceStep(payload as string));

            _subscriptions[2] = StoryEvents.Subscribe("Quest.Completed",
                payload => Announce(_onCompleted, payload as string,
                    "Quest complete: {0}", ToastNode.ToastKind.Success));

            _subscriptions[3] = StoryEvents.Subscribe("Quest.Failed",
                payload => Announce(_onFailed, payload as string,
                    "Quest failed: {0}", ToastNode.ToastKind.Warning));
        }

        private void OnDisable()
        {
            for (int i = 0; i < _subscriptions.Length; i++)
            {
                _subscriptions[i]?.Dispose();
                _subscriptions[i] = null;
            }
        }

        private void Announce(bool enabled, string questId, string format,
            ToastNode.ToastKind kind)
        {
            if (!enabled || string.IsNullOrEmpty(questId)) return;
            Bridge.SimpleToastUI.ShowIfPresent(
                string.Format(format, TitleOf(questId)), kind, _duration);
        }

        /// <summary>
        /// Objective events carry "questId:stepId", and the player cares about
        /// the objective text, not either id.
        /// </summary>
        private void AnnounceStep(string payload)
        {
            if (!_onObjective || string.IsNullOrEmpty(payload)) return;

            int split = payload.IndexOf(':');
            if (split <= 0) return;

            string questId = payload.Substring(0, split);
            string stepId = payload.Substring(split + 1);

            var quest = QuestSystem.Instance != null
                ? QuestSystem.Instance.GetDefinition(questId)
                : null;
            if (quest == null) return;

            foreach (var objective in quest.objectives)
            {
                if (objective == null || objective.id != stepId) continue;

                string text = QuestDefinition.ResolveObjectiveText(objective);
                if (string.IsNullOrWhiteSpace(text)) return;

                Bridge.SimpleToastUI.ShowIfPresent(
                    "Done: " + text, ToastNode.ToastKind.Success, _duration);
                return;
            }
        }

        private static string TitleOf(string questId)
        {
            var quest = QuestSystem.Instance != null
                ? QuestSystem.Instance.GetDefinition(questId)
                : null;

            string title = quest != null ? quest.ResolveTitle() : null;
            return string.IsNullOrWhiteSpace(title) ? questId : title;
        }
    }
}
