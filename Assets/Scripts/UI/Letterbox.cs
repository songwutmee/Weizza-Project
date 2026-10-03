using System.Threading;
using Cysharp.Threading.Tasks;
using Sirenix.OdinInspector;
using UnityEngine;
using UnityEngine.UI;

namespace Weizza
{
    /// <summary>
    /// The two black bars a cutscene slides in from the top and bottom edge,
    /// leaving the middle of the frame open. Different from ScreenFade on
    /// purpose: a fade covers everything and reads as a scene change, a
    /// letterbox covers nothing in the middle and reads as "watch this".
    ///
    /// Builds its own canvas the same way ScreenFade does, above the HUD and
    /// below the fade so a cutscene can still cut to black through its own bars.
    /// </summary>
    [AddComponentMenu("Weizza/Letterbox")]
    public sealed class Letterbox : MonoBehaviour
    {
        public static Letterbox Instance { get; private set; }

        [BoxGroup("Look")]
        [PropertyRange(0f, 0.3f)]
        [Tooltip("Each bar's height as a fraction of the screen.")]
        [SerializeField] private float barFraction = 0.12f;

        [BoxGroup("Look")]
        [SerializeField] private Color colour = Color.black;

        [BoxGroup("Look")]
        [Tooltip("Below ScreenFade's sorting order, so a fade to black still " +
                 "covers the open middle a letterbox leaves alone.")]
        [SerializeField] private int sortingOrder = 4000;

        [BoxGroup("Live")]
        [ShowInInspector, ReadOnly]
        public bool Shown { get; private set; }

        private RectTransform _top;
        private RectTransform _bottom;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => Instance = null;

        private void Awake()
        {
            Instance = this;
            Build();
            Apply(0f);
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        private void Build()
        {
            var root = new GameObject("Letterbox Canvas", typeof(Canvas), typeof(CanvasScaler));
            root.transform.SetParent(transform, false);
            root.layer = LayerMask.NameToLayer("UI");

            var canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = sortingOrder;

            _top = BuildBar(root.transform, "Top Bar", TextAnchor.UpperCenter);
            _bottom = BuildBar(root.transform, "Bottom Bar", TextAnchor.LowerCenter);
        }

        private RectTransform BuildBar(Transform parent, string barName, TextAnchor anchor)
        {
            var go = new GameObject(barName, typeof(Image));
            go.transform.SetParent(parent, false);
            go.layer = parent.gameObject.layer;

            var image = go.GetComponent<Image>();
            image.color = colour;
            image.raycastTarget = false;

            var rect = go.GetComponent<RectTransform>();
            var fromTop = anchor == TextAnchor.UpperCenter;
            rect.anchorMin = new Vector2(0f, fromTop ? 1f : 0f);
            rect.anchorMax = new Vector2(1f, fromTop ? 1f : 0f);
            rect.pivot = new Vector2(0.5f, fromTop ? 1f : 0f);
            rect.offsetMin = new Vector2(0f, 0f);
            rect.offsetMax = new Vector2(0f, 0f);
            rect.sizeDelta = new Vector2(0f, 0f);

            return rect;
        }

        /// <summary>0 is fully open, 1 is the bars at their full height.</summary>
        private void Apply(float amount)
        {
            amount = Mathf.Clamp01(amount);
            var height = Screen.height * barFraction * amount;
            _top.sizeDelta = new Vector2(0f, height);
            _bottom.sizeDelta = new Vector2(0f, height);
            Shown = amount > 0.001f;
        }

        /// <summary>Jumps straight to shown or hidden with no animation.</summary>
        public void Set(bool show) => Apply(show ? 1f : 0f);

        /// <summary>Slides the bars in or out over time. Safe from a locked player.</summary>
        public async UniTask SetAsync(bool show, float seconds, CancellationToken ct)
        {
            var from = Shown ? 1f : 0f;
            var to = show ? 1f : 0f;

            if (seconds <= 0f)
            {
                Apply(to);
                return;
            }

            var elapsed = 0f;
            while (elapsed < seconds)
            {
                ct.ThrowIfCancellationRequested();
                elapsed += Time.unscaledDeltaTime;
                Apply(Mathf.Lerp(from, to, Mathf.SmoothStep(0f, 1f, elapsed / seconds)));
                await UniTask.Yield(PlayerLoopTiming.Update, ct);
            }

            Apply(to);
        }
    }
}
