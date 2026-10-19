using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Sirenix.OdinInspector;
using StoryKit;
using StoryKit.Bridge;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Weizza.Narrative
{
    /// <summary>
    /// Says lines in a speech bubble over the speaker's head instead of in a panel
    /// across the bottom of the screen.
    ///
    /// The world stays in view while someone talks, which suits a game where the
    /// forest is the point. It answers the same StoryKit dialogue interface the
    /// panel does, so a conversation asset, a flow graph and an NPC all reach it
    /// without knowing which one is on. Untick Use Bubbles and the old panel takes
    /// the lines back.
    ///
    /// Everything is built in code on Awake, bubble, tail and choices, so there is
    /// no prefab to keep in step, and everything runs on unscaled time for the
    /// same reason the panel does: a line can open during a hit stop.
    /// </summary>
    [DefaultExecutionOrder(50)]
    [AddComponentMenu("Weizza/Dialogue/Bubble Dialogue Screen")]
    public sealed class BubbleDialogueScreen : MonoBehaviour, IDialogueUI
    {
        [TitleGroup("Switch")]
        [ToggleLeft, Tooltip("On: lines appear as bubbles over the speaker. Off: the old panel at the bottom of the screen.")]
        [SerializeField] private bool useBubbles = true;

        [TitleGroup("Look")]
        [Tooltip("Font for the bubble text. Empty uses the dialogue panel's font, so Thai keeps working.")]
        [SerializeField] private TMP_FontAsset font;

        [TitleGroup("Look")]
        [PropertyRange(16f, 48f)]
        [SerializeField] private float textSize = 19f;

        [TitleGroup("Look")]
        [PropertyRange(200f, 900f), Tooltip("Widest a bubble may get, in reference pixels at 1920 wide. Longer lines wrap.")]
        [SerializeField] private float maxWidth = 380f;

        [TitleGroup("Art")]
        [PreviewField(40), Tooltip("The paper slip the line is written on. Nine sliced, so its torn edge keeps its size at any width.")]
        [SerializeField] private Sprite slip;

        [TitleGroup("Art")]
        [PreviewField(40), Tooltip("The dark slip the speaker's name sits on, pinned to the corner of the paper.")]
        [SerializeField] private Sprite nameSlip;

        [TitleGroup("Art")]
        [PreviewField(40), Tooltip("The thread from the paper down to the speaker. Its round end sits on the speaker's head.")]
        [SerializeField] private Sprite tailSprite;

        [TitleGroup("Art")]
        [PreviewField(40)]
        [SerializeField] private Sprite pinSprite;

        [TitleGroup("Art")]
        [PropertyRange(0.5f, 3f), Tooltip("Above 1 makes the torn edge thinner. The slip art carries a wide margin.")]
        [SerializeField] private float edgeThinness = 1.5f;

        [TitleGroup("Look")]
        [SerializeField] private Color paper = Color.white;

        [TitleGroup("Look")]
        [SerializeField] private Color ink = new(0.11f, 0.13f, 0.3f, 1f);

        [TitleGroup("Look")]
        [Tooltip("Colour of the speaker's name above the text.")]
        [SerializeField] private Color nameInk = new(0.97f, 0.96f, 0.93f, 1f);

        [TitleGroup("Look")]
        [PropertyRange(0f, 1.5f), Tooltip("Metres between the top of the speaker's drawing and the tail.")]
        [SerializeField] private float headroom = 0.15f;

        [TitleGroup("Pace")]
        [PropertyRange(0f, 120f), Tooltip("Letters per second. Zero prints the whole line at once.")]
        [SerializeField] private float lettersPerSecond = 40f;

        [TitleGroup("Pace")]
        [PropertyRange(0f, 0.8f), Tooltip("Extra pause after . ! ? and the Thai end of a sentence, so a line breathes like speech.")]
        [SerializeField] private float stopPause = 0.28f;

        [TitleGroup("Pace")]
        [PropertyRange(0f, 0.5f), Tooltip("Extra pause after a comma, and after a space in Thai, where a space marks a phrase.")]
        [SerializeField] private float commaPause = 0.1f;

        [TitleGroup("Speakers")]
        [Tooltip("Speaker ids that mean the player. Their bubble sits over the player.")]
        [SerializeField] private List<string> playerSpeakerIds = new() { "PLAYER", "MORI" };

        [TitleGroup("Wiring")]
        [Tooltip("Names come from here, the same database the panel uses.")]
        [SerializeField] private SpeakerDatabase speakers;

        private RectTransform _canvasRect;
        private RectTransform _bubble;
        private CanvasGroup _bubbleGroup;
        private TextMeshProUGUI _name;
        private RectTransform _nameTag;
        private TextMeshProUGUI _body;
        private RectTransform _tail;
        private RectTransform _next;
        private RectTransform _choiceRoot;
        private readonly List<Button> _choiceButtons = new();
        private Transform _speaker;
        private string _speakerId;
        private bool _built;

        private const float Pad = 58f;
        private const float ScreenMargin = 24f;

        public bool UseBubbles
        {
            get => useBubbles;
            set { useBubbles = value; Apply(); }
        }

        private GameObject _canvasRoot;

        private void Awake() => Build();

        private void OnDestroy()
        {
            if (_canvasRoot != null) Destroy(_canvasRoot);
        }

        private void OnEnable() => Apply();

        private void OnDisable()
        {
            if (!useBubbles) return;
            // Hand the lines back to the panel rather than leaving nobody listening.
            var panel = FindFirstObjectByType<DialogueScreen>(FindObjectsInactive.Include);
            StoryServices.Register<IDialogueUI>(panel);
        }

        private void OnValidate()
        {
            if (Application.isPlaying && isActiveAndEnabled) Apply();
        }

        private void Apply()
        {
            if (useBubbles)
            {
                StoryServices.Register<IDialogueUI>(this);
                return;
            }
            var panel = FindFirstObjectByType<DialogueScreen>(FindObjectsInactive.Include);
            if (panel != null) StoryServices.Register<IDialogueUI>(panel);
        }

        // ---- IDialogueUI ----------------------------------------------------

        public UniTask OpenAsync(CancellationToken ct)
        {
            Build();
            _bubble.gameObject.SetActive(false);
            _choiceRoot.gameObject.SetActive(false);
            _speakerId = null;
            return UniTask.CompletedTask;
        }

        public async UniTask CloseAsync(CancellationToken ct)
        {
            if (_bubble.gameObject.activeSelf) await Pop(false, ct);
            _bubble.gameObject.SetActive(false);
            _choiceRoot.gameObject.SetActive(false);
            _speaker = null;
        }

        public async UniTask SayAsync(string speakerId, string emotion, string text, CancellationToken ct)
        {
            Build();
            _next.gameObject.SetActive(false);

            // A new speaker gets a fresh bubble over their own head. The same
            // speaker keeps the bubble where it is and only the words change.
            var changed = speakerId != _speakerId || !_bubble.gameObject.activeSelf;
            if (changed && _bubble.gameObject.activeSelf) await Pop(false, ct);

            _speakerId = speakerId;
            _speaker = FindSpeaker(speakerId);
            _name.text = NameOf(speakerId);
            _nameTag.gameObject.SetActive(!string.IsNullOrEmpty(_name.text));
            Layout(text);

            if (changed)
            {
                _bubble.gameObject.SetActive(true);
                Follow();
                await Pop(true, ct);
            }

            await TypeAsync(text, ct);

            _next.gameObject.SetActive(true);
            await WaitForAdvance(ct);
            _next.gameObject.SetActive(false);
        }

        public async UniTask<int> ChoiceAsync(IReadOnlyList<string> options, CancellationToken ct)
        {
            Build();
            var picked = -1;

            while (_choiceButtons.Count < options.Count) _choiceButtons.Add(MakeChoiceButton());
            for (var i = 0; i < _choiceButtons.Count; i++)
            {
                var button = _choiceButtons[i];
                var usable = i < options.Count;
                button.gameObject.SetActive(usable);
                button.onClick.RemoveAllListeners();
                if (!usable) continue;
                button.GetComponentInChildren<TextMeshProUGUI>().text = options[i];
                var index = i;
                button.onClick.AddListener(() => picked = index);
            }

            // Choices are the player's words, so they sit over the player.
            _choiceRoot.gameObject.SetActive(true);
            PlaceChoices();
            if (options.Count > 0 && EventSystem.current != null)
                EventSystem.current.SetSelectedGameObject(_choiceButtons[0].gameObject);

            while (picked < 0)
            {
                PlaceChoices();
                await UniTask.Yield(PlayerLoopTiming.Update, ct);
            }

            _choiceRoot.gameObject.SetActive(false);
            if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
            return picked;
        }

        // ---- Following the speaker -------------------------------------------

        private void LateUpdate()
        {
            if (_built && _bubble.gameObject.activeSelf) Follow();
        }

        private Transform FindSpeaker(string speakerId)
        {
            if (!string.IsNullOrEmpty(speakerId))
            {
                foreach (var id in playerSpeakerIds)
                    if (string.Equals(id, speakerId, System.StringComparison.OrdinalIgnoreCase))
                        return PlayerLocator.Player != null ? PlayerLocator.Player.transform : null;

                var stage = StoryStageId.FindTransform(speakerId);
                if (stage != null) return stage;
            }

            // Whoever started the conversation is the best guess for anyone else,
            // which covers every NPC that talks alone.
            if (DialogueDirector.Speaker != null) return DialogueDirector.Speaker;
            return PlayerLocator.Player != null ? PlayerLocator.Player.transform : null;
        }

        /// <summary>The top of the drawing, not the pivot, so tall and short speakers both get the tail just over their head.</summary>
        private Vector3 HeadOf(Transform speaker)
        {
            var top = speaker.position;
            var found = false;
            foreach (var r in speaker.GetComponentsInChildren<SpriteRenderer>())
            {
                if (!r.enabled || r.sprite == null) continue;
                var y = r.bounds.max.y;
                if (!found || y > top.y) { top.y = y; found = true; }
            }
            if (!found) top.y += 1.8f;
            top.y += headroom;
            return top;
        }

        private void Follow()
        {
            var cam = Camera.main;
            if (cam == null) return;

            var target = _speaker != null ? HeadOf(_speaker) : cam.transform.position + cam.transform.forward * 10f;
            var anchor = ToCanvas(cam, target);

            // The paper hangs off to one side of the speaker with the thread dropping
            // from its lower edge, a quarter of the way in, the way a note is pinned
            // above someone rather than balanced on their head. The whole slip stays
            // on screen; the thread keeps its end on the speaker.
            var size = _bubble.sizeDelta;
            var half = _canvasRect.rect.size * 0.5f;
            // The slip art has a clear margin under its torn edge, so the paper sits lower
            // than the thread is long and the thread disappears under it.
            var drop = _tail.sizeDelta.y - 34f;
            var x = Mathf.Clamp(anchor.x + size.x * 0.25f, -half.x + ScreenMargin + size.x * 0.5f, half.x - ScreenMargin - size.x * 0.5f);
            var y = Mathf.Clamp(anchor.y + drop, -half.y + ScreenMargin, half.y - ScreenMargin - size.y);
            _bubble.anchoredPosition = new Vector2(x, y);

            var tailX = Mathf.Clamp(anchor.x - x, -size.x * 0.5f + 30f, size.x * 0.5f - 30f);
            _tail.anchoredPosition = new Vector2(tailX, anchor.y - y);
        }

        private void PlaceChoices()
        {
            var cam = Camera.main;
            var player = PlayerLocator.Player;
            if (cam == null || player == null) return;
            var anchor = ToCanvas(cam, HeadOf(player.transform));
            var half = _canvasRect.rect.size * 0.5f;
            var w = _choiceRoot.sizeDelta.x;
            var x = Mathf.Clamp(anchor.x, -half.x + ScreenMargin + w * 0.5f, half.x - ScreenMargin - w * 0.5f);
            _choiceRoot.anchoredPosition = new Vector2(x, Mathf.Min(anchor.y + 18f, half.y - ScreenMargin - 200f));
        }

        private Vector2 ToCanvas(Camera cam, Vector3 world)
        {
            var screen = cam.WorldToScreenPoint(world);
            RectTransformUtility.ScreenPointToLocalPointInRectangle(_canvasRect, screen, null, out var local);
            return local;
        }

        private string NameOf(string speakerId)
        {
            var key = speakers != null ? speakers.NameKey(speakerId) : speakerId;
            var loca = StoryServices.Get<ILocalizationService>();
            return loca != null ? loca.Resolve(key) : key;
        }

        // ---- Text -----------------------------------------------------------

        /// <summary>Sizes the bubble for the whole line before typing, so it never grows letter by letter.</summary>
        private void Layout(string text)
        {
            var inner = maxWidth - Pad * 2f;
            var body = _body.GetPreferredValues(text ?? string.Empty, inner, 0f);
            var width = Mathf.Clamp(body.x, 80f, inner);
            body = _body.GetPreferredValues(text ?? string.Empty, width, 0f);

            // The text sits a little lower when a name tag is pinned over the top edge.
            var tagged = _nameTag.gameObject.activeSelf;
            var top = Pad + (tagged ? 6f : 0f);
            _bubble.sizeDelta = new Vector2(width + Pad * 2f, body.y + top + Pad);

            var bodyRect = _body.rectTransform;
            bodyRect.anchoredPosition = new Vector2(Pad, -top);
            bodyRect.sizeDelta = new Vector2(width + 2f, body.y);

            if (tagged)
            {
                var name = _name.GetPreferredValues(_name.text);
                _nameTag.sizeDelta = new Vector2(name.x + 56f, name.y + 34f);
                var nameRect = _name.rectTransform;
                nameRect.anchoredPosition = new Vector2(30f, -16f);
                nameRect.sizeDelta = new Vector2(name.x + 2f, name.y);
            }

            _body.text = text;
            _body.maxVisibleCharacters = 0;
        }

        private async UniTask TypeAsync(string text, CancellationToken ct)
        {
            if (lettersPerSecond <= 0f || string.IsNullOrEmpty(text))
            {
                _body.maxVisibleCharacters = int.MaxValue;
                return;
            }

            _body.ForceMeshUpdate();
            var info = _body.textInfo;
            var total = info.characterCount;
            var shown = 0;
            var wait = 0f;

            while (shown < total)
            {
                if (AdvanceWanted()) break;

                wait -= Time.unscaledDeltaTime;
                while (wait <= 0f && shown < total)
                {
                    var c = info.characterInfo[shown].character;
                    shown++;
                    wait += 1f / lettersPerSecond + PauseAfter(c, shown < total ? info.characterInfo[shown].character : '\0');
                }

                _body.maxVisibleCharacters = shown;
                await UniTask.Yield(PlayerLoopTiming.Update, ct);
            }

            _body.maxVisibleCharacters = int.MaxValue;
        }

        private float PauseAfter(char c, char next)
        {
            // A run of dots or an ellipsis pauses once, at its end.
            if (next == '.' || next == '…') return 0f;
            switch (c)
            {
                case '.':
                case '!':
                case '?':
                case '…':
                case 'ฯ': // Thai paiyannoi, often ends a phrase
                    return stopPause;
                case ',':
                case ';':
                case ':':
                    return commaPause;
                case ' ':
                    // Thai writes no commas; a space is where a phrase ends.
                    return next >= '฀' && next <= '๿' ? commaPause : 0f;
                default:
                    return 0f;
            }
        }

        private async UniTask WaitForAdvance(CancellationToken ct)
        {
            // Same two frames of grace as the panel: the press that finished the
            // typing must not also skip the line, and must not complete the next one.
            await UniTask.Yield(PlayerLoopTiming.Update, ct);
            var t = 0f;
            while (!AdvanceWanted())
            {
                t += Time.unscaledDeltaTime;
                _next.anchoredPosition = new Vector2(-Pad + 4f, Pad - 14f + Mathf.Sin(t * 6f) * 3f);
                await UniTask.Yield(PlayerLoopTiming.Update, ct);
            }
            await UniTask.Yield(PlayerLoopTiming.Update, ct);
        }

        private static bool AdvanceWanted()
        {
            var reader = InputReader.Instance;
            return reader != null && reader.AdvancePressed;
        }

        private async UniTask Pop(bool show, CancellationToken ct)
        {
            const float seconds = 0.14f;
            var t = 0f;
            while (t < seconds)
            {
                t += Time.unscaledDeltaTime;
                var k = Mathf.Clamp01(t / seconds);
                var e = show ? 1f - (1f - k) * (1f - k) : 1f - k * k;
                _bubbleGroup.alpha = e;
                // Grows out of the tail, so the bubble reads as coming from the speaker.
                _bubble.localScale = Vector3.one * Mathf.Lerp(0.82f, 1f, e);
                await UniTask.Yield(PlayerLoopTiming.Update, ct);
            }
            _bubbleGroup.alpha = show ? 1f : 0f;
            _bubble.localScale = Vector3.one;
        }

        // ---- Building the pieces --------------------------------------------

        private void Build()
        {
            if (_built) return;
            _built = true;

            if (font == null)
            {
                var panel = FindFirstObjectByType<DialogueScreen>(FindObjectsInactive.Include);
                var label = panel != null ? panel.GetComponentInChildren<TextMeshProUGUI>(true) : null;
                font = label != null ? label.font : TMP_Settings.defaultFontAsset;
            }

            // A root canvas of its own, never a child of the dialogue panel's canvas:
            // a nested canvas takes a plain 100 by 100 rect from its parent, so every
            // screen position worked out against it lands in the wrong place.
            var canvasGo = new GameObject("Bubble Canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(canvasGo, gameObject.scene);
            _canvasRoot = canvasGo;
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 40;
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            _canvasRect = (RectTransform)canvasGo.transform;

            _bubble = NewRect("Bubble", _canvasRect);
            _bubble.anchorMin = _bubble.anchorMax = new Vector2(0.5f, 0.5f);
            _bubble.pivot = new Vector2(0.5f, 0f);
            _bubbleGroup = _bubble.gameObject.AddComponent<CanvasGroup>();
            _bubbleGroup.blocksRaycasts = false;
            // The thread goes in first so the paper draws over its top end.
            _tail = NewRect("Thread", _bubble);
            _tail.anchorMin = _tail.anchorMax = new Vector2(0.5f, 0f);
            _tail.pivot = new Vector2(0.5f, 0f);
            var tailImage = _tail.gameObject.AddComponent<Image>();
            if (tailSprite != null)
            {
                tailImage.sprite = tailSprite;
                tailImage.preserveAspect = true;
                // The art's round end sits on the speaker; its height sets how far the paper floats.
                _tail.sizeDelta = new Vector2(52f, 52f * tailSprite.rect.height / tailSprite.rect.width);
            }
            else
            {
                tailImage.sprite = TriangleSprite(down: true);
                tailImage.color = paper;
                _tail.sizeDelta = new Vector2(30f, 18f);
            }

            var back = NewRect("Paper", _bubble);
            back.anchorMin = Vector2.zero;
            back.anchorMax = Vector2.one;
            back.offsetMin = back.offsetMax = Vector2.zero;
            var paperImage = back.gameObject.AddComponent<Image>();
            paperImage.sprite = slip != null ? slip : RoundedSprite;
            paperImage.type = Image.Type.Sliced;
            paperImage.pixelsPerUnitMultiplier = slip != null ? edgeThinness : 1f;
            paperImage.color = paper;

            _body = NewLabel("Text", _bubble, textSize, ink, FontStyles.Normal);
            _body.lineSpacing = 12f;

            // The name is a dark slip pinned over the top left corner, tilted a little,
            // the way a label gets tacked onto a note in a hurry.
            _nameTag = NewRect("Name Tag", _bubble);
            _nameTag.anchorMin = _nameTag.anchorMax = new Vector2(0f, 1f);
            _nameTag.pivot = new Vector2(0f, 0.35f);
            _nameTag.anchoredPosition = new Vector2(8f, -24f);
            _nameTag.localRotation = Quaternion.Euler(0f, 0f, 4f);
            var tagImage = _nameTag.gameObject.AddComponent<Image>();
            tagImage.sprite = nameSlip != null ? nameSlip : RoundedSprite;
            tagImage.type = Image.Type.Sliced;
            tagImage.pixelsPerUnitMultiplier = nameSlip != null ? edgeThinness * 2.2f : 1f;
            tagImage.color = nameSlip != null ? Color.white : ink;
            _name = NewLabel("Name", _nameTag, textSize * 0.92f, nameInk, FontStyles.Bold | FontStyles.Italic);

            if (pinSprite != null)
            {
                var pin = NewRect("Pin", _nameTag);
                pin.anchorMin = pin.anchorMax = new Vector2(0f, 0.5f);
                pin.anchoredPosition = new Vector2(20f, -2f);
                pin.sizeDelta = new Vector2(18f, 18f);
                pin.gameObject.AddComponent<Image>().sprite = pinSprite;
            }

            _next = NewRect("Next", _bubble);
            _next.anchorMin = _next.anchorMax = new Vector2(1f, 0f);
            _next.pivot = new Vector2(1f, 0f);
            _next.sizeDelta = new Vector2(14f, 9f);
            var nextImage = _next.gameObject.AddComponent<Image>();
            nextImage.sprite = TriangleSprite(down: true);
            nextImage.color = ink;

            _choiceRoot = NewRect("Choices", _canvasRect);
            _choiceRoot.anchorMin = _choiceRoot.anchorMax = new Vector2(0.5f, 0.5f);
            _choiceRoot.pivot = new Vector2(0.5f, 0f);
            _choiceRoot.sizeDelta = new Vector2(maxWidth, 10f);
            var stack = _choiceRoot.gameObject.AddComponent<VerticalLayoutGroup>();
            stack.spacing = 8f;
            stack.childAlignment = TextAnchor.LowerCenter;
            stack.childControlHeight = true;
            stack.childControlWidth = true;
            stack.childForceExpandHeight = false;
            var fit = _choiceRoot.gameObject.AddComponent<ContentSizeFitter>();
            fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            _bubble.gameObject.SetActive(false);
            _choiceRoot.gameObject.SetActive(false);
        }

        private Button MakeChoiceButton()
        {
            var rect = NewRect("Choice", _choiceRoot);
            var image = rect.gameObject.AddComponent<Image>();
            image.sprite = slip != null ? slip : RoundedSprite;
            image.type = Image.Type.Sliced;
            image.pixelsPerUnitMultiplier = slip != null ? edgeThinness * 1.4f : 1f;
            image.color = paper;
            var button = rect.gameObject.AddComponent<Button>();
            var colours = button.colors;
            colours.normalColor = new Color(1f, 1f, 1f, 0.82f);
            colours.highlightedColor = Color.white;
            colours.selectedColor = new Color(1f, 0.93f, 0.8f, 1f);
            colours.pressedColor = new Color(0.9f, 0.85f, 0.75f, 1f);
            button.colors = colours;
            var pad = rect.gameObject.AddComponent<HorizontalLayoutGroup>();
            pad.padding = new RectOffset(30, 30, 20, 22);
            pad.childControlWidth = pad.childControlHeight = true;
            var label = NewLabel("Label", rect, textSize * 0.9f, ink, FontStyles.Normal);
            label.alignment = TextAlignmentOptions.Center;
            return button;
        }

        private TextMeshProUGUI NewLabel(string name, RectTransform parent, float size, Color colour, FontStyles style)
        {
            var rect = NewRect(name, parent);
            rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            var label = rect.gameObject.AddComponent<TextMeshProUGUI>();
            if (font != null) label.font = font;
            label.fontSize = size;
            label.color = colour;
            label.fontStyle = style;
            label.textWrappingMode = TextWrappingModes.Normal;
            label.raycastTarget = false;
            return label;
        }

        private static RectTransform NewRect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        // ---- Shapes drawn in code, no art needed ------------------------------

        private static Sprite _rounded;
        private static Sprite _triangle;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _rounded = null;
            _triangle = null;
        }

        /// <summary>A soft rounded rectangle, nine sliced so one small texture serves every bubble size.</summary>
        private static Sprite RoundedSprite
        {
            get
            {
                if (_rounded != null) return _rounded;
                const int size = 64;
                const float radius = 16f;
                var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.HideAndDontSave };
                var pixels = new Color32[size * size];
                for (var y = 0; y < size; y++)
                for (var x = 0; x < size; x++)
                {
                    var cx = Mathf.Clamp(x + 0.5f, radius, size - radius);
                    var cy = Mathf.Clamp(y + 0.5f, radius, size - radius);
                    var d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(cx, cy));
                    var a = Mathf.Clamp01(radius - d + 0.5f);
                    pixels[y * size + x] = new Color(1f, 1f, 1f, a);
                }
                tex.SetPixels32(pixels);
                tex.Apply();
                _rounded = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0,
                    SpriteMeshType.FullRect, new Vector4(radius + 2, radius + 2, radius + 2, radius + 2));
                _rounded.hideFlags = HideFlags.HideAndDontSave;
                return _rounded;
            }
        }

        private static Sprite TriangleSprite(bool down)
        {
            if (_triangle != null) return _triangle;
            const int w = 32, h = 20;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.HideAndDontSave };
            var pixels = new Color32[w * h];
            for (var y = 0; y < h; y++)
            {
                // Point at the bottom: the row's half width grows toward the top.
                var half = (y + 0.5f) / h * (w * 0.5f);
                for (var x = 0; x < w; x++)
                {
                    var dx = Mathf.Abs(x + 0.5f - w * 0.5f);
                    var a = Mathf.Clamp01(half - dx + 0.5f);
                    pixels[y * w + x] = new Color(1f, 1f, 1f, a);
                }
            }
            tex.SetPixels32(pixels);
            tex.Apply();
            _triangle = Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 1f), 100f);
            _triangle.hideFlags = HideFlags.HideAndDontSave;
            return _triangle;
        }
    }
}
