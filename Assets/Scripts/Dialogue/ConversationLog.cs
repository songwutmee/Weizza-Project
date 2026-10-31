using System;
using System.Text;
using Sirenix.OdinInspector;
using StoryKit;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Weizza.Narrative
{
    /// <summary>
    /// The log: a sheet of the same paper as the dialogue panel, listing what has been said
    /// this session, newest at the bottom. Opened and closed with the Log button (J, or Select
    /// on a pad), so a line read too fast, or a direction an NPC gave an hour ago, is never lost.
    ///
    /// While it is open the world holds still and the player cannot move.
    /// </summary>
    [AddComponentMenu("Weizza/Narrative/Conversation Log")]
    public sealed class ConversationLog : MonoBehaviour
    {
        [Required, Tooltip("The paper the dialogue panel is drawn on.")]
        [SerializeField] private Sprite paper;

        [Tooltip("The ink tag the dialogue panel puts names on.")]
        [SerializeField] private Sprite inkTag;

        [SerializeField] private TMP_FontAsset font;

        [Tooltip("For the names on each line.")]
        [SerializeField] private SpeakerDatabase speakers;

        [PropertyRange(4, 40), Tooltip("How many of the latest lines fit on the sheet.")]
        [SerializeField] private int linesShown = 14;

        [SerializeField] private Color ink = new(0.11f, 0.13f, 0.30f, 1f);

        private GameObject _root;
        private TextMeshProUGUI _body;
        private IDisposable _lock;
        private bool _open;

        private void Awake() => Build();

        private void OnDisable() => Close();

        private void Update()
        {
            var input = InputReader.Instance;
            if (input == null) return;

            if (_open)
            {
                if (input.ToggleLogPressed || input.PausePressed) Close();
                return;
            }
            // Never over another screen: a talk, a cutscene or the pause menu owns the frame.
            if (input.ToggleLogPressed && !DialogueDirector.Talking && !StoryControl.MovementLocked) Open();
        }

        private void Open()
        {
            _open = true;
            _body.text = Compose();
            _root.SetActive(true);
            _lock = StoryControl.Lock("Conversation log");
            StoryTimeScale.Stop();
        }

        private void Close()
        {
            if (!_open) return;
            _open = false;
            if (_root != null) _root.SetActive(false);
            _lock?.Dispose();
            _lock = null;
            StoryTimeScale.Resume();
        }

        private string Compose()
        {
            var history = DialogueDirector.History;
            if (history.Count == 0) return "<i>Nothing has been said yet.</i>";

            var loca = StoryServices.Get<ILocalizationService>();
            var sb = new StringBuilder();
            for (var i = Mathf.Max(0, history.Count - linesShown); i < history.Count; i++)
            {
                var (speakerId, text) = history[i];
                var key = speakers != null ? speakers.NameKey(speakerId) : speakerId;
                var name = loca != null ? loca.Resolve(key) : key;
                sb.Append("<b>").Append(name).Append("</b>   ").Append(text).Append('\n');
            }
            return sb.ToString();
        }

        // ---- Build -------------------------------------------------------------

        private void Build()
        {
            _root = new GameObject("Conversation Log", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            _root.transform.SetParent(transform, false);
            var canvas = _root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 4400;
            var scaler = _root.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            var dim = Child("Dim", _root.transform).AddComponent<Image>();
            Stretch(dim.rectTransform);
            dim.color = new Color(0f, 0f, 0f, 0.55f);

            var sheet = Child("Sheet", _root.transform).AddComponent<Image>();
            sheet.sprite = paper;
            sheet.type = Image.Type.Sliced;
            Centre(sheet.rectTransform, new Vector2(1180f, 780f));

            if (inkTag != null)
            {
                var tag = Child("Title Tag", sheet.transform).AddComponent<Image>();
                tag.sprite = inkTag;
                var tr = tag.rectTransform;
                tr.anchorMin = tr.anchorMax = new Vector2(0f, 1f);
                tr.pivot = new Vector2(0f, 0.5f);
                tr.anchoredPosition = new Vector2(60f, -10f);
                tr.sizeDelta = new Vector2(220f, 76f);
                var title = Child("Title", tag.transform).AddComponent<TextMeshProUGUI>();
                Stretch(title.rectTransform);
                title.text = "Log";
                title.alignment = TextAlignmentOptions.Center;
                title.fontSize = 34;
                title.fontStyle = FontStyles.Bold | FontStyles.Italic;
                title.color = new Color(0.97f, 0.96f, 0.93f);
                if (font != null) title.font = font;
            }

            _body = Child("Lines", sheet.transform).AddComponent<TextMeshProUGUI>();
            var br = _body.rectTransform;
            br.anchorMin = Vector2.zero;
            br.anchorMax = Vector2.one;
            br.offsetMin = new Vector2(80f, 70f);
            br.offsetMax = new Vector2(-80f, -80f);
            _body.fontSize = 28;
            _body.lineSpacing = 18f;
            _body.color = ink;
            _body.alignment = TextAlignmentOptions.BottomLeft;
            _body.textWrappingMode = TextWrappingModes.Normal;
            _body.overflowMode = TextOverflowModes.Truncate;
            if (font != null) _body.font = font;

            var hint = Child("Hint", sheet.transform).AddComponent<TextMeshProUGUI>();
            var hr = hint.rectTransform;
            hr.anchorMin = new Vector2(1f, 0f);
            hr.anchorMax = new Vector2(1f, 0f);
            hr.pivot = new Vector2(1f, 0f);
            hr.anchoredPosition = new Vector2(-50f, 24f);
            hr.sizeDelta = new Vector2(400f, 36f);
            hint.text = "J  close";
            hint.fontSize = 22;
            hint.alignment = TextAlignmentOptions.Right;
            hint.color = new Color(ink.r, ink.g, ink.b, 0.6f);
            if (font != null) hint.font = font;

            _root.SetActive(false);
        }

        private static GameObject Child(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return go;
        }

        private static void Stretch(RectTransform r)
        {
            r.anchorMin = Vector2.zero;
            r.anchorMax = Vector2.one;
            r.offsetMin = r.offsetMax = Vector2.zero;
        }

        private static void Centre(RectTransform r, Vector2 size)
        {
            r.anchorMin = r.anchorMax = r.pivot = new Vector2(0.5f, 0.5f);
            r.anchoredPosition = Vector2.zero;
            r.sizeDelta = size;
        }
    }
}
