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

        [PropertyRange(0.6f, 1f), Tooltip("How close the camera leans in while someone talks, as a share of the current size. 1 turns it off. A gentle lean in focuses a conversation without black bars.")]
        [SerializeField] private float talkZoom = 0.86f;

        [PropertyRange(-0.4f, 0f), Tooltip("How far the framing slides while someone talks, so the player and the speaker sit above the dialogue panel instead of behind it. Minus 0.01 lifts them one percent of the screen; minus 0.22 puts their feet a little above the panel's top edge.")]
        [SerializeField] private float talkLift = -0.22f;

        private float _talk;
        private bool _lifted;

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

            // Lean in while a conversation runs, on unscaled time since talking may slow the world.
            var talking = Narrative.DialogueDirector.Talking ? 1f : 0f;
            _talk = Mathf.MoveTowards(_talk, talking, Time.unscaledDeltaTime / 0.6f);
            var eased = Mathf.SmoothStep(0f, 1f, _talk);
            size *= Mathf.Lerp(1f, talkZoom, eased);

            // Only written while talking or easing back, so the ending cutscene, which
            // drives the same pan, is left alone the rest of the time.
            if (_talk > 0f || _lifted)
            {
                rig.SetPanUp(talkLift * eased);
                _lifted = _talk > 0f;
            }

            CurrentSize = size;
            if (Mathf.Abs(size - _last) < 0.002f) return;
            _last = size;
            rig.SetZoom(size);
        }
    }
}
