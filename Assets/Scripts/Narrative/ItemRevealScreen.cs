using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Sirenix.OdinInspector;
using StoryKit;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

namespace Weizza.Narrative
{
    /// <summary>
    /// The "you found something" card: background blurs, the world stops
    /// listening to input, the icon and its name and description fade up
    /// centred on screen, and a press or a few seconds of nobody pressing
    /// anything closes it again.
    ///
    /// Built the same shape as DialogueScreen and DialogueDirector rolled into
    /// one component, because a pickup reveal is a smaller version of the same
    /// job: take the world out of the way, show something, give control back.
    /// It shares the Dialogue Blur volume rather than owning a second one,
    /// since the two never run at the same time (PickupItem will not open one
    /// while DialogueDirector.Talking is true, and the reverse is not built
    /// but should hold the same rule if it ever needs to).
    /// </summary>
    [AddComponentMenu("Weizza/Item Reveal Screen")]
    [DefaultExecutionOrder(-400)]
    public sealed class ItemRevealScreen : MonoBehaviour
    {
        public static ItemRevealScreen Instance { get; private set; }

        /// <summary>True while a card owns the screen.</summary>
        public static bool Showing { get; private set; }

        [BoxGroup("Scene")]
        [Tooltip("The same volume DialogueDirector blurs with. Sharing it is " +
                 "safe because a pickup and a conversation never overlap.")]
        [SerializeField] private Volume blurVolume;

        [BoxGroup("Scene")]
        [PropertyRange(0f, 1f)]
        [SerializeField] private float blurSeconds = 0.2f;

        [BoxGroup("Scene")]
        [Tooltip("Turned off for as long as the card is up. The player controller " +
                 "belongs here.")]
        [SerializeField] private List<Behaviour> suspendWhileShowing = new();

        [BoxGroup("Timing")]
        [PropertyRange(0f, 2f)]
        [Tooltip("The card cannot be dismissed before this, so a pickup press " +
                 "that is still being held cannot also close the card it opened.")]
        [SerializeField] private float minSeconds = 0.4f;

        [BoxGroup("Timing")]
        [PropertyRange(1f, 15f)]
        [Tooltip("Closes on its own if nobody presses anything.")]
        [SerializeField] private float autoCloseSeconds = 6f;

        [BoxGroup("Look")]
        [PropertyRange(0f, 1f)]
        [SerializeField] private float dimAlpha = 0.55f;

        private Image _dim;
        private Image _icon;
        private TextMeshProUGUI _nameLabel;
        private TextMeshProUGUI _descriptionLabel;
        private CanvasGroup _cardGroup;
        private CancellationTokenSource _life;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Instance = null;
            Showing = false;
        }

        private void Awake()
        {
            Instance = this;
            Build();
            SetCardAlpha(0f);
            _life = new CancellationTokenSource();
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
            _life?.Cancel();
            _life?.Dispose();
        }

        private void Build()
        {
            var root = new GameObject("Item Reveal Canvas", typeof(Canvas), typeof(CanvasScaler));
            root.transform.SetParent(transform, false);
            root.layer = LayerMask.NameToLayer("UI");

            var canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 4500;

            var dimGo = new GameObject("Dim", typeof(Image));
            dimGo.transform.SetParent(root.transform, false);
            Stretch(dimGo.GetComponent<RectTransform>());
            _dim = dimGo.GetComponent<Image>();
            _dim.color = new Color(0f, 0f, 0f, 0f);
            _dim.raycastTarget = false;

            var cardGo = new GameObject("Card", typeof(RectTransform), typeof(CanvasGroup));
            cardGo.transform.SetParent(root.transform, false);
            var cardRect = cardGo.GetComponent<RectTransform>();
            cardRect.anchorMin = new Vector2(0.5f, 0.5f);
            cardRect.anchorMax = new Vector2(0.5f, 0.5f);
            cardRect.pivot = new Vector2(0.5f, 0.5f);
            cardRect.sizeDelta = new Vector2(520f, 420f);
            _cardGroup = cardGo.GetComponent<CanvasGroup>();

            var iconGo = new GameObject("Icon", typeof(Image));
            iconGo.transform.SetParent(cardGo.transform, false);
            var iconRect = iconGo.GetComponent<RectTransform>();
            iconRect.anchorMin = new Vector2(0.5f, 1f);
            iconRect.anchorMax = new Vector2(0.5f, 1f);
            iconRect.pivot = new Vector2(0.5f, 1f);
            iconRect.anchoredPosition = new Vector2(0f, 0f);
            iconRect.sizeDelta = new Vector2(220f, 220f);
            _icon = iconGo.GetComponent<Image>();
            _icon.preserveAspect = true;
            _icon.raycastTarget = false;

            _nameLabel = BuildLabel(cardGo.transform, "Name", 34f, FontStyles.Bold,
                new Vector2(0f, -236f), new Vector2(480f, 50f));
            _nameLabel.color = Color.white;

            _descriptionLabel = BuildLabel(cardGo.transform, "Description", 22f, FontStyles.Normal,
                new Vector2(0f, -292f), new Vector2(480f, 110f));
            _descriptionLabel.color = new Color(0.85f, 0.85f, 0.85f);
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        private static TextMeshProUGUI BuildLabel(
            Transform parent, string labelName, float size, FontStyles style,
            Vector2 anchoredPos, Vector2 sizeDelta)
        {
            var go = new GameObject(labelName, typeof(TextMeshProUGUI));
            go.transform.SetParent(parent, false);

            var rect = go.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 1f);
            rect.anchorMax = new Vector2(0.5f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.anchoredPosition = anchoredPos;
            rect.sizeDelta = sizeDelta;

            var label = go.GetComponent<TextMeshProUGUI>();
            label.fontSize = size;
            label.fontStyle = style;
            label.alignment = TextAlignmentOptions.Top;
            label.enableWordWrapping = true;
            label.raycastTarget = false;
            return label;
        }

        /// <summary>Fire-and-forget entry point for a pickup that has no CancellationToken of its own.</summary>
        public void Show(ItemView view)
        {
            if (Showing) return;
            var token = _life?.Token ?? CancellationToken.None;
            ShowAsync(view, token).Forget();
        }

        public async UniTaskVoid ShowAsync(ItemView view, CancellationToken ct)
        {
            Showing = true;
            Suspend(true);

            try
            {
                _icon.sprite = view.Icon;
                _icon.enabled = view.Icon != null;
                _nameLabel.text = view.DisplayName;
                _descriptionLabel.text = view.Description;

                var blur = FadeBlur(1f, ct);
                var fadeIn = FadeDim(dimAlpha, ct);
                var cardIn = FadeCard(1f, ct);
                await UniTask.WhenAll(blur, fadeIn, cardIn);

                await WaitForDismissAsync(ct);

                var blurOut = FadeBlur(0f, ct);
                var dimOut = FadeDim(0f, ct);
                var cardOut = FadeCard(0f, ct);
                await UniTask.WhenAll(blurOut, dimOut, cardOut);
            }
            catch (System.OperationCanceledException)
            {
                // Scene torn down mid reveal. Suspend(false) below still runs.
            }
            finally
            {
                Suspend(false);
                Showing = false;
                if (blurVolume != null) blurVolume.weight = 0f;
            }
        }

        private async UniTask WaitForDismissAsync(CancellationToken ct)
        {
            var elapsed = 0f;
            while (elapsed < autoCloseSeconds)
            {
                ct.ThrowIfCancellationRequested();
                elapsed += Time.unscaledDeltaTime;

                var reader = InputReader.Instance;
                if (elapsed >= minSeconds && reader != null && reader.InteractPressed) return;

                await UniTask.Yield(PlayerLoopTiming.Update, ct);
            }
        }

        private void Suspend(bool showing)
        {
            foreach (var behaviour in suspendWhileShowing)
                if (behaviour != null) behaviour.enabled = !showing;
        }

        private void SetCardAlpha(float alpha)
        {
            if (_cardGroup != null) _cardGroup.alpha = alpha;
        }

        private async UniTask FadeCard(float target, CancellationToken ct)
        {
            const float seconds = 0.25f;
            var from = _cardGroup.alpha;
            var elapsed = 0f;
            while (elapsed < seconds)
            {
                ct.ThrowIfCancellationRequested();
                elapsed += Time.unscaledDeltaTime;
                SetCardAlpha(Mathf.Lerp(from, target, elapsed / seconds));
                await UniTask.Yield(PlayerLoopTiming.Update, ct);
            }
            SetCardAlpha(target);
        }

        private async UniTask FadeDim(float target, CancellationToken ct)
        {
            const float seconds = 0.25f;
            var from = _dim.color.a;
            var elapsed = 0f;
            while (elapsed < seconds)
            {
                ct.ThrowIfCancellationRequested();
                elapsed += Time.unscaledDeltaTime;
                var a = Mathf.Lerp(from, target, elapsed / seconds);
                _dim.color = new Color(0f, 0f, 0f, a);
                await UniTask.Yield(PlayerLoopTiming.Update, ct);
            }
            _dim.color = new Color(0f, 0f, 0f, target);
        }

        private async UniTask FadeBlur(float target, CancellationToken ct)
        {
            if (blurVolume == null) return;

            if (blurSeconds <= 0f)
            {
                blurVolume.weight = target;
                return;
            }

            var from = blurVolume.weight;
            var elapsed = 0f;
            while (elapsed < blurSeconds)
            {
                ct.ThrowIfCancellationRequested();
                elapsed += Time.unscaledDeltaTime;
                blurVolume.weight = Mathf.Lerp(from, target, elapsed / blurSeconds);
                await UniTask.Yield(PlayerLoopTiming.Update, ct);
            }
            blurVolume.weight = target;
        }
    }
}
