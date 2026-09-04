using System.Collections.Generic;
using StoryKit.Flow;
using UnityEngine;
using UnityEngine.UI;

namespace StoryKit.Defaults
{
    /// <summary>
    /// The item screen, the one from survival horror.
    ///
    /// A list on the left, the selected item's art and text on the right, and
    /// two buttons: Examine and Use. That layout is Resident Evil, Silent Hill
    /// and Signalis, and it is the screen those games are built around, because
    /// the puzzle clue is usually written on the back of an item.
    ///
    /// It needs no prefab and no scene setup. Items come from IInventoryService,
    /// names and art from the Item Database, notes from Journal Entry nodes.
    ///
    /// Using an item raises "Item.Used" with the item id, so a Flow Graph waits
    /// on it with a Wait For Event node and decides what happens. The screen
    /// itself never knows what a key opens: that stays in the story.
    /// </summary>
    [AddComponentMenu("StoryKit/Simple Item Screen")]
    public sealed class SimpleItemScreen : MonoBehaviour
    {
        private enum Tab { Items, Notes }

        [Tooltip("Close the screen automatically after an item is used.")]
        [SerializeField] private bool _closeOnUse = true;

        [Tooltip("Hide the Use button. Set this for games where items are only read.")]
        [SerializeField] private bool _allowUse = true;

        public static SimpleItemScreen Active { get; private set; }
        public bool IsOpen { get; private set; }

        private readonly List<(string itemId, int count)> _owned = new();
        private readonly List<string> _notes = new();
        private readonly List<(string key, object value)> _scratch = new();

        private GameObject _root;
        private Text _listText;
        private Text _detailTitle;
        private Text _detailBody;
        private Image _detailIcon;
        private Button _examineButton;
        private Button _useButton;
        private Text _tabLabel;

        private Tab _tab = Tab.Items;
        private int _selected;
        private bool _examining;
        private bool _dirty = true;

        // ---------------- Lifecycle ----------------

        public static SimpleItemScreen Create()
        {
            var host = new GameObject("StoryKit.SimpleItemScreen");
            DontDestroyOnLoad(host);
            return host.AddComponent<SimpleItemScreen>();
        }

        internal static void ResetActive() => Active = null;

        private void Awake() => Active = this;

        private void Start()
        {
            Build();
            SetOpen(false);
        }

        private void OnDestroy()
        {
            ReleaseHolds();
            if (Active == this) Active = null;
        }

        // ---------------- Public control ----------------

        public void Toggle() => SetOpen(!IsOpen);

        private bool _holdingCursor;
        private System.IDisposable _movementLock;

        public void SetOpen(bool open)
        {
            if (IsOpen == open) return;
            IsOpen = open;
            
            if (_root != null)
            {
                if (open) _root.SetActive(true);
                FadePanelAsync(open).Forget();
            }

            // Examine and Use are buttons, so the pointer has to come back even
            // in a first person game that normally hides it.
            if (open)
            {
                if (!_holdingCursor)
                {
                    StoryUi.ReleaseCursor();
                    _holdingCursor = true;
                }
                _movementLock ??= StoryControl.Lock("item screen");
            }
            else
            {
                ReleaseHolds();
            }

            if (open)
            {
                _examining = false;
                _selected = 0;
                _dirty = true;
                StoryEvents.Raise("Ui.ItemScreenOpened", null);
            }
            else
            {
                StoryEvents.Raise("Ui.ItemScreenClosed", null);
            }
        }

        private async Cysharp.Threading.Tasks.UniTaskVoid FadePanelAsync(bool visible)
        {
            var cg = _root.GetComponent<CanvasGroup>();
            if (cg == null) return;

            float target = visible ? 1f : 0f;
            float start = cg.alpha;
            float duration = 0.15f;
            float elapsed = 0f;

            while (elapsed < duration)
            {
                await Cysharp.Threading.Tasks.UniTask.Yield(Cysharp.Threading.Tasks.PlayerLoopTiming.Update);
                if (this == null || _root == null) return;
                elapsed += StoryTime.DeltaTime;
                cg.alpha = Mathf.Lerp(start, target, elapsed / duration);
            }
            cg.alpha = target;
            if (!visible && _root != null) _root.SetActive(false);
        }

        /// <summary>Opens straight onto the notes tab, for a "read the note" moment.</summary>
        public void OpenNotes()
        {
            _tab = Tab.Notes;
            SetOpen(true);
        }

        public void Refresh() => _dirty = true;

        // ---------------- Update ----------------

        private void Update()
        {
            if (!IsOpen) return;

            // Advance doubles as "back out of the examine text", which is what
            // players try first without being told.
            if (_examining && StoryInput.Advance)
            {
                _examining = false;
                _dirty = true;
            }

            if (_dirty) Redraw();
        }

        // ---------------- Data ----------------

        private void Rebuild()
        {
            _owned.Clear();
            StoryServices.Get<IInventoryService>()?.CollectOwnedItems(_owned);
            _owned.Sort((a, b) => string.CompareOrdinal(a.itemId, b.itemId));

            // Notes are journal flags on the global board.
            _notes.Clear();
            StoryState.Global.CollectWithPrefix("journal.", _scratch);
            for (int i = 0; i < _scratch.Count; i++)
            {
                bool unlocked = _scratch[i].value is float number && number > 0f;
                if (unlocked) _notes.Add(_scratch[i].key.Substring("journal.".Length));
            }
            _notes.Sort(string.CompareOrdinal);
        }

        private int RowCount => _tab == Tab.Items ? _owned.Count : _notes.Count;

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
            Rebuild();

            _selected = RowCount == 0 ? 0 : Mathf.Clamp(_selected, 0, RowCount - 1);
            _tabLabel.text = _tab == Tab.Items ? "ITEMS" : "NOTES";

            DrawList();
            DrawDetail();
        }

        private void DrawList()
        {
            var text = new System.Text.StringBuilder(256);

            if (RowCount == 0)
            {
                text.Append(_tab == Tab.Items ? "Nothing carried." : "Nothing read yet.");
            }
            else if (_tab == Tab.Items)
            {
                for (int i = 0; i < _owned.Count; i++)
                {
                    var view = StoryItems.Describe(_owned[i].itemId);
                    string count = _owned[i].count > 1 ? $"  x{_owned[i].count}" : string.Empty;
                    text.Append(Row(i, view.DisplayName + count)).Append('\n');
                }
            }
            else
            {
                for (int i = 0; i < _notes.Count; i++)
                    text.Append(Row(i, NoteTitle(_notes[i]))).Append('\n');
            }

            _listText.text = text.ToString();
        }

        private string Row(int index, string label) => index == _selected
            ? $"<color=#FFD37A>> {label}</color>"
            : $"  {label}";

        private void DrawDetail()
        {
            bool empty = RowCount == 0;
            _examineButton.gameObject.SetActive(!empty && _tab == Tab.Items);
            _useButton.gameObject.SetActive(!empty && _allowUse && _tab == Tab.Items);

            if (empty)
            {
                _detailTitle.text = string.Empty;
                _detailBody.text = string.Empty;
                _detailIcon.enabled = false;
                return;
            }

            if (_tab == Tab.Notes)
            {
                string entryId = _notes[_selected];
                _detailTitle.text = NoteTitle(entryId);
                _detailBody.text = NoteBody(entryId);
                _detailIcon.enabled = false;
                return;
            }

            var view = StoryItems.Describe(_owned[_selected].itemId);
            _detailTitle.text = view.DisplayName + (view.IsKeyItem ? "   [key item]" : string.Empty);

            _detailIcon.sprite = view.Icon;
            _detailIcon.enabled = view.Icon != null;

            if (_examining && !string.IsNullOrWhiteSpace(view.ExamineText))
                _detailBody.text = view.ExamineText + "\n\n<i>Press advance to go back.</i>";
            else
                _detailBody.text = view.Description;
        }

        private static string NoteTitle(string entryId)
        {
            string text = StoryServices.Get<ILocalizationService>()?.Resolve(entryId + ".title");
            return string.IsNullOrEmpty(text) || text == entryId + ".title" ? entryId : text;
        }

        private static string NoteBody(string entryId)
        {
            string text = StoryServices.Get<ILocalizationService>()?.Resolve(entryId + ".body");
            return string.IsNullOrEmpty(text) || text == entryId + ".body"
                ? "(no text recorded for this note)"
                : text;
        }

        // ---------------- Buttons ----------------

        private void Move(int delta)
        {
            if (RowCount == 0) return;
            _selected = (_selected + delta + RowCount) % RowCount;
            _examining = false;
            _dirty = true;
        }

        private void SwitchTab()
        {
            _tab = _tab == Tab.Items ? Tab.Notes : Tab.Items;
            _selected = 0;
            _examining = false;
            _dirty = true;
        }

        private void Examine()
        {
            _examining = !_examining;
            _dirty = true;

            if (_tab == Tab.Items && _selected < _owned.Count)
                StoryEvents.Raise("Item.Examined", _owned[_selected].itemId);
        }

        private void Use()
        {
            if (_tab != Tab.Items || _selected >= _owned.Count) return;

            string itemId = _owned[_selected].itemId;

            // The screen never decides what using something does. It says what
            // happened and the story graph reacts, so one item can mean
            // different things in different rooms.
            StoryEvents.Raise("Item.Used", itemId);

            if (_closeOnUse) SetOpen(false);
            else _dirty = true;
        }

        // ---------------- Build the UI ----------------

        private void Build()
        {
            var canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 450;

            var scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            gameObject.AddComponent<GraphicRaycaster>();
            StoryUi.EnsureEventSystem();

            _root = Panel("Root", transform, Vector2.zero, Vector2.one,
                new Color(0.04f, 0.05f, 0.07f, 0.96f));
            var rootRect = (RectTransform)_root.transform;
            rootRect.offsetMin = Vector2.zero;
            rootRect.offsetMax = Vector2.zero;
            
            var cg = _root.AddComponent<CanvasGroup>();
            cg.alpha = 0f;

            // Header with the tab name and a switch button.
            _tabLabel = Label("Tab", rootRect, new Vector2(0, 1), new Vector2(0.5f, 1),
                new Vector2(60, -60), 34, TextAnchor.MiddleLeft);
            MakeButton("Tab switch", rootRect, new Vector2(1, 1), new Vector2(-260, -60),
                new Vector2(180, 46), "Items / Notes", SwitchTab);

            // Left column: the list.
            var listPanel = Panel("List", rootRect, new Vector2(0, 0), new Vector2(0.44f, 1),
                new Color(1f, 1f, 1f, 0.03f));
            var listRect = (RectTransform)listPanel.transform;
            listRect.offsetMin = new Vector2(60, 120);
            listRect.offsetMax = new Vector2(-20, -110);

            _listText = Label("ListText", listRect, Vector2.zero, Vector2.one,
                Vector2.zero, 26, TextAnchor.UpperLeft);
            Stretch((RectTransform)_listText.transform, 24, 24);

            // Right column: the selected thing.
            var detail = Panel("Detail", rootRect, new Vector2(0.46f, 0), new Vector2(1, 1),
                new Color(1f, 1f, 1f, 0.03f));
            var detailRect = (RectTransform)detail.transform;
            detailRect.offsetMin = new Vector2(20, 120);
            detailRect.offsetMax = new Vector2(-60, -110);

            var icon = new GameObject("Icon", typeof(RectTransform), typeof(Image));
            var iconRect = (RectTransform)icon.transform;
            iconRect.SetParent(detailRect, false);
            iconRect.anchorMin = new Vector2(0.5f, 1);
            iconRect.anchorMax = new Vector2(0.5f, 1);
            iconRect.pivot = new Vector2(0.5f, 1);
            iconRect.anchoredPosition = new Vector2(0, -30);
            iconRect.sizeDelta = new Vector2(220, 220);
            _detailIcon = icon.GetComponent<Image>();
            _detailIcon.preserveAspect = true;
            _detailIcon.enabled = false;

            _detailTitle = Label("Title", detailRect, new Vector2(0, 1), new Vector2(1, 1),
                new Vector2(0, -290), 30, TextAnchor.UpperCenter);
            Stretch((RectTransform)_detailTitle.transform, 24, 24);
            ((RectTransform)_detailTitle.transform).sizeDelta = new Vector2(0, 44);

            _detailBody = Label("Body", detailRect, Vector2.zero, Vector2.one,
                Vector2.zero, 24, TextAnchor.UpperLeft);
            var bodyRect = (RectTransform)_detailBody.transform;
            bodyRect.offsetMin = new Vector2(24, 24);
            bodyRect.offsetMax = new Vector2(-24, -350);

            // Footer buttons.
            MakeButton("Up", rootRect, Vector2.zero, new Vector2(80, 60),
                new Vector2(120, 54), "Up", () => Move(-1));
            MakeButton("Down", rootRect, Vector2.zero, new Vector2(210, 60),
                new Vector2(120, 54), "Down", () => Move(1));

            _examineButton = MakeButton("Examine", rootRect, Vector2.zero, new Vector2(380, 60),
                new Vector2(170, 54), "Examine", Examine);
            _useButton = MakeButton("Use", rootRect, Vector2.zero, new Vector2(570, 60),
                new Vector2(120, 54), "Use", Use);

            MakeButton("Close", rootRect, new Vector2(1, 0), new Vector2(-120, 60),
                new Vector2(160, 54), "Close", () => SetOpen(false));
        }

        // ---------------- Small UI helpers ----------------

        private static GameObject Panel(string name, Transform parent,
            Vector2 anchorMin, Vector2 anchorMax, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            go.GetComponent<Image>().color = color;
            return go;
        }

        private static void Stretch(RectTransform rect, float horizontal, float vertical)
        {
            rect.offsetMin = new Vector2(horizontal, vertical);
            rect.offsetMax = new Vector2(-horizontal, -vertical);
        }

        private static Text Label(string name, Transform parent, Vector2 anchorMin,
            Vector2 anchorMax, Vector2 position, int size, TextAnchor alignment)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Text));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.anchoredPosition = position;
            if (anchorMin != anchorMax) { rect.offsetMin = Vector2.zero; rect.offsetMax = Vector2.zero; }
            else rect.sizeDelta = new Vector2(600, 44);

            var text = go.GetComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = size;
            text.alignment = alignment;
            text.color = new Color(0.92f, 0.94f, 0.98f);
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            text.supportRichText = true;
            return text;
        }

        private static Button MakeButton(string name, Transform parent, Vector2 anchor,
            Vector2 position, Vector2 size, string label, UnityEngine.Events.UnityAction onClick)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = anchor;
            rect.anchorMax = anchor;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;

            go.GetComponent<Image>().color = new Color(1f, 1f, 1f, 0.09f);
            go.GetComponent<Button>().onClick.AddListener(onClick);

            var text = Label(name + " Label", rect, Vector2.zero, Vector2.one,
                Vector2.zero, 22, TextAnchor.MiddleCenter);
            text.text = label;
            return go.GetComponent<Button>();
        }
    }

    // ---------------- Nodes ----------------

    /// <summary>
    /// Opens or closes the item screen from a graph, and can wait for the
    /// player to close it before the story continues.
    /// </summary>
    [System.Serializable]
    [NodeMenu("Inventory",
        "Open the item screen. Optionally hold the story until the player closes it.")]
    public sealed class ItemScreenNode : TaskNode
    {
        public enum Action { Open, OpenNotes, Close, Toggle }

        [SerializeField] private Action _action = Action.Open;

        [Tooltip("Hold the story here until the player closes the screen.")]
        [SerializeField] private bool _waitForClose = true;

        public override NodeEffect Effect => NodeEffect.Consequence;
        public override string GetSummary() => _action.ToString();

        public override Cysharp.Threading.Tasks.UniTask ExecuteAsync(
            FlowContext context, System.Threading.CancellationToken ct)
        {
            var screen = SimpleItemScreen.Active;
            if (screen == null)
            {
                StoryEvents.Raise("Ui.ItemScreen", _action.ToString());
                return Cysharp.Threading.Tasks.UniTask.CompletedTask;
            }

            switch (_action)
            {
                case Action.Open: screen.SetOpen(true); break;
                case Action.OpenNotes: screen.OpenNotes(); break;
                case Action.Close: screen.SetOpen(false); break;
                case Action.Toggle: screen.Toggle(); break;
            }
            return Cysharp.Threading.Tasks.UniTask.CompletedTask;
        }

        public override async Cysharp.Threading.Tasks.UniTask OnRunningAsync(
            FlowContext context, System.Threading.CancellationToken ct)
        {
            var screen = SimpleItemScreen.Active;
            if (!_waitForClose || screen == null || _action == Action.Close) return;

            await Cysharp.Threading.Tasks.UniTask.WaitUntil(
                () => SimpleItemScreen.Active == null || !SimpleItemScreen.Active.IsOpen,
                cancellationToken: ct);
        }

        public override Cysharp.Threading.Tasks.UniTask OnFinallyAsync(
            FlowContext context, System.Threading.CancellationToken ct)
            => Cysharp.Threading.Tasks.UniTask.CompletedTask;
    }
}
