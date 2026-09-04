using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace StoryKit.Bridge
{
    /// <summary>
    /// Screen shake without Cinemachine: positional noise on any camera.
    /// Respects the reduce-shake accessibility preference automatically.
    /// </summary>
    [AddComponentMenu("StoryKit/Simple Camera Shake")]
    public sealed class SimpleCameraShake : MonoBehaviour
    {
        [SerializeField, Min(0f)] private float _maxOffset = 0.4f;

        private float _trauma;
        private Vector3 _basePosition;

        private void Awake() => _basePosition = transform.localPosition;

        /// <summary>Add shake. Trauma stacks and decays, so bursts feel natural.</summary>
        public void Shake(float amount = 0.5f)
        {
            if (StoryPreferences.ReduceCameraShake) return;
            _trauma = Mathf.Clamp01(_trauma + amount);
        }

        private void LateUpdate()
        {
            if (_trauma <= 0f) return;

            // Squaring trauma makes small shakes subtle and big ones violent.
            float strength = _trauma * _trauma;
            float time = Time.unscaledTime * 25f;

            transform.localPosition = _basePosition + new Vector3(
                (Mathf.PerlinNoise(time, 0f) - 0.5f) * 2f * _maxOffset * strength,
                (Mathf.PerlinNoise(0f, time) - 0.5f) * 2f * _maxOffset * strength,
                0f);

            _trauma = Mathf.Max(0f, _trauma - Time.unscaledDeltaTime * 1.6f);
            if (_trauma <= 0f) transform.localPosition = _basePosition;
        }
    }
}
