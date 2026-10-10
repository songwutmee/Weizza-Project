using UnityEngine;

namespace Weizza
{
    /// <summary>
    /// Fades a dead creature out so it does not lie there until the despawn timer ends.
    /// Colours are put back when the object comes back from a pool.
    /// </summary>
    [AddComponentMenu("")]
    internal sealed class DeathFade : MonoBehaviour
    {
        private SpriteRenderer[] _renderers;
        private Color[] _original;
        private float _started, _seconds;
        private bool _running;

        public void Begin(float seconds)
        {
            Restore();
            _renderers = GetComponentsInChildren<SpriteRenderer>(false);
            _original = new Color[_renderers.Length];
            for (var i = 0; i < _renderers.Length; i++) _original[i] = _renderers[i].color;
            _started = Time.time;
            _seconds = Mathf.Max(0.05f, seconds);
            _running = true;
        }

        private void Update()
        {
            if (!_running) return;
            var k = Mathf.Clamp01((Time.time - _started) / _seconds);
            var fade = 1f - k * k;
            for (var i = 0; i < _renderers.Length; i++)
            {
                if (_renderers[i] == null) continue;
                var c = _original[i];
                c.a *= fade;
                _renderers[i].color = c;
            }
            if (k >= 1f) _running = false;
        }

        // A pooled enemy is switched off and on again for its next fight.
        private void OnDisable() => Restore();

        private void Restore()
        {
            if (_renderers == null) return;
            for (var i = 0; i < _renderers.Length; i++)
                if (_renderers[i] != null) _renderers[i].color = _original[i];
            _running = false;
        }
    }
}
