using UnityEngine;
using UnityEngine.UI;

namespace Weizza
{
    /// <summary>
    /// A whole screen flash of light for the one or two biggest beats in a fight, like
    /// the killing blow on a boss. Fighting games call this an impact frame.
    ///
    /// Kept warm and soft rather than pure white, because this game is wholesome rather
    /// than violent, and it is the beat landing, not a blinding hit. Softer flashes in
    /// Settings drops it to a faint glow. Runs on unscaled time so a slow motion around
    /// it does not drag it out.
    /// </summary>
    public sealed class ScreenFlash : MonoBehaviour
    {
        private static ScreenFlash _runner;
        private Image _image;
        private float _started, _seconds, _peak;
        private Color _colour;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => _runner = null;

        public static void Play(float peak = 0.45f, float seconds = 0.35f, Color? colour = null)
        {
            if (_runner == null) _runner = Build();
            _runner._peak = GameSettings.ReduceFlashes ? peak * 0.25f : peak;
            _runner._seconds = Mathf.Max(0.05f, seconds);
            _runner._colour = colour ?? new Color(1f, 0.96f, 0.85f);
            _runner._started = Time.unscaledTime;
            _runner._image.enabled = true;
        }

        private static ScreenFlash Build()
        {
            var go = new GameObject("Screen Flash", typeof(Canvas));
            DontDestroyOnLoad(go);
            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            // Above the game, below the fade to black, so a flash never shows through a cut.
            canvas.sortingOrder = 900;

            var imageGo = new GameObject("Flash", typeof(RectTransform), typeof(Image));
            imageGo.transform.SetParent(go.transform, false);
            var rect = (RectTransform)imageGo.transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            var image = imageGo.GetComponent<Image>();
            image.raycastTarget = false;
            image.enabled = false;

            var flash = go.AddComponent<ScreenFlash>();
            flash._image = image;
            return flash;
        }

        private void Update()
        {
            if (!_image.enabled) return;
            var k = (Time.unscaledTime - _started) / _seconds;
            if (k >= 1f) { _image.enabled = false; return; }
            // Full at once, then an ease out: a burst, not a fade in.
            var c = _colour;
            c.a = _peak * (1f - k) * (1f - k);
            _image.color = c;
        }
    }
}
