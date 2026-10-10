using UnityEngine;

namespace Weizza
{
    /// <summary>
    /// Makes a background piece trail the camera, so far things slide past slower than
    /// the world: the depth cue every layered forest relies on. Same idea as
    /// ForegroundDrift, other direction.
    ///
    /// Measured from the piece's own spot: with the camera centred on it the piece sits
    /// exactly where it was painted, and it falls behind only as the camera moves away.
    /// That keeps Map Painter's Edit view true and works in any streamed chunk.
    ///
    /// Remember what a rate does to spacing. Pieces slide toward the camera, so two
    /// pieces painted 20 m apart end up 20 x (1 - rate) m apart on screen. A far layer
    /// has to be painted wider apart than it should look, or it bunches up.
    /// Play mode only.
    /// </summary>
    [AddComponentMenu("Weizza/Level/Background Drift")]
    public sealed class BackgroundDrift : MonoBehaviour
    {
        [Tooltip("0 moves with the world. 1 is glued to the camera, like a sky. " +
                 "Far trees read well around 0.4 to 0.6.")]
        [Range(0f, 1f)]
        [SerializeField] private float rate = 0.4f;

        private float _anchorX;
        private Transform _camera;

        public float Rate => rate;

        public void SetRate(float value) => rate = value;

        private void OnEnable() => _anchorX = transform.position.x;

        private void OnDisable()
        {
            var p = transform.position;
            p.x = _anchorX;
            transform.position = p;
        }

        private void LateUpdate()
        {
            if (_camera == null)
            {
                var cam = Camera.main;
                if (cam == null) return;
                _camera = cam.transform;
            }

            var p = transform.position;
            p.x = _anchorX + (_camera.position.x - _anchorX) * rate;
            transform.position = p;
        }
    }
}
