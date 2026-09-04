using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace StoryKit.Bridge
{
    /// <summary>
    /// Game feel helpers: the small things that separate a prototype from a
    /// game that feels good. Researched staples: coyote time, input buffering,
    /// hit-stop, squash-and-stretch, and camera shake.
    ///
    /// These are drop-in utilities for YOUR controller. StoryKit does not own
    /// your movement code; it just removes the boilerplate everyone rewrites.
    /// </summary>

    /// <summary>
    /// Forgiving jump timing. Two industry-standard grace windows:
    ///
    ///   Coyote time : you can still jump for a moment AFTER leaving a ledge
    ///   Jump buffer : a jump pressed just BEFORE landing fires on touchdown
    ///
    /// Defaults follow the common recommendation of ~5 frames coyote and
    /// ~7 frames buffer at 60fps.
    ///
    /// Usage in your controller:
    ///     _jump.SetGrounded(isGrounded);       // every frame
    ///     if (jumpPressed) _jump.PressJump();  // on input
    ///     if (_jump.ConsumeJump()) DoJump();   // every frame
    /// </summary>
    [Serializable]
    public sealed class JumpForgiveness
    {
        [Tooltip("Seconds you can still jump after walking off a ledge (~5 frames).")]
        [SerializeField, Min(0f)] private float _coyoteTime = 0.08f;

        [Tooltip("Seconds a jump press is remembered before landing (~7 frames).")]
        [SerializeField, Min(0f)] private float _bufferTime = 0.12f;

        private float _lastGroundedAt = -99f;
        private float _lastPressedAt = -99f;
        private bool _wasGrounded;

        public bool IsGrounded => _wasGrounded;

        /// <summary>Call every frame with your ground check result.</summary>
        public void SetGrounded(bool grounded)
        {
            if (grounded) _lastGroundedAt = Time.time;
            _wasGrounded = grounded;
        }

        /// <summary>Call when the jump button is pressed.</summary>
        public void PressJump() => _lastPressedAt = Time.time;

        /// <summary>
        /// Returns true once when a jump should happen, and clears both timers
        /// so the same press cannot trigger twice.
        /// </summary>
        public bool ConsumeJump()
        {
            bool bufferedPress = Time.time - _lastPressedAt <= _bufferTime;
            bool coyoteOk = Time.time - _lastGroundedAt <= _coyoteTime;
            if (!bufferedPress || !coyoteOk) return false;

            _lastPressedAt = -99f;
            _lastGroundedAt = -99f;
            return true;
        }

        public void Reset()
        {
            _lastPressedAt = -99f;
            _lastGroundedAt = -99f;
        }
    }

    /// <summary>
    /// Generic input buffer for any action (attack, dodge, interact).
    /// Keeps combat combos feeling responsive: a press slightly too early
    /// during an animation still lands instead of being swallowed.
    /// </summary>
    [Serializable]
    public sealed class InputBuffer
    {
        [SerializeField, Min(0f)] private float _bufferTime = 0.15f;
        private float _pressedAt = -99f;

        public void Press() => _pressedAt = Time.time;
        public bool HasBuffered => Time.time - _pressedAt <= _bufferTime;

        public bool Consume()
        {
            if (!HasBuffered) return false;
            _pressedAt = -99f;
            return true;
        }

        public void Clear() => _pressedAt = -99f;
    }

    /// <summary>
    /// Hit-stop (freeze frames): briefly stops time on impact so hits land with
    /// weight. Real-time based, so it works even while timeScale is 0.
    /// Overlapping calls extend rather than fight each other.
    /// </summary>
    public static class HitStop
    {
        private static float _resumeAt;
        // Removed: private static float _cachedScale. Hit stop no longer caches
        // the speed it found, because StoryTimeScale owns that now and the value
        // read here was whatever another system had already set.
        private static bool _active;

        /// <summary>Freeze for a moment. 0.05-0.12s covers most impacts.</summary>
        public static void Freeze(float seconds = 0.08f)
        {
            if (seconds <= 0f) return;

            if (!_active)
            {
                _active = true;
                StoryTimeScale.Stop();
                Runner.Ensure();
            }

            // Overlapping hits extend one freeze rather than nesting, so a burst
            // of five impacts cannot stack into half a second of dead air.
            _resumeAt = Mathf.Max(_resumeAt, Time.unscaledTime + seconds);
        }

        internal static void Tick()
        {
            if (!_active || Time.unscaledTime < _resumeAt) return;
            _active = false;
            StoryTimeScale.Resume();
        }

        /// <summary>Hidden driver so callers do not need a MonoBehaviour.</summary>
        private sealed class Runner : MonoBehaviour
        {
            private static Runner _instance;

            public static void Ensure()
            {
                if (_instance != null) return;
                var go = new GameObject("StoryKit.HitStop");
                UnityEngine.Object.DontDestroyOnLoad(go);
                go.hideFlags = HideFlags.HideInHierarchy;
                _instance = go.AddComponent<Runner>();
            }

            private void Update() => HitStop.Tick();
        }
    }


}

namespace StoryKit.Flow
{
    using StoryKit.Bridge;

    /// <summary>Freeze-frame on impact, straight from a graph.</summary>
    [Serializable]
    [NodeMenu("Player",
        "Hit-stop: freeze the game for a fraction of a second so an impact lands with weight.")]
    public sealed class HitStopNode : ActionNode
    {
        [SerializeField, Range(0.01f, 0.5f)] private float _seconds = 0.08f;

        public override NodeEffect Effect => NodeEffect.Presentation;
        public override string GetSummary() => $"{_seconds:0.###}s";

        public override UniTask ExecuteAsync(FlowContext context, CancellationToken ct)
        {
            HitStop.Freeze(_seconds);
            return UniTask.CompletedTask;
        }
    }

    /// <summary>
    /// Spawns a prefab at a stage anchor: VFX, props, pickups, particles.
    /// Optional auto-destroy so one-shot effects clean themselves up.
    /// </summary>
    [Serializable]
    [NodeMenu("Level",
        "Spawn a prefab at a Story Stage Id anchor (VFX, prop, pickup). Can auto-destroy.")]
    public sealed class SpawnPrefabNode : ActionNode
    {
        [SerializeField] private GameObject _prefab;
        [SerializeField] private string _atStageId;
        [Tooltip("Seconds before it is destroyed. 0 = keep forever.")]
        [SerializeField, Min(0f)] private float _lifetime = 3f;
        [SerializeField] private bool _parentToAnchor;

        public override NodeEffect Effect => NodeEffect.Presentation;
        public override string GetSummary() =>
            _prefab != null ? $"{_prefab.name} @ {_atStageId}" : "(no prefab)";

        public override UniTask ExecuteAsync(FlowContext context, CancellationToken ct)
        {
            if (_prefab == null) return UniTask.CompletedTask;

            var anchor = StoryStageId.FindTransform(_atStageId);
            var instance = anchor != null
                ? UnityEngine.Object.Instantiate(_prefab, anchor.position, anchor.rotation)
                : UnityEngine.Object.Instantiate(_prefab);

            if (_parentToAnchor && anchor != null)
                instance.transform.SetParent(anchor, true);

            if (_lifetime > 0f)
                UnityEngine.Object.Destroy(instance, _lifetime);

            return UniTask.CompletedTask;
        }
    }

    /// <summary>
    /// Smoothly animates a blackboard number over time with a curve: health
    /// bars filling, doors sliding, fade values, anything the game reads.
    /// </summary>
    [Serializable]
    [NodeMenu("Variable",
        "Animate a blackboard number from A to B over time using a curve.")]
    public sealed class TweenVarNode : ActionNode
    {
        [SerializeField] private string _key;
        [SerializeField] private float _from;
        [SerializeField] private float _to = 1f;
        [SerializeField, Min(0.01f)] private float _duration = 1f;
        [SerializeField] private AnimationCurve _curve = AnimationCurve.EaseInOut(0, 0, 1, 1);

        public override NodeEffect Effect => NodeEffect.Presentation;
        public override string GetSummary() => $"{_key}: {_from:0.##} → {_to:0.##}";

        public override async UniTask ExecuteAsync(FlowContext context, CancellationToken ct)
        {
            // On skip, jump straight to the final value so state stays correct.
            if (context.FastForward)
            {
                context.Board.Set(_key, _to);
                return;
            }

            float elapsed = 0f;
            while (elapsed < _duration)
            {
                ct.ThrowIfCancellationRequested();
                await UniTask.Yield(PlayerLoopTiming.Update, ct);
                elapsed += StoryTime.DeltaTime;
                float t = _curve.Evaluate(Mathf.Clamp01(elapsed / _duration));
                context.Board.Set(_key, Mathf.LerpUnclamped(_from, _to, t));
            }
            context.Board.Set(_key, _to);
        }
    }
}
