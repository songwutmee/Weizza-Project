using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace StoryKit.Bridge
{
    /// <summary>
    /// Squash and stretch on any transform: the cheapest, most effective juice
    /// there is. Call Punch() on land, hit, pickup, button press.
    /// Restores the original scale exactly, so repeated calls never drift.
    /// </summary>
    [AddComponentMenu("StoryKit/Squash And Stretch")]
    public sealed class SquashAndStretch : MonoBehaviour
    {
        [Tooltip("How far it deforms. 0.2 = 20%.")]
        [SerializeField, Range(0f, 1f)] private float _amount = 0.2f;

        [Tooltip("Seconds to spring back.")]
        [SerializeField, Min(0.02f)] private float _duration = 0.22f;

        [Tooltip("Bounciness of the spring back.")]
        [SerializeField, Range(0f, 12f)] private float _elasticity = 6f;

        private Vector3 _baseScale;
        private float _elapsed = -1f;
        private float _strength;
        private bool _horizontal;

        private void Awake() => _baseScale = transform.localScale;

        /// <summary>Squash flat (landing) or stretch tall (jumping).</summary>
        public void Punch(bool squash = true, float strengthScale = 1f)
        {
            _horizontal = squash;
            _strength = _amount * Mathf.Max(0.05f, strengthScale);
            _elapsed = 0f;
        }

        private void Update()
        {
            if (_elapsed < 0f) return;

            _elapsed += Time.unscaledDeltaTime;
            float t = _elapsed / _duration;

            if (t >= 1f)
            {
                transform.localScale = _baseScale;
                _elapsed = -1f;
                return;
            }

            // Damped sine: overshoots once then settles: the classic spring feel.
            float wave = Mathf.Sin(t * Mathf.PI * _elasticity) * (1f - t);
            float offset = _strength * wave;

            transform.localScale = _horizontal
                ? new Vector3(_baseScale.x * (1f + offset),
                              _baseScale.y * (1f - offset), _baseScale.z)
                : new Vector3(_baseScale.x * (1f - offset),
                              _baseScale.y * (1f + offset), _baseScale.z);
        }
    }
}
