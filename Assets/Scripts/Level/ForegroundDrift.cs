using UnityEngine;

namespace Weizza
{
    /// <summary>
    /// Makes a foreground silhouette slide past faster than the world, the depth cue
    /// Neva's black foreground gives. An orthographic camera has no parallax of its
    /// own (the project notes, parallax), so it is done by hand.
    ///
    /// Measured from the piece's own spot, not from where the level starts: when the
    /// camera is centred on the piece it sits exactly where it was painted, and it
    /// leads or trails only as the camera moves away. That keeps Map Painter's view
    /// in Edit mode true, and works in any chunk however far along the line it is,
    /// which ParallaxLayer's anchor captured on enable cannot promise for a streamed
    /// chunk. Play mode only.
    /// </summary>
    [AddComponentMenu("Weizza/Level/Foreground Drift")]
    public sealed class ForegroundDrift : MonoBehaviour
    {
        [Tooltip("How much faster than the world it slides. 0.3 to 0.5 reads as close to the lens.")]
        [Range(0f, 1f)]
        [SerializeField] private float rate = 0.4f;

        private float _anchorX;
        private Transform _camera;

        public void SetRate(float value) => rate = value;

        private void OnEnable() => _anchorX = transform.position.x;

        private void OnDisable()
        {
            // Put it back, so a chunk that unloads and loads again starts from the
            // painted spot rather than from wherever it had drifted to.
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
            p.x = _anchorX - (_camera.position.x - _anchorX) * rate;
            transform.position = p;
        }
    }
}
