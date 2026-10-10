using System.Threading;
using Cysharp.Threading.Tasks;
using Sirenix.OdinInspector;
using StoryKit;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Weizza
{
    /// <summary>
    /// A black rectangle over the whole screen, and the only thing that knows
    /// how to raise and lower it. Also registers itself as StoryKit's
    /// IScreenUI, so a FadeNode or a BannerNode dropped into a graph works
    /// without anyone wiring a new implementation for it.
    ///
    /// It builds its own canvas on Awake rather than needing a prefab wired up,
    /// because a fade that is missing its image is a fade that silently does
    /// nothing, and the failure only shows up the first time somebody dies.
    ///
    /// Everything here runs on unscaled time. The moments that want a fade are
    /// exactly the moments something else has stopped the clock.
    /// </summary>
    [AddComponentMenu("Weizza/Screen Fade")]
    public sealed class ScreenFade : MonoBehaviour, IScreenUI
    {
        public static ScreenFade Instance { get; private set; }

        [BoxGroup("Look")]
        [Tooltip("Alpha is driven by the fade, this is only the hue.")]
        [SerializeField] private Color colour = Color.black;

        [BoxGroup("Look")]
        [Tooltip("Above the dialogue canvas on purpose. A fade to black that " +
                 "leaves a dialogue panel floating on it is not a fade.")]
        [SerializeField] private int sortingOrder = 5000;

        [BoxGroup("Live")]
        [ShowInInspector, ReadOnly, PropertyRange(0f, 1f)]
        public float Alpha => _image != null ? _image.color.a : 0f;

        /// <summary>True while the screen is fully covered.</summary>
        public bool IsBlack => Alpha >= 0.999f;

        [BoxGroup("Look")]
        [Tooltip("No HUD exists yet, so this only logs once and does nothing. " +
                 "Wire it to the real HUD root the day one is built.")]
        [SerializeField] private GameObject hudRoot;

        private Image _image;
        private TextMeshProUGUI _banner;
        private CanvasGroup _bannerGroup;
        private bool _warnedNoHud;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => Instance = null;

        private void Awake()
        {
            Instance = this;
            Build();
            Set(0f);
        }

        private void OnEnable() => StoryServices.Register<IScreenUI>(this);

        private void OnDisable() => StoryServices.Register<IScreenUI>(null);

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        private void Build()
        {
            var root = new GameObject("Fade Canvas",
                typeof(Canvas), typeof(CanvasScaler));
            root.transform.SetParent(transform, false);
            root.layer = LayerMask.NameToLayer("UI");

            var canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = sortingOrder;

            var image = new GameObject("Fill", typeof(Image));
            image.transform.SetParent(root.transform, false);
            image.layer = root.layer;

            var rect = image.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            _image = image.GetComponent<Image>();

            // Nothing behind a fully transparent fade should stop being
            // clickable, and nothing behind a black one should start being
            // clickable either. The director owns input, not this.
            _image.raycastTarget = false;

            var bannerGo = new GameObject("Banner", typeof(TextMeshProUGUI), typeof(CanvasGroup));
            bannerGo.transform.SetParent(root.transform, false);
            bannerGo.layer = root.layer;

            var bannerRect = bannerGo.GetComponent<RectTransform>();
            bannerRect.anchorMin = new Vector2(0.1f, 0.42f);
            bannerRect.anchorMax = new Vector2(0.9f, 0.58f);
            bannerRect.offsetMin = Vector2.zero;
            bannerRect.offsetMax = Vector2.zero;

            _banner = bannerGo.GetComponent<TextMeshProUGUI>();
            _banner.alignment = TextAlignmentOptions.Center;
            _banner.fontSize = 48f;
            _banner.color = Color.white;
            _banner.text = string.Empty;
            _banner.raycastTarget = false;

            _bannerGroup = bannerGo.GetComponent<CanvasGroup>();
            _bannerGroup.alpha = 0f;
        }

        // ---- IScreenUI --------------------------------------------------------------

        UniTask IScreenUI.FadeAsync(float toAlpha, float duration, CancellationToken ct) =>
            ToAsync(toAlpha, duration, ct);

        async UniTask IScreenUI.ShowBannerAsync(string text, float duration, CancellationToken ct)
        {
            if (_banner == null) return;

            _banner.text = text;
            const float crossfade = 0.3f;

            await FadeGroupAsync(_bannerGroup, 1f, crossfade, ct);
            await UniTask.Delay(
                System.TimeSpan.FromSeconds(Mathf.Max(0f, duration)),
                DelayType.UnscaledDeltaTime, PlayerLoopTiming.Update, ct);
            await FadeGroupAsync(_bannerGroup, 0f, crossfade, ct);
        }

        void IScreenUI.SetHudVisible(bool visible)
        {
            // The runtime HUD is UI Toolkit now, not an object that can be switched off.
            var hud = HudScreen.Instance;
            if (hud != null) hud.SetVisible(visible);

            if (hudRoot != null)
            {
                hudRoot.SetActive(visible);
                return;
            }

            // Not a failure: there is no runtime HUD built yet. Logged once so
            // a graph calling this is not mistaken for silently doing nothing
            // by design versus doing nothing because the field was never set.
            if (_warnedNoHud) return;
            _warnedNoHud = true;
            Debug.Log("[Weizza] SetHudVisible called with no hudRoot assigned. " +
                      "There is no runtime HUD yet, so this is a no-op.", this);
        }

        private static async UniTask FadeGroupAsync(
            CanvasGroup group, float target, float seconds, CancellationToken ct)
        {
            var from = group.alpha;
            var elapsed = 0f;

            while (elapsed < seconds)
            {
                ct.ThrowIfCancellationRequested();
                elapsed += Time.unscaledDeltaTime;
                group.alpha = Mathf.Lerp(from, target, elapsed / seconds);
                await UniTask.Yield(PlayerLoopTiming.Update, ct);
            }

            group.alpha = target;
        }

        /// <summary>Jumps straight to an alpha with no animation.</summary>
        public void Set(float alpha)
        {
            if (_image == null) return;
            var c = colour;
            c.a = Mathf.Clamp01(alpha);
            _image.color = c;
            _image.enabled = c.a > 0.001f;
        }

        /// <summary>Rides the alpha to a target. Safe to await from a dead player.</summary>
        public async UniTask ToAsync(float target, float seconds, CancellationToken ct)
        {
            target = Mathf.Clamp01(target);

            if (seconds <= 0f)
            {
                Set(target);
                return;
            }

            var from = Alpha;
            var elapsed = 0f;

            while (elapsed < seconds)
            {
                ct.ThrowIfCancellationRequested();
                elapsed += Time.unscaledDeltaTime;
                Set(Mathf.Lerp(from, target, Mathf.SmoothStep(0f, 1f, elapsed / seconds)));
                await UniTask.Yield(PlayerLoopTiming.Update, ct);
            }

            Set(target);
        }
    }
}
