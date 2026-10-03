using UnityEngine;

namespace Weizza
{
    /// <summary>
    /// Keeps an atmosphere object (motes, spores) centred on the camera along X,
    /// so one emitter covers a whole linear world instead of one copy per chunk.
    /// Particles already alive stay where they are in world space and only new
    /// ones are born around the camera, so the drift never slides with the view.
    /// Play mode only: in Edit mode the object stays where it was placed.
    /// </summary>
    [AddComponentMenu("Weizza/Level/Follow Camera X")]
    public sealed class FollowCameraX : MonoBehaviour
    {
        [Tooltip("Metres ahead of the camera centre. A small lead keeps new motes " +
                 "appearing where the player is walking to.")]
        [SerializeField] private float lead = 2f;

        private Transform _camera;

        private void LateUpdate()
        {
            if (_camera == null)
            {
                var cam = Camera.main;
                if (cam == null) return;
                _camera = cam.transform;
            }

            var p = transform.position;
            p.x = _camera.position.x + lead;
            transform.position = p;
        }
    }
}
