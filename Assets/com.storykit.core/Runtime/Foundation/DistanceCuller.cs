using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace StoryKit
{
    /// <summary>
    /// Distance-based activity culling: turns off expensive behaviour when the
    /// player is far away, without unloading the object. Pair with FrameSlicer
    /// for crowds that stay cheap at scale.
    /// </summary>
    [AddComponentMenu("StoryKit/Distance Culler")]
    public sealed class DistanceCuller : MonoBehaviour
    {
        [Tooltip("Behaviours disabled when the player is beyond the far distance.")]
        [SerializeField] private Behaviour[] _behaviours;

        [Tooltip("Renderers hidden beyond the far distance (optional).")]
        [SerializeField] private Renderer[] _renderers;

        [SerializeField, Min(1f)] private float _nearDistance = 30f;
        [SerializeField, Min(1f)] private float _farDistance = 40f;   // hysteresis

        [Tooltip("How often to re-check, in seconds. Cheap by design.")]
        [SerializeField, Min(0.1f)] private float _checkInterval = 0.5f;

        private float _nextCheck;
        private bool _active = true;

        private void Update()
        {
            if (Time.time < _nextCheck) return;
            _nextCheck = Time.time + _checkInterval;

            // Shared cached lookup: no per-instance scene search.
            var player = StoryPlayer.Transform;
            if (player == null) return;

            // sqrMagnitude avoids a square root on every check.
            float sqrDistance = (player.position - transform.position).sqrMagnitude;

            // Two thresholds prevent flickering right at the boundary.
            if (_active && sqrDistance > _farDistance * _farDistance) SetActive(false);
            else if (!_active && sqrDistance < _nearDistance * _nearDistance) SetActive(true);
        }

        private void SetActive(bool active)
        {
            _active = active;

            if (_behaviours != null)
                foreach (var behaviour in _behaviours)
                    if (behaviour != null) behaviour.enabled = active;

            if (_renderers != null)
                foreach (var renderer in _renderers)
                    if (renderer != null) renderer.enabled = active;
        }
    }
}
