using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using StoryKit.Bridge;
using UnityEngine;

namespace StoryKit.Flow
{
    /// <summary>
    /// The conversation shot. Frames whoever is talking for as long as the task
    /// runs, then hands the camera back.
    ///
    /// This is the node that makes a scene feel directed instead of watched
    /// from across the room, and it is the one people ask for first: "when the
    /// player talks to this NPC, the camera should go to them".
    /// </summary>
    [Serializable]
    [NodeMenu("Camera",
        "Frame a character for a conversation. Close up, medium, wide or over the shoulder.")]
    public sealed class CameraFrameNode : TaskNode
    {
        [Tooltip("Story Stage Id of whoever the shot is on.")]
        [SerializeField] private string _targetId;

        [SerializeField] private ShotFraming _framing = ShotFraming.Medium;
        [SerializeField, Min(0f)] private float _blendTime = 0.6f;

        [Tooltip("Return the camera to the game when this task ends. Untick to "
               + "hold the shot for the next node to take over cleanly.")]
        [SerializeField] private bool _releaseOnEnd = true;

        public string TargetId { get => _targetId; set => _targetId = value; }
        public ShotFraming Framing { get => _framing; set => _framing = value; }

        public override NodeEffect Effect => NodeEffect.Presentation;
        public override string GetSummary() => $"{_framing} on {_targetId}";

        public override async UniTask ExecuteAsync(FlowContext context, CancellationToken ct)
        {
            var camera = StoryServices.Get<ICameraService>();
            var target = StoryStageId.FindTransform(_targetId);

            if (camera == null || target == null) return;
            if (context.FastForward)
            {
                // Skipping a cutscene should still land on the right shot, just
                // without spending half a second getting there.
                await camera.FrameAsync(target, _framing, 0f, ct);
                return;
            }

            await camera.FrameAsync(target, _framing, _blendTime, ct);
        }

        public override UniTask OnRunningAsync(FlowContext context, CancellationToken ct)
            => UniTask.WaitUntilCanceled(ct);

        public override UniTask OnFinallyAsync(FlowContext context, CancellationToken ct)
        {
            if (_releaseOnEnd)
                StoryServices.Get<ICameraService>()?.ReturnToDefault(_blendTime);
            return UniTask.CompletedTask;
        }
    }

    /// <summary>
    /// Cuts to a camera already placed in the scene, found by its Story Stage
    /// Id. For games with hand-composed shots, this is the only camera node
    /// they need.
    /// </summary>
    [Serializable]
    [NodeMenu("Camera",
        "Cut to a camera you placed in the scene, found by its Story Stage Id.")]
    public sealed class CameraSwitchNode : TaskNode
    {
        [Tooltip("Story Stage Id on the Cinemachine camera to cut to.")]
        [SerializeField] private string _cameraId;

        [SerializeField, Min(0f)] private float _blendTime = 0.5f;
        [SerializeField] private bool _releaseOnEnd = true;

        public string CameraId { get => _cameraId; set => _cameraId = value; }

        public override NodeEffect Effect => NodeEffect.Presentation;
        public override string GetSummary() => _cameraId;

        public override UniTask ExecuteAsync(FlowContext context, CancellationToken ct)
        {
            StoryServices.Get<ICameraService>()?.Activate(
                _cameraId, context.FastForward ? 0f : _blendTime);
            return UniTask.CompletedTask;
        }

        public override UniTask OnRunningAsync(FlowContext context, CancellationToken ct)
            => UniTask.WaitUntilCanceled(ct);

        public override UniTask OnFinallyAsync(FlowContext context, CancellationToken ct)
        {
            if (_releaseOnEnd)
                StoryServices.Get<ICameraService>()?.ReturnToDefault(_blendTime);
            return UniTask.CompletedTask;
        }
    }

    /// <summary>Swings the current shot around its subject.</summary>
    [Serializable]
    [NodeMenu("Camera",
        "Orbit the current shot around whoever it is framing. Use after a Camera Frame node.")]
    public sealed class CameraOrbitNode : ActionNode
    {
        [Tooltip("Degrees to swing. Negative goes the other way.")]
        [SerializeField] private float _degrees = 90f;

        [SerializeField, Min(0f)] private float _duration = 2f;

        public override NodeEffect Effect => NodeEffect.Presentation;
        public override string GetSummary() => $"{_degrees:0}deg over {_duration:0.#}s";

        public override async UniTask ExecuteAsync(FlowContext context, CancellationToken ct)
        {
            if (context.FastForward) return;   // pure presentation, safe to skip

            var camera = StoryServices.Get<ICameraService>();
            if (camera != null) await camera.OrbitAsync(_degrees, _duration, ct);
        }
    }
}
