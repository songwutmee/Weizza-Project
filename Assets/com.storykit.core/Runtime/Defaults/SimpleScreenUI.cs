using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;

namespace StoryKit.Defaults
{
    /// <summary>
    /// Default IScreenUI: full-screen fade overlay + location banner,
    /// built at runtime like SimpleDialogueUI. No shaders required, so it
    /// works identically on Built-in / URP / HDRP and every platform.
    /// (Fancy dissolve transitions can be layered on later by swapping the
    /// overlay Image material: the service contract stays the same.)
    /// </summary>
    public sealed class SimpleScreenUI : MonoBehaviour, IScreenUI
    {
        private Image _overlay;
        private Text _banner;
        private CanvasGroup _bannerGroup;

        public static SimpleScreenUI Create()
        {
            var host = new GameObject("StoryKit.SimpleScreenUI");
            DontDestroyOnLoad(host);
            var ui = host.AddComponent<SimpleScreenUI>();
            ui.Build();
            return ui;
        }

        public async UniTask FadeAsync(float toAlpha, float duration, CancellationToken ct)
        {
            float from = _overlay.color.a;
            if (duration <= 0f)
            {
                SetOverlay(toAlpha);
                return;
            }

            float t = 0f;
            while (t < duration)
            {
                ct.ThrowIfCancellationRequested();
                await UniTask.Yield(PlayerLoopTiming.Update, ct);
                t += StoryTime.DeltaTime;
                SetOverlay(Mathf.Lerp(from, toAlpha, Mathf.Clamp01(t / duration)));
            }
            SetOverlay(toAlpha);
        }

        public async UniTask ShowBannerAsync(string text, float duration, CancellationToken ct)
        {
            _banner.text = text;
            _bannerGroup.alpha = 0f;
            _banner.gameObject.SetActive(true);

            await TweenBanner(1f, 0.35f, ct);

            float t = 0f;
            while (t < duration)
            {
                ct.ThrowIfCancellationRequested();
                await UniTask.Yield(PlayerLoopTiming.Update, ct);
                t += StoryTime.DeltaTime;
            }

            await TweenBanner(0f, 0.35f, ct);
            _banner.gameObject.SetActive(false);
        }

        private async UniTask TweenBanner(float to, float duration, CancellationToken ct)
        {
            float from = _bannerGroup.alpha;
            float t = 0f;
            while (t < duration)
            {
                ct.ThrowIfCancellationRequested();
                await UniTask.Yield(PlayerLoopTiming.Update, ct);
                t += StoryTime.DeltaTime;
                _bannerGroup.alpha = Mathf.Lerp(from, to, Mathf.Clamp01(t / duration));
            }
            _bannerGroup.alpha = to;
        }

        /// <summary>
        /// Swaps the fade overlay material: e.g. the bundled StoryKit/UIDissolve
        /// shader turns plain fades into a burning dissolve. The overlay's alpha
        /// (which FadeAsync already animates) drives the shader's progress, so
        /// no other code changes.
        /// </summary>
        public void SetOverlayMaterial(Material material)
        {
            _overlay.material = material;
        }

        public void SetHudVisible(bool visible)
        {
            // The package cannot know your HUD; broadcast instead so any HUD
            // root can listen: StoryEvents.Subscribe("Ui.SetHudVisible", ...)
            StoryEvents.Raise("Ui.SetHudVisible", visible ? "1" : "0");
        }

        private void SetOverlay(float alpha)
        {
            var color = _overlay.color;
            color.a = alpha;
            _overlay.color = color;
            _overlay.raycastTarget = alpha > 0.99f;
        }

        private void Build()
        {
            var canvasGo = new GameObject("Canvas",
                typeof(Canvas), typeof(CanvasScaler));
            canvasGo.transform.SetParent(transform, false);
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 999;   // above the dialogue box
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);

            // Fade overlay
            var overlayGo = new GameObject("Overlay", typeof(RectTransform), typeof(Image));
            overlayGo.transform.SetParent(canvasGo.transform, false);
            Stretch((RectTransform)overlayGo.transform, Vector2.zero, Vector2.one);
            _overlay = overlayGo.GetComponent<Image>();
            _overlay.color = new Color(0f, 0f, 0f, 0f);
            _overlay.raycastTarget = false;

            // Banner
            var bannerGo = new GameObject("Banner",
                typeof(RectTransform), typeof(Text), typeof(CanvasGroup));
            bannerGo.transform.SetParent(canvasGo.transform, false);
            Stretch((RectTransform)bannerGo.transform,
                new Vector2(0.2f, 0.72f), new Vector2(0.8f, 0.85f));
            _banner = bannerGo.GetComponent<Text>();
            _banner.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            _banner.fontSize = 52;
            _banner.fontStyle = FontStyle.Bold;
            _banner.alignment = TextAnchor.MiddleCenter;
            _banner.color = Color.white;
            _bannerGroup = bannerGo.GetComponent<CanvasGroup>();
            _bannerGroup.alpha = 0f;
            bannerGo.SetActive(false);
        }

        private static void Stretch(RectTransform rect, Vector2 min, Vector2 max)
        {
            rect.anchorMin = min;
            rect.anchorMax = max;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }
    }

    /// <summary>
    /// Per-character text animation without shaders: a BaseMeshEffect that
    /// displaces glyph quads each frame. Supports the classic VN set
    /// wave, shake: driven by plain markup in the line text:
    ///     "Hello &lt;wave&gt;world&lt;/wave&gt;!"  ·  "&lt;shake&gt;LOOK OUT&lt;/shake&gt;"
    /// SimpleDialogueUI strips the tags before display and tells this effect
    /// which character ranges animate. Works on legacy Text; the same math
    /// ports to TextMeshPro's mesh API if your game uses TMP.
    /// </summary>
    [RequireComponent(typeof(Text))]
    public sealed class StoryTextEffects : BaseMeshEffect
    {
        public struct Span
        {
            public int Start;
            public int Length;
            public EffectKind Kind;
        }

        public enum EffectKind { Wave, Shake }

        private readonly System.Collections.Generic.List<Span> _spans = new();
        [SerializeField] private float _waveAmplitude = 6f;
        [SerializeField] private float _waveSpeed = 6f;
        [SerializeField] private float _shakeAmplitude = 2.5f;

        public void SetSpans(System.Collections.Generic.List<Span> spans)
        {
            _spans.Clear();
            if (spans != null) _spans.AddRange(spans);
        }

        private void Update()
        {
            if (_spans.Count > 0)
                graphic.SetVerticesDirty();   // re-run ModifyMesh each frame
        }

        public override void ModifyMesh(VertexHelper vh)
        {
            if (_spans.Count == 0) return;

            var vertex = new UIVertex();
            float time = Time.unscaledTime;

            foreach (var span in _spans)
            {
                for (int c = span.Start; c < span.Start + span.Length; c++)
                {
                    int firstVert = c * 4;
                    if (firstVert + 3 >= vh.currentVertCount) break;

                    Vector3 offset = span.Kind switch
                    {
                        EffectKind.Wave => new Vector3(0f,
                            Mathf.Sin(time * _waveSpeed + c * 0.55f) * _waveAmplitude, 0f),
                        EffectKind.Shake => new Vector3(
                            (Mathf.PerlinNoise(c * 7.13f, time * 24f) - 0.5f) * 2f * _shakeAmplitude,
                            (Mathf.PerlinNoise(time * 24f, c * 3.71f) - 0.5f) * 2f * _shakeAmplitude, 0f),
                        _ => Vector3.zero,
                    };

                    for (int v = 0; v < 4; v++)
                    {
                        vh.PopulateUIVertex(ref vertex, firstVert + v);
                        vertex.position += offset;
                        vh.SetUIVertex(vertex, firstVert + v);
                    }
                }
            }
        }

        /// <summary>
        /// Strips &lt;wave&gt;/&lt;shake&gt; tags from text and returns the animated spans
        /// (indices refer to the stripped string). Call before assigning Text.text.
        /// </summary>
        public static string ParseMarkup(string text,
            System.Collections.Generic.List<Span> spansOut)
        {
            spansOut.Clear();
            if (string.IsNullOrEmpty(text) || text.IndexOf('<') < 0) return text;

            var result = new System.Text.StringBuilder(text.Length);
            var openStack = new System.Collections.Generic.Stack<(EffectKind kind, int start)>();

            for (int i = 0; i < text.Length; i++)
            {
                if (text[i] == '<')
                {
                    int close = text.IndexOf('>', i);
                    if (close > i)
                    {
                        string tag = text.Substring(i + 1, close - i - 1).ToLowerInvariant();
                        switch (tag)
                        {
                            case "wave": openStack.Push((EffectKind.Wave, result.Length)); i = close; continue;
                            case "shake": openStack.Push((EffectKind.Shake, result.Length)); i = close; continue;
                            case "/wave":
                            case "/shake":
                                if (openStack.Count > 0)
                                {
                                    var (kind, start) = openStack.Pop();
                                    spansOut.Add(new Span
                                    {
                                        Start = start,
                                        Length = result.Length - start,
                                        Kind = kind,
                                    });
                                }
                                i = close;
                                continue;
                        }
                    }
                }
                result.Append(text[i]);
            }
            return result.ToString();
        }
    }
}
