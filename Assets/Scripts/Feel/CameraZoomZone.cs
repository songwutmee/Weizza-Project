using System;
using Sirenix.OdinInspector;
using UnityEngine;

namespace Weizza
{
    /// <summary>
    /// Pulls the camera back over stretches of the level, so a long open walk shows the whole
    /// forest. The size eases in before a zone starts and out after it ends, driven by where
    /// the camera is, so it is the same every time and never jumps.
    /// </summary>
    [AddComponentMenu("Weizza/Feel/Camera Zoom Zone")]
    public sealed class CameraZoomZone : MonoBehaviour
    {
        [Serializable]
        public sealed class Zone
        {
            [Tooltip("World x where the zoom is fully out.")] public float from;
            [Tooltip("World x where it starts to ease back in.")] public float to = 100f;
            [PropertyRange(4f, 16f), Tooltip("Orthographic size. 6 is the normal game view, 9.5 shows about 60% more.")]
            public float size = 9.5f;
            [PropertyRange(2f, 30f), Tooltip("Metres it takes to ease in before from and out after to.")]
            public float ease = 12f;
        }

        [ListDrawerSettings(ShowFoldout = true, DefaultExpandedState = true)]
        [SerializeField] private Zone[] zones = Array.Empty<Zone>();

        /// <summary>A cutscene that wants the zoom for itself switches this on.</summary>
        public bool Paused { get; set; }

        public float CurrentSize { get; private set; }

        private float _last = -1f;

        private void LateUpdate()
        {
            var rig = ViewRig.Instance;
            var cam = Camera.main;
            if (rig == null || cam == null || Paused) return;

            var x = cam.transform.position.x;
            var size = rig.OrthographicSize;
            foreach (var zone in zones)
            {
                if (zone == null || zone.to <= zone.from) continue;
                var ease = Mathf.Max(0.1f, zone.ease);
                var on = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(zone.from - ease, zone.from, x))
                         * (1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(zone.to, zone.to + ease, x)));
                size = Mathf.Lerp(size, zone.size, on);
            }

            CurrentSize = size;
            if (Mathf.Abs(size - _last) < 0.002f) return;
            _last = size;
            rig.SetZoom(size);
        }
    }
}
