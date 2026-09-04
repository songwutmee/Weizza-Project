using System.Collections.Generic;
using StoryKit.Quest;
using UnityEngine;
using UnityEngine.UI;

namespace StoryKit.Defaults
{
    /// <summary>
    /// The quest log the player actually sees.
    ///
    /// Quests were authorable but invisible: you could write objectives with
    /// counters and conditions and the player had no way to read any of it.
    /// This draws two things, both built in code so there is no prefab to wire:
    ///
    ///   Tracker  a small always-on line in the corner showing the objective
    ///            you are on right now. This is the part players read.
    ///   Log      the full panel, opened with a key, listing every active quest
    ///            with its objectives, ticks and counters.
    ///
    /// It reads QuestSystem directly and redraws when a quest changes, so there
    /// is nothing to keep in sync. Replace it with your own UI whenever you
    /// like: it holds no state the game depends on.
    /// </summary>
    [AddComponentMenu("StoryKit/Simple Quest Log")]
    public sealed class SimpleQuestLog : MonoBehaviour
    {
        [Tooltip("Show the small corner tracker for the quest being followed.")]
        [SerializeField] private bool _showTracker = true;

        [Tooltip("Objectives to list in the tracker before it stops. Keeps the corner tidy.")]
        [SerializeField, Range(1, 8)] private int _trackerLines = 3;

        [Tooltip("Quest shown in the tracker. Empty = the one started most recently.")]
        [SerializeField] private string _trackedQuestId;

        private Canvas _canvas;
        private GameObject _panel;
        private Text _panelText;
        private Text _trackerText;
        private bool _open;
        private bool _dirty = true;

        /// <summary>The quest the corner tracker follows. Empty = most recent.</summary>
        public string TrackedQuestId
        {
            get => _trackedQuestId;
            set { _trackedQuestId = value; _dirty = true; }
        }

        public bool IsOpen => _open;

        /// <summary>The log in this scene, if there is one. Null is fine:
        /// it means the game draws its own quest UI.</summary>
        public static SimpleQuestLog Active { get; private set; }

        // ---------------- Lifecycle ----------------

        public static SimpleQuestLog Create()
        {
            var host = new GameObject("StoryKit.SimpleQuestLog");
            DontDestroyOnLoad(host);
            return host.AddComponent<SimpleQuestLog>();
        }

        private void Awake() => Active = this;

        /// <summary>Cleared by StoryKitLifecycle so a stale one from the last
        /// play session cannot survive a disabled domain reload.</summary>
        internal static void ResetActive() => Active = null;

        private void Start()
        {
            Build();
            Subscribe();
            SetOpen(false);
        }

        private void OnDestroy()
        {
            Unsubscribe();
            ReleaseHolds();
            if (Active == this) Active = null;
        }

        private void Subscribe()
        {
            if (QuestSystem.Instance != null)
                QuestSystem.Instance.QuestChanged += OnQuestChanged;
        }

        private void Unsubscribe()
        {
            if (QuestSystem.Instance != null)
                QuestSystem.Instance.QuestChanged -= OnQuestChanged;
        }

        private void OnQuestChanged(string questId)
        {
            // A quest that just started becomes the one the tracker follows,
            // which is what a player expects without any extra setup.
            if (string.IsNullOrEmpty(_trackedQuestId) ||
                QuestSystem.Instance == null ||
                !IsActive(QuestSystem.Instance, _trackedQuestId))
                _trackedQuestId = questId;

            _dirty = true;
        }

        private void Update()
        {
            // QuestSystem may be created after this component, so keep trying
            // until the subscription takes. Cheap: one null check per frame.
            if (!_subscribed && QuestSystem.Instance != null)
            {
                Subscribe();
                _subscribed = true;
                _dirty = true;
            }

            // Opening a menu mid cutscene hides the scene the player is meant
            // to be watching. Closing one is always allowed.
            if (StoryInput.ToggleLog && (_open || !StoryControl.MovementLocked))
                Toggle();
            if (_dirty) Redraw();
        }

        private bool _subscribed;

        // ---------------- Public control ----------------

        public void Toggle() => SetOpen(!_open);

        private bool _holdingCursor;
        private System.IDisposable _movementLock;

        public void SetOpen(bool open)
        {
            if (_open == open) return;
            _open = open;
            
            if (_panel != null)
            {
                if (open) _panel.SetActive(true);
                FadePanelAsync(open).Forget();
            }
            
            _dirty = true;

            // A full screen panel needs the pointer even in a game that
            // normally hides it.
            if (open)
            {
                if (!_holdingCursor)
                {
                    StoryUi.ReleaseCursor();
                    _holdingCursor = true;
                }
                _movementLock ??= StoryControl.Lock("quest log");
            }
            else
            {
                ReleaseHolds();
            }
        }

        private async Cysharp.Threading.Tasks.UniTaskVoid FadePanelAsync(bool visible)
        {
            var cg = _panel.GetComponent<CanvasGroup>();
            if (cg == null) return;

            float target = visible ? 1f : 0f;
            float start = cg.alpha;
            float duration = 0.15f;
            float elapsed = 0f;

            while (elapsed < duration)
            {
                await Cysharp.Threading.Tasks.UniTask.Yield(Cysharp.Threading.Tasks.PlayerLoopTiming.Update);
                if (this == null || _panel == null) return;
                elapsed += StoryTime.DeltaTime;
                cg.alpha = Mathf.Lerp(start, target, elapsed / duration);
            }
            cg.alpha = target;
            if (!visible && _panel != null) _panel.SetActive(false);
        }

        /// <summary>Forces a redraw. Only needed if you drive quests yourself.</summary>
        public void Refresh() => _dirty = true;

        /// <summary>Gives back the cursor and the movement lock, exactly once.</summary>
        private void ReleaseHolds()
        {
            if (_holdingCursor)
            {
                StoryUi.RestoreCursor();
                _holdingCursor = false;
            }

            _movementLock?.Dispose();
            _movementLock = null;
        }

        private void OnDisable() => ReleaseHolds();

        // ---------------- Drawing ----------------

        private void Redraw()
        {
            _dirty = false;
            var quests = QuestSystem.Instance;

            if (_trackerText != null)
            {
                string tracker = _showTracker && !_open ? BuildTracker(quests) : string.Empty;
                _trackerText.text = tracker;
                _trackerText.gameObject.SetActive(!string.IsNullOrEmpty(tracker));
            }

            if (_open && _panelText != null)
                _panelText.text = BuildFullLog(quests);
        }

        private string BuildTracker(QuestSystem quests)
        {
            if (quests == null || quests.ActiveQuestIds.Count == 0) return string.Empty;

            string questId = IsActive(quests, _trackedQuestId)
                ? _trackedQuestId
                : quests.ActiveQuestIds[quests.ActiveQuestIds.Count - 1];

            var definition = quests.GetDefinition(questId);
            var text = new System.Text.StringBuilder(160);
            text.Append("<b>").Append(TitleOf(definition, questId)).Append("</b>");

            int shown = 0;
            foreach (var (objective, done, current, target) in quests.GetVisibleObjectives(questId))
            {
                if (done) continue;                      // tracker shows what is left
                if (shown >= _trackerLines) break;

                text.Append('\n').Append(Line(objective, false, current, target));
                shown++;
            }

            return text.ToString();
        }

        private static string BuildFullLog(QuestSystem quests)
        {
            if (quests == null) return "Quest system not in this scene.";
            if (quests.ActiveQuestIds.Count == 0) return "No active quests.";

            var text = new System.Text.StringBuilder(512);

            // A copy, because completing a quest from a reward can change the
            // list while it is being walked.
            var ids = new List<string>(quests.ActiveQuestIds);
            for (int i = 0; i < ids.Count; i++)
            {
                var definition = quests.GetDefinition(ids[i]);
                if (i > 0) text.Append("\n\n");

                text.Append("<b>").Append(TitleOf(definition, ids[i])).Append("</b>");

                string description = definition != null ? definition.ResolveDescription() : null;
                if (!string.IsNullOrWhiteSpace(description))
                    text.Append('\n').Append(description);

                foreach (var (objective, done, current, target)
                         in quests.GetVisibleObjectives(ids[i]))
                    text.Append('\n').Append(Line(objective, done, current, target));
            }

            return text.ToString();
        }

        private static string Line(QuestDefinition.Objective objective,
            bool done, int current, int target)
        {
            string body = QuestDefinition.ResolveObjectiveText(objective);
            string counter = target > 0 ? $"  {current}/{target}" : string.Empty;
            string optional = objective != null && objective.optional ? "  (optional)" : string.Empty;

            return done
                ? $"  <color=#7ED08C>[x] {body}{counter}{optional}</color>"
                : $"  [ ] {body}{counter}{optional}";
        }

        /// <summary>List lookup without allocating an enumerator every redraw.</summary>
        private static bool IsActive(QuestSystem quests, string questId)
        {
            if (quests == null || string.IsNullOrEmpty(questId)) return false;

            var ids = quests.ActiveQuestIds;
            for (int i = 0; i < ids.Count; i++)
                if (ids[i] == questId) return true;
            return false;
        }

        private static string TitleOf(QuestDefinition definition, string fallbackId)
        {
            string title = definition != null ? definition.ResolveTitle() : null;
            return string.IsNullOrWhiteSpace(title) ? fallbackId : title;
        }

        // ---------------- Build the UI ----------------

        private void Build()
        {
            _canvas = gameObject.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 400;      // above dialogue, below fades
            gameObject.AddComponent<CanvasScaler>().uiScaleMode =
                CanvasScaler.ScaleMode.ScaleWithScreenSize;
            gameObject.GetComponent<CanvasScaler>().referenceResolution =
                new Vector2(1920, 1080);
            gameObject.AddComponent<GraphicRaycaster>();
            StoryUi.EnsureEventSystem();

            _trackerText = BuildTracker();
            _panel = BuildPanel(out _panelText);
        }

        private Text BuildTracker()
        {
            var go = new GameObject("Tracker", typeof(RectTransform), typeof(Text));
            var rect = Attach(go, new Vector2(1, 1), new Vector2(1, 1),
                new Vector2(-40, -40), new Vector2(520, 200));
            rect.pivot = new Vector2(1, 1);

            var text = go.GetComponent<Text>();
            StyleText(text, 22, TextAnchor.UpperRight);
            return text;
        }

        private GameObject BuildPanel(out Text bodyText)
        {
            var panel = new GameObject("Log", typeof(RectTransform), typeof(Image));
            var rect = Attach(panel, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                Vector2.zero, new Vector2(900, 720));
            rect.pivot = new Vector2(0.5f, 0.5f);
            panel.GetComponent<Image>().color = new Color(0.06f, 0.07f, 0.10f, 0.94f);
            
            var cg = panel.AddComponent<CanvasGroup>();
            cg.alpha = 0f;

            var header = new GameObject("Header", typeof(RectTransform), typeof(Text));
            var headerRect = Attach(header, new Vector2(0, 1), new Vector2(1, 1),
                new Vector2(0, -18), new Vector2(0, 44));
            headerRect.SetParent(rect, false);
            headerRect.offsetMin = new Vector2(28, headerRect.offsetMin.y);
            headerRect.offsetMax = new Vector2(-28, headerRect.offsetMax.y);
            StyleText(header.GetComponent<Text>(), 28, TextAnchor.MiddleLeft);
            header.GetComponent<Text>().text = "Quests";

            var body = new GameObject("Body", typeof(RectTransform), typeof(Text));
            var bodyRect = Attach(body, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            bodyRect.SetParent(rect, false);
            bodyRect.offsetMin = new Vector2(28, 28);
            bodyRect.offsetMax = new Vector2(-28, -72);

            bodyText = body.GetComponent<Text>();
            StyleText(bodyText, 22, TextAnchor.UpperLeft);
            return panel;
        }

        private RectTransform Attach(GameObject go, Vector2 anchorMin, Vector2 anchorMax,
            Vector2 position, Vector2 size)
        {
            var rect = go.GetComponent<RectTransform>();
            rect.SetParent(transform, false);
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.anchoredPosition = position;
            if (size != Vector2.zero) rect.sizeDelta = size;
            return rect;
        }

        private static void StyleText(Text text, int size, TextAnchor alignment)
        {
            // The built-in font is always present, so this works in a fresh
            // project with nothing imported.
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = size;
            text.alignment = alignment;
            text.color = new Color(0.92f, 0.94f, 0.98f);
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            text.supportRichText = true;
        }
    }
}
