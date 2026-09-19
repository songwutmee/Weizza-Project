using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Sirenix.OdinInspector;
using StoryKit;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Weizza.Narrative
{
    /// <summary>
    /// The panel that says the lines.
    ///
    /// Everything in here runs on unscaled time. A conversation can open while
    /// the game is frozen, either by a hitstop that has not finished or by the
    /// director stopping the world on purpose, and a typewriter that counts
    /// scaled seconds would sit on its first letter until the freeze lifted.
    ///
    /// The screen implements StoryKit's interface rather than being driven
    /// directly, so a line spoken from a flow graph, a cutscene, or the NPC
    /// standing in front of the player all arrive by the same door.
    /// </summary>
    public sealed class DialogueScreen : MonoBehaviour, IDialogueUI
    {
        [System.Serializable]
        public sealed class ChoiceButton
        {
            public Button button;
            public TextMeshProUGUI label;
        }

        [TitleGroup("Wiring")]
        [Required] [SerializeField] private CanvasGroup group;
        [TitleGroup("Wiring")]
        [Required] [SerializeField] private TextMeshProUGUI nameLabel;
        [TitleGroup("Wiring")]
        [Required] [SerializeField] private TextMeshProUGUI bodyLabel;
        [TitleGroup("Wiring")]
        [SerializeField] private Image portrait;
        [TitleGroup("Wiring")]
        [SerializeField] private GameObject advanceHint;
        [TitleGroup("Wiring")]
        [SerializeField] private GameObject choiceRoot;
        [TitleGroup("Wiring")]
        [SerializeField] private List<ChoiceButton> choices = new();

        [TitleGroup("Wiring")]
        [Required("Portraits and names are looked up here.")]
        [SerializeField] private SpeakerDatabase speakers;

        [TitleGroup("Feel")]
        [PropertyRange(0f, 120f)]
        [Tooltip("Letters per second. Zero prints the whole line at once.")]
        [SerializeField] private float lettersPerSecond = 45f;

        [TitleGroup("Feel")]
        [PropertyRange(0f, 0.6f)]
        [SerializeField] private float fadeSeconds = 0.18f;

        [TitleGroup("Feel")]
        [PropertyRange(0f, 0.8f)]
        [Tooltip("How long a change of expression takes. Out is faster than in, " +
                 "so the new face feels like it arrives rather than like the old " +
                 "one was cut.")]
        [SerializeField] private float portraitFadeSeconds = 0.16f;

        [TitleGroup("Feel")]
        [Tooltip("Pressing advance while the line is still printing finishes it " +
                 "instead of skipping it. Without this a fast reader loses lines " +
                 "to their own second press.")]
        [SerializeField] private bool firstPressCompletesLine = true;

        private void Awake()
        {
            StoryServices.Register<IDialogueUI>(this);
            WarnAboutMissingFont();
            Hide();
        }

        /// <summary>
        /// Says out loud when a label has no font.
        ///
        /// TextMeshPro draws nothing at all in that state and reports nothing,
        /// so the panel opens, waits for a keypress, and closes again with the
        /// text invisible the whole way through. Every part of that looks like a
        /// dialogue bug rather than like a project that never imported the TMP
        /// resources.
        /// </summary>
        private void WarnAboutMissingFont()
        {
            var missing = (nameLabel != null && nameLabel.font == null)
                          || (bodyLabel != null && bodyLabel.font == null);
            if (!missing) return;

            Debug.LogWarning("[Weizza] A dialogue label has no font asset, so its text " +
                             "will be invisible. Run Window > TextMeshPro > Import TMP " +
                             "Essential Resources, then build the dialogue rig again.", this);
        }

        private void OnDestroy() => StoryServices.Register<IDialogueUI>(null);

        // ---- IDialogueUI ----------------------------------------------------

        public async UniTask OpenAsync(CancellationToken ct)
        {
            // This used to be gameObject.SetActive(true), back when this
            // script lived on Panel itself. It now lives on Dialogue Canvas so
            // that Panel can be switched off in the Inspector without
            // breaking IDialogueUI registration, which means that line was
            // reactivating the Canvas (already active, a no-op) instead of
            // the Panel someone had just hidden for editing. group.gameObject
            // is Panel by construction, so this is the one line that has to
            // reach it explicitly: whatever Panel's state was left in, a real
            // conversation starting always forces it back on.
            if (group != null) group.gameObject.SetActive(true);
            if (choiceRoot != null) choiceRoot.SetActive(false);
            await FadeTo(1f, ct);
        }

        public async UniTask CloseAsync(CancellationToken ct)
        {
            await FadeTo(0f, ct);
            Hide();
        }

        /// <summary>
        /// Dresses the panel for a speaker without saying anything yet.
        ///
        /// Called before the fade so the face and the frame arrive together. Set
        /// during the first line instead, the panel fades up empty and the
        /// portrait snaps in a moment later, which reads as two things happening
        /// rather than as one person stepping forward to speak.
        /// </summary>
        public void Prime(string speakerId, string emotion)
        {
            ShowSpeaker(speakerId, emotion);

            if (bodyLabel == null) return;

            bodyLabel.text = string.Empty;
            bodyLabel.maxVisibleCharacters = 0;
        }

        private void ShowSpeaker(string speakerId, string emotion)
        {
            ShowName(speakerId);
            SetFace(speakers != null ? speakers.Face(speakerId, emotion) : null, 1f);
        }

        private void ShowName(string speakerId)
        {
            if (nameLabel == null) return;

            var loca = StoryServices.Get<ILocalizationService>();
            var key = speakers != null ? speakers.NameKey(speakerId) : speakerId;
            nameLabel.text = loca != null ? loca.Resolve(key) : key;
        }

        private void SetFace(Sprite face, float alpha)
        {
            if (portrait == null) return;

            portrait.sprite = face;

            // Kept enabled but transparent would still catch clicks, and an
            // invisible sprite that eats the advance press reads as the panel
            // being stuck.
            portrait.enabled = face != null;

            var colour = portrait.color;
            colour.a = alpha;
            portrait.color = colour;
        }

        /// <summary>
        /// Changes who is on screen, softly.
        ///
        /// A face that swaps on one frame reads as a glitch rather than as a
        /// change of expression, because nothing in a drawn world moves that
        /// fast. Out then in, with the name changing while the picture is gone,
        /// so the two never disagree on screen even for a frame.
        ///
        /// Skipped entirely when the face is not actually changing, which is most
        /// lines: a conversation that dipped its portrait on every line would
        /// flicker its way through a paragraph.
        /// </summary>
        private async UniTask ShowSpeakerAsync(string speakerId, string emotion, CancellationToken ct)
        {
            var face = speakers != null ? speakers.Face(speakerId, emotion) : null;

            if (portrait == null || portrait.sprite == face)
            {
                ShowName(speakerId);
                return;
            }

            await FadeFace(0f, portraitFadeSeconds * 0.45f, ct);

            ShowName(speakerId);
            SetFace(face, 0f);

            await FadeFace(1f, portraitFadeSeconds, ct);
        }

        private async UniTask FadeFace(float target, float seconds, CancellationToken ct)
        {
            if (portrait == null) return;

            var from = portrait.color.a;
            if (seconds <= 0f)
            {
                SetFace(portrait.sprite, target);
                return;
            }

            var elapsed = 0f;
            while (elapsed < seconds)
            {
                elapsed += Time.unscaledDeltaTime;

                var colour = portrait.color;
                colour.a = Mathf.Lerp(from, target, elapsed / seconds);
                portrait.color = colour;

                await UniTask.Yield(PlayerLoopTiming.Update, ct);
            }

            var settled = portrait.color;
            settled.a = target;
            portrait.color = settled;
        }

        public async UniTask SayAsync(string speakerId, string emotion, string text, CancellationToken ct)
        {
            if (advanceHint != null) advanceHint.SetActive(false);

            await ShowSpeakerAsync(speakerId, emotion, ct);

            await TypeAsync(text, ct);

            if (advanceHint != null) advanceHint.SetActive(true);
            await WaitForAdvance(ct);
            if (advanceHint != null) advanceHint.SetActive(false);
        }

        public async UniTask<int> ChoiceAsync(IReadOnlyList<string> options, CancellationToken ct)
        {
            if (choiceRoot == null || choices.Count == 0)
            {
                Debug.LogWarning("[Weizza] DialogueScreen has no choice buttons wired, taking the first option.");
                return 0;
            }

            var picked = -1;

            for (var i = 0; i < choices.Count; i++)
            {
                var slot = choices[i];
                if (slot?.button == null) continue;

                var usable = i < options.Count;
                slot.button.gameObject.SetActive(usable);
                if (!usable) continue;

                if (slot.label != null) slot.label.text = options[i];

                var index = i;
                slot.button.onClick.RemoveAllListeners();
                slot.button.onClick.AddListener(() => picked = index);
            }

            choiceRoot.SetActive(true);
            if (advanceHint != null) advanceHint.SetActive(false);

            await UniTask.WaitUntil(() => picked >= 0, PlayerLoopTiming.Update, ct);

            choiceRoot.SetActive(false);
            foreach (var slot in choices)
            {
                if (slot?.button != null) slot.button.onClick.RemoveAllListeners();
            }

            return picked;
        }

        // ---- Guts -----------------------------------------------------------

        private async UniTask TypeAsync(string text, CancellationToken ct)
        {
            if (bodyLabel == null) return;

            bodyLabel.text = text;

            if (lettersPerSecond <= 0f || string.IsNullOrEmpty(text))
            {
                bodyLabel.maxVisibleCharacters = int.MaxValue;
                return;
            }

            // maxVisibleCharacters rather than substring assignment: TextMeshPro
            // lays the whole string out once, so the line does not re-wrap as it
            // prints and a word never jumps to the next row mid-sentence.
            bodyLabel.maxVisibleCharacters = 0;
            bodyLabel.ForceMeshUpdate();
            var total = bodyLabel.textInfo.characterCount;

            var shown = 0f;
            while (shown < total)
            {
                if (firstPressCompletesLine && AdvanceWanted()) break;

                shown += lettersPerSecond * Time.unscaledDeltaTime;
                bodyLabel.maxVisibleCharacters = Mathf.FloorToInt(shown);
                await UniTask.Yield(PlayerLoopTiming.Update, ct);
            }

            bodyLabel.maxVisibleCharacters = int.MaxValue;
        }

        private async UniTask WaitForAdvance(CancellationToken ct)
        {
            // One frame of grace, so the press that finished the typewriter does
            // not also advance past the line it just revealed.
            await UniTask.Yield(PlayerLoopTiming.Update, ct);
            await UniTask.WaitUntil(AdvanceWanted, PlayerLoopTiming.Update, ct);

            // And one on the way out. A press reads as pressed for the whole
            // frame it happened on, and the next line starts typing on that same
            // frame, so without this the typewriter sees the press that asked for
            // the line and completes it instantly. Every line after the first
            // arrived whole.
            await UniTask.Yield(PlayerLoopTiming.Update, ct);
        }

        private static bool AdvanceWanted()
        {
            var reader = InputReader.Instance;
            return reader != null && reader.AdvancePressed;
        }

        private async UniTask FadeTo(float target, CancellationToken ct)
        {
            if (group == null) return;

            if (fadeSeconds <= 0f)
            {
                group.alpha = target;
                return;
            }

            var from = group.alpha;
            var elapsed = 0f;
            while (elapsed < fadeSeconds)
            {
                elapsed += Time.unscaledDeltaTime;
                group.alpha = Mathf.Lerp(from, target, elapsed / fadeSeconds);
                await UniTask.Yield(PlayerLoopTiming.Update, ct);
            }

            group.alpha = target;
        }

        private void Hide()
        {
            if (group != null) group.alpha = 0f;

            // An Image with no sprite draws a white rectangle, so a portrait
            // left enabled between conversations is a blank card sitting over
            // the game rather than nothing at all.
            if (portrait != null) portrait.enabled = false;
            if (choiceRoot != null) choiceRoot.SetActive(false);
            if (advanceHint != null) advanceHint.SetActive(false);

            // This used to be gameObject.SetActive(false), the same bug
            // OpenAsync had. This script now lives on Dialogue Canvas, so
            // that line was switching the whole canvas off on every close,
            // including the very first call from Awake before anything ever
            // talked. Awake never runs again on a disabled GameObject, so
            // the dialogue system disabled its own IDialogueUI registration
            // path the moment the scene started. Reaching group.gameObject
            // (Panel) instead is the same fix as OpenAsync, and leaves the
            // canvas itself always on, which is the one rule that matters
            // now that Awake lives here.
            if (group != null) group.gameObject.SetActive(false);
        }
    }
}
