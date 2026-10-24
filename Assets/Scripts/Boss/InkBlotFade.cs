using UnityEngine;

namespace Weizza
{
    /// <summary>
    /// Fades one ink blot out and removes it. Lives on the blot, not on the beetle, so the ink
    /// always clears even when the beetle dies or is unloaded mid fade.
    /// </summary>
    [AddComponentMenu("Weizza/Boss/Ink Blot Fade")]
    public sealed class InkBlotFade : MonoBehaviour
    {
        private SpriteRenderer _sprite;
        private float _startAt = -1f;
        private float _duration = 1f;
        private float _fromAlpha;

        /// <summary>Starts fading after delay seconds. An earlier request always wins over a later one.</summary>
        public void FadeAfter(float delay, float duration)
        {
            var at = Time.time + delay;
            if (_startAt >= 0f && _startAt <= at) return;
            _startAt = at;
            _duration = Mathf.Max(0.05f, duration);
        }

        private void Update()
        {
            if (_startAt < 0f || Time.time < _startAt) return;
            if (_sprite == null)
            {
                _sprite = GetComponent<SpriteRenderer>();
                _fromAlpha = _sprite != null ? _sprite.color.a : 1f;
            }
            var t = (Time.time - _startAt) / _duration;
            if (t >= 1f)
            {
                Destroy(gameObject);
                return;
            }
            if (_sprite == null) return;
            var c = _sprite.color;
            c.a = _fromAlpha * (1f - t);
            _sprite.color = c;
        }
    }
}
