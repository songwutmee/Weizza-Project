using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace StoryKit.Defaults
{
    /// <summary>
    /// Zero-setup dialogue box. Builds its own Canvas at runtime: no prefab,
    /// no TextMeshPro dependency. Typewriter text, click/space to advance
    /// (first press reveals all, second advances), vertical choice buttons.
    ///
    /// Meant to get a game playable on day one; swap for your own IDialogueUI
    /// when you have real art. Registering:
    ///     StoryServices.Register&lt;IDialogueUI&gt;(SimpleDialogueUI.Create());
    /// </summary>
    public sealed class SimpleDialogueUI : MonoBehaviour, IDialogueUI
    {
        [SerializeField] private float _charsPerSecond = 40f;

        private GameObject _panel;
        private Image _portrait;
        private Text _speakerText;
        private Text _bodyText;
        private StoryTextEffects _textEffects;
        private readonly System.Collections.Generic.List<StoryTextEffects.Span> _effectSpans = new();
        private Transform _choiceRoot;

        // ---- VN player-experience layer (Naninovel/Ren'Py research) ----

        public readonly struct BacklogEntry
        {
            public readonly string Speaker;
            public readonly string Text;
            public BacklogEntry(string speaker, string text) { Speaker = speaker; Text = text; }
        }

        /// <summary>Rolling dialogue history (last 100 lines) for a backlog UI.</summary>
        public static readonly System.Collections.Generic.List<BacklogEntry> Backlog = new(100);

        /// <summary>Auto mode: advances automatically after AutoDelay seconds.</summary>
        public static bool AutoMode;
        public static float AutoDelay = 1.6f;

        /// <summary>
        /// Skip mode: instantly advances lines the player has already read.
        /// "Read" = the line key was shown before, persisted in the Global board
        /// as visit-count keys ("_read.&lt;hash&gt;"), so it survives save/load.
        /// </summary>
        public static bool SkipReadMode;

        /// <summary>Clears session state so a second play run starts clean.</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetSessionState()
        {
            Backlog.Clear();
            AutoMode = false;
            SkipReadMode = false;
            AutoDelay = 1.6f;
        }
        private UniTaskCompletionSource _advance;
        private UniTaskCompletionSource<int> _choicePick;
        private bool _revealing;

        public static SimpleDialogueUI Create()
        {
            var host = new GameObject("StoryKit.SimpleDialogueUI");
            DontDestroyOnLoad(host);
            var ui = host.AddComponent<SimpleDialogueUI>();
            ui.BuildCanvas();
            return ui;
        }

        // ---------- IDialogueUI ----------

        [Tooltip("Stop the player walking while the box is open. On for almost " +
                 "every game: without it the player strolls away mid sentence and " +
                 "the dialogue follows them around. Turn it off for walk and talk " +
                 "barks that should not interrupt play.")]
        [SerializeField] private bool _lockMovementWhileTalking = true;

        private bool _holdingCursor;
        private System.IDisposable _movementLock;

        public async UniTask OpenAsync(CancellationToken ct)
        {
            ShowPanel(true);
            await FadePanelAsync(true, ct);
        }

        public async UniTask CloseAsync(CancellationToken ct)
        {
            ReleaseHolds();
            await FadePanelAsync(false, ct);
            _panel.SetActive(false);
        }

        private async UniTask FadePanelAsync(bool visible, CancellationToken ct)
        {
            var cg = _panel.GetComponent<CanvasGroup>();
            if (cg == null) return;

            float target = visible ? 1f : 0f;
            float start = cg.alpha;
            float duration = 0.15f;
            float elapsed = 0f;

            while (elapsed < duration)
            {
                ct.ThrowIfCancellationRequested();
                await UniTask.Yield(PlayerLoopTiming.Update, ct);
                elapsed += StoryTime.DeltaTime;
                cg.alpha = Mathf.Lerp(start, target, elapsed / duration);
            }
            cg.alpha = target;
        }

        /// <summary>
        /// Shows or hides the box, and borrows the cursor while it is open.
        /// A first person game hides the pointer to look around, and then a
        /// conversation with clickable choices is unusable without it, so the
        /// box takes the cursor and gives it straight back.
        /// </summary>
        private void ShowPanel(bool visible)
        {
            // Read the old state before changing it: the frame the box opens on
            // is what protects the first line, and only the opening transition
            // counts.
            bool wasOpen = _panel.activeSelf;
            _panel.SetActive(visible);

            if (visible)
            {
                if (!wasOpen) _openedOnFrame = Time.frameCount;
                if (!_holdingCursor)
                {
                    StoryUi.ReleaseCursor();
                    _holdingCursor = true;
                }

                // Holding the player still is the difference between a
                // conversation and a box that chases someone around a room.
                if (_lockMovementWhileTalking && _movementLock == null)
                    _movementLock = StoryControl.Lock("dialogue");
            }
            // Note: Releasing holds is now handled directly by CloseAsync before fading out.
        }

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

        // Switched off, destroyed or the scene changed mid line: the player must
        // not be left frozen with an invisible box holding them.
        private void OnDisable() => ReleaseHolds();
        private void OnDestroy() => ReleaseHolds();

        public async UniTask SayAsync(string speakerId, string emotion, string text, CancellationToken ct)
        {
            ShowPanel(true);
            ClearChoices();

            // Character database: display name, name color, portrait (with emotion).
            var characters = StoryServices.Get<ICharacterService>();
            if (characters != null && characters.TryGet(speakerId, emotion, out var view))
            {
                _speakerText.text = view.DisplayName;
                _speakerText.color = view.NameColor;
                _portrait.sprite = view.Portrait;
                _portrait.gameObject.SetActive(view.Portrait != null);
            }
            else
            {
                _speakerText.text = speakerId ?? string.Empty;
                _speakerText.color = new Color(1f, 0.85f, 0.4f);
                _portrait.gameObject.SetActive(false);
            }

            // Live player preferences (persisted via StorySave automatically).
            _charsPerSecond = StoryPreferences.TextSpeed;
            AutoMode = StoryPreferences.AutoMode;
            AutoDelay = StoryPreferences.AutoDelay;
            SkipReadMode = StoryPreferences.SkipReadMode;

            // <wave>/<shake> markup → per-character animation spans.
            text = StoryTextEffects.ParseMarkup(text, _effectSpans);
            if (StoryPreferences.ReduceTextMotion) _effectSpans.Clear();  // accessibility
            _textEffects.SetSpans(_effectSpans);

            // Backlog + read tracking (key = stable hash of the line content).
            Backlog.Add(new BacklogEntry(speakerId, text));
            if (Backlog.Count > 100) Backlog.RemoveAt(0);
            string readKey = "_read." + (uint)text.GetHashCode();
            bool alreadyRead = StoryState.Global.Get<float>(readKey) > 0f;
            StoryState.Global.Set(readKey, 1f);

            // Skip-read: known line → show instantly, advance after a beat.
            if (SkipReadMode && alreadyRead)
            {
                _bodyText.text = text;
                _revealing = false;
                float pause = 0f;
                while (pause < 0.06f)
                {
                    ct.ThrowIfCancellationRequested();
                    await UniTask.Yield(PlayerLoopTiming.Update, ct);
                    pause += StoryTime.DeltaTime;
                }
                return;
            }

            // Typewriter reveal, skippable by one click.
            _revealing = true;
            _advance = new UniTaskCompletionSource();
            _bodyText.text = string.Empty;

            float perChar = _charsPerSecond <= 0f ? 0f : 1f / _charsPerSecond;
            for (int i = 0; i < text.Length; i++)
            {
                if (!_revealing) break; // player skipped ahead
                _bodyText.text = text.Substring(0, i + 1);

                float t = 0f;
                while (t < perChar && _revealing)
                {
                    ct.ThrowIfCancellationRequested();
                    await UniTask.Yield(PlayerLoopTiming.Update, ct);
                    t += StoryTime.DeltaTime;
                }
            }
            _bodyText.text = text;
            _revealing = false;

            // Wait for the advance press; in auto mode a timer can advance too.
            if (AutoMode)
            {
                float waited = 0f;
                while (waited < AutoDelay && _advance != null &&
                       _advance.Task.Status == UniTaskStatus.Pending)
                {
                    ct.ThrowIfCancellationRequested();
                    await UniTask.Yield(PlayerLoopTiming.Update, ct);
                    waited += StoryTime.DeltaTime;
                }
            }
            else
            {
                await _advance.Task.AttachExternalCancellation(ct);
            }
            _advance = null;
        }

        public async UniTask<int> ChoiceAsync(IReadOnlyList<string> options, CancellationToken ct)
        {
            ShowPanel(true);
            ClearChoices();
            _choicePick = new UniTaskCompletionSource<int>();

            GameObject firstButton = null;

            // Buttons are pooled: choices open every few seconds in dialogue-heavy
            // games, so we reuse instead of Instantiate/Destroy churn.
            for (int i = 0; i < options.Count; i++)
            {
                int captured = i;
                var button = RentButton();
                button.Label.text = $"{i + 1}. {options[i]}";
                button.Button.onClick.RemoveAllListeners();
                button.Button.onClick.AddListener(() => _choicePick?.TrySetResult(captured));
                button.Root.SetActive(true);
                firstButton ??= button.Root;
            }

            // Without a selected button, a gamepad or keyboard cannot pick a
            // choice at all: the stick moves nothing because nothing is
            // focused. Mouse players never notice, which is how it ships broken.
            var events = EventSystem.current;
            if (events != null && firstButton != null)
            {
                events.SetSelectedGameObject(null);
                events.SetSelectedGameObject(firstButton);
            }

            int picked = await _choicePick.Task.AttachExternalCancellation(ct);
            _choicePick = null;
            ClearChoices();

            if (events != null) events.SetSelectedGameObject(null);
            return picked;
        }

        private readonly struct PooledButton
        {
            public readonly GameObject Root;
            public readonly Button Button;
            public readonly Text Label;
            public PooledButton(GameObject root, Button button, Text label)
            {
                Root = root; Button = button; Label = label;
            }
        }

        private readonly System.Collections.Generic.List<PooledButton> _buttonPool = new();
        private int _buttonsInUse;

        private PooledButton RentButton()
        {
            if (_buttonsInUse < _buttonPool.Count)
                return _buttonPool[_buttonsInUse++];

            var go = MakeButton(string.Empty, null);
            go.transform.SetParent(_choiceRoot, false);
            var pooled = new PooledButton(go,
                go.GetComponent<Button>(),
                go.GetComponentInChildren<Text>());
            _buttonPool.Add(pooled);
            _buttonsInUse++;
            return pooled;
        }

        // ---------- Input ----------

        private int _openedOnFrame = -1;

        private void Update()
        {
            // A dialogue box that cannot be advanced is the worst failure this
            // package can have, so say something the first time input is missing.
            StoryInput.WarnIfMissing(this);

            // The button press that started the conversation is often still
            // reading as pressed on the frame the box appears, which eats the
            // first line before anyone can read it.
            if (Time.frameCount == _openedOnFrame) return;

            if (StoryInput.Advance) Advance();
        }

        /// <summary>Advance/skip. Public so new Input System users can bind it.</summary>
        public void Advance()
        {
            if (_choicePick != null) return;   // choices need a button press
            if (_revealing) { _revealing = false; return; }
            _advance?.TrySetResult();
        }

        // ---------- UI construction ----------

        private void BuildCanvas()
        {
            // Asks for the module that matches the project. Building one here
            // with StandaloneInputModule threw every frame on a new Input
            // System project and left the buttons dead anyway.
            StoryUi.EnsureEventSystem();

            var canvasGo = new GameObject("Canvas",
                typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasGo.transform.SetParent(transform, false);
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 500;
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);

            // Bottom panel
            _panel = MakeRect("DialoguePanel", canvasGo.transform,
                anchorMin: new Vector2(0.08f, 0.03f), anchorMax: new Vector2(0.92f, 0.30f));
            var bg = _panel.AddComponent<Image>();
            bg.color = new Color(0.06f, 0.07f, 0.10f, 0.92f);
            
            var cg = _panel.AddComponent<CanvasGroup>();
            cg.alpha = 0f;

            // Speaker name
            var speakerGo = MakeRect("Speaker", _panel.transform,
                new Vector2(0.02f, 0.72f), new Vector2(0.5f, 0.98f));
            _speakerText = speakerGo.AddComponent<Text>();
            StyleText(_speakerText, 30, FontStyle.Bold, new Color(1f, 0.85f, 0.4f));

            // Body
            var bodyGo = MakeRect("Body", _panel.transform,
                new Vector2(0.02f, 0.05f), new Vector2(0.72f, 0.70f));
            _bodyText = bodyGo.AddComponent<Text>();
            StyleText(_bodyText, 26, FontStyle.Normal, Color.white);
            _textEffects = bodyGo.AddComponent<StoryTextEffects>();

            // Choice column (right side)
            var choicesGo = MakeRect("Choices", _panel.transform,
                new Vector2(0.74f, 0.05f), new Vector2(0.98f, 0.95f));
            var layout = choicesGo.AddComponent<VerticalLayoutGroup>();
            layout.spacing = 8;
            layout.childForceExpandHeight = false;
            _choiceRoot = choicesGo.transform;

            _panel.SetActive(false);
        }

        private static GameObject MakeRect(string name, Transform parent,
            Vector2 anchorMin, Vector2 anchorMax)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            return go;
        }

        private static void StyleText(Text text, int size, FontStyle style, Color color)
        {
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = size;
            text.fontStyle = style;
            text.color = color;
            text.alignment = TextAnchor.UpperLeft;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
        }

        private GameObject MakeButton(string label, System.Action onClick)
        {
            var go = new GameObject("Choice", typeof(RectTransform), typeof(Image), typeof(Button),
                typeof(LayoutElement));
            go.GetComponent<LayoutElement>().minHeight = 44;
            var image = go.GetComponent<Image>();
            image.color = new Color(0.18f, 0.22f, 0.32f, 0.95f);
            var button = go.GetComponent<Button>();
            if (onClick != null)
                button.onClick.AddListener(() => onClick());

            var textGo = MakeRect("Label", go.transform, Vector2.zero, Vector2.one);
            var text = textGo.AddComponent<Text>();
            StyleText(text, 22, FontStyle.Bold, Color.white);
            text.alignment = TextAnchor.MiddleLeft;
            ((RectTransform)textGo.transform).offsetMin = new Vector2(12, 0);
            text.text = label;
            return go;
        }

        private void ClearChoices()
        {
            // Pooled: hide instead of destroy.
            foreach (var pooled in _buttonPool)
                pooled.Root.SetActive(false);
            _buttonsInUse = 0;
        }
    }

    /// <summary>
    /// One-component game setup. Drop on a GameObject in your first scene:
    /// registers the default dialogue UI + localization + a working input lock.
    /// After this, graphs with Say/Choice/LockControl nodes just work.
    /// </summary>
    [AddComponentMenu("StoryKit/StoryKit Bootstrap")]
    public sealed class StoryKitBootstrap : MonoBehaviour
    {
        [SerializeField] private LocalizationTableAsset _localizationTable;
        [SerializeField] private string _language = "";

        private void Awake()
        {
            StoryServices.Register<IDialogueUI>(SimpleDialogueUI.Create());
            StoryServices.Register<IScreenUI>(SimpleScreenUI.Create());
            StoryServices.Register<ILocalizationService>(
                new TableLocalizationService(_localizationTable,
                    string.IsNullOrEmpty(_language) ? null : _language));
            StoryServices.Register<IInputService>(new StoryControlService());
        }

    }
}
