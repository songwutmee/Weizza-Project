using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using StoryKit.Bridge;
using UnityEngine;

namespace StoryKit.Flow
{
    /// <summary>
    /// A camera move, described the way a director describes one.
    ///
    /// Every entry is a real shot with a name a person would use, and every
    /// number behind it can still be edited. Picking a preset fills the numbers
    /// in; changing a number afterwards keeps the preset name but uses your
    /// value, so nothing is ever locked away behind a dropdown.
    /// </summary>
    public enum CameraMovePreset
    {
        /// <summary>Slow drift closer. Tension, a realisation, bad news.</summary>
        SlowPushIn,
        /// <summary>Quick snap closer. A shock, a reveal.</summary>
        SnapIn,
        /// <summary>Drift back. Ending a scene, showing the room.</summary>
        PullBack,
        /// <summary>Sweep sideways across the subject. An establishing look.</summary>
        Pan,
        /// <summary>Circle the subject. A boss reveal, a hero shot.</summary>
        Orbit,
        /// <summary>Rise and look down. Making someone small.</summary>
        CraneUp,
        /// <summary>Drop and look up. Making someone large.</summary>
        CraneDown,
        /// <summary>Small constant wobble. Unease, found footage, horror.</summary>
        Handheld,
        /// <summary>Nothing moves, only the numbers you set below.</summary>
        Custom,
    }

    /// <summary>
    /// Runs one camera move on the current shot.
    ///
    /// Put a Camera Frame node first to say who the shot is on, then this to
    /// say what the camera does. Both are tasks, so the move runs while the
    /// dialogue underneath keeps going.
    /// </summary>
    [Serializable]
    [NodeMenu("Camera",
        "A camera move: push in, pull back, pan, orbit, crane, handheld. Every number stays editable.")]
    public sealed class CameraMoveNode : TaskNode
    {
        [Tooltip("The shot to run. Choosing one fills in the numbers below.")]
        [SerializeField] private CameraMovePreset _preset = CameraMovePreset.SlowPushIn;

        [Tooltip("Who the move is around. Empty keeps whoever the last Camera " +
                 "Frame node was pointing at.")]
        [SerializeField] private string _targetId;

        [Tooltip("Seconds the move takes.")]
        [SerializeField, Min(0.05f)] private float _duration = 2.5f;

        [Tooltip("How far the camera ends up from where it started, in metres. " +
                 "Negative moves closer.")]
        [SerializeField] private float _distanceChange = -1.2f;

        [Tooltip("Degrees to swing around the subject. Negative goes the other way.")]
        [SerializeField] private float _degrees;

        [Tooltip("Metres to rise. Negative drops.")]
        [SerializeField] private float _heightChange;

        [Tooltip("Constant wobble while the move runs. 0 is locked off, " +
                 "0.1 is a nervous hand, 0.4 is running.")]
        [SerializeField, Range(0f, 1f)] private float _handheld;

        [Tooltip("Ease the start and end instead of moving at a constant rate. " +
                 "On for almost everything; off for a mechanical camera.")]
        [SerializeField] private bool _smooth = true;

        [Tooltip("Hold the shot after the move instead of returning the camera.")]
        [SerializeField] private bool _holdAtEnd = true;

        public CameraMovePreset Preset { get => _preset; set => _preset = value; }
        public string TargetId { get => _targetId; set => _targetId = value; }
        public float Duration { get => _duration; set => _duration = Mathf.Max(0.05f, value); }

        public override NodeEffect Effect => NodeEffect.Presentation;

        public override string GetSummary() =>
            string.IsNullOrEmpty(_targetId)
                ? $"{Nice(_preset)} over {_duration:0.#}s"
                : $"{Nice(_preset)} on {_targetId} over {_duration:0.#}s";

        private static string Nice(CameraMovePreset preset) => preset switch
        {
            CameraMovePreset.SlowPushIn => "slow push in",
            CameraMovePreset.SnapIn => "snap in",
            CameraMovePreset.PullBack => "pull back",
            CameraMovePreset.CraneUp => "crane up",
            CameraMovePreset.CraneDown => "crane down",
            _ => preset.ToString().ToLowerInvariant(),
        };

        /// <summary>
        /// The numbers behind each preset. Applied by the editor when the
        /// preset changes, never at runtime, so an edited value always wins.
        /// </summary>
        public static void FillFrom(CameraMovePreset preset,
            out float duration, out float distance, out float degrees,
            out float height, out float handheld, out bool smooth)
        {
            switch (preset)
            {
                case CameraMovePreset.SlowPushIn:
                    duration = 3.5f; distance = -1.2f; degrees = 0f;
                    height = 0f; handheld = 0f; smooth = true; break;

                case CameraMovePreset.SnapIn:
                    duration = 0.35f; distance = -1.8f; degrees = 0f;
                    height = 0f; handheld = 0f; smooth = false; break;

                case CameraMovePreset.PullBack:
                    duration = 3f; distance = 3.5f; degrees = 0f;
                    height = 0.6f; handheld = 0f; smooth = true; break;

                case CameraMovePreset.Pan:
                    duration = 4f; distance = 0f; degrees = 35f;
                    height = 0f; handheld = 0f; smooth = true; break;

                case CameraMovePreset.Orbit:
                    duration = 6f; distance = 0f; degrees = 360f;
                    height = 0f; handheld = 0f; smooth = false; break;

                case CameraMovePreset.CraneUp:
                    duration = 3f; distance = 1f; degrees = 0f;
                    height = 3f; handheld = 0f; smooth = true; break;

                case CameraMovePreset.CraneDown:
                    duration = 3f; distance = 0.5f; degrees = 0f;
                    height = -1.2f; handheld = 0f; smooth = true; break;

                case CameraMovePreset.Handheld:
                    duration = 5f; distance = 0f; degrees = 0f;
                    height = 0f; handheld = 0.15f; smooth = true; break;

                default:
                    duration = 2f; distance = 0f; degrees = 0f;
                    height = 0f; handheld = 0f; smooth = true; break;
            }
        }

        public override async UniTask ExecuteAsync(FlowContext context, CancellationToken ct)
        {
            var camera = StoryServices.Get<ICameraMoves>();
            if (camera == null)
            {
                // No camera that can do moves. Say so once rather than leave
                // someone wondering why the shot never happened.
                Debug.LogWarning(
                    "[StoryKit] Camera Move needs a camera that supports moves. " +
                    "Add a Simple Camera, or the Cinemachine Camera Service.",
                    context.Runner);
                return;
            }

            if (!string.IsNullOrEmpty(_targetId))
            {
                var target = StoryStageId.FindTransform(_targetId);
                if (target != null)
                    await StoryServices.Get<ICameraService>()
                        .FrameAsync(target, ShotFraming.Default, 0.3f, ct);
            }

            // A skipped cutscene should land on the end of the move at once,
            // not spend six seconds orbiting nobody.
            float duration = context.FastForward ? 0f : _duration;

            await camera.MoveAsync(new CameraMove
            {
                Duration = duration,
                DistanceChange = _distanceChange,
                Degrees = _degrees,
                HeightChange = _heightChange,
                Handheld = _handheld,
                Smooth = _smooth,
            }, ct);
        }

        public override UniTask OnRunningAsync(FlowContext context, CancellationToken ct)
            => UniTask.WaitUntilCanceled(ct);

        public override UniTask OnFinallyAsync(FlowContext context, CancellationToken ct)
        {
            StoryServices.Get<ICameraMoves>()?.StopHandheld();
            if (!_holdAtEnd) StoryServices.Get<ICameraService>()?.ReturnToDefault(0.6f);
            return UniTask.CompletedTask;
        }
    }
}
